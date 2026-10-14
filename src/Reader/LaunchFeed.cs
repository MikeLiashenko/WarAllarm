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
        ("саки", "Саки"), ("єйськ", "Єйськ"), ("ростов", "Ростов"), ("воронеж", "Воронеж"), ("тамбов", "Тамбов"),
        ("ліпецьк", "Ліпецьк"), ("калуг", "Калуга"), ("шайков", "Шайковка"), ("халін", "Халіно"),
        ("морозовськ", "Морозовськ"), ("енгельс", "Енгельс"), ("каспі", "Каспій"), ("азов", "Азовське море"),
        ("макіїв", "Макіївка"), ("горлівк", "Горлівка"), ("луганськ", "Луганськ"), ("таганрог", "Таганрог"),
    };

    // Координати місць пуску (для ліній на карті підсумку ночі). Ті, що далеко за картою
    // (Каспій, Енгельс), притиснуті до східного краю — щоб лінія йшла «звідти».
    static readonly Dictionary<string, (double lat, double lon)> SiteXY = new()
    {
        ["Приморсько-Ахтарськ"] = (46.04, 38.17), ["Смоленськ"] = (54.78, 32.05), ["Курськ"] = (51.73, 36.19),
        ["Міллерово"] = (48.92, 40.39), ["Брянськ"] = (53.24, 34.36), ["Орел"] = (52.97, 36.06),
        ["Крим"] = (45.10, 34.00), ["Шаталово"] = (54.37, 32.48), ["Навля"] = (52.83, 34.50),
        ["Гвардійське"] = (45.11, 33.98), ["Донецьк"] = (48.00, 37.80), ["Бєлгород"] = (50.60, 36.60),
        ["Чауда"] = (45.00, 35.84), ["Саки"] = (45.08, 33.56), ["Єйськ"] = (46.71, 38.27),
        ["Ростов"] = (47.24, 39.71), ["Воронеж"] = (51.67, 39.18), ["Тамбов"] = (52.72, 41.45),
        ["Ліпецьк"] = (52.61, 39.60), ["Калуга"] = (54.51, 36.26), ["Шайковка"] = (54.23, 34.37),
        ["Халіно"] = (51.75, 36.30), ["Морозовськ"] = (48.35, 41.82), ["Таганрог"] = (47.21, 38.94),
        ["Азовське море"] = (46.20, 36.80), ["Макіївка"] = (48.05, 37.93), ["Горлівка"] = (48.33, 38.05),
        ["Луганськ"] = (48.57, 39.31), ["Каспій"] = (47.00, 41.90), ["Енгельс"] = (51.40, 41.90),
    };
    public static (double lat, double lon)? SiteCoords(string name) => SiteXY.TryGetValue(name, out var c) ? c : null;
    public static IEnumerable<string> AllSites => SiteXY.Keys;

    /// Усі відомі місця пуску, згадані в тексті («балістика з Брянська і Курська»).
    public static List<string> OriginsIn(string text)
    {
        var l = text.ToLowerInvariant();
        return Origins.Where(o => l.Contains(o.stem)).Select(o => o.name).Distinct().ToList();
    }

    // Відоме місце → канон; невідоме → null (крім явного «з Місто» з великої літери).
    static string? Canon(string raw, bool afterZ)
    {
        var l = raw.ToLowerInvariant();
        foreach (var (stem, name) in Origins) if (l.Contains(stem)) return name;
        var t = raw.Trim(' ', '.', ',', ';', ':');
        return afterZ && t.Length >= 3 && char.IsUpper(t[0]) && !t.Contains('?') ? t : null;
    }

    /// Пост про пуск шахедів → хвиля з розбивкою. null, якщо це не пуск.
    public static LaunchBatch? ParseLaunch(string post)
    {
        var low = post.ToLowerInvariant();
        // Саме «пуск(и/ах) шахедів/дронів» — а не будь-яке «запускаємо» в поясненнях.
        if (!Regex.IsMatch(low, @"пуск\w*\s+(шахед|шахід|дрон|бпла|герань|ударн)")) return null;

        // «N штук [реактивних|звичайних] [з] Місто»
        var items = new List<LaunchItem>();
        foreach (Match mt in Regex.Matches(post,
            @"(\d{1,3})\s*(?:шт\w*\.?\s*)?(реактивн\w*|звичайн\w*)?\s*(з|із|зі)?\s*([А-ЯІЇЄҐA-Z][^\s\d,;:]*(?:[\s\-.]+[А-ЯІЇЄҐ][^\s\d,;:]*)?)(\s+реактивн\w*)?",
            RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(mt.Groups[1].Value, out var n) || n is <= 0 or > 300) continue;
            if (Canon(mt.Groups[4].Value, afterZ: mt.Groups[3].Success) is not { } origin) continue; // не аеродром
            bool reactive = mt.Groups[2].Value.StartsWith("реакт", StringComparison.OrdinalIgnoreCase)
                            || mt.Groups[5].Success // «4 штуки з Криму реактивні» — тип ПІСЛЯ міста
                            || mt.Groups[4].Value.Contains("реактивн", StringComparison.OrdinalIgnoreCase);
            items.Add(new(n, reactive, origin));
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
