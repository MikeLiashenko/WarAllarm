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
    // «в р-ні Бродів та Миколаївки», «Ракети на Луцьк та Рівне» — друге місце того ж рядка (Parse робить з нього ще одну ціль).
    public string? AlsoAt { get; init; }
    // Місце за старим правилом («на Y» з «повз X на Y»), якщо X нема в довіднику.
    public string? Fallback { get; init; }
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
        // Російськомовні канали.
        ["харьковщина"] = "Харківська", ["киевщина"] = "Київська", ["черниговщина"] = "Чернігівська",
        ["днепропетровщина"] = "Дніпропетровська", ["одесщина"] = "Одеська", ["николаевщина"] = "Миколаївська",
        ["черкасщина"] = "Черкаська", ["винничина"] = "Вінницька", ["вінничина"] = "Вінницька", ["хмельничина"] = "Хмельницька", ["донечина"] = "Донецька", ["хмельнитчина"] = "Хмельницька",
        ["ровенщина"] = "Рівненська", ["львовщина"] = "Львівська", ["тернопольщина"] = "Тернопільська",
        ["кировоградщина"] = "Кіровоградська", ["донетчина"] = "Донецька",
    };

    // «Харьковская область», «Днепропетровской области»: основа російського прикметника → область.
    static readonly (string stem, string obl)[] RuAdj =
    {
        ("харьков", "Харківська"), ("киевск", "Київська"), ("сумск", "Сумська"), ("чернигов", "Чернігівська"),
        ("полтав", "Полтавська"), ("днепропетров", "Дніпропетровська"), ("запорож", "Запорізька"),
        ("херсон", "Херсонська"), ("николаев", "Миколаївська"), ("одесс", "Одеська"), ("донец", "Донецька"),
        ("луган", "Луганська"), ("черкас", "Черкаська"), ("кировоград", "Кіровоградська"), ("винниц", "Вінницька"),
        ("житомир", "Житомирська"), ("хмельниц", "Хмельницька"), ("ровен", "Рівненська"), ("волын", "Волинська"),
        ("львов", "Львівська"), ("терноп", "Тернопільська"), ("ивано-франк", "Івано-Франківська"),
        ("черновиц", "Чернівецька"), ("закарпат", "Закарпатська"),
    };

    public static string? Canonical(string s)
    {
        // Емодзі, «:» і крапка навколо заголовка («❗⚠️Харьковская область.») — не частина назви.
        s = Regex.Replace(s.Trim().ToLowerInvariant(), @"^[^\p{L}]+|[^\p{L}]+$", "");
        if (Oblasts.TryGetValue(s, out var v)) return v;
        // Відмінки: «Сумщини», «на Сумщині», «Вінниччину», «Харьковщины» → «сумщина» / «вінниччина».
        var nom = Regex.Replace(s, @"(щ|чч|ч)(ини|ині|ину|иною|ины|ине|иной)$", "$1ина");
        if (nom != s && Oblasts.TryGetValue(nom, out var v2)) return v2;
        // «Харківська область», «Київська обл», «Харьковская область», «Днепропетровской области».
        if (Regex.Match(s, @"^([\p{L}'’-]+)\s+(?:обл|область|області|области)$") is { Success: true } m)
        {
            var adj = m.Groups[1].Value;
            foreach (var (stem, obl) in RuAdj)
                if (adj.StartsWith(stem)) return obl;
            foreach (var obl in Oblasts.Values.Distinct().OrderByDescending(o => o.Length))
                if (adj.StartsWith(obl[..^4].ToLowerInvariant())) return obl;
        }
        return null;
    }
}

public static class ThreatParser
{
    // Усі ходові назви дронів/шахедів/приманок/розвідників (сленг каналів) — ОДНЕ джерело
    // правди для всіх трьох парсерів (ThreatParser / KyivMonitor / KharkivMonitor).
    // шахед · герань(geran) · гербера-пастка · бандероль · мопед · FPV · молнія · шаболда ·
    // ланцет · мавік · орлан · зала · supercam.
    public const string DroneRe =
        @"бпла|дрон|шахед|шахід|shahed|італмас|italmas|геран|geran|гербер|мопед|балалайк|косилк|газонокос|\bтурб[аиоу]\b|\bfpv\b|молні|шаболд|ланцет|мавік|mavic|орлан|\bзала\b|supercam|суперкам";

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

