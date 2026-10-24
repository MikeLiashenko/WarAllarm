using System.Text.RegularExpressions;

// ── WarAlarm · Словник засобів ураження ──────────────────────────────────────
// Усі цілі, які пишуть канали: офіційні індекси («Х-101», «9М723»), назви
// («Калібр», «Іскандер-М»), російське/латинське написання і сленг моніторів.
// Одне джерело правди для ВСІХ парсерів (ThreatParser / KyivMonitor /
// KharkivMonitor / SourceHub): тип цілі, нормальна назва на карті та швидкість
// (для часу підльоту).
//
// Правила каналів (зі слів користувача):
//   • «ціль» / «бандероль»   — крилата ракета;
//   • «швидкісна ціль»       — балістика.

public sealed record Weapon(string Name, ThreatKind Kind, double SpeedKmh, Regex Re, bool Decoy = false);

public static class WeaponCatalog
{
    // Х-NN кирилицею чи латиницею, з дефісом/пробілом або без: «Х-101», «x101», «Х 59МК».
    static string Kh(string n) => $@"(?<![a-zа-яіїєґ0-9])[хx]\s?[-‑–]?\s?{n}(?!\d)";
    // 9М723, 9M723, «9м-723».
    static string Idx(string a, string n) => $@"(?<![a-zа-яіїєґ0-9]){a}\s?[-‑–]?\s?{n}(?!\d)";

    static Weapon W(string name, ThreatKind kind, double kmh, string re, bool decoy = false) =>
        new(name, kind, kmh, new Regex(re, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), decoy);

