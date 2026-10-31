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

public static class EveningOutlook
{
    static readonly object Gate = new();
    static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static DateTime _evening = DateTime.MinValue;              // вечір, до якого належить оцінка
    static readonly List<(int n, string kind, string where)> Stocks = new();
    static string? _regions, _strat, _stratReady, _sea;
    static int? _ballProb, _stratProb, _salvo;
    static DateTime _updated = DateTime.MinValue;

    /// Вечір, до якого належить момент [at]: до 12:00 — ще попередній вечір.
    public static DateTime EveningOf(DateTime at) => at.Hour < 12 ? at.Date.AddDays(-1) : at.Date;

    public static DateTime Updated { get { lock (Gate) return _updated; } }
    public static DateTime Evening { get { lock (Gate) return _evening; } }

    static readonly Regex Outlook = new(@"по загрозах на (?:цю|сьогоднішню|наступну) ніч|загроз\w* на (?:цю )?ніч|оцінка діяльності ворожої авіації|ймовірність застосування балістичн", O);
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
                strat = Regex.IsMatch(l, "без загроз", O) ? "без загроз" : l.TrimEnd('.');
        }
        var all = string.Join(" ", lines);
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

        bool any = stocks.Count > 0 || regions != null || strat != null || ballProb != null || stratProb != null || salvo != null || sea != null;
        if (!any) return false;
        lock (Gate)
        {
            var ev = EveningOf(at);
            if (ev != _evening)
            {
                _evening = ev; Stocks.Clear();
                _regions = _strat = _stratReady = _sea = null; _ballProb = _stratProb = _salvo = null;
            }
            // Нова оцінка запасів замінює стару (а не додається до неї).
            if (stocks.Count > 0) { Stocks.Clear(); Stocks.AddRange(stocks); }
            _regions = regions ?? _regions; _strat = strat ?? _strat; _stratReady = stratReady ?? _stratReady; _sea = sea ?? _sea;
            _ballProb = ballProb ?? _ballProb; _stratProb = stratProb ?? _stratProb; _salvo = salvo ?? _salvo;
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
             : l.Contains("400") ? "С-400"
             : l.Contains("300") ? "С-300"
             : "крилаті";
    }

    // «Брянській області» → «Брянщина», «в межах Курської області» → «Курщина», «Криму» → «Крим».
    static string Region(string s)
    {
        s = s.Trim().TrimEnd('.', ',');
        if (Regex.IsMatch(s, @"^крим", O)) return "Крим";
        if (Regex.IsMatch(s, @"^воронез", O)) return "Воронежчина";
        var m = Regex.Match(s, @"^(\p{L}+?)(?:ській|ської|ська|ську|цькій|цької)\s+област", O);
        if (m.Success) return m.Groups[1].Value + "щина";
        return s.Length > 30 ? s[..30] : s;
    }

    /// Чи є що сказати про вечір [evening].
    public static bool Has(DateTime evening) { lock (Gate) return _evening == evening && _updated != DateTime.MinValue; }

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
                outp.Add("🚀 Балістика: " + string.Join(" · ", parts));
            }
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
            if (launched is { Length: > 0 }) outp.Add($"🛵 Шахеди: {launched}");
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
