using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// ── WarAlarm · «Чого чекати цієї ночі» ──────────────────────────────────────────
// Увечері монітори (@ukraine_pyxx) пишуть оцінку загроз: «По загрозах на цю ніч: … 13
// балістичних ракет у Брянській області, 6 цирконів … По загрозах: м.Київ, Київська область…»
// (~21:00) і «Оцінка діяльності ворожої авіації… Ймовірність застосування балістичного
// озброєння … — 50%» (~23:00). Збираємо з них коротку зведену картину ночі: що напоготові,
// які області під загрозою, стратегічна авіація, Калібри, і що вже пущено. О 21:15 —
// пост у канал + повідомлення в бот + картка в застосунку; нові оцінки — правка того ж поста.
// Не лише один канал: прогнози на ніч від усіх моніторів («Підвищена на балістику протягом ночі»,
// «Не вбачаю суттєвих загроз для нашої зони…», «готується масований удар») — коротким рядком
// по регіону, щоб було видно, що кажуть Київ, Захід, Харків… а не лише @ukraine_pyxx.

public static class EveningOutlook
{
    static readonly object Gate = new();
    static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static DateTime _evening = DateTime.MinValue;              // вечір, до якого належить оцінка
    static readonly List<(int n, string kind, string where)> Stocks = new();
    static string? _regions, _strat, _stratReady, _sea;
    static int? _ballProb, _stratProb, _salvo;
    static string? _ballNote, _nextAttack;
    // Голоси моніторів: канал → (коли, текст, рівень 0 спокійно / 1 можливо / 2 підвищено, регіон).
    static readonly Dictionary<string, Voice> Voices = new(StringComparer.OrdinalIgnoreCase);
    public sealed record Voice(DateTime At, string Text, int Level, string Area, string Channel);
    static DateTime _updated = DateTime.MinValue;

    /// Вечір, до якого належить момент [at]: до 12:00 — ще попередній вечір.
    public static DateTime EveningOf(DateTime at) => at.Hour < 12 ? at.Date.AddDays(-1) : at.Date;

    public static DateTime Updated { get { lock (Gate) return _updated; } }
    public static DateTime Evening { get { lock (Gate) return _evening; } }

    static readonly Regex Outlook = new(@"по загрозах на (?:цю|сьогоднішню|наступну) ніч|загроз\w* на (?:цю )?ніч|оцінка діяльності ворожої авіації|ймовірність застосування балістичн"
        + @"|накопичив\w*|ракетний арсенал|запаси балістичн", O);
    // «• Брянськ — орієнтовно 18 ракет «Іскандер», 13 ракет до комплексів С-400, 5 ракет «Онікс-М»» (25.11).
    static readonly Regex BulletLine = new(@"^\W*(\p{Lu}[\p{L}'’-]+(?:\s+\p{Lu}[\p{L}'’-]+)?)\s*[—–-]\s*(.+)$", RegexOptions.CultureInvariant);
    static readonly Regex BulletItem = new(@"(\d{1,3})\s+(?:балістичн\w*\s+)?ракет\w*\s+(?:до\s+комплекс\w*\s+)?[«""]?(іскандер|кн-?23|циркон|онікс|кинджал|калібр|с-?400|с-?300|х-?\d{2,3})", O);
    // «у Брянській області є орієнтовно 14 балістичних ракет та ще 5 балістичних ракет у Воронежі» (22.11).
    static readonly Regex InRegionFirst = new(@"(?i:у|в)\s+(\p{L}+(?:ській|цькій)\s+області|\p{Lu}\p{L}+[іу])\s+(?:є\s+)?(?:орієнтовно\s+|близько\s+|ще\s+)?(\d{1,3})\s+(?i:(балістичн\w*|ракет\w* іскандер\w*|іскандер\w*|циркон\w*|кинджал\w*))", RegexOptions.CultureInvariant);
    static readonly Regex NumFirst = new(@"(\d{1,3})\s+(?i:(балістичн\w*(?:\s+ракет\w*)?|іскандер\w*|циркон\w*|кинджал\w*))\s+(?i:у|в)\s+(\p{L}+(?:ській|цькій)\s+області|\p{Lu}\p{L}+[іу])(?!\p{L})", RegexOptions.CultureInvariant);
    static readonly Regex StockLine = new(
        @"^\W*(\d{1,3})\s+(балістичн\w*(?:\s+ракет\w*)?|ракет\w* (?:іскандер|кн-?23)\w*|іскандер\w*|циркон\w*|кинджал\w*|калібр\w*|с-?400|с-?300|х-?\d{2,3}\w*|крилат\w*\s+ракет\w*)\s+(?:у|в|на)\s+(?:межах\s+)?(.+?)\s*\.?$", O);
    static readonly Regex RegionsHead = new(@"^\W*(?:по загрозах|під загрозою|загрози)\s*:?\s*$", O);

