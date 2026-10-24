using System.Text.RegularExpressions;

// ── WarAlarm · Киевский монитор ──────────────────────────────────────────────
// Под локальные каналы Киева (@nebo_raketa «Київський купол», @chyste_neboo «Світлячок»).
// Пользователь в Киеве — ГОСТЬ (без фиксированного микрорайона), поэтому логика проще
// харьковской: любая угроза городу = сигнал 🟠 (весь Киев). Пригород = 🟢, транзит = ▫️.
// Возвращает тот же KhEvent/KhSignal/KhTier, что и KharkivMonitor — общий вывод и Verdict().

public static class KyivMonitor
{
    static readonly (string stem, GeoRole role, string name)[] Gaz =
    {
        // ── районы и массивы Киева (для гостя всё это = «весь город») ────────
        ("оболонь", GeoRole.CityGeneral, "Оболонь"),      ("виноградар", GeoRole.CityGeneral, "Виноградар"),
        ("троєщин", GeoRole.CityGeneral, "Троєщина"),     ("троя", GeoRole.CityGeneral, "Троєщина"),
        ("позняк", GeoRole.CityGeneral, "Позняки"),       ("осокорк", GeoRole.CityGeneral, "Осокорки"),
        ("березняк", GeoRole.CityGeneral, "Березняки"),   ("печерськ", GeoRole.CityGeneral, "Печерськ"),
        ("печерск", GeoRole.CityGeneral, "Печерськ"),     ("голосіїв", GeoRole.CityGeneral, "Голосіїв"),
        ("солом", GeoRole.CityGeneral, "Солом'янка"),     ("святошин", GeoRole.CityGeneral, "Святошин"),
        ("борщагівк", GeoRole.CityGeneral, "Борщагівка"), ("деміївк", GeoRole.CityGeneral, "Деміївка"),
        ("видубич", GeoRole.CityGeneral, "Видубичі"),     ("звіринец", GeoRole.CityGeneral, "Звіринець"),
        ("лукʼянівк", GeoRole.CityGeneral, "Лук'янівка"), ("лук'янівк", GeoRole.CityGeneral, "Лук'янівка"),
        ("лукянівк", GeoRole.CityGeneral, "Лук'янівка"),  ("шулявк", GeoRole.CityGeneral, "Шулявка"),
        ("жуляни", GeoRole.CityGeneral, "Жуляни"),        ("почайна", GeoRole.CityGeneral, "Почайна"),
        ("лісовий масив", GeoRole.CityGeneral, "Лісовий масив"), ("харківський масив", GeoRole.CityGeneral, "Харківський масив"),
        ("нижні сади", GeoRole.CityGeneral, "Нижні Сади"),("русанівк", GeoRole.CityGeneral, "Русанівка"),
        ("куренівк", GeoRole.CityGeneral, "Куренівка"),   ("поділ", GeoRole.CityGeneral, "Поділ"),
        ("нивки", GeoRole.CityGeneral, "Нивки"),          ("теремки", GeoRole.CityGeneral, "Теремки"),
        ("биківн", GeoRole.CityGeneral, "Биківня"),       ("лівобереж", GeoRole.CityGeneral, "Лівий берег"),
        ("лівому берез", GeoRole.CityGeneral, "Лівий берег"), ("лівий берег", GeoRole.CityGeneral, "Лівий берег"),
        ("правому берез", GeoRole.CityGeneral, "Правий берег"), ("правий берег", GeoRole.CityGeneral, "Правий берег"),
        // ── ближний пригород/область Киева (наближається) ───────────────────
        ("бровар", GeoRole.Suburb, "Бровари"),      ("бориспіл", GeoRole.Suburb, "Бориспіль"),
        ("вишгород", GeoRole.Suburb, "Вишгород"),   ("ірпін", GeoRole.Suburb, "Ірпінь"),
        ("буча", GeoRole.Suburb, "Буча"),           ("гостомел", GeoRole.Suburb, "Гостомель"),
        ("вишневе", GeoRole.Suburb, "Вишневе"),     ("боярк", GeoRole.Suburb, "Боярка"),
        ("білогородк", GeoRole.Suburb, "Білогородка"), ("княжич", GeoRole.Suburb, "Княжичі"),
        ("бортнич", GeoRole.Suburb, "Бортничі"),    ("петрівц", GeoRole.Suburb, "Нові Петрівці"),
        ("фастів", GeoRole.Suburb, "Фастів"),       ("васильк", GeoRole.Suburb, "Васильків"),
        ("обухів", GeoRole.Suburb, "Обухів"),       ("переяслав", GeoRole.Suburb, "Переяслав"),
        ("конча-заспа", GeoRole.Suburb, "Конча-Заспа"), ("рембаза", GeoRole.Suburb, "Рембаза"),
        // ── дальние области (транзит целей к Киеву) ─────────────────────────
        ("чернігівщин", GeoRole.Far, "Чернігівщина"), ("черкащин", GeoRole.Far, "Черкащина"),
        ("житомирщин", GeoRole.Far, "Житомирщина"),   ("сумщин", GeoRole.Far, "Сумщина"),
        ("полтавщин", GeoRole.Far, "Полтавщина"),     ("ростов", GeoRole.Far, "Ростов"),
    };

