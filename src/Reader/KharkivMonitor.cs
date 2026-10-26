using System.Text.RegularExpressions;

// ── WarAlarm · Харьковский монитор ───────────────────────────────────────────
// Заточен под ЛОКАЛЬНЫЕ каналы Харькова (@kharkivlife, @tlknewsua, @truexakharkiv…).
// В отличие от всеукраинского ThreatParser даёт три вещи:
//   1) статус тревоги по ГОРОДУ (вкл/выкл) — чистый сигнал из @kharkivlife;
//   2) тип угрозы с местным сленгом (Шаболда, Бандероль, Молнія, Петухи, FPV…);
//   3) к какому месту летит и НАСКОЛЬКО это близко к ТВОЕМУ району.

public enum KhSignal { AlarmOn, AlarmOff, Clear, Threat, News }

// Насколько сигнал близок к пользователю (для его личной тревоги). Меньше = ближе.
public enum KhTier { MyArea, CityWide, OtherDistrict, Suburb, FarOblast, Unknown }

enum GeoRole { CityDistrict, CityGeneral, Suburb, Far }

public record KhEvent(
    KhSignal Signal,
    ThreatKind Kind,
    IReadOnlyList<string> Places,
    KhTier Tier,
    string? Extra,
    string Raw)
{
    // Будить пользователя? Полноценная тревога — свой район или весь город.
    public bool Alarm => Signal == KhSignal.AlarmOn
                      || (Signal == KhSignal.Threat && (Tier == KhTier.MyArea || Tier == KhTier.CityWide));
    // Предупреждение (не будим, но показываем): рядом — соседний район/пригород.
    public bool PreAlert => Signal == KhSignal.Threat && (Tier == KhTier.OtherDistrict || Tier == KhTier.Suburb);
}

public static class KharkivMonitor
{
    // Явный мусор: реклама, донаты, подписки, «доброе утро», погода, служебное.
    static readonly string[] Noise =
    {
        "#реклама", "підписатися на", "купуємо контент", "надіслати новину", "tlkinst",
        "стрімами", "доброго ранку", "доброго субот", "синоптик", "ventusky", "спека",
        "стипенді", "освітні програми", "боост", "boost", "premium", "менеджеру",
    };

