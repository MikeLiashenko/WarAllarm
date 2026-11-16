// ── WarAlarm · ⭐ Небезпека надворі: зірки 0–5 ────────────────────────────────
// Наскільки небезпечно ЗАРАЗ бути на вулиці в точці (дім / місце користувача).
// Порт 1:1 з app/lib/domain/danger_stars.dart — ті самі зірки в боті й застосунку.
// Міняєш правила тут — міняй і там.
//
// Кожна ціль дає зірки за типом і відстанню до точки:
//                         далеко   поруч    досяжність  «поруч»
//   шахед / невідома ціль   1        2        40 км       12 км
//   реактивний шахед        2        3        60 км       20 км
//   ФПВ / Ланцет            2        3        20 км        8 км
//   КАБ                     3        4        60 км       25 км
//   крилата ракета          3        4       100 км       30 км
//   балістика               4        5       200 км       80 км
// «Поруч» — ближче за свій радіус «поруч» АБО курс прямо на точку (мимо ≤ 8 км, ближче
// 2.5 × радіуса). Удаляється (пролетіла) — на зірку менше. Вибух поруч — ще +1.
// Беремо найнебезпечнішу ціль; 5+ бортів у досяжності — ще +1.
// Авіація над областю: загроза КАБ — 3, лише літаки — 2. Загроза балістики в області (☄,
// BallisticAlert) — щонайменше 4. Офіційна тривога — щонайменше 1.
public static class DangerStars
{
    public sealed record Result(int Stars, string Level, string Advice, string Reason);

    sealed record Spec(int Far, int Near, double ReachKm, double NearKm);

    static Spec SpecOf(ThreatKind kind, string? ammo)
    {
        double kmh = ThreatGeo.SpeedKmh(kind, ammo);
        return kind switch
        {
            ThreatKind.Ballistic => new(4, 5, 200, 80),
            // РСЗВ б'є на 20–40 км від фронту: небезпечно лише поруч.
            ThreatKind.Missile when (ammo ?? "").StartsWith("РСЗВ", StringComparison.OrdinalIgnoreCase) => new(3, 4, 40, 15),
            ThreatKind.Missile => new(3, 4, 100, 30),
            ThreatKind.Kab => new(3, 4, 60, 25),
            ThreatKind.Aviation => new(2, 3, 80, 30),
            ThreatKind.Fpv => new(2, 3, 20, 8),
            _ when kmh >= 350 => new(2, 3, 60, 20), // реактивний шахед
            _ => new(1, 2, 40, 12),                 // шахед / ціль невідомого типу
        };
    }

    /// Зірки однієї цілі для точки (0 — не стосується).
    static int ForTarget(Threat t, (double lat, double lon) p, out double km)
    {
        km = double.MaxValue;
        if (t.Pos is not { } pos || t.Approx || t.Down) return 0;
        var s = SpecOf(t.Kind, t.Ammo);
        km = ThreatGeo.Km(pos, p);
        if (km > s.ReachKm) return 0;
        bool near = km <= s.NearKm;
        bool away = t.Stage == TrackStage.Passed;
        if (!near && t.Heading is { } hd && km <= s.NearKm * 2.5)
        {
            var (minKm, approaching) = Closest(pos, hd, p);
            if (approaching && minKm <= 8) near = true;
            else if (!approaching && t.HeadingKnown) away = true;
        }
        int stars = near ? s.Near : s.Far;
        if (t.Stage == TrackStage.Impact && km <= s.NearKm) stars++;
        if (away) stars--;
        return Math.Clamp(stars, 0, 5);
    }

    // Найменша відстань курсу до точки + чи летить до неї (як RiskModel.Closest).
    static (double minKm, bool approaching) Closest((double lat, double lon) pos, double hd, (double lat, double lon) p)
    {
        double la = p.lat * Math.PI / 180;
        double dN = (pos.lat - p.lat) * 111, dE = (pos.lon - p.lon) * 111 * Math.Cos(la);
        double th = hd * Math.PI / 180;
        double rE = -dE, rN = -dN, proj = rE * Math.Sin(th) + rN * Math.Cos(th);
        double min = proj <= 0 ? Math.Sqrt(rE * rE + rN * rN)
            : Math.Sqrt(Math.Max(0, rE * rE + rN * rN - proj * proj));
        return (min, proj > 0);
    }

    public static Result For((double lat, double lon) p, IEnumerable<Threat> threats,
        AviationZone? aviation = null, bool officialAlarm = false, (bool on, string? from) ballistic = default)
    {
        int best = 0, inReach = 0;
        string reason = "";
        foreach (var t in threats)
        {
            int s = ForTarget(t, p, out var km);
            if (s <= 0) continue;
            inReach += t.Amount;
            if (s > best) { best = s; reason = $"{ThreatTracker.Label(t)}{(t.Amount > 1 ? $" ×{t.Amount}" : "")} · {km:0} км"; }
        }
        if (best > 0 && inReach >= 5) { best = Math.Min(5, best + 1); reason += $" · у повітрі поруч {inReach}"; }
        if (aviation is { } av)
        {
            int s = av.Kab ? 3 : 2;
            if (s > best) { best = s; reason = av.Kab ? "Загроза КАБ над областю" : "Тактична авіація над областю"; }
        }
        if (ballistic.on && best < 4)
        {
            best = 4;
            reason = "Загроза балістики" + (ballistic.from is { Length: > 0 } bf ? $" · район пуску: {bf}" : "");
        }
        if (officialAlarm && best < 1) { best = 1; reason = "Повітряна тривога, цілей поруч не видно"; }
        var (level, advice) = Describe(best);
        return new Result(best, level, advice, reason);
    }

    public static (string level, string advice) Describe(int stars) => stars switch
    {
        >= 5 => ("Критична", "Негайно в укриття! Подалі від вікон."),
        4 => ("Дуже висока", "Не виходь надвір — іди в укриття."),
        3 => ("Висока", "Надворі небезпечно: залишайся в приміщенні, правило двох стін."),
        2 => ("Помірна", "Скороти час надворі й знай, де найближче укриття."),
        1 => ("Низька", "Можна виходити, але не затримуйся й тримай звук увімкненим."),
        _ => ("Спокійно", "Загроз поруч немає — можна гуляти."),
    };

    /// «★★★☆☆» — для бота й каналів.
    public static string Bar(int stars) => new string('★', stars) + new string('☆', 5 - stars);

    /// Рядок для бота: «⭐ Небезпека надворі: ★★★☆☆ 3/5 — висока».
    public static string Line(Result r) =>
        $"⭐ <b>Небезпека надворі: {Bar(r.Stars)} {r.Stars}/5</b> — {r.Level.ToLowerInvariant()}\n"
        + (r.Reason.Length > 0 ? $"      {System.Net.WebUtility.HtmlEncode(r.Reason)}\n" : "")
        + $"➤ {r.Advice}";
}
