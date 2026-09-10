using System.Text.Json;

// ── WarAlarm · область за координатами ───────────────────────────────────────
// Точка (lat,lon) → назва області (для групування цілей у новинах каналу).
// Полігони — ті самі, що на карті (MapOblasts.Json). Повертає коротку укр. назву
// («Волинь», «Київщина», «Харківщина»…).
public static class OblastGeo
{
    static readonly List<(string en, List<List<double[]>> rings)> Polys = Parse();

    // Англ. «n» полігона → коротка укр. назва.
    static readonly Dictionary<string, string> ShortUa = new()
    {
        ["Cherkasy"] = "Черкащина",
        ["Chernihiv"] = "Чернігівщина",
        ["Chernivtsi"] = "Буковина",
        ["Crimea"] = "Крим",
        ["Dnipropetrovs'k"] = "Дніпропетровщина",
        ["Donets'k"] = "Донеччина",
        ["Ivano-Frankivs'k"] = "Прикарпаття",
        ["Kharkiv"] = "Харківщина",
        ["Kherson"] = "Херсонщина",
        ["Khmel'nyts'kyy"] = "Хмельниччина",
        ["Kiev"] = "Київщина",
        ["Kiev City"] = "Київ",
        ["Kirovohrad"] = "Кіровоградщина",
        ["Luhans'k"] = "Луганщина",
        ["L'viv"] = "Львівщина",
        ["Mykolayiv"] = "Миколаївщина",
        ["Odessa"] = "Одещина",
        ["Poltava"] = "Полтавщина",
        ["Rivne"] = "Рівненщина",
        ["Sevastopol'"] = "Севастополь",
        ["Sumy"] = "Сумщина",
        ["Ternopil'"] = "Тернопільщина",
        ["Transcarpathia"] = "Закарпаття",
        ["Vinnytsya"] = "Вінниччина",
        ["Volyn"] = "Волинь",
        ["Zaporizhzhya"] = "Запоріжжя",
        ["Zhytomyr"] = "Житомирщина",
    };

    /// Коротка укр. назва області, що містить точку (або «Україна», якщо поза).
    public static string Region(double lat, double lon)
    {
        var en = NameAt(lat, lon);
        return en != null && ShortUa.TryGetValue(en, out var s) ? s : "Україна";
    }

    static string? NameAt(double lat, double lon)
    {
        foreach (var (en, rings) in Polys)
        {
            bool inside = false;
            foreach (var ring in rings)
                for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                {
                    double xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
                    if (((yi > lat) != (yj > lat)) &&
                        (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                        inside = !inside;
                }
            if (inside) return en;
        }
        return null;
    }

    static List<(string, List<List<double[]>>)> Parse()
    {
        var outp = new List<(string, List<List<double[]>>)>();
        try
        {
            using var doc = JsonDocument.Parse(MapOblasts.Json);
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                var name = o.TryGetProperty("n", out var n) ? n.GetString() ?? "" : "";
                var rings = new List<List<double[]>>();
                foreach (var r in o.GetProperty("r").EnumerateArray())
                {
                    var ring = new List<double[]>();
                    foreach (var p in r.EnumerateArray())
                        ring.Add(new[] { p[0].GetDouble(), p[1].GetDouble() });
                    if (ring.Count >= 3) rings.Add(ring);
                }
                if (rings.Count > 0) outp.Add((name, rings));
            }
        }
        catch { /* немає полігонів */ }
        return outp;
    }
}
