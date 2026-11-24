using System.Globalization;
using System.Text;

// ── WarAlarm · групи пуску в польоті (оцінка) ────────────────────────────────
// Пост «пуск 8 реактивних з Орла» — одразу на карті: група ×8 вилітає з Орла й летить
// типовим маршрутом (TypicalRoutes) зі швидкістю шахеда. Щойно канали пишуть про
// справжні цілі поблизу того місця, де група мала б бути (або на її дорозі), — вони
// «забирають» дрони з групи (×N зменшується), і далі на карті вже справжні цілі.
//
// Прогноз — це про те, що ще НЕ побачили: дрони над Росією / на підльоті до кордону.
// Тому він (як і справжні цілі в застосунку) не летить туди, де немає тривоги: справжній
// дрон над областю = там тривога. Дійшла група до межі відбою — чекає на ній; тривоги
// там так і не оголосили за [HoldMax] — прогноз хибний, група зникає. І вглиб країни вона
// заходить недалеко ([InsideKm]): там цілі вже бачать монітори, а «прогноз» за сотні км
// від них — це шум (було: група з Орла ×1 «летіла» до Львова без жодної тривоги).
// Група зникає й тоді, коли її розібрали, долетіла до кінця маршруту або минуло 8 год.

public sealed class LaunchGroup
{
    public required string Id;
    public required string Origin;
    public int N, Left;
    public bool Reactive;
    public string Via = "";                 // «на Київ» / «на захід»
    public DateTime T0;                     // час пуску (з поста)
    public required List<(double lat, double lon)> Route;
    public double Kmh => Reactive ? 500 : 185;
    /// У середньому шахеди йдуть ~85 % швидкості по прямій.
    public double Speed => Kmh * 0.85;
    public double LengthKm => TypicalRoutes.LengthKm(Route);

    // Рух: «віртуальний старт» і межа. Мітка йде від старту зі швидкістю Speed, але не
    // далі [Limit] км маршруту (межа відбою / кордону / «вглиб країни»). Після зупинки
    // старт зсувається, щоб група рушила з того самого місця, а не стрибнула вперед.
    public DateTime Start;
    public double Limit = double.MaxValue;
    public DateTime? HoldSince;             // стоїть на межі (тривоги попереду нема)
    public double? EntryKm;                 // де маршрут заходить в Україну (км від старту)

    /// Скільки км маршруту група пролетіла на [now].
    public double FlownAt(DateTime now) => Math.Min(Limit, Speed * Math.Max(0, (now - Start).TotalHours));
    /// Де група мала б бути зараз.
    public (double lat, double lon) PosAt(DateTime now) => TypicalRoutes.At(Route, FlownAt(now));
    public bool Arrived(DateTime now) => FlownAt(now) >= LengthKm - 1;
    /// Уся решта типового маршруту від поточної точки (для «коли тривога тут»: туди, де її ще нема).
    public List<(double lat, double lon)> Rest(DateTime now) => TypicalRoutes.Slice(Route, FlownAt(now), LengthKm);
    /// Решта дороги від поточної точки до межі, куди група ще може долетіти.
    public List<(double lat, double lon)> Ahead(DateTime now) => TypicalRoutes.Slice(Route, FlownAt(now), Math.Min(Limit, LengthKm));
    /// Маршрут до межі (для застосунку: мітка зупиняється в його кінці).
    public List<(double lat, double lon)> Shown => TypicalRoutes.Slice(Route, 0, Math.Min(Limit, LengthKm));
}

public static class LaunchGroups
{
    static readonly object Gate = new();
    static readonly List<LaunchGroup> Groups = new();
    static int _seq;

    /// Чи є офіційна тривога в точці (Reader бере з фіду тривог; офлайн — з журналу
    /// @UkraineAlarmSignal). null — невідомо: тоді межі відбою немає, лише кордон і глибина.
    public static Func<double, double, bool>? InAlarm;
    /// Скільки чекати на межі відбою, поки там оголосять тривогу.
    public static readonly TimeSpan HoldMax = TimeSpan.FromMinutes(20);
    /// Наскільки вглиб України (км маршрутом від кордону) може зайти прогноз.
    public static double InsideKm(LaunchGroup g) => g.Reactive ? 220 : 130;

    public static List<LaunchGroup> Active { get { lock (Gate) return Groups.ToList(); } }

    /// Нова хвиля пусків → групи по місцях пуску (окремо реактивні й звичайні).
    public static void Add(LaunchBatch b, DateTime at)
    {
        lock (Gate)
        {
            foreach (var i in b.Items)
            {
                // Через Славутич дві дороги: група ділиться навпіл (одинак — типово на Київ).
                var branches = TypicalRoutes.Branches(i.Origin);
                int left = i.N;
                for (int k = 0; k < branches.Count && left > 0; k++)
                {
                    int n = k == branches.Count - 1 ? left : (left + 1) / 2;
                    left -= n;
                    var g = new LaunchGroup
                    {
                        Id = $"L{++_seq}:{i.Origin}{(i.Reactive ? ":R" : "")}", Origin = i.Origin, Via = branches[k].via,
                        N = n, Left = n, Reactive = i.Reactive, T0 = at, Start = at, Route = branches[k].route,
                    };
                    g.EntryKm = EntryOf(g.Route);
                    Groups.Add(g);
                }
            }
            Update(at);
        }
    }

    // Підконтрольна Україні точка (окупована — як Росія: там прогноз летить вільно).
    public static bool Free((double lat, double lon) p) => OblastGeo.InUkraine(p.lat, p.lon) && !OblastGeo.Occupied(p.lat, p.lon);

