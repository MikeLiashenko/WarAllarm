using System.Globalization;

// ── WarAlarm · потоки шахедів ────────────────────────────────────────────────
// Шахеди не літають хаосом: є потоки — з Чернігівщини на Київ, повз Київ на Житомирщину,
// з півдня вздовж Дніпра… Реактивні йдуть іншими дорогами, ніж звичайні. Канали ж називають
// місця із запізненням і вперемішку, тож курс «з двох останніх місць» скакав: на Київ — і
// одразу від Києва, а мітка, що відскочила назад, ставала другою ціллю.
// Тут — карта потоків, вивчена з журналів (`dotnet run -- flows journal/*.jsonl`): для кожної
// клітинки ~25 км — у які боки там летіли реактивні й звичайні шахеди. Мітка між постами йде
// потоком, а пост, що ставить ціль назад проти потоку, — це запізнення каналу, а не розворот.

public static class Flows
{
    const double CellLat = 0.25, CellLon = 0.35;
    const int Bins = 16;
    const string FileName = "flows.txt";
    static readonly Dictionary<(bool jet, int y, int x), double[]> Grid = new();
    static readonly object Gate = new();
    static bool _loaded, _learning; // під час навчання старі потоки не підказують трекеру (інакше вчаться самі в себе)

    static (int y, int x) Cell((double lat, double lon) p) => ((int)Math.Floor(p.lat / CellLat), (int)Math.Floor(p.lon / CellLon));
    static int Bin(double brg) => (int)Math.Round(((brg % 360) + 360) % 360 / (360.0 / Bins)) % Bins;
    static double Diff(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

    static void Load()
    {
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
            {
                var f = line.Split(' ');
                if (f.Length != 3 + Bins || line.StartsWith('#')) continue;
                var h = f.Skip(3).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                Grid[(f[0] == "J", int.Parse(f[1]), int.Parse(f[2]))] = h;
            }
        }
    }

    /// Чи є вивчені потоки взагалі (файл поруч із програмою).
    public static bool Ready { get { Load(); return Grid.Count > 0 && !_learning; } }

    // Гістограма напрямків у точці: своя клітинка + сусідні з половинною вагою.
    static double[] Hist((double lat, double lon) p, bool jet)
    {
        Load();
        var (y, x) = Cell(p);
        var h = new double[Bins];
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (Grid.TryGetValue((jet, y + dy, x + dx), out var c))
                {
                    double w = dy == 0 && dx == 0 ? 1 : 0.5;
                    for (int i = 0; i < Bins; i++) h[i] += c[i] * w;
                }
        // Сусідні сектори зливаємо (кутова похибка між центрами міст ~20°).
        var s = new double[Bins];
        for (int i = 0; i < Bins; i++) s[i] = h[i] + 0.5 * (h[(i + 1) % Bins] + h[(i + Bins - 1) % Bins]);
        return s;
    }

    /// Потоки в точці: напрямки (градуси) і їх вага. Реактивних тут не бачили — беремо звичайних.
    public static List<(double brg, double w)> Modes((double lat, double lon) p, bool jet)
    {
        var res = new List<(double brg, double w)>();
        if (_learning) return res;
        var h = Hist(p, jet);
        if (h.Sum() < 4 && jet) h = Hist(p, false);
        double total = h.Sum();
        if (total < 4) return res;
        for (int i = 0; i < Bins; i++)
        {
            double v = h[i];
            if (v < h[(i + 1) % Bins] || v < h[(i + Bins - 1) % Bins] || v < total * 0.12) continue;
            res.Add((i * 360.0 / Bins, v / total));
        }
        return res.OrderByDescending(m => m.w).ToList();
    }

    /// Напрямок потоку в точці: найближчий до [prefer] (якщо не далі 75°), інакше найсильніший.
    public static double? Dir((double lat, double lon) p, bool jet, double? prefer = null)
    {
        var m = Modes(p, jet);
        if (m.Count == 0) return null;
        if (prefer is { } pr && m.Where(x => Diff(x.brg, pr) <= 75).OrderBy(x => Diff(x.brg, pr)).FirstOrDefault() is { w: > 0 } near)
            return near.brg;
        return m[0].brg;
    }

    /// Рух a → b — проти всіх потоків у цьому місці (запізнілий пост про місце позаду цілі).
    public static bool Against((double lat, double lon) a, (double lat, double lon) b, bool jet)
    {
        var m = Modes(a, jet);
        if (m.Count == 0) return false;
        double brg = ThreatGeo.Bearing(a, b);
        return m.All(x => Diff(brg, x.brg) > 120);
    }

