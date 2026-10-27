using System.Globalization;
using System.Text.Json;

// ── WarAlarm · пошук найближчих укриттів ─────────────────────────────────────
// Тягне з OpenStreetMap (Overpass API) укриття, станції/входи метро поруч із
// точкою і повертає найближчі з відстанню та посиланням на маршрут у картах.
// Дані OSM неповні — це підказка, а не заміна знання свого укриття.
public static class Shelters
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    const string Api = "https://overpass-api.de/api/interpreter";

    public static async Task<string> Near(double lat, double lon, int radiusM = 3000)
    {
        string L(double d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        var around = $"(around:{radiusM},{L(lat)},{L(lon)})";
        var query =
            "[out:json][timeout:20];(" +
            $"node{around}[\"amenity\"=\"shelter\"];" +
            $"way{around}[\"amenity\"=\"shelter\"];" +
            $"node{around}[\"railway\"=\"subway_entrance\"];" +
            $"node{around}[\"station\"=\"subway\"];" +
            $"node{around}[\"railway\"=\"station\"][\"subway\"=\"yes\"];" +
            ");out center 60;";

        List<(string name, double lat, double lon, double km)> found;
        try
        {
            using var content = new StringContent("data=" + Uri.EscapeDataString(query));
            var resp = await Http.PostAsync(Api, content);
            if (!resp.IsSuccessStatusCode) return "Не вдалося отримати дані про укриття зараз. Спробуй пізніше.";
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            found = new();
            foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
            {
                double elat, elon;
                if (el.TryGetProperty("lat", out var la) && el.TryGetProperty("lon", out var lo))
                { elat = la.GetDouble(); elon = lo.GetDouble(); }
                else if (el.TryGetProperty("center", out var ce))
                { elat = ce.GetProperty("lat").GetDouble(); elon = ce.GetProperty("lon").GetDouble(); }
                else continue;

                var tags = el.TryGetProperty("tags", out var tg) ? tg : default;
                string name = Label(tags);
                found.Add((name, elat, elon, ThreatGeo.Km((lat, lon), (elat, elon))));
            }
        }
        catch { return "Не вдалося отримати дані про укриття зараз (мережа/сервіс). Спробуй пізніше."; }

        if (found.Count == 0)
            return "😔 <b>Поблизу не знайшов позначених укриттів</b> у відкритій карті (OSM).\n\n"
                 + "Найнадійніше — підземка, підвали, паркінги, «правило двох стін». Знай своє найближче укриття заздалегідь.";

        var top = found.OrderBy(x => x.km).Take(6).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🏠 <b>Найближчі укриття</b>");
        sb.AppendLine();
        int i = 0;
        foreach (var s in top)
        {
            int m = (int)Math.Round(s.km * 1000);
            // Назва — посилання на піший маршрут (а не довгий URL окремим рядком).
            var link = $"https://www.google.com/maps/dir/?api=1&amp;destination={L(s.lat)},{L(s.lon)}&amp;travelmode=walking";
            sb.AppendLine($"{++i}. <a href=\"{link}\">{H(s.name)}</a> — {m} м");
        }
        sb.AppendLine();
        sb.Append("<i>Натисни назву — маршрут пішки. Дані OSM можуть бути неповні: знай своє укриття заздалегідь.</i>");
        return sb.ToString();
    }

    static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static string Label(JsonElement tags)
    {
        if (tags.ValueKind == JsonValueKind.Object)
        {
            if (tags.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()))
                return n.GetString()!;
            if (tags.TryGetProperty("railway", out var rw))
            {
                var v = rw.GetString();
                if (v == "subway_entrance") return "Вхід у метро";
                if (v == "station") return "Станція метро";
            }
            if (tags.TryGetProperty("station", out _)) return "Станція метро";
            if (tags.TryGetProperty("amenity", out var am) && am.GetString() == "shelter") return "Укриття";
        }
        return "Укриття";
    }
}