    /// Пост каналу → чи це вечірня оцінка (тоді дані оновлено).
    public static bool Ingest(string text, DateTime at)
    {
        if (!Outlook.IsMatch(text)) return false;
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToList();
        var stocks = new List<(int n, string kind, string where)>();
        string? regions = null, strat = null, stratReady = null, sea = null;
        int? ballProb = null, stratProb = null, salvo = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (StockLine.Match(l) is { Success: true } sm && int.TryParse(sm.Groups[1].Value, out var n))
                stocks.Add((n, Kind(sm.Groups[2].Value), Region(sm.Groups[3].Value)));
            else if (RegionsHead.IsMatch(l))
            {
                var sb = new List<string>();
                for (int j = i + 1; j < lines.Count && lines[j].Length > 0; j++) sb.Add(lines[j]);
                if (sb.Count > 0) regions = string.Join(" ", sb).TrimEnd('.', ' ');
            }
            else if (Regex.IsMatch(l, @"стратегічн\w* авіац", O) && !Regex.IsMatch(l, @"\d+\s*%", O) && l.Length < 140)
            {
                // «По стратегічній авіації:» + наступний рядок — сама оцінка.
                var sl = l.TrimEnd().EndsWith(':') && i + 1 < lines.Count ? l + " " + lines[i + 1] : l;
                strat = Regex.IsMatch(sl, "без загроз", O) ? "без загроз" : sl.Length < 160 ? sl.TrimEnd('.') : null;
            }
        }
        if (stocks.Count == 0)
            foreach (var l in lines)
                if (BulletLine.Match(l) is { Success: true } bl && BulletItem.IsMatch(bl.Groups[2].Value))
                    foreach (Match it in BulletItem.Matches(bl.Groups[2].Value))
                        stocks.Add((int.Parse(it.Groups[1].Value), Kind(it.Groups[2].Value), Region(bl.Groups[1].Value)));
        var all = string.Join(" ", lines);
        if (stocks.Count == 0)
        {
            foreach (Match m in InRegionFirst.Matches(all)) stocks.Add((int.Parse(m.Groups[2].Value), Kind(m.Groups[3].Value), Region(m.Groups[1].Value)));
            foreach (Match m in NumFirst.Matches(all))
                if (!stocks.Any(x => x.n == int.Parse(m.Groups[1].Value) && x.where == Region(m.Groups[3].Value)))
                    stocks.Add((int.Parse(m.Groups[1].Value), Kind(m.Groups[2].Value), Region(m.Groups[3].Value)));
        }
        string? ballNote = null, nextAttack = null;
        // «На сьогодні без масованих пусків балістики. Можливі одиничні пуски» (21.11),
        // «застосування великої кількості ракет малоймовірне» (26.11).
        if (Regex.IsMatch(all, @"без масован\w* (?:пуск|застосуван)\w*|(?:великої кількості|масован\w*)[^.]{0,40}малоймовірн|малоймовірн\w*[^.]{0,40}масован", O))
            ballNote = Regex.IsMatch(all, @"одиничн|можуть бути|можлив\w* (?:поодинок|невелик|не великі)|не великі пуски", O) ? "масованого пуску не чекають, можливі одиничні" : "масованого пуску не чекають";
        // «на початку наступного місяця очікуємо нову атаку», «ймовірна ракетна атака (тобто вже з наступного тижня)».
        if (Regex.Match(all, @"(?:очікуємо|ймовірн\w*|готу\w*)\s+(?:нов\w*\s+)?(?:масован\w*\s+|ракетн\w*\s+|комбінован\w*\s+)*(?:атак|удар)\w*[^.]{0,80}?(з наступного тижня|наступного тижня|на початку наступного місяця|цими днями|найближчими днями|найближчі\s+\w+\s+(?:ноч|дн)\w*)", O) is { Success: true } na)
            nextAttack = na.Groups[1].Value;
        else if (Regex.Match(all, @"(на початку наступного місяця|під кінець наступного тижня|з наступного тижня)[^.]{0,60}?(?:очікуємо|ймовірн\w*)\s+(?:нов\w*\s+)?(?:масован\w*\s+|ракетн\w*\s+)*(?:атак|удар)", O) is { Success: true } nb)
            nextAttack = nb.Groups[1].Value;
        if (strat is null && Regex.Match(all, @"(?:по\s+)?стратегічн\w* авіац\w*\s*:?\s*([^.]{0,40}без загроз)", O).Success) strat = "без загроз";
        if (Regex.IsMatch(all, @"спорядж\w* ракет", O)) strat = (strat is { } st0 ? st0 + ", " : "") + "йде спорядження ракетами";
        if (Regex.Match(all, @"ймовірність застосування балістичн\w* озброєння[^%]{0,60}?(\d{1,3})\s*%", O) is { Success: true } bp) ballProb = int.Parse(bp.Groups[1].Value);
        if (Regex.Match(all, @"загроза\s+вильоту стратегічної авіації[^%]{0,40}?(\d{1,3})\s*%", O) is { Success: true } spm) stratProb = int.Parse(spm.Groups[1].Value);
        if (Regex.Match(all, @"залп до (\d{1,3}) крилатих", O) is { Success: true } sv) salvo = int.Parse(sv.Groups[1].Value);
        // Лише речення «в готовності …»: «Тренування 3 бортів Ту-95МС» — не про цю ніч.
        var readyLine = lines.FirstOrDefault(l => Regex.IsMatch(l, "готовност", O)) ?? "";
        var planes = Regex.Matches(readyLine, @"(\d{1,2})\s+борт\w*\s+(Ту-?\d{2,3}\w*)", O).Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}").ToList();
        if (planes.Count > 0) stratReady = string.Join(", ", planes);
        if (Regex.IsMatch(all, @"ракетонос\w* відсутн", O)) sea = "носіїв у морі немає";
        else if (Regex.Match(all, @"(\d{1,2})\s+ракетонос\w*[^.]{0,60}?(?:до\s+)?(\d{1,3})\s+(?:крилатих|калібр)", O) is { Success: true } cm)
            sea = $"{cm.Groups[1].Value} носії(в), до {cm.Groups[2].Value} ракет";
        else if (Regex.Match(all, @"(\d{1,2})\s+ракетонос", O) is { Success: true } cm2) sea = $"{cm2.Groups[1].Value} носії(в) у морі";

        bool any = stocks.Count > 0 || regions != null || strat != null || ballProb != null || stratProb != null || salvo != null || sea != null
            || ballNote != null || nextAttack != null;
        if (!any) return false;
        lock (Gate)
        {
            NewEvening(at);
            // Нова оцінка запасів замінює стару (а не додається до неї).
            if (stocks.Count > 0) { Stocks.Clear(); Stocks.AddRange(stocks); }
            _regions = regions ?? _regions; _strat = strat ?? _strat; _stratReady = stratReady ?? _stratReady; _sea = sea ?? _sea;
            _ballProb = ballProb ?? _ballProb; _stratProb = stratProb ?? _stratProb; _salvo = salvo ?? _salvo;
            _ballNote = ballNote ?? _ballNote; _nextAttack = nextAttack ?? _nextAttack;
            _updated = at;
        }
        return true;
    }

    static string Kind(string s)
    {
        var l = s.ToLowerInvariant();
        return l.StartsWith("баліст") || l.Contains("іскандер") || l.Contains("кн") ? "балістика"
             : l.StartsWith("циркон") ? "Циркони"
             : l.StartsWith("кинджал") ? "Кинджали"
             : l.StartsWith("калібр") ? "Калібри"
             : l.StartsWith("онікс") ? "Онікси"
             : l.Contains("400") ? "С-400"
             : l.Contains("300") ? "С-300"
             : "крилаті";
    }

    // «Брянській області» → «Брянщина», «в межах Курської області» → «Курщина», «Криму» → «Крим».
    static string Region(string s)
    {
        s = s.Trim().TrimEnd('.', ',');
        if (Regex.IsMatch(s, @"^крим", O)) return "Крим";
        if (Regex.IsMatch(s, @"^воронеж", O)) return "Воронежчина";
        var m = Regex.Match(s, @"^(\p{L}+?)(?:ській|ської|ська|ську|цькій|цької)\s+област", O);
        if (m.Success) return m.Groups[1].Value + "щина";
        return s.Length > 30 ? s[..30] : s;
    }

    /// Чи є що сказати про вечір [evening].
    public static bool Has(DateTime evening) { lock (Gate) return _evening == evening && _updated != DateTime.MinValue; }

    // Під Gate: новий вечір — забуваємо вчорашнє.
    static void NewEvening(DateTime at)
    {
        var ev = EveningOf(at);
        if (ev == _evening) return;
        _evening = ev; Stocks.Clear(); Voices.Clear();
        _regions = _strat = _stratReady = _sea = _ballNote = _nextAttack = null; _ballProb = _stratProb = _salvo = null;
        _updated = DateTime.MinValue;
    }

    // ── 🗣 Прогнози моніторів на ніч ──
    static readonly Regex NightMark = new(@"на (?:цю |сьогоднішню |наступну )?ніч(?!\p{L})|протягом (?:вечора\s*/\s*)?ночі|цієї ночі|цього вечора|вечора\s*/\s*ночі|сьогодні вночі"
        + @"|ситуаці\w* на ніч|найближч\w* (?:\d+ )?(?:ноч|доб|годин)|найближчими днями|цими днями|на \d{2} годин|до ранку"
        + @"|этой ночью|на эту ночь|сегодня ночью|в ближайш\w* (?:ночь|сутки|часы)", O);
    static readonly Regex VoiceTopic = new(@"пуск|баліст|баллист|ракет|іскандер|кинджал|циркон|калібр|стратегічн|ту-?95|ту-?160|бпла|шахед|дрон|масован|комбінован|удар|атак|загроз|угроз|тривог", O);
    // Не прогноз: живий рух, новини постфактум, донати, «бажаю тихої ночі».
    static readonly Regex NotVoice = new(@"курсом|на підльоті|над містом|вибух|збит|рух\s+на|летить|кружля|заходить|пуски каб|повз\s|^\W*🗞|вечір,\s*новини|внаслідок|постраждал|загинул|поранен"
        + @"|потрапи\w* під удар|уражено|знищено|вчора|минулої ночі|напередодні|банка|донат|підпис\w* петиц|реквізит|звернення|блекаут|відключен|графік", O);
    static readonly Regex High = new(@"висок\w* ймовірн|підвищен\w*|готу\w*\s+(?:до\s+)?(?:нов\w*\s+|серії\s+)?(?:масован|масштабн|комбінован|удар|атак)|підготовк\w* (?:\p{L}+\s+){0,2}(?:масован|удар|атак)"
        + @"|загроза зберігається|реагуємо на|накопичив|можлив\w* масован|загроз\w* на \d{2} годин|все може бути", O);
    static readonly Regex Calm = new(@"не вбачаю|без (?:суттєвих )?загроз|спокійн|тиша|малоймовірн|без масован|не очікуємо|не передбача", O);
    static readonly Regex Maybe = new(@"можлив|уважн|одиничн|можуть бути|не виключ|ймовірн|будьте", O);

    // Регіон каналу для рядка «🔴 Київ: …».
    static readonly Dictionary<string, string> AreaOf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kyiv_nebo"] = "Київ", ["chyste_neboo"] = "Київ", ["monitoring_kyiv"] = "Київ", ["kievinform_ua1"] = "Київ",
        ["my_safety_Chernigiv"] = "Чернігів", ["monitoring_zaxid"] = "Захід", ["zahidnimonitoring"] = "Захід",
        ["rocket_danger_lviv"] = "Львів", ["vin_info_ppo"] = "Вінниця", ["PoltavaRadar"] = "Полтава",
    };
    static readonly Dictionary<string, string> CityOfOblast = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Київська"] = "Київ", ["Харківська"] = "Харків", ["Дніпропетровська"] = "Дніпро", ["Запорізька"] = "Запоріжжя",
        ["Одеська"] = "Одеса", ["Миколаївська"] = "Миколаїв", ["Вінницька"] = "Вінниця", ["Сумська"] = "Суми",
        ["Полтавська"] = "Полтава", ["Чернігівська"] = "Чернігів", ["Житомирська"] = "Житомир", ["Донецька"] = "Донеччина",
        ["Львівська"] = "Львів", ["Херсонська"] = "Херсон", ["Черкаська"] = "Черкаси", ["Кіровоградська"] = "Кропивницький",
    };

    static string Area(string ch, IReadOnlyCollection<string>? oblasts)
    {
        if (AreaOf.TryGetValue(ch, out var a)) return a;
        var ob = oblasts ?? SourceCatalog.Get(ch)?.Oblasts ?? Array.Empty<string>();
        if (ob.Count == 1 && CityOfOblast.TryGetValue(ob.First(), out var c)) return c;
        if (ob.Count > 3 && ob.Contains("Львівська")) return "Захід";
        if (ob.Count > 1 && ob.Contains("Одеська")) return "Південь";
        if (ob.Count > 1 && ob.Contains("Чернігівська")) return "Північ";
        return "Україна";
    }

    /// Прогноз на ніч від будь-якого каналу моніторингу → один рядок «що кажуть». true — це прогноз.
    public static bool IngestVoice(string? ch, string text, DateTime at, IReadOnlyCollection<string>? oblasts = null)
    {
        if (string.IsNullOrWhiteSpace(ch) || text.Length > 1500 || text.Contains("WarAlarm", StringComparison.OrdinalIgnoreCase)) return false;
        if (at.Hour is >= 6 and < 17) return false; // прогноз на ніч пишуть увечері
        if (!NightMark.IsMatch(text) || !VoiceTopic.IsMatch(text) || NotVoice.IsMatch(text)) return false;
        // Речення про загрози — без емодзі, зірочок і «На добраніч!».
        var sents = Regex.Split(text.Replace("\r", ""), @"(?<=[.!?])\s+|\n+")
            .Select(x => Regex.Replace(x, @"[^\p{L}\p{N}\s.,:;%«»""'’ʼ()/+–—-]", "").Trim(' ', '*', '-', '•'))
            .Where(x => x.Length > 3 && VoiceTopic.IsMatch(x) && !Regex.IsMatch(x, @"добраніч|доброї ночі|тихої|бережіть", O)
                        && !Regex.IsMatch(x, @"^\W*(?:було|прилет|вибух|збито|по факту)", O)) // «Було 8 балістичних ракет» — підсумок, не прогноз
            .ToList();
        if (sents.Count == 0) return false;
        // Заява на всю країну (Зеленський, Повітряні сили, ГУР) — «Україна», а не регіон каналу, що її переслав.
        var own = Area(ch, oblasts);
        bool national = Regex.IsMatch(text, @"зеленськ|президент|повітрян\w* сил|сирськ|буданов|(?<!\p{L})гур(?!\p{L})|генштаб", O);
        string? who = Regex.Match(text, @"[—–-]\s*(Зеленський|Буданов|Сирський|Ігнат)", O) is { Success: true } wm ? wm.Groups[1].Value : null;
        // «Для Києва існує загроза…» від західного монітора — це про Київ, а не про його зону (26.11).
        var byArea = new Dictionary<string, List<string>>();
        foreach (var x in sents)
        {
            var a = Regex.Match(x, @"(?:для|по)\s+(Києв[аую]|Київщин[иі]|Харков[аую]|Дніпр[аоу]|Одес[иіу]|Запоріжж[яю]|Львов[аую]|Сум|Сумам|Полтав[иіу]|Чернігов[аую]|Миколаєв[аую]|Вінниц[іяю])(?!\p{L})", O) is { Success: true } am
                ? am.Groups[1].Value.ToLowerInvariant() switch
                {
                    var w when w.StartsWith("киї") || w.StartsWith("києв") => "Київ", var w when w.StartsWith("харк") => "Харків",
                    var w when w.StartsWith("дніпр") => "Дніпро", var w when w.StartsWith("одес") => "Одеса",
                    var w when w.StartsWith("запор") => "Запоріжжя", var w when w.StartsWith("льв") => "Львів",
                    var w when w.StartsWith("сум") => "Суми", var w when w.StartsWith("полт") => "Полтава",
                    var w when w.StartsWith("черн") => "Чернігів", var w when w.StartsWith("микол") => "Миколаїв", _ => "Вінниця",
                }
                : national ? "Україна" : own;
            (byArea.TryGetValue(a, out var l) ? l : byArea[a] = new()).Add(x);
        }
        lock (Gate)
        {
            NewEvening(at);
            foreach (var (area, xs) in byArea)
            {
                var all = string.Join(" ", xs);
                var sum = (who is not null && area == "Україна" ? who + ": " : "") + string.Join(" ", xs.Take(2));
                if (sum.Length > 170) sum = sum[..167].TrimEnd(' ', ',', '.') + "…";
                // Перше речення — вердикт монітора: «Не вбачаю суттєвих загроз…» далі про спорядження бортів — усе одно спокійно.
                int lv = Regex.IsMatch(xs[0], @"не вбачаю|не очікуємо|спокійн", O) && !High.IsMatch(xs[0]) ? 0 : LevelOf(all);
                Voices[area == own ? ch : ch + ":" + area] = new Voice(at, sum, lv, area, ch);
            }
            _updated = at;
        }
        return true;
    }

    // 2 — підвищено, 1 — можливо, 0 — спокійно. «Не вбачаю суттєвих загроз… можливе наближення БпЛА» — спокійно.
    static int LevelOf(string t) =>
        High.IsMatch(t) ? 2
        : Regex.IsMatch(t, @"не вбачаю|без суттєвих|спокійн|не очікуємо|не передбача", O) ? 0
        : Calm.IsMatch(t) ? (Maybe.IsMatch(t) ? 1 : 0)
        : 1;

    /// Будь-який пост каналу моніторингу: зведена оцінка й/або прогноз «що вночі».
    public static bool Feed(string? ch, string text, DateTime at, IReadOnlyCollection<string>? oblasts = null)
    {
        bool a = Ingest(text, at);
        // Зведену оцінку каналу пусків уже розібрано в рядки вище; від інших — ще й їхній вердикт («Ворог готує масований удар»).
        bool b = !(a && string.Equals(ch, "ukraine_pyxx", StringComparison.OrdinalIgnoreCase)) && IngestVoice(ch, text, at, oblasts);
        return a || b;
    }

    public static List<Voice> CurrentVoices() { lock (Gate) return Voices.Values.OrderByDescending(v => v.At).ToList(); }

    /// Рядки зведення (без заголовка) — для поста, бота й застосунку. [launched] — уже пущено за вечір.
    public static List<string> Lines(string? launched)
    {
        lock (Gate)
        {
            var outp = new List<string>();
            if (_regions is { } rg) outp.Add($"🎯 Під загрозою: {rg}");
            var ball = Stocks.Where(s => s.kind == "балістика").ToList();
            if (ball.Count > 0 || _ballProb is not null)
            {
                var parts = new List<string>();
                if (ball.Count > 0)
                    parts.Add($"напоготові ~{ball.Sum(b => b.n)} ({string.Join(", ", ball.GroupBy(b => b.where).Select(g => $"{g.Key} {g.Sum(b => b.n)}"))})");
                if (_ballProb is { } bp) parts.Add($"ймовірність масованого пуску {bp}%");
                if (_ballNote is { } bn) parts.Add(bn);
                outp.Add("🚀 Балістика: " + string.Join(" · ", parts));
            }
            else if (_ballNote is { } bn2) outp.Add("🚀 Балістика: " + bn2);
            foreach (var g in Stocks.Where(s => s.kind != "балістика").GroupBy(s => s.kind))
                outp.Add($"{(g.Key is "С-400" or "С-300" ? "🛡" : "☄")} {g.Key}: {g.Sum(s => s.n)} ({string.Join(", ", g.Select(s => s.where).Distinct())})");
            if (_strat != null || _stratProb != null || _stratReady != null || _salvo != null)
            {
                var parts = new List<string>();
                if (_stratProb is { } sp) parts.Add($"ймовірність вильоту {sp}%");
                else if (_strat is { } st) parts.Add(st);
                if (_stratReady is { } sr) parts.Add($"у готовності {sr}" + (_salvo is { } sv ? $" (залп до {sv} крилатих)" : ""));
                else if (_salvo is { } sv2) parts.Add($"залп до {sv2} крилатих");
                outp.Add("✈ Стратегічна авіація: " + string.Join(" · ", parts));
            }
            if (_sea is { } sea) outp.Add($"🌊 Калібри: {sea}");
            if (_nextAttack is { } na) outp.Add($"🗓 Масована атака: очікують {na}");
            if (launched is { Length: > 0 }) outp.Add($"🛵 Шахеди: {launched}");
            // 🗣 Що кажуть монітори: по регіону — найсвіжіший прогноз; дзеркальні канали (той самий текст) — один рядок.
            // Дзеркальні канали (kyiv_nebo / chyste_neboo пишуть майже те саме) — один рядок.
            static HashSet<string> Words(string t) => Regex.Matches(t.ToLowerInvariant(), @"\p{L}{3,}").Select(m => m.Value).ToHashSet();
            var uniq = new List<Voice>();
            foreach (var v in Voices.Values.OrderByDescending(v => v.At))
            {
                var w = Words(v.Text);
                if (uniq.Any(u => u.Area == v.Area && Words(u.Text) is var uw && uw.Intersect(w).Count() >= 0.6 * Math.Max(1, Math.Min(uw.Count, w.Count)))) continue;
                uniq.Add(v);
            }
            var vs = uniq
                .GroupBy(v => v.Area).SelectMany(g => g.Take(g.Key == "Україна" ? 3 : 2))
                .OrderBy(v => v.Area == "Україна" ? 0 : 1).ThenBy(v => v.Area == "Київ" ? 0 : 1).ThenBy(v => v.Area).ThenByDescending(v => v.Level)
                .Take(9).ToList();
            if (vs.Count > 0)
            {
                if (outp.Count > 0) outp.Add("");
                outp.Add($"🗣 Що кажуть монітори · джерел: {Voices.Values.Select(v => v.Channel).Distinct().Count()}");
                foreach (var v in vs)
                    outp.Add($"{(v.Level switch { 2 => "🔴", 1 => "🟡", _ => "🟢" })} {v.Area}: {v.Text}");
            }
            return outp;
        }
    }

    public static string Title(DateTime evening) => $"🌙 Чого чекати цієї ночі · {evening:dd.MM}";

    /// state.outlook — для застосунку.
    public static string Json(string? launched)
    {
        string E(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        DateTime ev, upd;
        lock (Gate) { ev = _evening; upd = _updated; }
        if (upd == DateTime.MinValue) return "{}";
        var lines = Lines(launched);
        return $"{{\"ts\":{((DateTimeOffset)upd).ToUnixTimeMilliseconds()},\"evening\":\"{ev:dd.MM}\",\"lines\":[{string.Join(",", lines.Select(l => "\"" + E(l) + "\""))}]}}";
    }
}
