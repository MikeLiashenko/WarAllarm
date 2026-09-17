using System.Text.RegularExpressions;

// ── WarAlarm · разбор целей из постов Telegram-канала ─────────────────────────
// Превращает свободный текст поста в список структурированных угроз (ThreatEvent).
// Пишется под формат @raketa_trevoga; легко расширяется на другие каналы.

public enum ThreatKind { Drone, Kab, Ballistic, Missile, Aviation, Target, AllClear, Unknown }

public enum RelLevel { None, Oblast, City }

public record ThreatEvent(ThreatKind Kind, string? Oblast, string? City, int Count, string? Extra, string Raw);

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
        return Oblasts.TryGetValue(s, out var v) ? v : null;
    }
}

public static class ThreatParser
{
    // Усі ходові назви дронів/шахедів/приманок/розвідників (сленг каналів) — ОДНЕ джерело
    // правди для всіх трьох парсерів (ThreatParser / KyivMonitor / KharkivMonitor).
    // шахед · герань(geran) · гербера-пастка · бандероль · мопед · FPV · молнія · шаболда ·
    // ланцет · мавік · орлан · зала · supercam.
    public const string DroneRe =
        @"бпла|дрон|шахед|шахід|shahed|геран|geran|гербер|бандерол|мопед|\bfpv\b|молні|шаболд|ланцет|мавік|mavic|орлан|\bзала\b|supercam|суперкам";

    // Строки-шум: подписки, ссылки, донаты, комментарии к фото, новости о прошлых ударах.
    static readonly string[] Noise =
    {
        "підписат", "підтримати", "t.me/", "http", "більше фото", "стежити за обстановк",
        "ситуація по", "видно вам", "буду вдячний", "monobank", "попередньо", "завдали удару"
    };

    public static List<ThreatEvent> Parse(string post)
    {
        var events = new List<ThreatEvent>();
        string? oblast = null; // текущий контекст области (из заголовка "Xщина:")

        foreach (var raw in post.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var low = line.ToLowerInvariant();
            if (Noise.Any(n => low.Contains(n))) continue;

            // Отбой
            if (low.Contains("відбій") || low == "чисто" || low.StartsWith("чисто"))
            {
                events.Add(new(ThreatKind.AllClear, null, null, 0, low.Contains("балісти") ? "балістика" : null, line));
                continue;
            }
            // Балистическая угроза (по направлению, без города)
            if (low.Contains("балісти"))
            {
                var mz = Regex.Match(line, @"\bз\s+(.+)$", RegexOptions.IgnoreCase);
                events.Add(new(ThreatKind.Ballistic, null, null, 0, mz.Success ? mz.Groups[1].Value.Trim() : null, line));
                continue;
            }
            // Авиация
            if (low.Contains("актив") && Regex.IsMatch(line, @"(су|ту|міг|mig)[-\s]?\d", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(line, @"((су|ту|міг|mig)[-\s]?\d+)", RegexOptions.IgnoreCase);
                events.Add(new(ThreatKind.Aviation, null, null, 0, m.Value, line));
                continue;
            }
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
            // Обычная строка цели: "[N] ТИП на Місто"
            var t = ParseTargetLine(line, oblast);
            if (t != null) events.Add(t);
        }

        return events;
    }

    static ThreatEvent? ParseTargetLine(string line, string? oblast)
    {
        int count = 1;
        // Кол-во в начале строки: «3 БпЛА» и множитель «2х/2x/2×» (формат @rozvidkaneba).
        var mc = Regex.Match(line, @"^\s*(\d+)\s*[хx×]?\s*(?=\S)");
        if (mc.Success) int.TryParse(mc.Groups[1].Value, out count);

        var kind = DetectKind(line);
        // «Реактивний» шахед летит ~500 км/ч (обычный ~180) — помечаем для верной ETA.
        string? extra = Regex.IsMatch(line, "реактивн", RegexOptions.IgnoreCase) ? "реакт. шахед" : null;

        var mn = Regex.Match(line, @"\bна\s+(.+)$", RegexOptions.IgnoreCase);
        string? city = mn.Success ? mn.Groups[1].Value.Trim().TrimEnd('.', ' ') : null;
        // Отсекаем «хвост» после названия: «на Васильків повз Борову», «на X у напрямку Y»…
        if (city != null)
            city = Regex.Split(city, @"\s+(повз|через|у напрямку|в напрямку|курс|напрямок)\b", RegexOptions.IgnoreCase)[0].Trim();

        if (city == null && kind == ThreatKind.Unknown) return null; // не похоже на цель
        return new(kind == ThreatKind.Unknown ? ThreatKind.Target : kind, oblast, city, count, extra, line);
    }

    static ThreatKind DetectKind(string line)
    {
        var l = line.ToLowerInvariant();
        if (Regex.IsMatch(l, DroneRe)) return ThreatKind.Drone;
        if (l.Contains("каб") || l.Contains("умпк") || l.Contains("умпб") || Regex.IsMatch(l, @"фаб[-\s]?\d")) return ThreatKind.Kab;
        if (Regex.IsMatch(l, @"балісти|кн-?23|точка[-\s]?у")) return ThreatKind.Ballistic;
        if (Regex.IsMatch(l, @"ракет|крилат|калібр|кинджал|іскандер|циркон|онікс|onyx|х[-\s]?\d{2,3}|с[-\s]?300")) return ThreatKind.Missile;
        if (l.Contains("ціль")) return ThreatKind.Target;
        return ThreatKind.Unknown;
    }

    // ── помощники для демонстрации/фильтра ───────────────────────────────────
    public static string Icon(ThreatKind k) => k switch
    {
        ThreatKind.Drone => "🛩",
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