    /// Куди ціль полетить далі за потоком (перша точка — сама p); null, якщо потоку тут нема.
    public static List<(double lat, double lon)>? Route((double lat, double lon) p, bool jet, double? heading, double km = 220)
    {
        if (!Ready) return null;
        var route = new List<(double lat, double lon)> { p };
        var cur = p;
        double? prev = heading;
        for (double run = 0; run < km; run += 10)
        {
            if (Dir(cur, jet, prev) is not { } d) break;
            if (prev is { } pv && Diff(d, pv) > 70) break; // потік тут повертає надто різко — далі не вгадуємо
            cur = Move(cur, d, 10);
            if (!OblastGeo.InUkraine(cur.lat, cur.lon) && run > 0) break;
            route.Add(cur);
            prev = d;
        }
        if (route.Count < 3) return null;
        return TypicalRoutes.ClipToUkraine(Smooth(route));
    }

    // Згладжена лінія (без «зубців» по 10 км) — кожна друга точка й кінець.
    static List<(double lat, double lon)> Smooth(List<(double lat, double lon)> r) =>
        r.Where((_, i) => i == 0 || i % 2 == 0 || i == r.Count - 1).ToList();

    static (double lat, double lon) Move((double lat, double lon) p, double brg, double km)
    {
        double r = brg * Math.PI / 180;
        return (p.lat + km * Math.Cos(r) / 111, p.lon + km * Math.Sin(r) / (111 * Math.Cos(p.lat * Math.PI / 180)));
    }

    // ── навчання з журналів ──────────────────────────────────────────────────

    /// Прогнати журнали через хаб і трекер, зібрати рух дронів і названі курси, записати flows.txt.
    public static void Learn(IEnumerable<string> files, string outPath)
    {
        _learning = true;
        var grid = new Dictionary<(bool jet, int y, int x), double[]>();
        int moves = 0, courses = 0;
        var seenCourse = new HashSet<(bool, (int, int), (int, int), long)>();
        void Add(bool jet, (double lat, double lon) a, (double lat, double lon) b, double w, double maxKm)
        {
            double len = Math.Min(ThreatGeo.Km(a, b), maxKm), brg = ThreatGeo.Bearing(a, b);
            var seen = new HashSet<(int, int)>();
            for (double d = 0; d <= len; d += 5)
            {
                var c = Cell(Move(a, brg, d));
                if (!seen.Add(c)) continue;
                if (!grid.TryGetValue((jet, c.y, c.x), out var h)) grid[(jet, c.y, c.x)] = h = new double[Bins];
                h[Bin(brg)] += w;
            }
        }
        foreach (var file in files)
        {
            var tr = new ThreatTracker((50.45, 30.52));
            tr.FlowSink = (t, a, b, mins, course) =>
            {
                if (t.Kind != ThreatKind.Drone || t.Zone != "UA") return;
                bool jet = t.Ammo?.Contains("реакт", StringComparison.OrdinalIgnoreCase) == true;
                double km = ThreatGeo.Km(a, b);
                // Курс: «на X» — 60 км від цілі вперед; «на X з Y» — останні 60 км підльоту до X.
                // Той самий курс у десятці каналів — один курс.
                if (course)
                {
                    if (km < 8 || !seenCourse.Add((jet, Cell(a), Cell(b), t.LastUpdate.Ticks / TimeSpan.FromMinutes(20).Ticks))) return;
                    if (mins < 0) a = Move(b, ThreatGeo.Bearing(a, b) + 180, Math.Min(km, 60));
                    Add(jet, a, b, 2, 60); courses++;
                    return;
                }
                // Перестрибування (не та ціль) і стояння на місці — не рух.
                if (km < 4 || km > 100 || mins < 0.5 || km / (mins / 60) > (jet ? 1000 : 400)) return;
                // Рух між постами трекер часто зв'язує не з тією ціллю (у списках уночі десятки дронів) —
                // він лише заповнює місця, де курсів не називали.
                Add(jet, a, b, 0.5, 100);
                moves++;
            };
            Journal.Drive(file, tr);
            Console.WriteLine($"   {Path.GetFileName(file)}: рухів {moves}, курсів {courses}");
        }
        using var w = new StreamWriter(outPath);
        w.WriteLine($"# WarAlarm · потоки шахедів: J — реактивні, P — звичайні; клітинка {CellLat}°×{CellLon}°, {Bins} напрямків від півночі. Рухів {moves}, курсів {courses}.");
        foreach (var ((jet, y, x), h) in grid.OrderBy(g => g.Key.jet).ThenBy(g => g.Key.y).ThenBy(g => g.Key.x))
            w.WriteLine($"{(jet ? "J" : "P")} {y} {x} " + string.Join(' ', h.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture))));
        Console.WriteLine($"🧭 {outPath}: клітинок {grid.Count} (реактивні {grid.Keys.Count(k => k.jet)}, звичайні {grid.Keys.Count(k => !k.jet)})");
    }
}