        foreach (var raw in Slang.Normalize(post).Split('\n'))
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
                // «Балістика повз Чернігів на Київ» — ціль Київ (за 1–3 хв уже там), а не те, повз що летить.
                events.Add(new(ThreatKind.Ballistic, oblast, tl?.Fallback ?? tl?.City, tl?.Count ?? 0,
                    bw is { } bww ? WeaponCatalog.DisplayName(bww) : null, line, FromOf(line)));
                continue;
            }
            // Авиация
            if (low.Contains("актив") && Regex.IsMatch(line, @"(су|ту|міг|миг|mig)[-\s]?\d", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(line, @"((су|ту|міг|миг|mig)[-\s]?\d+)", RegexOptions.IgnoreCase);
                events.Add(new(ThreatKind.Aviation, null, null, 0, Slang.PlaneName(line) ?? m.Value, line));
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
            // Так само «повз Турбів, курсом на Стрижавку» — кома лише перед курсом. Але «над Старою Гнилицею
            // на Малинівку, Чугуїв, Есхар» — кілька місць, тут ділимо.
            if (ParseTargetLine(body, oblast) is { Toward: not null } whole && (whole.City is null || CourseTail.IsMatch(body)))
            { events.Add(whole); continue; }
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

        return events.SelectMany(e => e.AlsoAt is { } also
            ? new[] { e with { AlsoAt = null }, e with { City = also, AlsoAt = null } }
            : new[] { e }).ToList();
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
        // Але «16 Фонтана/Лиманка» — це зупинка в Одесі (16-та станція Великого Фонтану), а не 16 цілей.
        var mc = Regex.Match(line, @"^\s*(?:(?:і\s+)?ще\s+|\+\s*)?(\d+)(?!\d|\s*(?:-?(?:а|та|ти)\s+)?(?:фонтан|станці|ст\.\s|км\b|хв|м(?!\p{L})))\s*[хx×]?\s*(?=\S)", RegexOptions.IgnoreCase); // «Ще 2 балістики», «+3»; «1800м на Вербове» — висота
        if (mc.Success) int.TryParse(mc.Groups[1].Value, out count);
        // «З'явилися 2 бандеролі на Самар», «Павлоград, 4 бандеролі …» — число перед засобом усередині рядка.
        else if (Regex.Match(line, @"(?<![\p{L}\d\-])(\d{1,2})\s*[хx×]?\s+" + Slang.Weapon, RegexOptions.IgnoreCase) is { Success: true } mw)
            int.TryParse(mw.Groups[1].Value, out count);

        // Конкретний засіб зі словника («Х-101», «Іскандер-М», «реактивний», «ціль»…) —
        // і тип, і назва на карті, і швидкість для ETA. Інакше — загальне визначення.
        var weapon = WeaponCatalog.Match(line);
        var kind = weapon?.Kind ?? DetectKind(line);
        string? extra = weapon is { } w ? WeaponCatalog.DisplayName(w) : null;

        // ПС ЗСУ пише, ДЕ ціль зараз: «в р-ні Ладижина (Вінниччина) курс західний», «повз Дунаївці на
        // Хмельниччині у західному напрямку», «на Вінниччині на/повз Ситківці», «з Хмельниччини на
        // Тернопільщину (р-н Гусятина)», «південніше Коростеня», «поряд із Житомиром у напрямку Хмельниччини».
        // Тоді місце — воно, а «на X» / «курсом на X» — лише куди летить. Раніше бралось перше «на …»:
        // «курсом на захід» не давало місця зовсім (20.11: третина постів ПС ЗСУ — «місце не знайдено»).
        var at = AtPlaceLine(line, oblast);

        // ПС ЗСУ / агрегатори: «БпЛА на півночі Сумщини, курсом на Полтаву», «з Сумщини курсом
        // на Полтаву». Ціль — у названій частині області; місто після «курсом на» — лише
        // напрямок. Раніше ціль ставили НА Полтаву, хоча вона ще над Сумщиною.
        var course = Regex.Match(line, @"курс(?:ом)?\s*(?:на|—|–|-|:)?\s*([А-ЯІЇЄҐ][\w'’ʼ\-]+(?:\s+[А-ЯІЇЄҐ][\w'’ʼ\-]+)?)");
        var where = Regex.Match(line,
            @"\b(?:на|в|у|з|із|зі|над)\s+(?:(півночі|півдні|сході|заході|центрі|північному\s+сході|північному\s+заході|південному\s+сході|південному\s+заході|північному-сході|південному-сході)\s+)?([\w'’ʼ\-]+(?:щин|ччин)\w*)",
            RegexOptions.IgnoreCase);
        // «з Брянщини на Чернігівщині курсом на Сновськ» — перша «-щина» російська: беремо першу нашу.
        if (course.Success)
            for (var wm = where; wm.Success && wm.Index < course.Index; wm = wm.NextMatch())
                if (Geo.Canonical(wm.Groups[2].Value) is not null) { where = wm; break; }
        if (at is null && course.Success && where.Success && where.Index < course.Index && Geo.Canonical(where.Groups[2].Value) is { } inObl)
            return new(kind == ThreatKind.Unknown ? ThreatKind.Target : kind, inObl, null, count, extra, line, FromOf(line))
            { Toward = course.Groups[1].Value.Trim(), Side = where.Groups[1].Success ? where.Groups[1].Value : null };

        var mn = Regex.Match(line, @"\bна\s+(.+)$", RegexOptions.IgnoreCase);
        // Інші звороти регіональних моніторів: «курс Куликівка», «в районі Ірпеня»,
        // «над Борисполем», «у бік Білої Церкви», «через Ананьїв».
        if (!mn.Success)
            mn = Regex.Match(line,
                @"\b(?:курсом|курс|над|біля|поблизу|в\s+районі|у\s+районі|в\s+напрямку|у\s+напрямку|напрямок|в\s+бік|у\s+бік|в\s+сторону|через|повз|(?:по|до)(?=\s+(?-i:[А-ЯІЇЄҐ])))\s+(.+)$",
                RegexOptions.IgnoreCase);
        string? city = mn.Success ? mn.Groups[1].Value.Trim().TrimEnd('.', ' ', '!') : null;
        // Отсекаем «хвост» после названия: «на Васильків повз Борову», «на X у напрямку Y»,
        // «на Бровари з півночі», «на Вишгород/Київ»…
        if (city != null)
            city = Regex.Split(city,
                @"\s+(?:повз|через|у напрямку|в напрямку|курс|напрямок|з півночі|з півдня|зі сходу|із заходу|низько|приблизно|знову|кружля\w*"
                + @"|з\s+(?:північно|південно)[\p{L}\-]*|з\s+(?:північного|південного|східного|західного)|рухаю\w*|рухаєт\w*|(?:та\s+)?змін\w*)\b|/",
                RegexOptions.IgnoreCase)[0].Trim();
        // «на Черкащину західним курсом», «на Миколаївщину курсом» — лише область.
        // Але не «Миколаївщині з Криму» (після області — назва, не напрямок).
        if (city != null && Regex.Match(city, @"^([\p{L}'\-]+(?:щин|ччин)\p{L}*)\s+(?-i:\p{Ll})(.*)$") is { Success: true } ot && Geo.Canonical(ot.Groups[1].Value) is not null
            && (Regex.Matches(ot.Groups[2].Value, @"(?-i:\p{Lu})[\p{L}'’ʼ\-]*").All(w => Geo.Canonical(w.Value) is not null)
                // «на Чернігівщину з Брянщини» — звідки (з Росії), а не друге місце.
                || Regex.IsMatch(ot.Value, @"^\S+\s+(?:з|із|зі|від)\s+(?:брянщин|курщин|бєлгородщин|білгородщин|белгородщин|орловщин|смоленщин|рос[іи]|рф\b)\p{L}*\W*$", RegexOptions.IgnoreCase)))
            city = ot.Groups[1].Value;
        // «Ракети на Луцьк та Рівне» — дві цілі.
        string? also = null;
        if (city != null && Regex.Match(city, @"^(" + Name + @")\s+(?:та|і|й)\s+(" + Name + @")\b") is { Success: true } two)
        { city = two.Groups[1].Value; also = two.Groups[2].Value; }

        if (at is { } a)
            return new(kind == ThreatKind.Unknown ? ThreatKind.Target : kind, a.oblast, a.place, count, extra, line, FromOf(line))
            { Toward = a.toward, AlsoAt = a.also, Fallback = city != a.place ? city : null };
        // «5 км до Новослобідки» без засобу — не ціль (тут «до» лише відстань), нехай читає BareNames.
        if (kind == ThreatKind.Unknown && Regex.IsMatch(line, @"\bкм\s+до\s", RegexOptions.IgnoreCase)) return null;
        if (city == null && kind == ThreatKind.Unknown) return null; // не похоже на цель
        return new(kind == ThreatKind.Unknown ? ThreatKind.Target : kind, oblast, city, count, extra, line, FromOf(line)) { AlsoAt = also };
    }

    static readonly Regex CourseTail = new(@"^[^,]*,\s*(?:курс\p{L}*|(?:у|в)\s+напрямку|далі|(?:у|в)\s+бік)(?!\p{L})[^,]*$", RegexOptions.IgnoreCase);

    // Назва з великої, до двох слів («Велику Олександрівку», «Кривого Рогу»).
    const string Name = @"(?-i:[А-ЯІЇЄҐ])[\p{L}'’ʼ\-]+(?:\s+(?-i:[А-ЯІЇЄҐ])[\p{L}'’ʼ\-]+)?";
    static readonly Regex AtPlaceRe = new(
        @"(?<!\p{L})(?:(?:в|у)\s+(?:районі|р-ні|р-н)|(?<![сц]ьк\p{L}{0,3}\s+)(?:р-ні|р-н)|повз|через|над|біля|поблизу|неподалік\s+від|поряд\s+(?:і?з|зі)|центр[іуа]?|(?:півн|півд|сх|зах)\p{L}*іше)"
        + @"\.?\s+(?:(?:н\.\s?п|нп|м|с|смт)\.?\s*)?(?<p>" + Name + @")(?:\s*,?\s+(?:та|і|й)\s+(?:(?:в|у)\s+(?:районі|р-ні)\s+)?(?<q>" + Name + @"))?",
        RegexOptions.IgnoreCase);
    static readonly Regex TowardRe = new(@"(?<!\p{L})(?:курс(?:ом)?\s+на|(?:у|в)\s+напрямку|на)\s+(?<t>" + Name + ")", RegexOptions.IgnoreCase);
    // Річки, а не міста: «через Дніпро», «над Десною».
    static readonly Regex River = new(@"^(?:десн|дністр|дуна[йюя]|ворскл|сейм|інгул|південн\p{L}* буг)", RegexOptions.IgnoreCase);

    /// Місце, де ціль зараз («в р-ні X», «повз X», «через X»…), область-підказка і куди летить.
    static (string place, string? oblast, string? toward, string? also)? AtPlaceLine(string line, string? oblast)
    {
        var ln = Regex.Replace(line, @"(?<!\p{L})(?:на\s*/\s*повз|повз\s*/\s*на)(?!\p{L})", "повз", RegexOptions.IgnoreCase);
        // «через зону відчуження Чорнобильської АЕС» — це Чорнобиль.
        ln = Regex.Replace(ln, @"зон\p{L}*\s+відчуження(?:\s+Чорнобильськ\p{L}*\s+АЕС)?|Чорнобильськ\p{L}*\s+АЕС|ЧАЕС|ЧЗВ|ЧЗО", "Чорнобиль", RegexOptions.IgnoreCase);
        Match? pm = null;
        foreach (Match m in AtPlaceRe.Matches(ln))
        {
            var name = m.Groups["p"].Value;
            if (Geo.Canonical(name) is not null || PlaceFinder.OblastWord.IsMatch(name) || River.IsMatch(name)
                || Regex.IsMatch(ln[(m.Groups["p"].Index + name.Length)..], @"^\s+обл", RegexOptions.IgnoreCase)) continue; // «через Сумську область»
            pm = m; break;
        }
        if (pm is null) return null;
        var place = pm.Groups["p"].Value;
        // «через Бериславский район» — район цілком.
        if (Regex.IsMatch(place, @"(?:ськ|цьк|ск|цк)\p{L}*$") && Regex.Match(ln[(pm.Index + pm.Length)..], @"^\s+(район|р-н)(?!\p{L})", RegexOptions.IgnoreCase) is { Success: true } rn) place += " " + rn.Groups[1].Value;
        // Область: «(Вінниччина)» одразу після назви, «на Хмельниччині» у рядку, «на Тернопільщину (р-н Гусятина)».
        string? hint = Regex.Match(ln[(pm.Index + pm.Length)..], @"^\s*\(([^)]+)\)") is { Success: true } pr ? Geo.Canonical(pr.Groups[1].Value) : null;
        hint ??= Regex.Match(ln, @"(?<!\p{L})(?:на|в|у)\s+([\p{L}'\-]+(?:щин|ччин)і)(?!\p{L})", RegexOptions.IgnoreCase) is { Success: true } lo ? Geo.Canonical(lo.Groups[1].Value) : null;
        hint ??= Regex.Match(ln[..pm.Index], @"([\p{L}'\-]+(?:щин|ччин)у)\s*\(\s*$", RegexOptions.IgnoreCase) is { Success: true } ac ? Geo.Canonical(ac.Groups[1].Value) : null;
        // «на Львівщину повз Золочів» — Золочів на Львівщині (не той, що на Харківщині).
        hint ??= Regex.Match(ln[..pm.Index], @"(?<!\p{L})(?:на|в|у)\s+([\p{L}'\-]+(?:щин|ччин)у)\s*$", RegexOptions.IgnoreCase) is { Success: true } ap ? Geo.Canonical(ap.Groups[1].Value) : null;
        string? toward = null;
        foreach (Match t in TowardRe.Matches(ln))
        {
            if (t.Index >= pm.Index && t.Index < pm.Index + pm.Length) continue;
            var tn = t.Groups["t"].Value;
            if (Geo.Canonical(tn) is not null || PlaceFinder.OblastWord.IsMatch(tn) || tn == place) continue;
            toward = tn; break;
        }
        var also = pm.Groups["q"].Success && Geo.Canonical(pm.Groups["q"].Value) is null ? pm.Groups["q"].Value : null;
        return (place, hint ?? oblast, toward, also);
    }

    /// Звідки летить: «з Курська», «із Чернігівщини», «від Куликівки», «з акваторії Чорного моря».
    /// Напрямки з малої («з півночі») — не місце, тому пропускаємо.
    // Слово одразу перед назвою, що означає «летить туди», а не «вже там» («над», «біля», «в районі»).
    static readonly Regex HeadingBefore = new(@"(?:(?<!\p{L})(?:на|курс|курсом|напрямку|напрямок|направлении|бік|сторону|до|далі|далее|дальше|потім|потом)|→)\W*$", RegexOptions.IgnoreCase);
    static readonly Regex HeadingAny = new(@"(?:(?<!\p{L})(?:на|курс\w*|напрямку|напрямок|бік|сторону|далі|далее|дальше)(?!\p{L})|→)", RegexOptions.IgnoreCase);
    static readonly Regex AtPlace = new(@"(?<!\p{L})(?:над|біля|возле|около|поблизу|районі|районе)(?!\p{L})", RegexOptions.IgnoreCase);

    /// «БпЛА на Харків», «курсом на Полтаву», «Далі Мерефа» — ціль ще летить до місця.
    /// «над Харковом», «в районі Ірпеня», голе «Оболонь» — уже там.
    public static bool HeadingTo(string raw, string? city)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(city)) return false;
        var stem = city.Trim();
        stem = stem[..Math.Min(3, stem.Length)];
        var m = Regex.Match(raw, @"(?<!\p{L})" + Regex.Escape(stem), RegexOptions.IgnoreCase);
        if (m.Success) return HeadingBefore.IsMatch(raw[..m.Index]);
        // Назву написано інакше («Песочин» → Пісочин, «на город» → Харків): дивимось на весь рядок.
        return HeadingAny.IsMatch(raw) && !AtPlace.IsMatch(raw);
    }

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
        if (l.Contains("каб") || Regex.IsMatch(l, @"авіабомб|авіаційн\w*\s+бомб|керован\w*\s+(?:авіаційн\w*\s+)?бомб") || l.Contains("умпк") || l.Contains("умпб") || Regex.IsMatch(l, @"фаб[-\s]?\d")) return ThreatKind.Kab;
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
