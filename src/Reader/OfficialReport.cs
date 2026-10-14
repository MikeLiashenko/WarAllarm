using System.Text.RegularExpressions;

// ── WarAlarm · ранковий звіт Повітряних Сил (@kpszsu) ─────────────────────────
// «У ніч на 14 жовтня (з 18:00 13 жовтня)… зафіксовано 167 засобів повітряного
//  нападу – 39 ракет і 128 БпЛА: - 39 ракет Іскандер-М/KN-23/… (райони пуску –
//  Курська, Брянська, Воронізька, Ростовська обл.); - 128 ударних БпЛА (в т.ч. 77
//  реактивних) … (із напрямків: Міллерово, Орел, ТОТ Донецьк, ТОТ АР Крим…); …
//  збито/подавлено 119 … влучання … на 22 локаціях».
// Звідси беремо ОФІЦІЙНІ райони пуску ракет і напрямки запуску БпЛА для карти ночі.

public record OfficialReport(int Missiles, int Drones, int Reactive, int Down, int Hits,
                             List<string> MissileSites, List<string> DroneSites, string MissileTypes);

public static class OfficialReports
{
    static int Num(string text, string pattern) =>
        Regex.Match(text, pattern, RegexOptions.IgnoreCase) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;

    /// null — якщо це не підсумковий нічний звіт.
    public static OfficialReport? Parse(string text)
    {
        var t = text.Replace("\n", " ");
        if (!Regex.IsMatch(t, @"засоб\w*\s+повітряного\s+нападу", RegexOptions.IgnoreCase)) return null;
        if (!Regex.IsMatch(t, @"у ніч на|з \d{1,2}:\d{2}", RegexOptions.IgnoreCase)) return null;

        int missiles = 0, drones = 0;
        if (Regex.Match(t, @"(\d+)\s+ракет\w*\s+(?:і|та)\s+(\d+)\s+(?:ударних\s+)?БпЛА", RegexOptions.IgnoreCase) is { Success: true } mm)
        { missiles = int.Parse(mm.Groups[1].Value); drones = int.Parse(mm.Groups[2].Value); }
        else
        {
            missiles = Num(t, @"-\s*(\d+)\s+(?:балістичн\w*\s+|крилат\w*\s+)?ракет");
            drones = Num(t, @"-\s*(\d+)\s+ударних\s+БпЛА");
        }

        // «(райони пуску – Курська, Брянська …)» — для ракет; «(із напрямків: …)» — для БпЛА.
        var mSites = new List<string>(); var dSites = new List<string>();
        foreach (Match m in Regex.Matches(t, @"\((?:райони?|район)\s+пуску\s*[–\-:]\s*([^)]*)\)", RegexOptions.IgnoreCase))
            mSites.AddRange(LaunchFeed.OriginsIn(m.Groups[1].Value));
        foreach (Match m in Regex.Matches(t, @"\(із\s+напрямк\w*\s*[–\-:]?\s*([^)]*)\)", RegexOptions.IgnoreCase))
            dSites.AddRange(LaunchFeed.OriginsIn(m.Groups[1].Value));
        // «ТОТ АР Крим - Гвардійське» дає і «Крим», і «Гвардійське» — лишаємо конкретне.
        if (dSites.Contains("Гвардійське")) dSites.Remove("Крим");
        if (mSites.Contains("Гвардійське")) mSites.Remove("Крим");

        var types = Regex.Match(t, @"-\s*\d+\s+(?:балістичн\w*\s+)?ракет\w*\s+([^(;]+)\(", RegexOptions.IgnoreCase) is { Success: true } tm
            ? tm.Groups[1].Value.Trim() : "";

        return new(missiles, drones,
            Reactive: Num(t, @"в\s+т\.\s*ч\.\s*(\d+)\s+реактивн"),
            Down: Num(t, @"збито\s*/?\s*(?:подавлено)?\s+(\d+)\s+(?:цілей|ворож)"),
            Hits: Num(t, @"на\s+(\d+)\s+локаці"),
            MissileSites: mSites.Where(s => LaunchFeed.SiteCoords(s) is not null).Distinct().ToList(),
            DroneSites: dSites.Where(s => LaunchFeed.SiteCoords(s) is not null).Distinct().ToList(),
            MissileTypes: types);
    }
}
