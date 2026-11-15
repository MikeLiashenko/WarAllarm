using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

// ── WarAlarm · пошук місця за адресою / мікрорайоном (OpenStreetMap Nominatim) ─────
// Список Cities знає лише назви населених пунктів. Коли користувач бота пише
// «603-мікрорайон», «Харків, Ювілейний проспект 50» чи «вул. Сумська 10» — шукаємо тут.
// Правила Nominatim: ≤1 запит/с, свій User-Agent, кешувати відповіді — дотримуємось.
public static class Geocode
{
    public sealed record Hit(string Label, double Lat, double Lon);

    const string Api = "https://nominatim.openstreetmap.org/search";
    static readonly HttpClient Http = CreateHttp();
    static readonly SemaphoreSlim Gate = new(1, 1);
    static DateTime _lastCall = DateTime.MinValue;
    static readonly Dictionary<string, (DateTime at, List<Hit> hits)> Cache = new();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("WarAlarm-bot/1.0");
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk,ru;q=0.5");
        return h;
    }

    /// Схоже на адресу/мікрорайон, а не на назву міста: є цифра, кома або «вул.», «просп.», «мкр»…
    public static bool LooksLikeAddress(string q) =>
        Regex.IsMatch(q, @"\d|,") ||
        Regex.IsMatch(q, @"(?<!\p{L})(вул|вулиця|улица|ул|просп|проспект|пр-т|пров|провулок|пер|переулок|бул|бульв|бульвар|пл|площа|площадь|шосе|шоссе|наб|набережна|узвіз|мкр|мкрн|мкр-н|м-н|мікрорайон|микрорайон|масив|массив|жк|житловий|квартал|селище)(?!\p{L})",
            RegexOptions.IgnoreCase);

    /// Запит до вигляду, який знає OSM: «мкр 603», «603-мікрорайон», «мікрорайон №603» → «603-й мікрорайон».
    public static string Normalize(string q)
    {
        var s = Regex.Replace(q.Trim(), @"[’ʼ`´]", "'");
        s = Regex.Replace(s, @"\s+", " ");
        s = Regex.Replace(s, @"(?<!\p{L})(мкрн|мкр-н|мкр|м-н|микрорайон|мікрорайон)\.?(?!\p{L})", "мікрорайон", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?<!\d)(\d{1,3})\s*-?\s*(?:й|ий|ій|тий|ой)?\s+мікрорайон", "$1-й мікрорайон", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?<!\d)(\d{1,3})-мікрорайон", "$1-й мікрорайон", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"мікрорайон\s*№?\s*(\d{1,3})(?!\d)", "$1-й мікрорайон", RegexOptions.IgnoreCase);
        return s.Trim(' ', ',', '.');
    }

    /// До 4 різних місць в Україні (найкращі першими). Порожньо — нічого не знайшли або мережа впала.
    public static async Task<List<Hit>> Search(string query, CancellationToken ct = default)
    {
        var norm = Normalize(query);
        var variants = new List<string> { norm };
        if (!string.Equals(norm, query.Trim(), StringComparison.OrdinalIgnoreCase)) variants.Add(query.Trim());
        // «603-й мікрорайон» в OSM буває лише зупинкою «603 мікрорайон» — запасний варіант.
        var plain = Regex.Replace(norm, @"(\d{1,3})-й мікрорайон", "$1 мікрорайон");
        if (plain != norm) variants.Add(plain);

        foreach (var v in variants.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hits = await SearchOne(v, ct);
            if (hits.Count > 0) return hits;
        }
        return new();
    }

    static async Task<List<Hit>> SearchOne(string q, CancellationToken ct)
    {
        var key = q.ToLowerInvariant();
        lock (Cache)
            if (Cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.at < TimeSpan.FromDays(1)) return c.hits;

        var outp = new List<Hit>();
        await Gate.WaitAsync(ct);
        try
        {
            var wait = _lastCall.AddMilliseconds(1100) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCall = DateTime.UtcNow;
            var url = $"{Api}?format=jsonv2&addressdetails=1&countrycodes=ua&limit=8&accept-language=uk&q={Uri.EscapeDataString(q)}";
            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"⚠ geocode {(int)resp.StatusCode} «{q}»");
                return outp; // не кешуємо помилку
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (!double.TryParse(r.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(r.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                    continue;
                var label = Label(r);
                if (label.Length == 0) continue;
                // Дублікати: та сама назва або точка поруч (будинок і його ділянка, зупинка біля кварталу).
                if (outp.Any(h => h.Label == label || ThreatGeo.Km((h.Lat, h.Lon), (lat, lon)) < 1.5)) continue;
                outp.Add(new Hit(label, lat, lon));
                if (outp.Count >= 4) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"⚠ geocode «{q}»: {ex.Message}");
            return outp;
        }
        finally { Gate.Release(); }

        lock (Cache)
        {
            if (Cache.Count > 500) Cache.Clear();
            Cache[key] = (DateTime.UtcNow, outp);
        }
        return outp;
    }

    // Коротка назва для бота: «603-й мікрорайон, Харків», «вулиця Сумська, 10, Харків».
    static string Label(JsonElement r)
    {
        string? A(string k) => r.TryGetProperty("address", out var a) && a.TryGetProperty(k, out var v)
            ? v.GetString() is { Length: > 0 } s ? s : null : null;
        var locality = A("city") ?? A("town") ?? A("village") ?? A("hamlet") ?? A("municipality") ?? A("state");
        var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        string? primary = A("house_number") is { } hn && A("road") is { } road ? $"{road}, {hn}"
            : name.Length > 0 ? name
            : A("road") ?? A("quarter") ?? A("neighbourhood") ?? A("suburb");
        string label = primary is null || primary == locality ? locality ?? ""
            : locality is null ? primary : $"{primary}, {locality}";
        return label.Length > 60 ? label[..57].TrimEnd(' ', ',') + "…" : label;
    }
}
