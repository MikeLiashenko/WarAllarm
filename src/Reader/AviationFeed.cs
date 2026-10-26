using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// ── WarAlarm · тактична авіація і загроза КАБ ────────────────────────────────
// Вдень Суми/Харків/Донеччину б'ють КАБами з літаків. Канали пишуть «Активність
// тактичної авіації на північному напрямку», «Су-34/35», «Сушки», «петухи», «КАБи на
// схід Дніпропетровщини» — точки в такому пості немає. Тримаємо ЗОНУ над областю (і
// частиною області, якщо названо): є авіація / є загроза КАБ, коли згадували, скільки
// каналів. Зона знімається через 20 хв тиші або за відбоєм по області.

public sealed class AviationZone
{
    public required string Canon;            // «Сумська»
    public string? Side;                     // «півночі» / «сході»… (частина області)
    public bool Kab, Planes;
    public DateTime First, Last;
    public readonly HashSet<string> Srcs = new(StringComparer.OrdinalIgnoreCase);
    public string Note = "";
}

public static class AviationFeed
{
    static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex Planes = new(
        @"тактичн\w*\s+авіац|тактическ\w*\s+авиац|(?<!\p{L})т/а(?!\p{L})|(?<!\p{L})(?:су|ту|міг|миг)[-\s]?(?:24|25|30|34|35)(?!\d)|сушк|петух|бортов\w*\s+авіац|руснявої\s+авіац|активн\w*\s+(?:\w+\s+)?авіац|авиация работает|авіація працює|активн\w*\s+(?:\w+\s+)?авиац",
        O);
    // «КАБ», «КАБи», «КАБів», «КАБов», «УМПК», «ФАБ» — але не «кабелі», «кабінет».
    static readonly Regex Kab = new(@"(?<!\p{L})(?:каб|каби|кабы|кабів|кабов|кабами|ксаб|умпк|фаб)(?!\p{L})", O);
    static readonly Regex Clear = new(@"відбій|отбой|чисто|не фіксуються|не фиксируются", O);
    static readonly Regex News = new(@"пресслужб|повідомля|заяв|використовує|видео|відео|фото", O);
    static readonly Regex SideRe = new(
        @"північн\w*\s+сх\w*|північн\w*\s+зах\w*|південн\w*\s+сх\w*|південн\w*\s+зах\w*|північн\w*|півночі|південн\w*|півдні|східн\w*|сході|схід|західн\w*|заході|захід|северн\w*|южн\w*|восточн\w*|западн\w*",
        O);

    static readonly object Gate = new();
    static readonly Dictionary<string, AviationZone> Zones = new(StringComparer.OrdinalIgnoreCase);

    public static List<AviationZone> Active { get { lock (Gate) { Prune(DateTime.Now); return Zones.Values.ToList(); } } }

    /// Пост про авіацію / КАБ → оновити зони областей. [fallback] — області каналу,
    /// якщо в самому тексті область не названо.
    public static bool Ingest(string text, IReadOnlyCollection<string> fallback, string src, DateTime now)
    {
        if (text.Length > 300 || News.IsMatch(text)) return false;
        bool planes = Planes.IsMatch(text), kab = Kab.IsMatch(text);
        var named = OblastsIn(text);
        var regions = named.Count > 0 ? named : fallback.ToList();
        if (regions.Count == 0) return false;
        lock (Gate)
        {
            // Відбій / «не фіксуються» без нових згадок авіації — знімаємо зону.
            if (Clear.IsMatch(text) && !kab && !planes)
            {
                bool any = false;
                foreach (var r in regions) any |= Zones.Remove(r);
                return any;
            }
            if (!planes && !kab) return false;
            var side = Side(text);
            foreach (var r in regions)
            {
                if (!Zones.TryGetValue(r, out var z)) Zones[r] = z = new AviationZone { Canon = r, First = now };
                z.Last = now;
                z.Kab |= kab;
                z.Planes |= planes;
                if (side is not null) z.Side = side;
                z.Srcs.Add(src);
                var line = text.Replace("\n", " ").Trim();
                z.Note = line.Length > 90 ? line[..90] + "…" : line;
            }
        }
        return true;
    }

