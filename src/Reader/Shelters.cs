using System.Globalization;
using System.Text.Json;

// ── WarAlarm · пошук найближчих укриттів ─────────────────────────────────────
// Тягне з OpenStreetMap (Overpass API) укриття, станції/входи метро поруч із
// точкою і повертає найближчі з відстанню та посиланням на маршрут у картах.
// Дані OSM неповні — це підказка, а не заміна знання свого укриття.
public static class Shelters
{
    // Не працювало (26.11): один сервер Overpass без User-Agent — зайнятий чи відмовив, і бот писав
    // «не вдалося»; а amenity=shelter у містах — це здебільшого козирки зупинок і альтанки
    // («Укриття 40 м» = зупинка). Тепер: кілька дзеркал по черзі, User-Agent, зупинки/альтанки
    // відкинуто, додано сховища (bunker) і підземні паркінги, входи однієї станції — одним рядком.
    static readonly HttpClient Http = MakeHttp();
    static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("WarAlarm/1.0 (shelter search)");
        return h;
    }
    static readonly string[] Mirrors =
    {
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
    };

    public static string Query(double lat, double lon, int radiusM)
    {
        string L(double d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        var a = $"(around:{radiusM},{L(lat)},{L(lon)})";
        return "[out:json][timeout:12];(" +
            $"nw{a}[\"amenity\"=\"shelter\"]" +
            "[\"shelter_type\"!~\"^(public_transport|picnic_shelter|weather_shelter|lean_to|basic_hut|field_shelter|rock_shelter|sun_shelter|gazebo|changing_rooms)$\"]" +
            "[\"public_transport\"!~\".\"][\"bus\"!=\"yes\"];" +
            $"nw{a}[\"building\"=\"bunker\"];" +
            $"node{a}[\"railway\"=\"subway_entrance\"];" +
            $"nw{a}[\"amenity\"=\"parking\"][\"parking\"=\"underground\"];" +
            ");out center tags 60;";
    }

    // Щотижневий файл усіх укриттів України з OSM (збирає GitHub, реліз «shelters-latest»): шукаємо
    // в пам'яті — миттєво й без Overpass, який під час нальотів часто лежить. Оновлюємо раз на добу.
    const string DataUrl = "https://github.com/MikeLiashenko/WarAllarm/releases/download/shelters-latest/shelters.json";
    const string DataFile = "shelters.json";
    static readonly HttpClient DataHttp = MakeDataHttp();
    static HttpClient MakeDataHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("WarAlarm/1.0 (shelter search)");
        return h;
    }
    static List<(double lat, double lon, string label)>? _pts;
    static DateTime _ptsAt = DateTime.MinValue;
    static readonly SemaphoreSlim _ptsGate = new(1, 1);

    static List<(double lat, double lon, string label)> Decode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var l = new List<(double, double, string)>();
        foreach (var p in doc.RootElement.GetProperty("p").EnumerateArray())
            l.Add((p[0].GetDouble(), p[1].GetDouble(), p.GetArrayLength() > 3 && p[3].GetString() is { Length: > 0 } s ? s : "Укриття"));
        return l;
    }

    /// Завантажити файл укриттів заздалегідь (на старті), щоб перший запит у бота не чекав.
    public static void Warmup() => _ = Task.Run(Data);

    static async Task<List<(double lat, double lon, string label)>?> Data()
    {
        if (_pts is not null && DateTime.Now - _ptsAt < TimeSpan.FromHours(24)) return _pts;
        await _ptsGate.WaitAsync();
        try
        {
            if (_pts is not null && DateTime.Now - _ptsAt < TimeSpan.FromHours(24)) return _pts;
            try
            {
                var json = await DataHttp.GetStringAsync(DataUrl);
                _pts = Decode(json); _ptsAt = DateTime.Now;
                try { File.WriteAllText(DataFile, json); } catch { }
                Console.WriteLine($"🏠 укриття: {_pts.Count} точок з OSM");
                return _pts;
            }
            catch (Exception ex) { Console.Error.WriteLine("⚠ укриття (файл): " + ex.Message); }
            // Немає мережі до GitHub — стара копія краща, ніж нічого; спробуємо знову за годину.
            if (_pts is null && File.Exists(DataFile)) { try { _pts = Decode(File.ReadAllText(DataFile)); } catch { } }
            if (_pts is not null) _ptsAt = DateTime.Now - TimeSpan.FromHours(23);
            return _pts;
        }
        finally { _ptsGate.Release(); }
    }

    /// Найближчі з файлу в межах [km], одне місце — один рядок.
    public static List<(string name, double lat, double lon, double km)> Local(List<(double lat, double lon, string label)> pts, double lat, double lon, double km)
    {
        double dLat = km / 111.0, dLon = km / (111.0 * Math.Cos(lat * Math.PI / 180));
        var all = pts.Where(p => Math.Abs(p.lat - lat) <= dLat && Math.Abs(p.lon - lon) <= dLon)
            .Select(p => (name: p.label, p.lat, p.lon, km: ThreatGeo.Km((lat, lon), (p.lat, p.lon))))
            .Where(x => x.km <= km).ToList();
        return Merge(all);
    }

    /// Найближчі місця (до 8): з файлу (1,5 км, порожньо — 3 км); файлу нема — живий Overpass: спершу 1,5 км (пішки — це і є «поруч», і легкий запит для
    /// перевантаженого Overpass), порожньо — 3 км. null — жодне дзеркало не відповіло.
    public static async Task<List<(string name, double lat, double lon, double km)>?> Find(double lat, double lon)
    {
        if (await Data() is { } pts)
        {
            var near = Local(pts, lat, lon, 1.5);
            return near.Count > 0 ? near : Local(pts, lat, lon, 3);
        }
        var first = await Ask(lat, lon, 1500);
        return first is { Count: 0 } ? await Ask(lat, lon, 3000) ?? first : first;
    }

    static async Task<List<(string name, double lat, double lon, double km)>?> Ask(double lat, double lon, int radiusM)
    {
        var q = Query(lat, lon, radiusM);
        foreach (var url in Mirrors)
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = q });
                    using var resp = await Http.PostAsync(url, content);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        return Parse(doc.RootElement, lat, lon);
                    }
                    Console.Error.WriteLine($"⚠ укриття: {url} → {(int)resp.StatusCode}");
                    if ((int)resp.StatusCode is 400 or 406) break; // повтор не допоможе
                }
                catch (Exception ex) { Console.Error.WriteLine($"⚠ укриття: {url} → {ex.Message}"); }
                await Task.Delay(2000);
            }
        return null;
    }

    public static List<(string name, double lat, double lon, double km)> Parse(JsonElement root, double lat, double lon)
    {
        var all = new List<(string name, double lat, double lon, double km)>();
        foreach (var el in root.GetProperty("elements").EnumerateArray())
        {
            double elat, elon;
            if (el.TryGetProperty("lat", out var la) && el.TryGetProperty("lon", out var lo))
            { elat = la.GetDouble(); elon = lo.GetDouble(); }
            else if (el.TryGetProperty("center", out var ce))
            { elat = ce.GetProperty("lat").GetDouble(); elon = ce.GetProperty("lon").GetDouble(); }
            else continue;
            var tags = el.TryGetProperty("tags", out var tg) ? tg : default;
            all.Add((Label(tags), elat, elon, ThreatGeo.Km((lat, lon), (elat, elon))));
        }
        return Merge(all);
    }

    static List<(string name, double lat, double lon, double km)> Merge(List<(string name, double lat, double lon, double km)> all)
    {
        // Одне місце — один рядок: кілька входів станції, станція і її вхід, вузол + будівля (≤120 м).
        var outp = new List<(string name, double lat, double lon, double km)>();
        foreach (var s in all.OrderBy(x => x.km))
        {
            if (outp.Any(o => ThreatGeo.Km((o.lat, o.lon), (s.lat, s.lon)) < 0.12
                    && (o.name == s.name || o.name.Contains("метро") && s.name.Contains("метро")))) continue;
            outp.Add(s);
            if (outp.Count >= 8) break;
        }
        return outp;
    }

    public static async Task<string> Near(double lat, double lon)
    {
        string L(double d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        var mapSearch = $"https://www.google.com/maps/search/{Uri.EscapeDataString("укриття")}/@{L(lat)},{L(lon)},16z";
        var found = await Find(lat, lon);
        if (found is null)
            return "😔 <b>Карта укриттів (OpenStreetMap) зараз не відповідає.</b>\n\n"
                 + $"🗺 <a href=\"{mapSearch}\">Шукати укриття на карті</a>\n\n"
                 + "Найнадійніше — підземка, підвали, паркінги, «правило двох стін». Спробуй ще раз за хвилину.";
        if (found.Count == 0)
            return "😔 <b>Поблизу не знайшов позначених укриттів</b> у відкритій карті (OSM).\n\n"
                 + $"🗺 <a href=\"{mapSearch}\">Шукати укриття на карті</a>\n\n"
                 + "Найнадійніше — підземка, підвали, паркінги, «правило двох стін». Знай своє найближче укриття заздалегідь.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🏠 <b>Найближчі укриття</b>");
        sb.AppendLine();
        int i = 0;
        foreach (var s in found.Take(6))
        {
            int m = (int)Math.Round(s.km * 1000);
            // Назва — посилання на піший маршрут (а не довгий URL окремим рядком).
            var link = $"https://www.google.com/maps/dir/?api=1&amp;destination={L(s.lat)},{L(s.lon)}&amp;travelmode=walking";
            sb.AppendLine($"{++i}. <a href=\"{link}\">{H(s.name)}</a> — {m} м");
        }
        sb.AppendLine();
        sb.AppendLine($"🗺 <a href=\"{mapSearch}\">Усі укриття на карті</a>");
        sb.Append("<i>Натисни назву — маршрут пішки. Дані OSM можуть бути неповні: знай своє укриття заздалегідь.</i>");
        return sb.ToString();
    }

    static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static string Label(JsonElement tags)
    {
        if (tags.ValueKind != JsonValueKind.Object) return "Укриття";
        string T(string k) => tags.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        var n = T("name");
        if (T("railway") == "subway_entrance")
            return n.Length > 0 ? $"Метро «{n}» (вхід)" : T("ref") is { Length: > 0 } r ? $"Вхід у метро №{r}" : "Вхід у метро";
        if (T("station") == "subway" || T("subway") == "yes") return n.Length > 0 ? $"Метро «{n}»" : "Станція метро";
        if (T("amenity") == "parking") return n.Length > 0 ? $"Підземний паркінг «{n}»" : "Підземний паркінг";
        if (T("building") == "bunker" || T("bunker_type").Length > 0) return n.Length > 0 ? n : "Укриття (сховище)";
        var addr = string.Join(", ", new[] { T("addr:street"), T("addr:housenumber") }.Where(x => x.Length > 0));
        return n.Length > 0 ? n : addr.Length > 0 ? $"Укриття · {addr}" : "Укриття";
    }
}
