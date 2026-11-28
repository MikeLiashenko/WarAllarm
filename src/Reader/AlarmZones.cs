// ── 🟢 Ціль не висить у районі без тривоги ────────────────────────────────────────────────────
// 27.11 скарги: «шахеди літають у зонах відбою». Причини (аудит дня за @UkraineAlarmSignal):
//  • затихлі цілі знімали лише при відбої на ВСЮ область, а в Харківській / Дніпропетровській
//    прифронтовий район під тривогою завжди — «Далее Ч. Лозовая» (00:40, Лозівський район у
//    відбої з 00:13) провисів 227 хв, хоча за хвилину канал написав «не фиксируется»;
//  • свіжі пости, що раз у раз оновлюють ціль у районі без тривоги, тримали її там годинами;
//  • «Ціль на Київщині (обл.)» стояла в точці області, яка вже у відбої.
// Правило одне для всього (карта застосунку, сайт, картинки каналів, бот): ціль (шахед,
// реактивний, ракета, FPV, «ціль»), що довше [Grace] (2 хв) стоїть у районі без тривоги, знімається.
// Рух «на місто» зупиняється на межі тривоги (Drawn). Перші 2 хв — даємо: тривогу
// оголошують із запізненням, і раннє попередження не стираємо.
// Аудит 27.11 (audit … з @UkraineAlarmSignal): у зоні відбою було 10,3% хвилин-міток, стало 2,9%
// (затихлих 1016 → 46 хв; решта — перші 2 хв до оголошення тривоги та FPV біля фронту).
// Повторний пост про ту саму ціль (той самий Id) у ще «зеленому» районі не дає нових 2 хв.
public static class AlarmZones
{
    public static TimeSpan Grace = TimeSpan.FromMinutes(double.TryParse(Environment.GetEnvironmentVariable("ZONES_GRACE"), System.Globalization.CultureInfo.InvariantCulture, out var g) ? g : 2);
    static readonly Dictionary<string, DateTime> GreenSince = new();
    static readonly object Gate = new();

    public static bool Applies(Threat t) =>
        !t.Launch && t.Pos is not null && t.Kind is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Missile or ThreatKind.Fpv;

    /// Де мітку видно: DrawPos, але рух «звідки → куди» не далі межі тривоги.
    public static (double lat, double lon)? Drawn(Threat t, DateTime now, Func<double, double, bool> inAlarm)
    {
        if (t.DrawPos(now) is not { } q) return null;
        if (t.Approach is { } a && !inAlarm(q.lat, q.lon) && inAlarm(a.lat, a.lon))
        {
            double lo = 0, hi = 1;
            for (int i = 0; i < 14; i++)
            {
                double m = (lo + hi) / 2;
                if (inAlarm(a.lat + (q.lat - a.lat) * m, a.lon + (q.lon - a.lon) * m)) lo = m; else hi = m;
            }
            q = (a.lat + (q.lat - a.lat) * lo, a.lon + (q.lon - a.lon) * lo);
        }
        return q;
    }

    /// Над окупованою територією й поза Україною тривог не оголошують — там правило не діє.
    static bool Exempt((double lat, double lon) q) => !OblastGeo.InUkraine(q.lat, q.lon) || OblastGeo.Occupied(q.lat, q.lon);

    /// Зняти цілі, що довше [Grace] у районі без тривоги. [inAlarm] — офіційна тривога в точці.
    public static List<Threat> Sweep(ThreatTracker tr, DateTime now, Func<double, double, bool> inAlarm)
    {
        var drop = new HashSet<string>();
        lock (Gate)
        {
            var seen = new HashSet<string>();
            foreach (var t in tr.Active)
            {
                if (!Applies(t) || Drawn(t, now, inAlarm) is not { } q) continue;
                seen.Add(t.Id);
                if (Exempt(q) || inAlarm(q.lat, q.lon)) { GreenSince.Remove(t.Id); continue; }
                if (!GreenSince.TryGetValue(t.Id, out var since)) GreenSince[t.Id] = since = now;
                if (now - since >= Grace) drop.Add(t.Id);
            }
            // Пам'ять про «зелені» Id — щоб повторний пост не давав нових 2 хв; стара — геть.
            foreach (var id in GreenSince.Where(kv => !seen.Contains(kv.Key) && now - kv.Value > TimeSpan.FromMinutes(45)).Select(kv => kv.Key).ToList())
                GreenSince.Remove(id);
        }
        return drop.Count == 0 ? new() : tr.SweepWhere(t => drop.Contains(t.Id));
    }

    public static void Reset() { lock (Gate) GreenSince.Clear(); }
}
