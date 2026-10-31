using System.Text.RegularExpressions;

// ── WarAlarm · сленг моніторів ───────────────────────────────────────────────
// Канали пишуть як завгодно: російською й українською, «Далее Центр», «→Харків/околиці
// (2х кружляє)», «мінус», «-1», «балалайка», «турба», 🛵. Тут усе це зводиться до слів,
// які знають парсери, і читаються короткі статуси без місця («мінус», «кружляє») — вони
// стосуються останньої цілі того ж каналу.
// Перевірка на прикладах: `dotnet run -- slang`, на справжніх постах: `dotnet run -- replay`.

public static class Slang
{
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    const string B = @"(?<!\p{L})", E = @"(?!\p{L})";

    static (Regex re, string to) W(string re, string to) => (new Regex(re, O), to);

    // Порядок важливий: довші звороти — вище. Лише службові слова: назви місць не чіпаємо.
    static readonly (Regex re, string to)[] Words =
    {
        W(B + @"(?:дальше|далее)" + E, "далі"),
        W(B + @"на\s*/\s*повз" + E, "на"),
        W(B + @"(?:нарізає|наматує|нарезает|наматывает)\s+(?:кола|круги)" + E, "кружляє"),
        W(B + @"(?:на\s+круг|колами|кругами|кругом)" + E, "кружляє"),
        W(B + @"(?:кружит|кружат|кружатся|кружиться|крутится|крутиться|вертится|кружлять|кружлять)" + E, "кружляє"),
        W(@"кружл\w*\s+містом", "кружляє над містом"),
        W(@"кружл\w*\s+городом", "кружляє над містом"),
        W(B + @"над\s+різними\s+районами" + E, "над"),
        W(B + @"над\s+разными\s+районами" + E, "над"),
        W(B + @"в\s+районе" + E, "в районі"),
        W(B + @"в\s+направлении" + E, "в напрямку"),
        W(B + @"в\s+сторону" + E, "в бік"),
        W(B + @"(?:зі\s+сторони|со\s+стороны|з\s+боку)(?=\s+(?-i:\p{Lu}))", "з"),
        // Лише перед назвою: «неподалік від вас» — не місце.
        W(B + @"(?:возле|около|рядом\s+с|неподалік\s+від|неподалік|недалеко\s+від|недалеко\s+от|навпроти|напротив|між|между)(?=\s+(?:с\.\s*)?(?-i:\p{Lu}))", "біля"),
        W(B + @"мимо" + E, "повз"),
        W(@"\s+на\s+виліт" + E, ""),
        W(B + @"(?:с|м|н\.п|смт)\.\s*(?=\p{Lu})", ""),
        W(B + @"минус" + E, "мінус"),
        W(B + @"(?:сбили|сбит\w*)" + E, "збито"),
        W(B + @"(?:упал|упала|упало)" + E, "впав"),
    };

    static readonly Dictionary<char, char> Twins = new()
    {
        ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['E'] = 'Е', ['H'] = 'Н', ['I'] = 'І', ['K'] = 'К', ['M'] = 'М', ['O'] = 'О', ['P'] = 'Р', ['T'] = 'Т', ['X'] = 'Х',
        ['a'] = 'а', ['c'] = 'с', ['e'] = 'е', ['i'] = 'і', ['o'] = 'о', ['p'] = 'р', ['x'] = 'х', ['y'] = 'у',
    };
    static readonly Regex LatinTwin = new(@"[ABCEHIKMOPTXaceiopxy](?=\p{IsCyrillic})|(?<=\p{IsCyrillic})[ABCEHIKMOPTXaceiopxy]");

    // «→Харків/околиці (2х кружляє).», «→Сновськ/Городня.» — стрілка @povitryanatrivogaaa.
    // Лише коли одразу назва: «→далі курс Смоліне» і так читається звичайним розбором.
    static readonly Regex Arrow = new(@"^\W*→\s*(?-i:(?<body>\p{Lu}[^(\n]*?))\s*(?:\((?<note>[^)]*)\))?\W*$", O);
    // «Вільнянськ уважно по БпЛА.» — місце попереду, а засіб після.
    static readonly Regex Careful = new(@"^\W*(?<place>\p{Lu}[\p{L}'’ʼ\-]+(?:\s+\p{Lu}[\p{L}'’ʼ\-]+)?)\s+(?:уважно|обережно|увага)\s+по\s+(?<what>.+)$", O);
    // «Дніпро все ще червоний кружляє», «Харків кружляє» — місце перед дієсловом.
    static readonly Regex PlaceCircling = new(@"^\W*(?-i:(?<place>\p{Lu}[\p{L}'’ʼ\-]+))(?:\s+(?:все|ще|досі|також|червоний|жовтий))*\s+кружляє\W*$", O);

