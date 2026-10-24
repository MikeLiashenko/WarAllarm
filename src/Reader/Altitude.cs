using System.Text.RegularExpressions;

// ── WarAlarm · Висота та «падає» з тексту каналів ────────────────────────────
// Канали моніторингу часто пишуть, як летить ціль: «шахед на висоті ~100 м»,
// «низько», «знижується», «набирає висоту», «падає», «збили / мінус шахед».
// Тут лише читаємо це з тексту; трекер зберігає на цілі, застосунок і бот показують.

public static class Altitude
{
    public readonly record struct Info(double? Meters, string? Note, bool Down);

    static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // «на висоті 150 м», «висота ~2 км», «высоте 300м», «до 50 метрів»
    static readonly Regex Num = new(
        @"(?:висот\w*|высот\w*)\s*(?:близько|приблизно|около|до|~|≈|:|-)?\s*(\d+(?:[.,]\d+)?)\s*(км|м|метр\w*)?", O);
    // «150 м над землею», «100 метрів висоти»
    static readonly Regex NumAfter = new(
        @"(\d+(?:[.,]\d+)?)\s*(км|м|метр\w*)\s*(?:над\s+землею|над\s+землей|висоти|высоты)", O);

    static readonly Regex Shot = new(
        @"\bзбит\w*|\bзбил\w*|\bсбит\w*|\bсбил\w*|знешкодж\w*|\bмінус\s*\d*\s*(?:шахед|шахід|бпла|дрон|ціл|ракет)", O);
    static readonly Regex Falling = new(@"\bпада\w*|\bпадін\w*|\bвпав\b|\bвпало\b|\bупал\w*", O);
    static readonly Regex Diving = new(
        @"знижу\w*|зниженн\w*|сниж\w*|пікіру\w*|пикиру\w*|заходить\s+на\s+ціль|йде\s+на\s+зниж\w*", O);
    static readonly Regex Climbing = new(@"набира\w*\s+висот\w*|набира\w*\s+высот\w*", O);
    static readonly Regex Low = new(
        @"низьк\w*|низько|низко\b|на\s+малій\s+висоті|на\s+малой\s+высоте|бреющ\w*|над\s+самою\s+землею", O);
    static readonly Regex High = new(@"на\s+великій\s+висоті|на\s+большой\s+высоте|високо\b|высоко\b", O);

    /// Що текст каже про висоту / падіння цілі; null — нічого.
    public static Info? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        double? m = null;
        var mm = Num.Match(text);
        if (!mm.Success) mm = NumAfter.Match(text);
        if (mm.Success && double.TryParse(mm.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            var unit = mm.Groups[2].Value.ToLowerInvariant();
            // Без одиниці: «висота 2» — це км, «висота 300» — метри.
            m = unit == "км" || (unit.Length == 0 && v < 30) ? v * 1000 : v;
            if (m is < 5 or > 40000) m = null;
        }

        if (Shot.IsMatch(text)) return new Info(m, "збито", true);
        if (Falling.IsMatch(text)) return new Info(m, "падає", true);
        string? note = Diving.IsMatch(text) ? "знижується"
                     : Climbing.IsMatch(text) ? "набирає висоту"
                     : Low.IsMatch(text) ? "низько"
                     : High.IsMatch(text) ? "високо"
                     : null;
        if (m is null && note == "низько") m = 100; // «низько» без числа ≈ 50–150 м
        return m is null && note is null ? null : new Info(m, note, false);
    }
}