    /// «на півночі», «північному напрямку», «на схід Дніпропетровщини» → «півночі»/«сході»… (PartPoint).
    static string? Side(string text)
    {
        var m = SideRe.Match(text);
        if (!m.Success) return null;
        var s = m.Value.ToLowerInvariant();
        string ns = s.Contains("півн") || s.Contains("север") ? "півн" : s.Contains("півд") || s.Contains("южн") ? "півд" : "";
        string ew = s.Contains("сх") || s.Contains("восточ") ? "сх" : s.Contains("зах") || s.Contains("запад") ? "зах" : "";
        return ns + ew is { Length: > 0 } v ? v : null;
    }

    /// Області, названі в тексті: «Сумщина», «на Донеччину», «Дніпропетровщини».
    public static List<string> OblastsIn(string text) =>
        Regex.Split(text, @"[^\p{L}'’ʼ\-]+").Select(w => Geo.Canonical(w)).Where(c => c is not null).Select(c => c!).Distinct().ToList();

    static void Prune(DateTime now)
    {
        foreach (var k in Zones.Where(z => now - z.Value.Last > TimeSpan.FromMinutes(20)).Select(z => z.Key).ToList())
            Zones.Remove(k);
    }

    /// Зона над областю місця (для статусу бота / «Зараз»), або null.
    public static AviationZone? At(double lat, double lon)
    {
        var en = OblastGeo.PolyName(lat, lon);
        return en is null ? null : Active.FirstOrDefault(z => OblastGeo.EnName(z.Canon) == en);
    }

    // Частину області не названо — ставимо туди, куди КАБи реально летять (підконтрольні
    // міста біля фронту), а не в середину полігона (для Донеччини це окупований Донецьк).
    static readonly Dictionary<string, (double lat, double lon)> KabFocus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Донецька"] = (48.73, 37.56),          // Краматорськ / Слов'янськ
        ["Запорізька"] = (47.84, 35.14),        // Запоріжжя
        ["Херсонська"] = (46.64, 32.61),        // Херсон
        ["Харківська"] = (49.99, 36.23),        // Харків
        ["Сумська"] = (50.91, 34.80),           // Суми
        ["Луганська"] = (49.05, 38.20),         // Лиман / Сіверськ (лінія фронту)
    };

    static (double lat, double lon)? Point(AviationZone z) =>
        z.Side is null && KabFocus.TryGetValue(z.Canon, out var f) ? f : OblastGeo.PartPoint(z.Canon, z.Side);

    /// state.aviation — для застосунку: точка в частині області, радіус ураження КАБ.
    public static string Json(DateTime now)
    {
        var sb = new StringBuilder("[");
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string E(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        bool first = true;
        foreach (var z in Active)
        {
            if (Point(z) is not { } p) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"obl\":\"").Append(E(OblastGeo.EnName(z.Canon) ?? "")).Append("\",\"name\":\"").Append(E(OblastGeo.Region(p.lat, p.lon))).Append('"');
            sb.Append(",\"lat\":").Append(N(p.lat)).Append(",\"lon\":").Append(N(p.lon));
            sb.Append(",\"kab\":").Append(z.Kab ? "true" : "false").Append(",\"planes\":").Append(z.Planes ? "true" : "false");
            sb.Append(",\"side\":\"").Append(E(z.Side ?? "")).Append('"');
            sb.Append(",\"ts\":").Append(((DateTimeOffset)z.Last).ToUnixTimeMilliseconds());
            sb.Append(",\"src\":").Append(z.Srcs.Count).Append(",\"note\":\"").Append(E(z.Note)).Append("\"}");
        }
        return sb.Append(']').ToString();
    }
}
