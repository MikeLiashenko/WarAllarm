using System.Text.RegularExpressions;

// ── WarAlarm · світло (відключення/відновлення) ──────────────────────────────
// Під канал @esvitlo_kyiv_ua («єСвітло Київ»). Розбирає пост у подію про світло:
//   Off      — зникло/відключили/аварійні/екстрені відключення;
//   On       — з'явилося/відновлено/повернули світло;
//   Groups   — перемикання ЧЕРГ: «🔋 Вмикаємо групи: 2, 6…», «🪫 Вимикаємо групу 11»;
//   Schedule — графік відключень (інформативно);
//   None     — реклама/новини/транспорт тощо.
// Плюс визначає район Києва; якщо названо ТВІЙ район — він у пріоритеті.

public enum PowerKind { None, Off, On, Groups, Schedule }

public record PowerEvent(PowerKind Kind, string Area, string Raw,
                         int[] GroupsOn, int[] GroupsOff, bool MyArea, string? When);

public static class PowerMonitor
{
    // Підпис каналу містить «єСвітло»/«Є світло» — прибираємо, щоб не вважати «світло є».
    static string Clean(string s) => Regex.Replace(s,
        @"єсвітло|є\s*світло|🅱️|відправляйте друзям[^\n]*|багато хто не знає[^\n]*",
        " ", RegexOptions.IgnoreCase);

    static readonly (string re, string name)[] Areas =
    {
        (@"дарницьк", "Дарницький"), (@"деснянськ", "Деснянський"), (@"голосіївськ", "Голосіївський"),
        (@"печерськ", "Печерський"), (@"шевченківськ", "Шевченківський"), (@"святошинськ", "Святошинський"),
        (@"оболонськ|оболонь", "Оболонь"), (@"подільськ|\bподіл\b", "Поділ"), (@"солом.?янськ", "Солом'янський"),
        (@"дніпровськ", "Дніпровський"), (@"правому берез|правий берег|правобереж", "Правий берег"),
        (@"лівому берез|лівий берег|лівобереж", "Лівий берег"), (@"бровар", "Бровари"),
        (@"київщин|київськ\w*\s+обл", "Київська обл."),
    };

    // Номери груп із рядка; час «11:30» спершу вирізаємо, щоб не став групами 11/30.
    static int[] Nums(string s) =>
        Regex.Matches(Regex.Replace(s, @"\d{1,2}[:.]\d{2}", " "), @"\b\d{1,2}\b").Select(m => int.Parse(m.Value)).Where(n => n is >= 1 and <= 99).Distinct().ToArray();

    /// [myDistrict] — твій район («Голосіївський»): якщо він згаданий, Area = він, MyArea = true.
    public static PowerEvent Analyze(string post, string? myDistrict = null)
    {
        var raw = post.Replace("\n", " ").Trim();
        var cleaned = Clean(post);
        var l = cleaned.ToLowerInvariant();

        // Транспорт/реклама/загальні новини — НЕ про домашнє світло.
        if (Regex.IsMatch(l, @"електротранспорт|тролейбус|трамва|контактн\w*\s+мереж|київпастранс|призупинено рух|ecoflow|bluetti|jackery|реклам|ремонт станц|прошивк|інвертор"))
            return new(PowerKind.None, "Київ", raw, [], [], false, null);

        // ── Перемикання черг: «🔋 Вмикаємо групи: 2, 6», «🪫 Вимикаємо групу 11» ──
        // «Залишаються відключеними…» — це статус, а не перемикання → не рахуємо.
        var on = new List<int>(); var off = new List<int>();
        foreach (var line in cleaned.Replace("\r", "").Split('\n'))
        {
            var ll = line.ToLowerInvariant();
            if (!ll.Contains("груп") || ll.Contains("залиша")) continue;
            if (Regex.IsMatch(ll, @"вмика|увімкн|вмикн")) on.AddRange(Nums(line));
            else if (Regex.IsMatch(ll, @"вимика|вимкн|відключа")) off.AddRange(Nums(line));
        }
        var when = Regex.Match(l, @"\bо\s*(\d{1,2}:\d{2})") is { Success: true } wm ? wm.Groups[1].Value : null;
        if (on.Count > 0 || off.Count > 0)
            return new(PowerKind.Groups, "Київ", raw, on.Distinct().ToArray(), off.Distinct().ToArray(), false, when);

        PowerKind kind;
        if (Regex.IsMatch(l, @"графік\w*\s+(відключ|погодинн)"))
            kind = PowerKind.Schedule;
        else if (Regex.IsMatch(l,
            @"зникл\w*\s+світл|знеструмл|без світла|без електрик|нема\w*\s+світла|відсутн\w*\s+(світл|напруг|електро)"
          + @"|аварійн\w*\s+відключ|екстрен\w*\s+відключ|стабілізаційн\w*\s+відключ|застосован\w*\s+відключ"
          + @"|буде відключено|відключення електро|відключення світл|вимика\w*\s+світл"))
            kind = PowerKind.Off;
        else if (Regex.IsMatch(l,
            @"з.?явил\w*\s+світл|відновл\w*\s+(світл|електро|живлен|постачанн|напруг)"
          + @"|повернул\w*\s+світл|повернулося світл|подали світл|увімкн\w*\s+світл|вмика\w*\s+світл"))
            kind = PowerKind.On;
        else
            kind = PowerKind.None;

        // Район: якщо згадано ТВІЙ — він у пріоритеті (навіть коли перелічено кілька).
        bool mine = false;
        string area = "";
        if (myDistrict is { Length: > 3 })
        {
            var stem = Regex.Replace(myDistrict.ToLowerInvariant(), @"ськ\w*$", "");
            if (stem.Length >= 4 && l.Contains(stem)) { area = myDistrict; mine = true; }
        }
        if (area.Length == 0)
            foreach (var (re, name) in Areas)
                if (Regex.IsMatch(l, re)) { area = name; break; }
        if (area.Length == 0) area = "Київ";

        return new(kind, area, raw, [], [], mine, when);
    }
}
