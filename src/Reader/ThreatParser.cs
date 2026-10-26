using System.Text.RegularExpressions;

// ── WarAlarm · разбор целей из постов Telegram-канала ─────────────────────────
// Превращает свободный текст поста в список структурированных угроз (ThreatEvent).
// Пишется под формат @raketa_trevoga; легко расширяется на другие каналы.

// Fpv — ФПВ / Ланцет / Молнія / «дронова загроза» біля фронту: окремо від шахедів
// (короткий радіус, не летять на Київ, але небезпечні прифронтовим громадам).
public enum ThreatKind { Drone, Kab, Ballistic, Missile, Aviation, Target, AllClear, Unknown, Fpv }

public enum RelLevel { None, Oblast, City }

/// <param name="From">Звідки летить («з Курська», «з Чернігівщини»), якщо названо.</param>
public record ThreatEvent(ThreatKind Kind, string? Oblast, string? City, int Count, string? Extra, string Raw, string? From = null)
{
    public Altitude.Info? Alt { get; init; } // висота / «падає» з рядка поста (Altitude.Parse)
    // «БпЛА на півночі Сумщини, курсом на Полтаву»: ціль у ЧАСТИНІ області (Oblast + Side),
    // а Toward — лише куди летить. City тоді null — це не «ціль над Полтавою».
    public string? Toward { get; init; }
    public string? Side { get; init; }
}

public static class Geo
{
    // Разговорное «-щина/область» → каноническое название области.
    public static readonly Dictionary<string, string> Oblasts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["вінниччина"] = "Вінницька", ["волинь"] = "Волинська", ["дніпропетровщина"] = "Дніпропетровська",
        ["донеччина"] = "Донецька", ["житомирщина"] = "Житомирська", ["закарпаття"] = "Закарпатська",
        ["запоріжжя"] = "Запорізька", ["івано-франківщина"] = "Івано-Франківська", ["прикарпаття"] = "Івано-Франківська",
        ["київщина"] = "Київська", ["кіровоградщина"] = "Кіровоградська", ["луганщина"] = "Луганська",
        ["львівщина"] = "Львівська", ["миколаївщина"] = "Миколаївська", ["одещина"] = "Одеська",
        ["полтавщина"] = "Полтавська", ["рівненщина"] = "Рівненська", ["сумщина"] = "Сумська",
        ["тернопільщина"] = "Тернопільська", ["харківщина"] = "Харківська", ["херсонщина"] = "Херсонська",
        ["хмельниччина"] = "Хмельницька", ["черкащина"] = "Черкаська", ["чернігівщина"] = "Чернігівська",
        ["чернівеччина"] = "Чернівецька", ["буковина"] = "Чернівецька",
    };

    public static string? Canonical(string s)
    {
        s = s.Trim().TrimEnd(':').Trim().ToLowerInvariant();
        if (Oblasts.TryGetValue(s, out var v)) return v;
        // Відмінки: «Сумщини», «на Сумщині», «Вінниччину» → «сумщина» / «вінниччина».
        var nom = Regex.Replace(s, @"(щ|чч)(ини|ині|ину|иною)$", "$1ина");
        return nom != s && Oblasts.TryGetValue(nom, out var v2) ? v2 : null;
    }
}

public static class ThreatParser
{
    // Усі ходові назви дронів/шахедів/приманок/розвідників (сленг каналів) — ОДНЕ джерело
    // правди для всіх трьох парсерів (ThreatParser / KyivMonitor / KharkivMonitor).
    // шахед · герань(geran) · гербера-пастка · бандероль · мопед · FPV · молнія · шаболда ·
    // ланцет · мавік · орлан · зала · supercam.
    public const string DroneRe =
        @"бпла|дрон|шахед|шахід|shahed|італмас|italmas|геран|geran|гербер|мопед|\bfpv\b|молні|шаболд|ланцет|мавік|mavic|орлан|\bзала\b|supercam|суперкам";

    // Строки-шум: подписки, ссылки, донаты, комментарии к фото, новости о прошлых ударах.
    static readonly string[] Noise =
    {
        "підписат", "підтримати", "t.me/", "http", "більше фото", "стежити за обстановк",
        "ситуація по", "видно вам", "буду вдячний", "monobank", "попередньо", "завдали удару"
    };