    public static KhEvent Analyze(string post)
    {
        // выкидываем подпись канала «✅@chyste_neboo» и пустые строки
        var text = string.Join("\n", post.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(t => t.Length > 0 && !t.Contains("@chyste_neboo") && !t.StartsWith("✅@"))).Trim();
        var low = text.ToLowerInvariant();

        var (kind, extra) = DetectKind(low);
        bool ballistic = kind is ThreatKind.Ballistic or ThreatKind.Missile;
        bool hide = Regex.IsMatch(low, @"заховайт|не висовуй|над вами|на київ|у столиц");

        // Явный отбой («чисто», «відбій», «не фіксуються»…) — перебивает слово угрозы:
        // «чисто на реактивні» = чисто ОТ реактивных, это хорошая новость, а не тревога.
        // ИСКЛЮЧЕНИЕ — баллистика: её нельзя глушить (напр. «...але балістика актуальна»).
        bool strongClear = Regex.IsMatch(low,
            @"відбій|чисто|чиста|чисте|не фіксу|без фіксац|немає загроз|неактивн|нічого не лет|повертаємось до");
        if (strongClear && !ballistic)
            return new(KhSignal.Clear, ThreatKind.AllClear, Array.Empty<string>(), KhTier.Unknown, null, text);

        bool activeThreat = hide || ballistic || Regex.IsMatch(low,
            @"курс|летить|проривається|підліт|тривог|уважно|шахед|шахід|бпла|дрон|каб|реактивн");

        // Слабый отбой («мінус», «збили», «дорозвідка») — только если нет активной угрозы.
        if (!activeThreat && Regex.IsMatch(low, @"\bмінус\b|збили|побили|дорозвід"))
            return new(KhSignal.Clear, ThreatKind.AllClear, Array.Empty<string>(), KhTier.Unknown, null, text);

        var places = new List<(string name, KhTier tier)>();
        foreach (var (stem, role, name) in Gaz)
            if (low.Contains(stem))
            {
                var tier = role switch { GeoRole.CityGeneral => KhTier.CityWide, GeoRole.Suburb => KhTier.Suburb, _ => KhTier.FarOblast };
                if (!places.Any(p => p.name == name)) places.Add((name, tier));
            }

        // ничего похожего на угрозу — новость/шум
        if (!activeThreat && places.Count == 0)
            return new(KhSignal.News, ThreatKind.Unknown, Array.Empty<string>(), KhTier.Unknown, null, text);

        // длинный абзац-предупреждение без «ховайтеся» — это совет на потом, не сирена
        bool terse = text.Length <= 180 && text.Count(c => c == '\n') <= 2;
        if (!terse && !hide)
            return new(KhSignal.News, ThreatKind.Unknown, Array.Empty<string>(), KhTier.Unknown, null, text);

        bool cityKey = hide || Regex.IsMatch(low, @"\bкиїв|столиц|над містом");

        KhTier tierAll =
            cityKey || ballistic || places.Any(p => p.tier == KhTier.CityWide) ? KhTier.CityWide :
            places.Any(p => p.tier == KhTier.Suburb) ? KhTier.Suburb :
            places.Any(p => p.tier == KhTier.FarOblast) ? KhTier.FarOblast :
            KhTier.CityWide; // угроза без места на киевском канале — на всякий по городу

        var names = places.Select(p => p.name).Distinct().ToArray();
        return new(KhSignal.Threat, kind == ThreatKind.Unknown ? ThreatKind.Target : kind,
                   names, tierAll, extra ?? (ballistic ? "балістика" : null), text);
    }

    static (ThreatKind, string?) DetectKind(string l)
    {
        // Розвідники — своя мітка; решта — словник засобів (Х-101, Іскандер, «бандероль»
        // = крилата ракета, «ціль» = крилата, «швидкісна ціль» = балістика).
        if (Regex.IsMatch(l, @"мавік|mavic|орлан|зала|supercam|суперкам")) return (ThreatKind.Drone, "розвідник");
        if (Regex.IsMatch(l, @"розвід|дорозвід") && !Regex.IsMatch(l, @"ракет|балісти|каб")) return (ThreatKind.Drone, "розвідка");
        if (WeaponCatalog.Match(l) is { } w) return (w.Kind, WeaponCatalog.DisplayName(w));
        if (l.Contains("балісти") || l.Contains("баллист") || Regex.IsMatch(l, @"кн-?23|точка[-\s]?у")) return (ThreatKind.Ballistic, null);
        if (l.Contains("каб") || l.Contains("умпк") || l.Contains("умпб")) return (ThreatKind.Kab, null);
        if (l.Contains("реактивн") || l.Contains("шаболд") || l.Contains("молні")) return (ThreatKind.Drone, "реакт. шахед");
        if (l.Contains("гербер")) return (ThreatKind.Drone, "гербера-пастка");
        if (l.Contains("шахед") || l.Contains("шахід") || l.Contains("геран") || l.Contains("geran")) return (ThreatKind.Drone, "шахед");
        if (Regex.IsMatch(l, @"мавік|mavic|орлан|\bзала\b|supercam|суперкам")) return (ThreatKind.Drone, "розвідник");
        if (Regex.IsMatch(l, ThreatParser.DroneRe)) return (ThreatKind.Drone, null);
        if (l.Contains("ракет") || l.Contains("крилат") || l.Contains("кинджал") || l.Contains("іскандер") || l.Contains("онікс") || Regex.IsMatch(l, @"х[-\s]?\d{2,3}|с[-\s]?300")) return (ThreatKind.Missile, null);
        if (Regex.IsMatch(l, @"розвід|дорозвід")) return (ThreatKind.Drone, "розвідка");
        return (ThreatKind.Unknown, null);
    }
}
