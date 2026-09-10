using System.Text.Json;

// ── WarAlarm · офіційні повітряні тривоги ────────────────────────────────────
// Тягне офіційний статус тривог по областях (безкоштовний фід Вадима Клименка,
// без токена) і повертає рівень по кожній НАШІЙ області-полігону (той самий «n»,
// що в MapOblasts): "red" = тривога в області, "yellow" = часткова/попередження.
// Порт логіки з app/lib/services/official_alerts_service.dart.
public static class OfficialAlerts
{
    const string Url = "https://vadimklimenko.com/map/statuses.json";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    // Назва області у фіді (укр.) → наш полігон «n» (англ.).
    static readonly Dictionary<string, string> UaToEn = new()
    {
        ["АР Крим"] = "Crimea",
        ["Севастополь"] = "Sevastopol'",
        ["Вінницька область"] = "Vinnytsya",
        ["Волинська область"] = "Volyn",
        ["Дніпропетровська область"] = "Dnipropetrovs'k",
        ["Донецька область"] = "Donets'k",
        ["Житомирська область"] = "Zhytomyr",
        ["Закарпатська область"] = "Transcarpathia",
        ["Запорізька область"] = "Zaporizhzhya",
        ["Івано-Франківська область"] = "Ivano-Frankivs'k",
        ["Київська область"] = "Kiev",
        ["Кіровоградська область"] = "Kirovohrad",
        ["Луганська область"] = "Luhans'k",
        ["Львівська область"] = "L'viv",
        ["Миколаївська область"] = "Mykolayiv",
        ["Одеська область"] = "Odessa",
        ["Полтавська область"] = "Poltava",
        ["Рівненська область"] = "Rivne",
        ["Сумська область"] = "Sumy",
        ["Тернопільська область"] = "Ternopil'",
        ["Харківська область"] = "Kharkiv",
        ["Херсонська область"] = "Kherson",
        ["Хмельницька область"] = "Khmel'nyts'kyy",
        ["Черкаська область"] = "Cherkasy",
        ["Чернівецька область"] = "Chernivtsi",
        ["Чернігівська область"] = "Chernihiv",
        ["м. Київ"] = "Kiev City",
    };

    /// Знімок офіційних тривог: рівень по полігону («n»→red/yellow) + читабельні
    /// українські назви областей, що зараз червоні / жовті (для новин у каналі).
    public sealed record Snapshot(
        Dictionary<string, string> ByPolygon,
        List<string> RedUa,
        List<string> YellowUa);

    /// Наш полігон «n» → "red"/"yellow". Порожній словник = немає тривог / офлайн.
    public static async Task<Dictionary<string, string>> Fetch() => (await FetchFull()).ByPolygon;

    public static async Task<Snapshot> FetchFull()
    {
        var byPoly = new Dictionary<string, string>();
        var redUa = new List<string>();
        var yellowUa = new List<string>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.Add("User-Agent", "WarAlarm/1.0");
            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new Snapshot(byPoly, redUa, yellowUa);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("states", out var states))
                return new Snapshot(byPoly, redUa, yellowUa);

            foreach (var st in states.EnumerateObject())
            {
                if (!UaToEn.TryGetValue(st.Name, out var en)) continue;
                var v = st.Value;
                bool enabled = v.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
                string lvl = v.TryGetProperty("alert_level", out var al) ? al.GetString() ?? "" : "";

                int districtsOn = 0;
                if (v.TryGetProperty("districts", out var ds) && ds.ValueKind == JsonValueKind.Object)
                    foreach (var d in ds.EnumerateObject())
                        if (d.Value.TryGetProperty("enabled", out var de) && de.ValueKind == JsonValueKind.True)
                            districtsOn++;

                string? level = enabled ? (lvl == "yellow" ? "yellow" : "red")
                              : districtsOn > 0 ? "yellow" : null;
                if (level == null) continue;
                byPoly[en] = level;
                (level == "red" ? redUa : yellowUa).Add(st.Name);
            }
        }
        catch { /* офлайн — порожньо */ }
        return new Snapshot(byPoly, redUa, yellowUa);
    }

    /// Читабельний перелік для команди /alerts, + чи є тривога у місті [myCity].
    public static async Task<string> Summary(string myCity, string myOblast)
    {
        var levels = await Fetch();
        var mineKeys = new HashSet<string>();
        foreach (var (ua, en) in UaToEn)
            if (ua.Contains(myOblast, StringComparison.OrdinalIgnoreCase)
                || (myCity == "Київ" && en is "Kiev" or "Kiev City")
                || ua.Contains(myCity, StringComparison.OrdinalIgnoreCase))
                mineKeys.Add(en);

        bool mine = mineKeys.Any(k => levels.ContainsKey(k));
        int red = levels.Count(kv => kv.Value == "red");
        int yellow = levels.Count(kv => kv.Value == "yellow");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🚨 Офіційні тривоги:");
        sb.AppendLine(mine ? $"🔴 У «{myCity}» — ТРИВОГА!" : $"🟢 У «{myCity}» тривоги немає.");
        sb.AppendLine($"По країні: 🔴 {red} обл. · 🟡 {yellow} обл.");
        return sb.ToString();
    }
}