    /// Текст поста словами, які знають парсери. Повторний виклик нічого не змінює.
    public static string Normalize(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            // Латинські двійники в кириличному слові: «Kиїв», «Xарків».
            var l = LatinTwin.Replace(lines[i], m => Twins[m.Value[0]].ToString());
            foreach (var (re, to) in Words) l = re.Replace(l, to);
            if (Arrow.Match(l) is { Success: true } a) l = ArrowLine(a.Groups["body"].Value, a.Groups["note"].Value);
            else if (Careful.Match(l) is { Success: true } c) l = $"{c.Groups["what"].Value.Trim(' ', '.', '!')} біля {c.Groups["place"].Value}";
            else if (PlaceCircling.Match(l) is { Success: true } p) l = $"кружляє над {p.Groups["place"].Value}";
            lines[i] = l;
        }
        return string.Join("\n", lines);
    }

    // «Харків/околиці», «2х кружляє» → «2х БпЛА кружляє над Харків/околиці».
    static string ArrowLine(string body, string note)
    {
        body = body.Trim(' ', '.');
        var n = Regex.Match(note, @"(\d+)\s*[хx×]", O);
        var count = n.Success ? n.Groups[1].Value + "х " : "";
        bool around = Regex.IsMatch(body, @"/\s*(?:р-н|район|околиц\w*)\s*$", O);
        var head = Circling(note) ? "кружляє над" : around ? "в районі" : "на";
        return $"{count}БпЛА {head} {body}";
    }

    static readonly Regex CircleWord = new(B + @"(?:кружля\w*|кружл\w*)" + E, O);
    /// «кружляє» в рядку (після Normalize всі варіанти — це слово).
    public static bool Circling(string? text) => text is { Length: > 0 } && CircleWord.IsMatch(Normalize(text));

    static readonly Regex Over = new(B + "над" + E, O);
    /// «над Харковом» — ціль уже над місцем, а не летить кудись далі.
    public static bool OverPlace(string? text) => text is { Length: > 0 } && Over.IsMatch(text);

    // ── «мінус» ───────────────────────────────────────────────────────────────
    const string Filler = @"(?:(?:цей|ця|цього|той|та|також|теж|тоже|наче|вже|ще|попередньо|і|и|по\s+цьому|там|є|ось|один|одна|майже|схоже|кажуть)\s+)*";
    const string Target = @"(?:шахед\w*|шахід\w*|бпла|дрон\w*|ціл[ьі]\w*|реактив\w*|мопед\w*|італмас\w*|балалайк\w*|турб\w*|він|вона|його|її)";
    static readonly Regex[] Shots =
    {
        new(@"^" + Filler + @"(?:мінус|мінусик|мінусяра)(?:\s+(?<n>\d+))?(?:\s+" + Target + @")?(?:\s+\S+){0,2}$", O),
        new(@"^[-−–]\s?(?<n>\d{1,2})\s*[хx×]?$", O),
        new(@"^[-−–]$", O), // @Karkivw, @kyiv_airdef: мінус пишуть просто рискою
        new(@"^(?:" + Filler + @")(?<n>\d{1,2})\s*[хx×]?\s+мінус\w*$", O), // «2 мінуса»
        new(@"^" + Filler + @"(?:(?<n>\d{1,2})\s+)?(?:" + Target + @"\s+)?(?:збито|збили|впав|знищено|знищили|приземлили)$", O),
        new(@"^" + Target + @"\s+нема(?:є)?\s+більше$", O),
    };
    // «мінус світло», «Ще відео мінусяри», «У меня минус такой…» — не про збиття.
    static readonly Regex NotShot = new(@"світл|свет|відео|видео|фото|такой|твой|градус|мороз|температур", O);

    /// Рядок цілком каже «цю збили»: скільки (за замовч. 1), інакше null.
    public static int? ShotCount(string line)
    {
        var l = Regex.Replace(Normalize(line).Trim().Replace('—', '–'), @"[^\p{L}\d\s\-−–]+", " ");
        l = Regex.Replace(l, @"\s+", " ").Trim().ToLowerInvariant();
        if (l.Length is 0 or > 48 || NotShot.IsMatch(l)) return null;
        if (Both.IsMatch(l)) return 2;
        foreach (var re in Shots)
            if (re.Match(l) is { Success: true } m)
                return int.TryParse(m.Groups["n"].Value, out var n) && n > 0 ? n : 1;
        return null;
    }

    static readonly Regex Both = new(@"^(?:обидва|обидві|оба|обе|двоє|двое|два|дві|две)\s+мінус\w*$", O);
    // Лише слово «мінус»: «Сумщина — 8х», «Харків - 2» — це скільки там цілей, а «ТЕЦ-6» — назва.
    const string ShotTail = @"(?:\s*[—–-]\s*|\s+)мінус\w*(?:\s+(?<n>\d{1,2}))?";
    static readonly Regex ShotAt = new(@"(?:^|\s)(?:на|над|біля|по)\s+(?<place>.+?)(?:" + ShotTail + @")\W*$", O);
    static readonly Regex ShotBare = new(@"^(?<place>(?-i:\p{Lu})[\p{L}'’ʼ\-]{2,}(?:\s(?-i:\p{Lu})[\p{L}'’ʼ\-]+)?)(?:" + ShotTail + @")\W*$", O);
    static Match PlaceShot(string line)
    {
        var l = Normalize(line).Trim();
        return ShotAt.Match(l) is { Success: true } m ? m : ShotBare.Match(l);
    }
    /// «Той що на Долинське – мінус» → «Долинське», «Ізюм мінус 2» → «Ізюм».
    public static string? ShotPlace(string line) =>
        PlaceShot(line) is { Success: true } m ? m.Groups["place"].Value.Trim() : null;
    /// Скільки збито в такому пості: «Ізюм мінус 2» → 2.
    public static int ShotPlaceCount(string line) =>
        int.TryParse(PlaceShot(line).Groups["n"].Value, out var n) && n > 0 ? n : 1;

    // «2 новые Р. Шаболды», «Новая шаболда», «+2 Р. Шахед», «Ещё 1 шахед» — це інші цілі, а не та, що летіла.
    static readonly Regex NewWord = new(@"^\W*\+\s*\d|(?<!\p{L})(?:ще|ещё|еще)\s+\d|(?<!\p{L})нов(?:ая|ые|ых|ый|а|і|ий|их)\s+(?:р\.|шаб|шах|бпла|реакт|дрон|ціл|цел|ударн)", O);
    public static bool NewTarget(string? text) => text is { Length: > 0 } && NewWord.IsMatch(text);

    // Сказано, що це НЕ реактивний: «ударних БпЛА», «шаболды» (реактивну пишуть «Р. Шаболда»), «звичайний».
    static readonly Regex Plain = new(@"(?<!\p{L})(?:ударн\w*|звичайн\w*|обычн\w*)|(?<!р\.\s?|р\.\s\s|реактивн\w{1,3}\s)шаболд", O);
    public static bool PlainDrone(string? text) => text is { Length: > 0 } && Plain.IsMatch(text);

    /// «По ракетам мінус», «По ракетам на сейчас минус» — ракети закінчились.
    public static bool MissilesDone(string line) =>
        Regex.IsMatch(Normalize(line).Trim(), @"^по\s+ракет\w*\s+(?:(?:наразі|зараз|на\s+сейчас|сейчас)\s+)?мінус\W*$", O);

    // Слова з великої, що не є назвою місця: початок речення.
    static readonly Regex StatusWord = new(@"^(?:кружл\w*|кружляє|почав|почала|досі|знову|так|все|він|вона|також|теж|ще|и|і|а|вже|зараз)$", O);

    /// Короткий статус без місця: «Почав кружляти», «досі кружляє» — про останню ціль каналу.
    /// Є назва («Куликівка - кружляє», «2х Славутич/Чернігів») — це вже звичайний пост про місце.
    public static bool CirclingStatus(string line)
    {
        var l = Normalize(line).Trim();
        if (l.Length > 40 || !Circling(l) || l.Contains('?')) return false;
        if (Regex.IsMatch(l, B + @"(?:над|біля|район\w*|околиц\w*)" + E + @"|сушк|авіац|\b(?:су|ту)-?\d", O)) return false;
        return Regex.Matches(l, @"(?-i:\p{Lu})[\p{L}'’ʼ\-]*").All(m => StatusWord.IsMatch(m.Value));
    }

    // ── Авіація ───────────────────────────────────────────────────────────────
    static readonly Regex Mig = new(@"(?:м[іи]г|mig)[-\s]?31", O);
    static readonly Regex Strategic = new(B + @"(?:ту|tu)[-\s]?(95|160|22)", O);
    static readonly Regex Tactical = new(B + @"(?:су|su)[-\s]?(24|25|30|34|35)", O);

    /// Нормальна назва літака з посту: «миг31к» → «МіГ-31К», «ту 95» → «Ту-95», інакше null.
    public static string? PlaneName(string text)
    {
        if (Mig.IsMatch(text)) return "МіГ-31К";
        if (Strategic.Match(text) is { Success: true } s) return "Ту-" + s.Groups[1].Value;
        return Tactical.Match(text) is { Success: true } t ? "Су-" + t.Groups[1].Value : null;
    }

    /// Підпис для цілі-авіації: МіГ і Ту — своя назва (це не «пуски КАБ»), Су й «петухи» — КАБ.
    public static string AviationLabel(string text) =>
        Mig.IsMatch(text) || Strategic.IsMatch(text) ? PlaneName(text)! : "пуски КАБ";

    static readonly Regex Landed = new(
        @"(?:м[іи]г|mig)[-\s]?31[^.\n]{0,30}(?:сів|сіли|сел|сели|посадк|приземл|на\s+аеродром)|(?:посадк|приземл|відбій|отбой)[^.\n]{0,25}(?:м[іи]г|mig)[-\s]?31", O);
    /// «МіГ-31К сів», «Відбій по МіГу» — загроза «Кинжалів» минула.
    public static bool CarrierLanded(string text) => Landed.IsMatch(text);

    static readonly Regex KabWord = new(B + @"(?:каб|каби|кабы|кабів|кабов|кабами|кабом|фаб|умпк|умпб)" + E, O);
    static readonly Regex RocketWord = new(@"ракет|крилат|крылат|калібр|калибр|х-?\d{2,3}", O);
    static readonly Regex KinzhalWord = new(@"кинжал|кинджал", O);
    static readonly Regex MlrsWord = new(B + @"(?:рсзо|рсзв|смерч|ураган|торнадо)" + E, O);
    static readonly Regex Threatened = new(@"заход\w*\s+на\s+пуск|на\s+пускові|на\s+пусковые|загроз\w*\s+(?:повторн\w*\s+)?пуск|угроз\w*\s+(?:повторн\w*\s+)?пуск", O);
    static readonly Regex DroneLaunch = new(@"шахед|шахід|бпла|дрон|геран|мопед|балалайк", O);

    /// Заголовок і пояснення для поста про пуск / зліт: щоб не було «ПУСКИ РАКЕТ/КАБ»,
    /// а прямо те, що сталось, і що робити саме з цим засобом.
    public static (string head, ThreatKind kind, string advice) LaunchInfo(string text, int? count)
    {
        var low = text.ToLowerInvariant();
        bool launchWord = Regex.IsMatch(low, @"пуск|запуск");
        if (KinzhalWord.IsMatch(low) && launchWord)
            return ("ПУСК «КИНЖАЛА»", ThreatKind.Ballistic, "☄ До цілі лічені хвилини — негайно в укриття.");
        if (Mig.IsMatch(low) || KinzhalWord.IsMatch(low))
            return ("ЗЛІТ МіГ-31К", ThreatKind.Ballistic, "🛫 Носій «Кинжалів». Ракетна небезпека по всій Україні, поки він у повітрі — будьте в укритті.");
        if (Strategic.IsMatch(low) && !launchWord)
            return ($"ЗЛІТ {PlaneName(low)}", ThreatKind.Missile, "🛫 Стратегічна авіація — носії крилатих ракет. Стежте за картою, до пуску ще є час.");
        if (DroneLaunch.IsMatch(low))
            return (count is { } c ? $"ПУСК ~{c} ШАХЕДІВ" : "ПУСК ШАХЕДІВ", ThreatKind.Drone, "⚠️ Летять на Україну — очікуйте появу на карті.");
        bool kab = KabWord.IsMatch(low), rocket = RocketWord.IsMatch(low);
        if (kab && Threatened.IsMatch(low))
            return ("ЗАГРОЗА ПУСКІВ КАБ", ThreatKind.Kab, "💣 Авіація заходить на пуски. КАБ летить 40–70 км — будьте ближче до укриття.");
        if (kab && rocket)
            return ("ПУСКИ КАБ І РАКЕТ", ThreatKind.Missile, "⚠️ Подалі від вікон, в укриття.");
        if (kab)
            return ("ПУСКИ КАБ", ThreatKind.Kab, "💣 КАБ долітає за 1–2 хв — негайно в укриття, подалі від вікон.");
        if (rocket)
            return (count is { } r ? $"ПУСК ~{r} РАКЕТ" : "ПУСКИ РАКЕТ", ThreatKind.Missile, "🚀 Прямують на Україну — очікуйте появу на карті.");
        if (MlrsWord.IsMatch(low))
            return ("ПУСКИ РСЗВ", ThreatKind.Ballistic, "⚠️ Прифронтові громади — негайно в укриття.");
        if (Strategic.IsMatch(low))
            return ($"ПУСК З {PlaneName(low)}", ThreatKind.Missile, "🚀 Прямують на Україну — очікуйте появу на карті.");
        if (Tactical.IsMatch(low))
            return ("АКТИВНІСТЬ ТАКТИЧНОЇ АВІАЦІЇ", ThreatKind.Kab, "✈ Можливі пуски КАБ — будьте ближче до укриття.");
        return ("РАКЕТНА НЕБЕЗПЕКА", ThreatKind.Missile, "⚠️ Будьте в укритті, стежте за картою.");
    }
}
