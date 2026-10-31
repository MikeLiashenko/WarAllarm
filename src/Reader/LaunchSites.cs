using System.Text.RegularExpressions;

// ── WarAlarm · Звідки летить ─────────────────────────────────────────────────
// Канали пишуть «балістика з Курська», «Іскандер з Криму», «з Чернігівщини на
// Водосховище». Тут — координати районів пуску (РФ/окуповані) і орієнтири лінії
// фронту для КАБ, щоб малювати РЕАЛЬНИЙ політ: від точки пуску до цілі з
// швидкістю засобу (WeaponCatalog), а не «стоїть у місті».

public static class LaunchSites
{
    // Райони пусків балістики / ракет (основа назви → координати).
    static readonly (string re, string name, double lat, double lon)[] Sites =
    {
        (@"курськ|курщин|kursk", "Курськ", 51.73, 36.19),
        (@"брянськ|брянщин|bryansk", "Брянськ", 53.24, 34.37),
        (@"бєлгород|белгород|бєлгородщин|belgorod", "Бєлгород", 50.60, 36.59),
        (@"воронеж|voronezh", "Воронеж", 51.66, 39.20),
        (@"ростов|rostov", "Ростов", 47.23, 39.72),
        (@"таганрог|taganrog", "Таганрог", 47.22, 38.90),
        (@"міллерово|миллерово", "Міллерово", 48.92, 40.39),
        (@"орл[аі]|орел|орьол|орёл|oryol", "Орел", 52.97, 36.06),
        (@"липецьк|липецк", "Липецьк", 52.61, 39.59),
        (@"джанко", "Джанкой", 45.71, 34.39),
        (@"сак(?:и|ів)\b", "Саки", 45.13, 33.60),
        (@"севастопол", "Севастополь", 44.62, 33.53),
        (@"крим|крым|crimea", "Крим", 45.30, 34.40),
        (@"чорного моря|черного моря|чорне море|акваторі", "Чорне море", 44.60, 32.80),
        (@"каспі|каспи", "Каспій", 45.00, 47.50),
        (@"донецьк|донецк", "Донецьк", 48.00, 37.80),
        (@"луганськ|луганск", "Луганськ", 48.57, 39.31),
        (@"маріупол|мариупол", "Маріуполь", 47.10, 37.55),
        (@"бердянськ|бердянск", "Бердянськ", 46.76, 36.79),
        (@"шаталово|smolensk|смоленськ", "Смоленськ", 54.78, 32.05),
        (@"енгельс|энгельс|engels", "Енгельс", 51.48, 46.20),
    };

    // Звідки зазвичай б'є балістика, коли канал не назвав район пуску.
    static readonly string[] BallisticDefaults = { "Бєлгород", "Курськ", "Брянськ", "Воронеж", "Таганрог", "Крим" };

    // Орієнтири лінії фронту / кордону, звідки скидають КАБ.
    static readonly (string name, double lat, double lon)[] KabFront =
    {
        ("кордон Бєлгород", 50.45, 36.70),
        ("кордон Суми", 51.20, 34.95),
        ("кордон Чернігів", 52.05, 32.80),
        ("фронт Куп'янськ", 49.70, 37.90),
        ("фронт Донеччина", 48.30, 37.70),
        ("фронт Запоріжжя", 47.45, 35.85),
        ("лівий берег Херсон", 46.55, 33.20),
    };

    static (string name, double lat, double lon)? Site(string name) =>
        Sites.FirstOrDefault(s => s.name == name) is { re: not null } s ? (s.name, s.lat, s.lon) : null;

    /// Район пуску з фрази «з Курська», «із Криму», «з акваторії Чорного моря» або null.
    public static (string name, double lat, double lon)? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        foreach (var s in Sites)
            if (Regex.IsMatch(t, s.re)) return (s.name, s.lat, s.lon);
        return null;
    }

    /// Звідки летить ціль за фразою «з X»: район пуску РФ, або місто/область України.
    public static (string name, double lat, double lon)? Resolve(string? from, IReadOnlyCollection<string>? region = null)
    {
        if (string.IsNullOrWhiteSpace(from)) return null;
        if (FromText(from) is { } site) return site;
        // «з Чернігівщини», «із Сумщини» → центр області.
        var low = from.Trim().ToLowerInvariant();
        var obl = Geo.Oblasts.FirstOrDefault(kv => low.StartsWith(kv.Key[..Math.Max(4, kv.Key.Length - 3)])).Value;
        if (obl != null) return ThreatGeo.ResolveOblast(obl);
        return PlaceFinder.Find(from, region ?? Array.Empty<string>(), 400);
    }

    /// Найімовірніший район пуску балістики по цілі (найближчий зі звичних).
    public static (string name, double lat, double lon) BallisticOrigin((double lat, double lon) target)
    {
        var best = Site(BallisticDefaults[0])!.Value;
        double bd = double.MaxValue;
        foreach (var n in BallisticDefaults)
            if (Site(n) is { } s && ThreatGeo.Km((s.lat, s.lon), target) is var d && d < bd) { bd = d; best = s; }
        return best;
    }

    /// Найближчий орієнтир фронту / кордону — звідки ціль найімовірніше заходить.
    public static (double lat, double lon) NearestFront((double lat, double lon) target)
    {
        var f = KabFront.MinBy(x => ThreatGeo.Km((x.lat, x.lon), target));
        return (f.lat, f.lon);
    }

    /// Точка скиду КАБ: ~55 км від цілі в бік найближчої лінії фронту / кордону.
    public static (double lat, double lon) KabRelease((double lat, double lon) target)
    {
        var front = KabFront.MinBy(f => ThreatGeo.Km((f.lat, f.lon), target));
        var d = ThreatGeo.Km((front.lat, front.lon), target);
        if (d <= 55) return (front.lat, front.lon);
        double k = 55 / d;
        return (target.lat + (front.lat - target.lat) * k, target.lon + (front.lon - target.lon) * k);
    }
}