    /// Порядок важливий: специфічніше — вище («Іскандер-К» перед «Іскандер»,
    /// «Х-555» перед «Х-55», «швидкісна ціль» перед «ціль»).
    public static readonly Weapon[] All =
    {
        // ── Балістика ──
        W("Іскандер-М", ThreatKind.Ballistic, 7000, @"іскандер[\s-]*м\b|искандер[\s-]*м\b|iskander[\s-]*m\b|" + Idx("9[мm]", "723")),
        W("Іскандер-К", ThreatKind.Missile, 900, @"іскандер[\s-]*к\b|искандер[\s-]*к\b|iskander[\s-]*k\b|кр[\s-]*9[мm]72[89]|" + Idx("9[мm]", "72[89]")),
        W("KN-23", ThreatKind.Ballistic, 7000, @"\bkn[\s-]?23|\bкн[\s-]?23|хвасон|hwasong"),
        W("Кинджал", ThreatKind.Ballistic, 4500, @"кинджал|кинжал|kinzhal|" + Kh("47")),
        W("Орєшник", ThreatKind.Ballistic, 12000, @"орєшник|орешник|oreshnik"),
        W("Точка-У", ThreatKind.Ballistic, 3000, @"точк[аи][\s-]*у\b|tochka|" + Idx("9[мm]", "79")),
        W("С-300", ThreatKind.Ballistic, 6000, @"(?<![a-zа-яіїєґ0-9])[сc][\s-]?300(?!\d)|" + Idx("5[вv]", "55")),
        W("С-400", ThreatKind.Ballistic, 7000, @"(?<![a-zа-яіїєґ0-9])[сc][\s-]?400(?!\d)|" + Idx("48[нh]", "6")),
        W("РСЗВ «Торнадо-С»", ThreatKind.Ballistic, 3500, @"торнадо|tornado|смерч|" + Idx("9[мm]", "542")),
        W("РСЗВ «Ураган»", ThreatKind.Ballistic, 3000, @"\bураган|" + Idx("9[мm]", "27")),
        W("РСЗВ «Град»", ThreatKind.Ballistic, 2500, @"\bрсз[вo]\b|(?<![a-zа-яіїєґ])град(?:ом|у|а|и)?\b|" + Idx("9[мm]", "22")),
        W("Швидкісна ціль", ThreatKind.Ballistic, 4000, @"швидкісн\w*\s+ціл\w*|скоростн\w*\s+цел\w*"),
        W("Іскандер", ThreatKind.Ballistic, 7000, @"іскандер|искандер|iskander"),

        // ── Крилаті / протикорабельні / авіаційні ракети ──
        W("Циркон", ThreatKind.Missile, 7000, @"циркон|zircon|" + Idx("3[мm]", "22")),
        W("Онікс", ThreatKind.Missile, 2600, @"онікс|оникс|onyx|(?<![a-zа-яіїєґ0-9])[пp][\s-]?800(?!\d)"),
        W("Калібр", ThreatKind.Missile, 800, @"калібр|калибр|kalibr|" + Idx("3[мm]", "14")),
        W("Х-101", ThreatKind.Missile, 750, Kh("101")),
        W("Х-555", ThreatKind.Missile, 800, Kh("555")),
        W("Х-55", ThreatKind.Missile, 800, Kh("55")),
        W("Х-59", ThreatKind.Missile, 1000, Kh("59")),
        W("Х-69", ThreatKind.Missile, 1000, Kh("69")),
        W("Х-35", ThreatKind.Missile, 950, Kh("35")),
        W("Х-22", ThreatKind.Missile, 3500, Kh("22")),
        W("Х-32", ThreatKind.Missile, 4000, Kh("32")),
        W("Х-31", ThreatKind.Missile, 3600, Kh("31")),
        W("Х-29", ThreatKind.Missile, 2700, Kh("29")),
        W("Х-25", ThreatKind.Missile, 2700, Kh("25")),
        // НАР авіації — лише з дефісом, щоб «з 8 до 9» не стало ракетою.
        W("НАР С-8", ThreatKind.Missile, 2000, @"(?<![a-zа-яіїєґ0-9])[сc]-8(?!\d)"),
        W("НАР С-13", ThreatKind.Missile, 2000, @"(?<![a-zа-яіїєґ0-9])[сc]-13(?!\d)"),
        W("НАР С-24", ThreatKind.Missile, 2000, @"(?<![a-zа-яіїєґ0-9])[сc]-24(?!\d)"),

        // ── КАБ (керовані авіабомби) ──
        W("ФАБ-3000 з УМПК", ThreatKind.Kab, 600, @"фаб[\s-]?3000"),
        W("ФАБ-1500 з УМПК", ThreatKind.Kab, 600, @"фаб[\s-]?1500"),
        W("ФАБ-500 з УМПК", ThreatKind.Kab, 600, @"фаб[\s-]?500"),
        W("ФАБ-250 з УМПК", ThreatKind.Kab, 600, @"фаб[\s-]?250"),
        W("ОДАБ-500 з УМПК", ThreatKind.Kab, 600, @"одаб"),
        W("УМПБ Д-30СН", ThreatKind.Kab, 700, @"умпб|д[\s-]?30\s?сн"),

        // ── Ударні БпЛА / приманки ──
        W("реакт. шахед", ThreatKind.Drone, 500, @"герань[\s-]?3|shahed[\s-]?238|шахед[\s-]?238|реактивн|рбпла|\bреактив|шаболд"),
        W("Shahed-136", ThreatKind.Drone, 185, @"(?:shahed|шахед|шахід)[\s-]?136|(?:герань|geran)[\s-]?2"),
        W("Shahed-131", ThreatKind.Drone, 180, @"(?:shahed|шахед|шахід)[\s-]?131|(?:герань|geran)[\s-]?1(?!\d)"),
        W("Ланцет", ThreatKind.Drone, 110, @"ланцет|lancet"),
        W("КУБ-БЛА", ThreatKind.Drone, 130, @"куб[\s-]?бла|kub[\s-]?bla"),
        W("Гербера (приманка)", ThreatKind.Drone, 160, @"гербер|gerbera", decoy: true),
        W("Пародія (приманка)", ThreatKind.Drone, 160, @"пароді|пароди", decoy: true),
        W("Італмас", ThreatKind.Drone, 180, @"італмас|итальмас|italmas"),
        W("Молнія", ThreatKind.Drone, 120, @"молні[яї]|молни[яи]"),

        // ── Загальні класи без конкретної назви («~» — на карті лише тип) ──
        W("~шахед", ThreatKind.Drone, 185, @"шахед|шахід|shahed|геран|geran|мопед|бпла|дрон|\bfpv\b"),
        W("~балістика", ThreatKind.Ballistic, 4000, @"балісти|баллист"),
        W("~каб", ThreatKind.Kab, 600, @"(?<![a-zа-яіїєґ])каб(?:и|ів|ами|ом|а)?(?![a-zа-яіїєґ])|умпк|(?<![a-zа-яіїєґ])фаб"),
        W("~ракета", ThreatKind.Missile, 800, @"ракет"),

        // ── Загальні слова — найнижчий пріоритет ──
        // Сленг моніторів: «бандероль», «ціль» — крилата ракета.
        W("бандероль", ThreatKind.Missile, 800, @"бандерол"),
        W("крилата", ThreatKind.Missile, 800, @"крилат|крылат"),
        W("ціль", ThreatKind.Missile, 800, @"(?<![a-zа-яіїєґ])(?:ціл(?:ь|і|я|ей|ям|ями)|цел(?:ь|и|ей|ям|ями))(?![a-zа-яіїєґ])"),
    };

    /// Найспецифічніший засіб, названий у тексті, або null.
    public static Weapon? Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var w in All)
            if (w.Re.IsMatch(text)) return w;
        return null;
    }

    /// Швидкість за назвою боєприпасу (Ammo трекера) або null.
    public static double? SpeedFor(string? ammo) => Match(ammo)?.SpeedKmh;

    /// Назва для мітки на карті: загальні слова («ціль», «крилата», «бандероль»)
    /// не дублюємо як «Ракета (ціль)» — для них лишаємо лише тип.
    public static string? DisplayName(Weapon w) => w.Name switch
    {
        "ціль" or "крилата" => "крилата",
        _ when w.Name.StartsWith('~') => null,
        _ => w.Name,
    };
}