    // Заголовок-місто замість області («Київ:», «Харків:») — контекст його області.
    static readonly Dictionary<string, string> CityHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["київ"] = "Київська", ["харків"] = "Харківська", ["одеса"] = "Одеська", ["дніпро"] = "Дніпропетровська",
        ["запоріжжя"] = "Запорізька", ["миколаїв"] = "Миколаївська", ["суми"] = "Сумська", ["чернігів"] = "Чернігівська",
        ["полтава"] = "Полтавська", ["херсон"] = "Херсонська", ["черкаси"] = "Черкаська", ["львів"] = "Львівська",
    };

    /// <param name="defaultOblast">Контекст області каналу (регіональний монітор пише
    /// «на Бровари» без заголовка) — стартове значення, заголовки в пості його міняють.</param>
    public static List<ThreatEvent> Parse(string post, string? defaultOblast = null)
    {
        var events = new List<ThreatEvent>();
        string? oblast = defaultOblast; // текущий контекст области (из заголовка "Xщина:")
        int prevMark = 0, mark = 0;
        Altitude.Info? alt = null;
        void Stamp() => StampAltitude(events, prevMark, mark, alt);

        foreach (var raw in post.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            Stamp();
            if (events.Count > mark) prevMark = mark;
            mark = events.Count;
            alt = Altitude.Parse(line);
            var low = line.ToLowerInvariant();
            if (Noise.Any(n => low.Contains(n))) continue;

            // Отбой
            if (low.Contains("відбій") || low == "чисто" || low.StartsWith("чисто") || Regex.IsMatch(low, @"^\S+щин\w*\s*[:\-—–]\s*чисто"))
            {
                // Відбій по конкретній області («Відбій по Київщині», «Сумщина — чисто») → лише її.
                // Без області — загальний відбій по країні (oblast = null).
                var clr = Geo.Oblasts.FirstOrDefault(kv => low.Contains(kv.Key[..^1])).Value ?? oblast;
                if (low.Contains("відбій") && !Regex.IsMatch(low, @"щин|област")) clr = null; // «відбій» без області — загальний
                events.Add(new(ThreatKind.AllClear, clr, null, 0, low.Contains("балісти") ? "балістика" : null, line));
                continue;
            }
            // Балистическая угроза (по направлению, без города)
            // Балістика: «з Курська» — район пуску, «на Харків» — ціль (якщо названо).
            if (low.Contains("балісти"))
            {
                var tl = ParseTargetLine(line, oblast);
                var bw = WeaponCatalog.Match(line);
                events.Add(new(ThreatKind.Ballistic, oblast, tl?.City, tl?.Count ?? 0,
                    bw is { } bww ? WeaponCatalog.DisplayName(bww) : null, line, FromOf(line)));
                continue;
            }
            // Авиация
            if (low.Contains("актив") && Regex.IsMatch(line, @"(су|ту|міг|mig)[-\s]?\d", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(line, @"((су|ту|міг|mig)[-\s]?\d+)", RegexOptions.IgnoreCase);
                events.Add(new(ThreatKind.Aviation, null, null, 0, m.Value, line));
                continue;
            }
            // Заголовок-місто з двокрапкою: «Київ:» → область міста.
            if (Regex.Match(line, @"^([А-ЯІЇЄҐа-яіїєґ'ʼ’-]+)\s*:\s*$") is { Success: true } hc
                && CityHeaders.TryGetValue(hc.Groups[1].Value, out var hob))
            { oblast = hob; continue; }
            // Заголовок области: "Сумщина:" ИЛИ просто "Сумщина" (без двоеточия, как в реальных постах).
            if (!Regex.IsMatch(low, @"\bна\b"))
            {
                var c = Geo.Canonical(line);
                if (c != null) { oblast = c; continue; }
            }
            // Инлайн "Область - <...> на Місто"  либо  "Місто - обережно"
            if (Regex.IsMatch(line, @"\s[-—–]\s"))
            {
                var parts = Regex.Split(line, @"\s+[-—–]\s+");
                if (parts.Length >= 2)
                {
                    var left = parts[0].Trim();
                    var right = string.Join(" - ", parts.Skip(1)).Trim();
                    var c = Geo.Canonical(left);
                    if (c != null)
                    {
                        var ev = ParseTargetLine(right, c);
                        if (ev != null) { events.Add(ev); continue; }
                    }
                    if (Regex.IsMatch(right, "обережно|увага|небезпе", RegexOptions.IgnoreCase))
                    {
                        events.Add(new(ThreatKind.Target, oblast, left, 1, "обережно", line));
                        continue;
                    }
                }
            }
            // Инлайн-формат @rozvidkaneba: "Xщина: N× БпЛА на A, M× БпЛА на B, ..."
            // (несколько целей в одной строке через запятую, с префиксом области).
            var colon = line.IndexOf(':');
            string body = line;
            bool pref = false;
            if (colon > 0 && Geo.Canonical(line[..colon]) is { } oc)
            {
                oblast = oc;
                body = line[(colon + 1)..].Trim();
                pref = true;
            }
            // «на півночі Сумщини, курсом на Полтаву» — одна ціль, кома тут не розділяє цілі.
            if (ParseTargetLine(body, oblast) is { Toward: not null } whole) { events.Add(whole); continue; }
            if (pref || body.Contains(','))
            {
                bool any = false;
                foreach (var seg in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var te = ParseTargetLine(seg.Trim().TrimEnd('.', ';', ' '), oblast);
                    if (te != null) { events.Add(te); any = true; }
                }
                if (any || pref) continue; // цель(и) разобраны ИЛИ это чистый заголовок области
            }
            // Обычная строка цели: "[N] ТИП на Місто"
            var t = ParseTargetLine(line, oblast);
            if (t != null) events.Add(t);
        }
        Stamp();

        return events;
    }

    // Висота з рядка («…, висота ~150 м», «знижується») — на всі цілі цього рядка; рядок
    // лише з висотою («висота 100 м» окремо) — на цілі попереднього рядка.
    static void StampAltitude(List<ThreatEvent> events, int prevMark, int mark, Altitude.Info? alt)
    {
        if (alt is not { } a) return;
        int from = events.Count > mark ? mark : prevMark;
        for (int i = from; i < events.Count; i++)
            if (events[i].Kind != ThreatKind.AllClear) events[i] = events[i] with { Alt = a };
    }

    static ThreatEvent? ParseTargetLine(string line, string? oblast)
    {
        int count = 1;
        // Кол-во в начале строки: «3 БпЛА» и множитель «2х/2x/2×» (формат @rozvidkaneba).
        var mc = Regex.Match(line, @"^\s*(?:(?:і\s+)?ще\s+|\+\s*)?(\d+)\s*[хx×]?\s*(?=\S)", RegexOptions.IgnoreCase); // «Ще 2 балістики», «+3»
        if (mc.Success) int.TryParse(mc.Groups[1].Value, out count);

        // Конкретний засіб зі словника («Х-101», «Іскандер-М», «реактивний», «ціль»…) —
        // і тип, і назва на карті, і швидкість для ETA. Інакше — загальне визначення.
        var weapon = WeaponCatalog.Match(line);
        var kind = weapon?.Kind ?? DetectKind(line);
        string? extra = weapon is { } w ? WeaponCatalog.DisplayName(w) : null;

        // ПС ЗСУ / агрегатори: «БпЛА на півночі Сумщини, курсом на Полтаву», «з Сумщини курсом
        // на Полтаву». Ціль — у названій частині області; місто після «курсом на» — лише
        // напрямок. Раніше ціль ставили НА Полтаву, хоча вона ще над Сумщиною.
        var course = Regex.Match(line, @"курс(?:ом)?\s*(?:на|—|–|-|:)?\s*([А-ЯІЇЄҐ][\w'’ʼ\-]+(?:\s+[А-ЯІЇЄҐ][\w'’ʼ\-]+)?)");
        var where = Regex.Match(line,
            @"\b(?:на|в|у|з|із|зі|над)\s+(?:(півночі|півдні|сході|заході|центрі|північному\s+сході|північному\s+заході|південному\s+сході|південному\s+заході|північному-сході|південному-сході)\s+)?([\w'’ʼ\-]+(?:щин|ччин)\w*)",
            RegexOptions.IgnoreCase);
        if (course.Success && where.Success && where.Index < course.Index && Geo.Canonical(where.Groups[2].Value) is { } inObl)
            return new(kind == ThreatKind.Unknown ? ThreatKind.Target : kind, inObl, null, count, extra, line, FromOf(line))
            { Toward = course.Groups[1].Value.Trim(), Side = where.Groups[1].Success ? where.Groups[1].Value : null };

        var mn = Regex.Match(line, @"\bна\s+(.+)$", RegexOptions.IgnoreCase);
        // Інші звороти регіональних моніторів: «курс Куликівка», «в районі Ірпеня»,
        // «над Борисполем», «у бік Білої Церкви», «через Ананьїв».
        if (!mn.Success)
            mn = Regex.Match(line,
                @"\b(?:курсом|курс|над|біля|поблизу|в\s+районі|у\s+районі|в\s+напрямку|у\s+напрямку|напрямок|в\s+бік|у\s+бік|в\s+сторону|через|повз|по(?=\s+(?-i:[А-ЯІЇЄҐ])))\s+(.+)$",
                RegexOptions.IgnoreCase);
        string? city = mn.Success ? mn.Groups[1].Value.Trim().TrimEnd('.', ' ', '!') : null;
        // Отсекаем «хвост» после названия: «на Васильків повз Борову», «на X у напрямку Y»,
        // «на Бровари з півночі», «на Вишгород/Київ»…
        if (city != null)
            city = Regex.Split(city,
                @"\s+(?:повз|через|у напрямку|в напрямку|курс|напрямок|з півночі|з півдня|зі сходу|із заходу|низько|приблизно|знову)\b|/",
                RegexOptions.IgnoreCase)[0].Trim();

        if (city == null && kind == ThreatKind.Unknown) return null; // не похоже на цель
        return new(kind == ThreatKind.Unknown ? ThreatKind.Target : kind, oblast, city, count, extra, line, FromOf(line));
    }

    /// Звідки летить: «з Курська», «із Чернігівщини», «від Куликівки», «з акваторії Чорного моря».
    /// Напрямки з малої («з півночі») — не місце, тому пропускаємо.
    public static string? FromOf(string line)
    {
        var m = Regex.Match(line, @"(?:^|[\s(])(?:з|із|зі|від)\s+((?-i:[А-ЯІЇЄҐ])[\w'ʼ’-]+(?:\s+(?-i:[А-ЯІЇЄҐ])[\w'ʼ’-]+)?)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        return LaunchSites.FromText(line) is { } site && Regex.IsMatch(line, @"\b(?:з|із)\s+\w", RegexOptions.IgnoreCase) ? site.name : null;
    }

    static ThreatKind DetectKind(string line)
    {
        var l = line.ToLowerInvariant();
        if (Regex.IsMatch(l, DroneRe)) return ThreatKind.Drone;
        if (l.Contains("каб") || l.Contains("умпк") || l.Contains("умпб") || Regex.IsMatch(l, @"фаб[-\s]?\d")) return ThreatKind.Kab;
        if (Regex.IsMatch(l, @"балісти|кн-?23|точка[-\s]?у")) return ThreatKind.Ballistic;
        if (Regex.IsMatch(l, @"ракет|крилат|калібр|кинджал|іскандер|циркон|онікс|onyx|х[-\s]?\d{2,3}|с[-\s]?300")) return ThreatKind.Missile;
        return WeaponCatalog.Match(l)?.Kind ?? ThreatKind.Unknown;
    }

    // ── помощники для демонстрации/фильтра ───────────────────────────────────
    public static string Icon(ThreatKind k) => k switch
    {
        ThreatKind.Drone => "🛩",
        ThreatKind.Fpv => "🪖",
        ThreatKind.Kab => "💣",
        ThreatKind.Ballistic => "🚀",
        ThreatKind.Missile => "🚀",
        ThreatKind.Aviation => "✈",
        ThreatKind.Target => "🎯",
        ThreatKind.AllClear => "✅",
        _ => "•"
    };

    public static RelLevel Relevance(ThreatEvent e, string myOblast, string myCity)
    {
        if (e.Kind == ThreatKind.AllClear) return RelLevel.None;
        if (e.City != null && CityMatch(e.City, myCity)) return RelLevel.City;
        if (e.Oblast != null && string.Equals(e.Oblast, myOblast, StringComparison.OrdinalIgnoreCase)) return RelLevel.Oblast;
        return RelLevel.None;
    }

    // Грубое сравнение города (посты — в винительном падеже: «на Суми», «на Шостку»).
    static bool CityMatch(string a, string b)
    {
        a = a.ToLowerInvariant().Trim();
        b = b.ToLowerInvariant().Trim();
        if (a.Length < 3 || b.Length < 3) return a == b;
        var stem = b[..Math.Min(b.Length, Math.Max(4, b.Length - 1))];
        return a.StartsWith(stem);
    }
}
