using System.Text.RegularExpressions;

// ── WarAlarm · пуски шахедів і «скільки в повітрі» ───────────────────────────
// Під канал @ukraine_pyxx («Повітряний простір | Напрямок ракет»). Він пише чітко:
//   «18:35 по пусках шахедів:\n4 штуки Брянськ\n10 штук реактивних з Орла …»
//   «+ 17.10 пуск шахедів: 12 штук з Пр.Ахтарська 14 штук з Смоленська …» (в один рядок)
//   «На зараз у повітрі. +- 40 штук» · «під 60 дронів» · «Їх усіх орієнтовно 45»

public record LaunchItem(int N, bool Reactive, string Origin);
public record LaunchBatch(int Total, List<LaunchItem> Items, string? When);

public static class LaunchFeed
{
    // Канонічні назви місць запуску (родовий відмінок у постах → називний).
    static readonly (string stem, string name)[] Origins =
    {
        ("ахтарськ", "Приморсько-Ахтарськ"), ("смоленськ", "Смоленськ"), ("курськ", "Курськ"),
        ("міллеров", "Міллерово"), ("брянськ", "Брянськ"), ("орл", "Орел"), ("орел", "Орел"),
        ("крим", "Крим"), ("шаталов", "Шаталово"), ("навл", "Навля"), ("гвардійськ", "Гвардійське"),
        ("донецьк", "Донецьк"), ("бєлгород", "Бєлгород"), ("білгород", "Бєлгород"), ("чауд", "Чауда"),
    };

    static string Canon(string raw)
    {
        var l = raw.ToLowerInvariant();
        foreach (var (stem, name) in Origins) if (l.Contains(stem)) return name;
        return raw.Trim(' ', '.', ',', ';', ':');
    }

    /// Пост про пуск шахедів → хвиля з розбивкою. null, якщо це не пуск.
    public static LaunchBatch? ParseLaunch(string post)
    {
        var low = post.ToLowerInvariant();
        if (!Regex.IsMatch(low, @"пуск|запуск|запустил")) return null;
        if (!Regex.IsMatch(low, @"шахед|шахід|дрон|бпла|герань")) return null;

        // «N штук [реактивних|звичайних] [з] Місто»
        var items = new List<LaunchItem>();
        foreach (Match mt in Regex.Matches(post,
            @"(\d{1,3})\s*шт\w*\.?\s*(реактивн\w*|звичайн\w*)?\s*(?:з|із|зі)?\s*([А-ЯІЇЄҐA-Z][^\s\d,;:]*(?:[\s\-.]+[А-ЯІЇЄҐ][^\s\d,;:]*)?)(\s+реактивн\w*)?",
            RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(mt.Groups[1].Value, out var n) || n is <= 0 or > 300) continue;
            bool reactive = mt.Groups[2].Value.StartsWith("реакт", StringComparison.OrdinalIgnoreCase)
                            || mt.Groups[4].Success // «4 штуки з Криму реактивні» — тип ПІСЛЯ міста
                            || mt.Groups[3].Value.Contains("реактивн", StringComparison.OrdinalIgnoreCase);
            items.Add(new(n, reactive, Canon(mt.Groups[3].Value)));
        }
        if (items.Count == 0) return null;

        // Зливаємо однакові «місце+тип» (буває, що одне місце двічі).
        var merged = items.GroupBy(i => (i.Origin, i.Reactive))
                          .Select(g => new LaunchItem(g.Sum(x => x.N), g.Key.Reactive, g.Key.Origin))
                          .OrderByDescending(i => i.N).ToList();
        var when = Regex.Match(post, @"\b(\d{1,2}:\d{2})\b") is { Success: true } wm ? wm.Groups[1].Value : null;
        return new(merged.Sum(i => i.N), merged, when);
    }

    /// «Скільки зараз у повітрі» з поста-оцінки. null — якщо це не такий пост.
    public static int? ParseAirborne(string post)
    {
        var low = post.ToLowerInvariant();
        if (!Regex.IsMatch(low, @"на зараз|у повітрі|в повітрі|їх усіх|усього|всього")) return null;
        if (!Regex.IsMatch(low, @"дрон|шахед|шахід|бпла|штук|їх усіх|орієнтовно")) return null;
        var m = Regex.Match(low, @"(?:\+-|\+/-|±|~|під|орієнтовно|близько|приблизно|понад)\s*(\d{1,3})");
        if (!m.Success) m = Regex.Match(low, @"(\d{1,3})\s*(?:штук|дрон|шахед|бпла)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is > 0 and <= 600 ? n : null;
    }
}
