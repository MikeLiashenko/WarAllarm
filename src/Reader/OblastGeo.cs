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

    // Канонічна назва з ThreatParser («Сумська») → полігон.
    static readonly Dictionary<string, string> CanonToEn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Черкаська"] = "Cherkasy", ["Чернігівська"] = "Chernihiv", ["Чернівецька"] = "Chernivtsi",
        ["Дніпропетровська"] = "Dnipropetrovs'k", ["Донецька"] = "Donets'k", ["Івано-Франківська"] = "Ivano-Frankivs'k",
        ["Харківська"] = "Kharkiv", ["Херсонська"] = "Kherson", ["Хмельницька"] = "Khmel'nyts'kyy",
        ["Київська"] = "Kiev", ["Кіровоградська"] = "Kirovohrad", ["Луганська"] = "Luhans'k", ["Львівська"] = "L'viv",
        ["Миколаївська"] = "Mykolayiv", ["Одеська"] = "Odessa", ["Полтавська"] = "Poltava", ["Рівненська"] = "Rivne",
        ["Сумська"] = "Sumy", ["Тернопільська"] = "Ternopil'", ["Закарпатська"] = "Transcarpathia",
        ["Вінницька"] = "Vinnytsya", ["Волинська"] = "Volyn", ["Запорізька"] = "Zaporizhzhya", ["Житомирська"] = "Zhytomyr",
    };

    /// Канонічна назва («Сумська») → англ. назва полігона («Sumy»), як у ключах тривог.
    public static string? EnName(string canonOblast) => CanonToEn.TryGetValue(canonOblast.Trim(), out var en) ? en : null;

    /// Точка в потрібній ЧАСТИНІ області: «на півночі Сумщини» → північ полігона Сумщини.
    /// [side] — «півночі», «південному сході», «центрі»… (null — середина). null, якщо
    /// область невідома.
    public static (double lat, double lon)? PartPoint(string canonOblast, string? side)
    {
        if (!CanonToEn.TryGetValue(canonOblast.Trim(), out var en)) return null;
        var poly = Polys.FirstOrDefault(p => p.en == en);
        if (poly.rings is null) return null;
        var outer = poly.rings[0];
        double minLat = outer.Min(p => p[1]), maxLat = outer.Max(p => p[1]);
        double minLon = outer.Min(p => p[0]), maxLon = outer.Max(p => p[0]);
        double fy = 0.5, fx = 0.5;
        var s = (side ?? "").ToLowerInvariant();
        if (s.Contains("півн")) fy = 0.8; else if (s.Contains("півд")) fy = 0.2;
        if (s.Contains("сх")) fx = 0.8; else if (s.Contains("зах")) fx = 0.2;
        var pt = (lat: minLat + (maxLat - minLat) * fy, lon: minLon + (maxLon - minLon) * fx);
        // Неопукла область: точка поза полігоном — тягнемо до середини, доки не всередині.
        for (int i = 0; i < 6 && NameAt(pt.lat, pt.lon) != en; i++)
            pt = ((pt.lat + (minLat + maxLat) / 2) / 2, (pt.lon + (minLon + maxLon) / 2) / 2);
        return pt;
    }

    /// Англ. назва полігона області для точки (збіг із ключами OfficialAlerts.ByPolygon),
    /// або null поза межами. Для звірки «чи є офіційна тривога в області цілі».
    public static string? PolyName(double lat, double lon) => NameAt(lat, lon);

    /// Точка в межах України (полігони спрощені: біля кордону/узбережжя похибка ~10–15 км).
    public static bool InUkraine(double lat, double lon) => NameAt(lat, lon) != null;

    // Рамки полігонів — швидко відкинути «явно не та область» (маршрути перевіряють
    // сотні точок на кожен запис карти).
    static readonly (double minLat, double maxLat, double minLon, double maxLon)[] Boxes = Polys
        .Select(p => p.rings.SelectMany(r => r).ToList())
        .Select(pts => (pts.Min(x => x[1]), pts.Max(x => x[1]), pts.Min(x => x[0]), pts.Max(x => x[0])))
        .ToArray();

    static string? NameAt(double lat, double lon)
    {
        for (int k = 0; k < Polys.Count; k++)
        {
            var b = Boxes[k];
            if (lat < b.minLat || lat > b.maxLat || lon < b.minLon || lon > b.maxLon) continue;
            var (en, rings) = Polys[k];
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

// ── Райони: «Херсонський район — дронова загроза» → центр району ─────────────
// Назви районів повторюються в різних областях («Дніпровський район»), тож беремо той,
// що найближче до області каналу.
public static class RaionGeo
{
    static readonly List<(string name, string core, double lat, double lon)> Centres = Parse();

    static string Core(string w) => System.Text.RegularExpressions.Regex.Replace(w.ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\''), "[аеєиіїоуюяь]+$", "");
    static string Consonants(string w) => System.Text.RegularExpressions.Regex.Replace(w, "[аеєиіїоуюяьыэёъ'-]", "");

    static List<(string, string, double, double)> Parse()
    {
        var res = new List<(string, string, double, double)>();
        using var doc = JsonDocument.Parse(MapRaions.Json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var name = el.GetProperty("n").GetString() ?? "";
            var ring = el.GetProperty("r")[0];
            double sx = 0, sy = 0; int n = 0;
            foreach (var p in ring.EnumerateArray()) { sx += p[0].GetDouble(); sy += p[1].GetDouble(); n++; }
            if (n == 0) continue;
            res.Add((name, Core(name.Split(' ')[0]), sy / n, sx / n));
        }
        return res;
    }

    /// «Херсонський район» / «Херсонському районі» → (назва, центр). null — не район / не знайдено.
    public static (string name, double lat, double lon)? Find(string q, IReadOnlyCollection<string> region)
    {
        // «Конотопський р-н» — так само, як «район».
        var m = System.Text.RegularExpressions.Regex.Match(q.Trim(), @"^([\p{L}'’ʼ\-]+)\s+(?:район\w*|р-н\w*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var core = Core(m.Groups[1].Value);
        if (core.Length < 4) return null;
        // «херсонськ» (називний) vs «херсонськ» (місцевий «Херсонському») — основа однакова.
        var cands = Centres.Where(c => c.core == core || (core.Length >= 6 && c.core.StartsWith(core[..^1]))).ToList();
        // Російською («Березовский», «Подольский», «Раздельнянский район») — однакові приголосні:
        // б-р-з-в-с-к = Березівський, п-д-л-с-к = Подільський.
        if (cands.Count == 0 && Consonants(core) is { Length: >= 5 } cc)
            cands = Centres.Where(c => Consonants(c.core) == cc).ToList();
        if (cands.Count == 0) return null;
        var best = cands.OrderBy(c => PlaceFinder.RegionKm((c.lat, c.lon), region) ?? 0).First();
        return (best.name, best.lat, best.lon);
    }
}
