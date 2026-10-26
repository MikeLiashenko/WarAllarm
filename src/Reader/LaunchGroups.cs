using System.Globalization;
using System.Text;

// ── WarAlarm · групи пуску в польоті (оцінка) ────────────────────────────────
// Пост «пуск 8 реактивних з Орла» — одразу на карті: група ×8 вилітає з Орла й летить
// типовим маршрутом (TypicalRoutes) зі швидкістю шахеда. Щойно канали пишуть про
// справжні цілі поблизу того місця, де група мала б бути, — вони «забирають» дрони
// з групи (×N зменшується), і далі на карті вже справжні цілі. Група зникає, коли її
// розібрали, долетіла до кінця маршруту або минуло 8 год.

public sealed class LaunchGroup
{
    public required string Id;
    public required string Origin;
    public int N, Left;
    public bool Reactive;
    public DateTime T0;
    public required List<(double lat, double lon)> Route;
    public double Kmh => Reactive ? 500 : 185;
    public double LengthKm => TypicalRoutes.LengthKm(Route);
    /// Де група мала б бути зараз (у середньому шахеди йдуть ~85 % швидкості по прямій).
    public (double lat, double lon) PosAt(DateTime now) =>
        TypicalRoutes.At(Route, Kmh * 0.85 * Math.Max(0, (now - T0).TotalHours));
    public bool Arrived(DateTime now) => Kmh * 0.85 * (now - T0).TotalHours >= LengthKm;
}

public static class LaunchGroups
{
    static readonly object Gate = new();
    static readonly List<LaunchGroup> Groups = new();
    static int _seq;

    public static List<LaunchGroup> Active { get { lock (Gate) return Groups.ToList(); } }

    /// Нова хвиля пусків → групи по місцях пуску (окремо реактивні й звичайні).
    public static void Add(LaunchBatch b, DateTime at)
    {
        lock (Gate)
            foreach (var i in b.Items)
            {
                if (TypicalRoutes.FromOrigin(i.Origin) is not { } route) continue;
                Groups.Add(new LaunchGroup
                {
                    Id = $"L{++_seq}:{i.Origin}{(i.Reactive ? ":R" : "")}", Origin = i.Origin,
                    N = i.N, Left = i.N, Reactive = i.Reactive, T0 = at, Route = route,
                });
            }
    }

    /// Справжня ціль-дрон з'явилась у [pos] — забираємо її з найближчої групи, що
    /// мала б бути поруч (≤150 км від своєї розрахункової точки).
    public static void Absorb((double lat, double lon) pos, int count, DateTime now)
    {
        lock (Gate)
        {
            Prune(now);
            var g = Groups.Select(x => (x, km: ThreatGeo.Km(x.PosAt(now), pos)))
                          .Where(x => x.km <= 150).OrderBy(x => x.km).Select(x => x.x).FirstOrDefault();
            if (g is null) return;
            g.Left -= Math.Max(1, count);
            if (g.Left <= 0) Groups.Remove(g);
        }
    }

    static void Prune(DateTime now) =>
        Groups.RemoveAll(g => g.Left <= 0 || g.Arrived(now) || now - g.T0 > TimeSpan.FromHours(8));

    /// state.launches — для застосунку.
    public static string Json(DateTime now)
    {
        lock (Gate)
        {
            Prune(now);
            var sb = new StringBuilder("[");
            string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            for (int i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(g.Id).Append("\",\"origin\":\"").Append(g.Origin).Append('"');
                sb.Append(",\"n\":").Append(g.Left).Append(",\"reactive\":").Append(g.Reactive ? "true" : "false");
                sb.Append(",\"kmh\":").Append(N(g.Kmh * 0.85));
                sb.Append(",\"t0\":").Append(((DateTimeOffset)g.T0).ToUnixTimeMilliseconds());
                sb.Append(",\"route\":[").Append(string.Join(",", g.Route.Select(p => $"[{N(p.lat)},{N(p.lon)}]"))).Append("]}");
            }
            return sb.Append(']').ToString();
        }
    }
}
