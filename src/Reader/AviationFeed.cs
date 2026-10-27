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

    // Де літак НАСПРАВДІ: тактична авіація над нашою територією не літає — КАБи скидають
    // з повітряного простору РФ (Бєлгородщина, Курщина, Брянщина) або над окупованою
    // територією за лінією фронту, за 30–60 км від кордону. Раніше літак кружляв над
    // самим Харковом — виглядало, ніби бомбардувальник над містом. Точки — по області,
    // яку б'ють (і частині: «на сході Харківщини» → Валуйки, а не Бєлгород).
    static readonly Dictionary<string, (string where, double lat, double lon)> AirBase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Харківська"] = ("Бєлгородщина (РФ)", 50.62, 36.75),
        ["Харківська:сх"] = ("Валуйки (РФ)", 50.20, 38.15),
        ["Харківська:півдсх"] = ("окупована Луганщина", 49.45, 38.55),
        ["Сумська"] = ("Курщина (РФ)", 51.55, 35.05),
        ["Сумська:півн"] = ("Курщина / Брянщина (РФ)", 52.30, 34.25),
        ["Сумська:півнзах"] = ("Брянщина (РФ)", 52.45, 33.95),
        ["Сумська:сх"] = ("Бєлгородщина (РФ)", 50.85, 35.85),
        ["Сумська:півд"] = ("Бєлгородщина (РФ)", 50.70, 35.95),
        ["Сумська:півдсх"] = ("Бєлгородщина (РФ)", 50.70, 35.95),
        ["Чернігівська"] = ("Брянщина (РФ)", 52.55, 33.10),
        ["Донецька"] = ("окупована Донеччина", 48.30, 38.25),
        ["Донецька:півн"] = ("окупована Луганщина", 48.95, 38.75),
        ["Луганська"] = ("окупована Луганщина", 49.00, 39.30),
        ["Запорізька"] = ("окупована Запорізька обл.", 47.15, 35.80),
        ["Херсонська"] = ("окупований лівий берег", 46.35, 33.25),
        ["Дніпропетровська"] = ("окупована Донеччина", 47.75, 37.30),
        ["Дніпропетровська:півд"] = ("окупований лівий берег (Енергодар)", 47.40, 34.75),
        ["Миколаївська"] = ("окупований лівий берег", 46.35, 33.25),
    };

    // На скільки км КАБ з УМПК летить від точки скиду (40–70 км) — коло досяжності.
    const int KabReachKm = 65;

    static (string where, double lat, double lon)? Point(AviationZone z) =>
        z.Side is { } s && AirBase.TryGetValue(z.Canon + ":" + s, out var ps) ? ps
        : AirBase.TryGetValue(z.Canon, out var p) ? p : null; // глибокий тил — літаків там нема, на карту не ставимо

    /// Зони для картинки бота: де літак (РФ / окупована), коло досяжності, чи є загроза КАБ.
    public static List<(string canon, string where, double lat, double lon, int areaKm, bool kab)> MapZones() =>
        Active.Select(z => (z, p: Point(z))).Where(x => x.p is not null)
              .Select(x => (x.z.Canon, x.p!.Value.where, x.p.Value.lat, x.p.Value.lon, x.z.Kab ? KabReachKm : 25, x.z.Kab))
              .ToList();

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
            // name — де літак (РФ / окупована), target — яку область б'ють.
            var target = OblastGeo.PartPoint(z.Canon, null) is { } c ? OblastGeo.Region(c.lat, c.lon) : z.Canon;
            sb.Append("{\"obl\":\"").Append(E(OblastGeo.EnName(z.Canon) ?? "")).Append("\",\"name\":\"").Append(E($"{p.where} → {target}")).Append('"');
            sb.Append(",\"where\":\"").Append(E(p.where)).Append("\",\"target\":\"").Append(E(target)).Append('"');
            sb.Append(",\"lat\":").Append(N(p.lat)).Append(",\"lon\":").Append(N(p.lon));
            sb.Append(",\"areaKm\":").Append(z.Kab ? KabReachKm : 25);
            sb.Append(",\"kab\":").Append(z.Kab ? "true" : "false").Append(",\"planes\":").Append(z.Planes ? "true" : "false");
            sb.Append(",\"side\":\"").Append(E(z.Side ?? "")).Append('"');
            sb.Append(",\"ts\":").Append(((DateTimeOffset)z.Last).ToUnixTimeMilliseconds());
            sb.Append(",\"src\":").Append(z.Srcs.Count).Append(",\"note\":\"").Append(E(z.Note)).Append("\"}");
        }
        return sb.Append(']').ToString();
    }
}