    // Перша підконтрольна точка маршруту (км від старту); null — маршрут туди не заходить.
    static double? EntryOf(List<(double lat, double lon)> route)
    {
        double total = TypicalRoutes.LengthKm(route);
        for (double d = 0; d <= total; d += 3)
            if (Free(TypicalRoutes.At(route, d))) return d;
        return null;
    }

    /// Справжня ціль-дрон з'явилась у [pos] — забираємо її з групи, що мала б бути поруч
    /// (≤150 км від своєї розрахункової точки) або чия дорога йде повз (≤60 км від маршруту
    /// трохи позаду / попереду групи): швидкість і час пуску — оцінка, а дорогу ми знаємо.
    public static void Absorb((double lat, double lon) pos, int count, DateTime now)
    {
        lock (Gate)
        {
            Update(now);
            LaunchGroup? best = null;
            double bestKm = double.MaxValue;
            foreach (var g in Groups)
            {
                double flown = g.FlownAt(now);
                double km = ThreatGeo.Km(g.PosAt(now), pos);
                if (km > 150)
                {
                    // Відстань до маршруту на відрізку [позаду 150 км … попереду 120 км].
                    double near = double.MaxValue, end = Math.Min(g.LengthKm, flown + 120);
                    for (double d = Math.Max(0, flown - 150); d <= end; d += 10)
                        near = Math.Min(near, ThreatGeo.Km(TypicalRoutes.At(g.Route, d), pos));
                    if (near > 60) continue;
                    km = 150 + near; // дорога повз — гірше, ніж розрахункова точка поруч
                }
                if (km < bestKm) { bestKm = km; best = g; }
            }
            if (best is null) return;
            best.Left -= Math.Max(1, count);
            if (best.Left <= 0) Groups.Remove(best);
        }
    }

    /// Рух груп: межа (відбій / кордон / глибина), очікування на межі, хто вже зник.
    public static void Update(DateTime now)
    {
        lock (Gate)
        {
            foreach (var g in Groups)
            {
                double flown = g.FlownAt(now);
                bool held = g.Limit < double.MaxValue && flown >= g.Limit - 0.5;
                double lim = LimitFrom(g, flown);
                // Стояла на межі, а тепер попереду тривога: рушає з того самого місця.
                if (held && lim > flown + 0.5) g.Start = now - TimeSpan.FromHours(flown / g.Speed);
                g.Limit = lim;
                bool stopped = lim < g.LengthKm - 1 && g.FlownAt(now) >= lim - 0.5;
                g.HoldSince = stopped ? g.HoldSince ?? now : null;
            }
            var inAlarm = InAlarm;
            Groups.RemoveAll(g => g.Left <= 0 || g.Arrived(now) || now - g.T0 > TimeSpan.FromHours(8)
                                  || g.HoldSince is { } h && now - h >= HoldMax
                                  // Відбій там, де група «зараз»: там нікого нема — прогноз хибний.
                                  || inAlarm is not null && g.PosAt(now) is var p && Free(p) && !inAlarm(p.lat, p.lon));
        }
    }

    // Як далеко маршрутом група може летіти від [flown]: до першої точки в Україні без
    // тривоги, до виходу з підконтрольної території (короткі вилазки ≤30 км — спрощений
    // контур і затоки) або на InsideKm від кордону / лінії фронту.
    static double LimitFrom(LaunchGroup g, double flown)
    {
        double total = g.LengthKm;
        double cap = g.EntryKm is { } e ? Math.Min(total, e + InsideKm(g)) : total;
        if (flown >= cap) return flown;
        var inAlarm = InAlarm;
        bool wasIn = false;
        double? leftAt = null;
        for (double d = flown; d <= cap; d += 3)
        {
            var p = TypicalRoutes.At(g.Route, d);
            if (!Free(p))
            {
                if (!wasIn && (g.EntryKm is null || d < g.EntryKm)) continue; // ще над Росією / морем
                leftAt ??= d;
                if (d - leftAt.Value > 30) return Math.Max(flown, leftAt.Value - 3);
                continue;
            }
            wasIn = true; leftAt = null;
            if (inAlarm is not null && !inAlarm(p.lat, p.lon)) return Math.Max(flown, d - 3);
        }
        return cap;
    }

    /// state.launches — для застосунку: маршрут лише до межі, а «t0» — віртуальний старт,
    /// тож мітка (і в старих версіях застосунку) іде від того місця, де група зараз, і
    /// зупиняється на межі відбою, а не летить країною навмання.
    public static string Json(DateTime now)
    {
        lock (Gate)
        {
            Update(now);
            var sb = new StringBuilder("[");
            string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            bool first = true;
            foreach (var g in Groups)
            {
                var route = g.Shown;
                if (route.Count < 2) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(g.Id).Append("\",\"origin\":\"").Append(g.Origin).Append('"');
                sb.Append(",\"via\":\"").Append(g.Via).Append('"');
                sb.Append(",\"n\":").Append(g.Left).Append(",\"reactive\":").Append(g.Reactive ? "true" : "false");
                sb.Append(",\"kmh\":").Append(N(g.Speed));
                sb.Append(",\"t0\":").Append(((DateTimeOffset)g.Start).ToUnixTimeMilliseconds());
                if (g.HoldSince is not null) sb.Append(",\"hold\":true");
                sb.Append(",\"route\":[").Append(string.Join(",", route.Select(p => $"[{N(p.lat)},{N(p.lon)}]"))).Append(']');
                // Увесь маршрут — для «коли орієнтовно тривога тут» (туди, де тривоги ще нема).
                sb.Append(",\"full\":[").Append(string.Join(",", g.Route.Select(p => $"[{N(p.lat)},{N(p.lon)}]"))).Append("]}");
            }
            return sb.Append(']').ToString();
        }
    }
}