    // Справочник географии. stem (нижний регистр) → роль + каноническое имя.
    // Микрорайоны/шоссе сводим к «своему» административному району города.
    // Мікрорайон/масив Харкова → його адмінрайон (для визначення тиру: «твій/інший район»).
    static readonly Dictionary<string, string> MicroRaion = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Салтівка"] = "Салтівський", ["Північна Салтівка"] = "Салтівський",
        ["Горизонт"] = "Салтівський", ["Велика Данилівка"] = "Салтівський",
        ["Олексіївка"] = "Шевченківський", ["П'ятихатки"] = "Шевченківський",
        ["Павлове Поле"] = "Шевченківський", ["Шатилівка"] = "Шевченківський",
        ["Основа"] = "Основ'янський", ["Нові Дома"] = "Основ'янський",
        ["Левада"] = "Основ'янський", ["Москалівка"] = "Основ'янський", ["Одеська"] = "Основ'янський",
        ["Журавлівка"] = "Київський", ["Сокольники"] = "Київський",
        ["Тюрінка"] = "Київський", ["Жуковського"] = "Київський",
        ["Холодна Гора"] = "Холодногірський", ["Залютине"] = "Холодногірський",
        ["Лиса Гора"] = "Немишлянський", ["Баварія"] = "Новобаварський",
        ["ХТЗ"] = "Індустріальний", ["Рогань"] = "Індустріальний",
    };

    static readonly (string stem, GeoRole role, string name)[] Gaz =
    {
        // ── масиви Салтівського (дім) ───────────────────────────────────────
        ("салтівське шосе", GeoRole.CityDistrict, "Салтівка"),
        ("салтівськ",       GeoRole.CityDistrict, "Салтівка"),
        ("салтівк",         GeoRole.CityDistrict, "Салтівка"), // Салтівка/Салтівки/Салтівку
        ("салтівц",         GeoRole.CityDistrict, "Салтівка"), // «на Салтівці» (місцевий відмінок)
        ("салтовк",         GeoRole.CityDistrict, "Салтівка"),
        ("північна салтівка",GeoRole.CityDistrict,"Північна Салтівка"),
        ("горизонт",        GeoRole.CityDistrict, "Горизонт"),
        ("велика данилівк", GeoRole.CityDistrict, "Велика Данилівка"),
        ("большая данилов", GeoRole.CityDistrict, "Велика Данилівка"),
        // ── Шевченківський ──────────────────────────────────────────────────
        ("шевченківськ",    GeoRole.CityDistrict, "Шевченківський"),
        ("олексіївк",       GeoRole.CityDistrict, "Олексіївка"),
        ("олексеевк",       GeoRole.CityDistrict, "Олексіївка"),
        ("п'ятихатк",       GeoRole.CityDistrict, "П'ятихатки"),
        ("пятихатк",        GeoRole.CityDistrict, "П'ятихатки"),
        ("павлове поле",    GeoRole.CityDistrict, "Павлове Поле"),
        ("павлово поле",    GeoRole.CityDistrict, "Павлове Поле"),
        ("шатилівк",        GeoRole.CityDistrict, "Шатилівка"),
        ("шатиловк",        GeoRole.CityDistrict, "Шатилівка"),
        // ── Основ'янський ───────────────────────────────────────────────────
        ("основ",           GeoRole.CityDistrict, "Основа"),
        ("сортув",          GeoRole.CityDistrict, "Основа"),
        ("нові дома",       GeoRole.CityDistrict, "Нові Дома"),
        ("новые дома",      GeoRole.CityDistrict, "Нові Дома"),
        ("левад",           GeoRole.CityDistrict, "Левада"),
        ("москалівк",       GeoRole.CityDistrict, "Москалівка"),
        ("москалевк",       GeoRole.CityDistrict, "Москалівка"),
        ("одеська",         GeoRole.CityDistrict, "Одеська"),
        // ── Київський ───────────────────────────────────────────────────────
        ("київському районі",GeoRole.CityDistrict,"Київський"),
        ("журавл",          GeoRole.CityDistrict, "Журавлівка"),
        ("сокольник",       GeoRole.CityDistrict, "Сокольники"),
        ("тюрінк",          GeoRole.CityDistrict, "Тюрінка"),
        ("тюринк",          GeoRole.CityDistrict, "Тюрінка"),
        ("жуковськ",        GeoRole.CityDistrict, "Жуковського"),
        ("жуковск",         GeoRole.CityDistrict, "Жуковського"),
        // ── Холодногірський / Новобаварський ────────────────────────────────
        ("холодногір",      GeoRole.CityDistrict, "Холодна Гора"),
        ("холодна гора",    GeoRole.CityDistrict, "Холодна Гора"),
        ("залютин",         GeoRole.CityDistrict, "Залютине"),
        ("новобаварськ",    GeoRole.CityDistrict, "Новобаварський"),
        ("баварі",          GeoRole.CityDistrict, "Баварія"),
        // ── Немишлянський / Індустріальний ──────────────────────────────────
        ("немишл",          GeoRole.CityDistrict, "Немишлянський"),
        ("немышл",          GeoRole.CityDistrict, "Немишлянський"),
        ("лиса гора",       GeoRole.CityDistrict, "Лиса Гора"),
        ("лысая гора",      GeoRole.CityDistrict, "Лиса Гора"),
        ("слобідськ",       GeoRole.CityDistrict, "Слобідський"),
        ("індустріальн",    GeoRole.CityDistrict, "Індустріальний"),
        ("хтз",             GeoRole.CityDistrict, "ХТЗ"),
        ("рогань",          GeoRole.CityDistrict, "Рогань"),
        // ── общегородское (весь Харьков) ────────────────────────────────────
        ("на харків",       GeoRole.CityGeneral, "Харків"),
        ("над містом",      GeoRole.CityGeneral, "Харків"),
        ("по харків",       GeoRole.CityGeneral, "Харків"),
        ("атакує харків",   GeoRole.CityGeneral, "Харків"),
        ("на місто",        GeoRole.CityGeneral, "Харків"),
        ("по місту",        GeoRole.CityGeneral, "Харків"),
        ("передмістя",      GeoRole.CityGeneral, "передмістя"),
        // ── ближний пригород (0–25 км) ──────────────────────────────────────
        ("дергач",  GeoRole.Suburb, "Дергачі"),   ("циркун", GeoRole.Suburb, "Циркуни"),
        ("кулинич", GeoRole.Suburb, "Кулиничі"),  ("васищ",  GeoRole.Suburb, "Васищеве"),
        ("безлюд",  GeoRole.Suburb, "Безлюдівка"),("слатин", GeoRole.Suburb, "Слатине"),
        ("цуповк",  GeoRole.Suburb, "Цуповка"),   ("прудянк",GeoRole.Suburb, "Прудянка"),
        ("питомник",GeoRole.Suburb, "Питомник"),  ("пітомник",GeoRole.Suburb,"Питомник"),
        ("козач",   GeoRole.Suburb, "Козача Лопань"),("жуки",GeoRole.Suburb, "Жуки"),
        ("вільхівк",GeoRole.Suburb, "Вільхівка"), ("кутузівк",GeoRole.Suburb,"Кутузівка"),
        ("лісне",   GeoRole.Suburb, "Лісне"),     ("черноглаз",GeoRole.Suburb,"Черноглазівка"),
        // ── дальняя область (>40 км) ────────────────────────────────────────
        ("ізюм", GeoRole.Far, "Ізюм"),      ("куп'янськ", GeoRole.Far, "Куп'янськ"),
        ("купянськ", GeoRole.Far, "Куп'янськ"),("лозов", GeoRole.Far, "Лозова"),
        ("барвінков", GeoRole.Far, "Барвінкове"),("барвенков", GeoRole.Far, "Барвінкове"),
        ("богодух", GeoRole.Far, "Богодухів"),("валки", GeoRole.Far, "Валки"),
        ("золоч", GeoRole.Far, "Золочів"),   ("балакл", GeoRole.Far, "Балаклія"),
        ("зміїв", GeoRole.Far, "Зміїв"),     ("змиев", GeoRole.Far, "Зміїв"),
        ("коломак", GeoRole.Far, "Коломак"), ("зачепилівк", GeoRole.Far, "Зачепилівка"),
        ("краснопавлівк", GeoRole.Far, "Краснопавлівка"),("орельк", GeoRole.Far, "Орілька"),
        ("сахновщин", GeoRole.Far, "Сахновщина"),("близнюк", GeoRole.Far, "Близнюки"),
        ("андреевк", GeoRole.Far, "Андріївка"),("андріївк", GeoRole.Far, "Андріївка"),
        ("бурлук", GeoRole.Far, "Бурлук"),   ("приколотн", GeoRole.Far, "Приколотне"),
        ("колодец", GeoRole.Far, "Вел. Бурлук"),("первомайск", GeoRole.Far, "Первомайський"),
        ("шалигине", GeoRole.Far, "Шалигине"),("малих проход", GeoRole.Far, "Малі Проходи"),
    };

    // ── главный разбор одного поста ──────────────────────────────────────────
    public static KhEvent Analyze(string post, string myDistrict)
    {
        var text = post.Replace("\r", "").Trim();
        var low = text.ToLowerInvariant();

        // 1) Тревога/отбой по городу — самый чистый сигнал (@kharkivlife, times).
        if (low.Contains("повітряна тривога") || low.Contains("оголошена повітряна"))
        {
            var (kind0, extra0) = DetectKind(low);
            var places0 = MatchPlaces(text, low);
            var tier0 = places0.Any(p => p.tier == KhTier.MyArea) ? KhTier.MyArea : KhTier.CityWide;
            return new(KhSignal.AlarmOn, kind0, Names(places0), tier0, extra0 ?? BallistExtra(low), text);
        }
        if (low.Contains("відбій тривоги"))
            return new(KhSignal.AlarmOff, ThreatKind.AllClear, Array.Empty<string>(), KhTier.CityWide, "по місту", text);

        // 2) Статус «чисто/отбой/не наблюдается/упал/сбили/без угрозы».
        if (Regex.IsMatch(low, @"\bвідбій\b|\bотбой\b|не наблюда|не фіксує|\bупал\b|\bвпав\b|збили|сбили|без угроз|без загроз"))
            return new(KhSignal.Clear, ThreatKind.AllClear, Array.Empty<string>(), KhTier.Unknown, null, text);

        // 3) Явный мусор — реклама/новости/погода.
        if (Noise.Any(n => low.Contains(n)))
            return new(KhSignal.News, ThreatKind.Unknown, Array.Empty<string>(), KhTier.Unknown, null, text);

        // 4) Живая угроза: тип + места. Длинные «постфактум»-абзацы приглушаем.
        var (kind, extra) = DetectKind(low);
        var places = MatchPlaces(text, low);
        bool hasThreatWord = kind != ThreatKind.Unknown
                          || Regex.IsMatch(low, @"вибух|влучанн|приліт|ударн|бандерол|курс|загроз|пуск|далее|далі|летят|летить|заход|кружит|кружля");
        bool hasImpact = Regex.IsMatch(low, @"вибух|влучанн|приліт");        // подтверждённое событие
        bool terse = text.Length <= 160 && text.Count(c => c == '\n') <= 2;

        // Длинный абзац пропускаем только если это подтверждённый прилёт (не просто новость).
        if (hasThreatWord && (places.Count > 0 || kind != ThreatKind.Unknown) && (terse || hasImpact))
        {
            var tier = places.Count > 0 ? places.Min(p => p.tier) : KhTier.CityWide;
            // если район назван, но это НЕ твой — понижаем с MyArea только для чужих
            return new(KhSignal.Threat, kind == ThreatKind.Unknown ? ThreatKind.Target : kind,
                       Names(places), tier, extra ?? BallistExtra(low), text);
        }

        return new(KhSignal.News, ThreatKind.Unknown, Array.Empty<string>(), KhTier.Unknown, null, text);

        // локальный помощник: тир зависит от myDistrict
        List<(string name, KhTier tier)> MatchPlaces(string src, string lower)
        {
            var found = new List<(string name, KhTier tier)>();
            void Add(string name, GeoRole role)
            {
                // Мікрорайон → його район (для тиру). «Салтівка» → «Салтівський» = твій дім.
                var raion = MicroRaion.GetValueOrDefault(name, name);
                var tier = role switch
                {
                    GeoRole.CityDistrict => string.Equals(raion, myDistrict, StringComparison.OrdinalIgnoreCase)
                                             ? KhTier.MyArea : KhTier.OtherDistrict,
                    GeoRole.CityGeneral => KhTier.CityWide,
                    GeoRole.Suburb      => KhTier.Suburb,
                    _                   => KhTier.FarOblast,
                };
                if (!found.Any(f => f.name == name)) found.Add((name, tier));
            }
            foreach (var (stem, role, name) in Gaz)
                if (lower.Contains(stem)) Add(name, role);
            // короткие аббревиатуры — по границам слова, чтобы не ловить мусор
            if (Regex.IsMatch(src, @"\bНД\b")) Add("Основ'янський", GeoRole.CityDistrict); // Нові Дома
            if (Regex.IsMatch(src, @"\bСС\b")) Add("Салтівський", GeoRole.CityDistrict);   // Півн. Салтівка
            return found;
        }
    }

    static string[] Names(List<(string name, KhTier tier)> p) => p.Select(x => x.name).Distinct().ToArray();

    static string? BallistExtra(string low) =>
        low.Contains("балісти") || low.Contains("баллист")
            ? "балістика" + (Regex.Match(low, @"з\s+([a-zа-яіїєґ .]+)").Groups[1].Value is { Length: > 0 } o ? $" ({o.Trim()})" : "")
            : null;

    // Тип угрозы + уточнение боеприпаса (местный сленг Харькова).
    static (ThreatKind, string?) DetectKind(string l)
    {
        // Розвідники й FPV — свої мітки; решта — словник засобів (Х-101, Іскандер, «бандероль»
        // = крилата ракета, «ціль» = крилата, «швидкісна ціль» = балістика).
        if (Regex.IsMatch(l, @"розвід|развед|мавік|mavic|орлан|\bзала\b|supercam|суперкам")) return (ThreatKind.Drone, "розвідувальний");
        if (l.Contains("fpv")) return (ThreatKind.Drone, "FPV");
        if (l.Contains("петух")) return (ThreatKind.Aviation, "пуски КАБ");
        if (WeaponCatalog.Match(l) is { } w) return (w.Kind, WeaponCatalog.DisplayName(w));
        if (l.Contains("каб") || l.Contains("умпк") || l.Contains("умпб")) return (ThreatKind.Kab, null);
        if (l.Contains("балісти") || l.Contains("баллист") || Regex.IsMatch(l, @"кн-?23|точка[-\s]?у")) return (ThreatKind.Ballistic, null);
        if (l.Contains("молні")) return (ThreatKind.Drone, "Молнія");
        if (l.Contains("шаболд")) return (ThreatKind.Drone, "реакт. шахед");
        if (l.Contains("fpv")) return (ThreatKind.Drone, "FPV");
        if (l.Contains("гербер")) return (ThreatKind.Drone, "гербера-пастка");
        if (l.Contains("шахед") || l.Contains("шахід") || l.Contains("геран") || l.Contains("geran")) return (ThreatKind.Drone, "шахед");
        if (Regex.IsMatch(l, @"розвід|развед|мавік|mavic|орлан|\bзала\b|supercam|суперкам")) return (ThreatKind.Drone, "розвідувальний");
        if (Regex.IsMatch(l, ThreatParser.DroneRe)) return (ThreatKind.Drone, null);
        if (l.Contains("ракет") || l.Contains("крилат") || l.Contains("кинджал") || l.Contains("іскандер") || l.Contains("циркон") || l.Contains("онікс") || Regex.IsMatch(l, @"х[-\s]?\d{2,3}|с[-\s]?300")) return (ThreatKind.Missile, null);
        if (l.Contains("петух") || Regex.IsMatch(l, @"(су|ту|міг)[-\s]?\d")) return (ThreatKind.Aviation, "пуски КАБ");
        return (ThreatKind.Unknown, null);
    }

    // ── человекочитаемый вердикт для пользователя ────────────────────────────
    public static (string emoji, string label) Verdict(KhEvent e) => e.Signal switch
    {
        KhSignal.AlarmOn when e.Tier == KhTier.MyArea => ("🔴", "ТРИВОГА · твій район"),
        KhSignal.AlarmOn                              => ("🟠", "ТРИВОГА по місту"),
        KhSignal.AlarmOff                             => ("📴", "відбій по місту"),
        KhSignal.Clear                                => ("✅", "чисто / не спостерігається"),
        KhSignal.Threat => e.Tier switch
        {
            KhTier.MyArea       => ("🔴", "загроза у ТВОЄМУ районі"),
            KhTier.CityWide     => ("🟠", "загроза по місту"),
            KhTier.OtherDistrict=> ("🟡", "інший район міста"),
            KhTier.Suburb       => ("🟢", "пригород — наближається"),
            KhTier.FarOblast    => ("▫️", "область, далеко"),
            _                   => ("•", "невизначено"),
        },
        _ => ("·", "новина/шум"),
    };
}
