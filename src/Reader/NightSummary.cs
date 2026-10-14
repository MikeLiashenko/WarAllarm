using System.Text;
using System.Text.RegularExpressions;
using TL;

// ── WarAlarm · «Підсумок ночі» ───────────────────────────────────────────────
// Відтворює ніч ІЗ ІСТОРІЇ каналів (а не з пам'яті бота — тож працює і заднім числом):
//   • радари (Розвідка неба)  → де літали дрони/ракети (точки по містах у часі);
//   • локальні канали Києва   → вибухи/прильоти, балістика на Київ;
//   • @ukraine_pyxx           → хвилі пусків по аеродромах, максимум «у повітрі».
// Із точок склеюються ТРАЄКТОРІЇ (сусідні міста, ≤50 хв) і чіпляються до місця пуску.
// MapImage.RenderNight малює їх лініями: шахеди — жовті, крилаті — помаранчеві,
// балістика — червоні (від місця пуску до цілі), вибухи — спалахи.

public static class NightSummary
{
    public record Point(double Lat, double Lon, ThreatKind Kind, int N, string Place, DateTime At, bool Approx = false); // Approx = центр області (місто не знайдено)
    public record Impact(double Lat, double Lon, string Place, DateTime At);
    // N — скільки ракет у цьому пуску («5х Балістик на Київ»); Guessed — місце пуску не
    // назване в пості, взято найближчий до цілі район пусків балістики (орієнтовно).
    public record Ballistic(string Origin, double OLat, double OLon, double TLat, double TLon, string Target, DateTime At,
                            int N = 1, bool Guessed = false);

    // Райони, звідки зазвичай б'ють балістикою (для «орієнтовного» місця пуску).
    static readonly string[] BallisticSites = { "Брянськ", "Курськ", "Бєлгород", "Воронеж", "Таганрог", "Крим" };
    public record Track(ThreatKind Kind, List<(double lat, double lon)> Pts, string? Site);

    public record Data(DateTime From, DateTime To, List<Point> Points, List<Impact> Impacts,
                       List<LaunchBatch> Waves, int MaxAir, List<Ballistic> Ballistics,
                       OfficialReport? Official = null) // ранковий звіт ПС ЗСУ, якщо знайшовся
    {
        public int Launched => Waves.Sum(w => w.Total);
        /// Області за кількістю згадок цілей (сума ×N по постах) — «куди летіло найбільше».
        public List<(string oblast, int n)> TopOblasts(int take = 6) =>
            Points.GroupBy(p => OblastGeo.Region(p.Lat, p.Lon))
                  .Where(g => g.Key != "Україна")
                  .Select(g => (g.Key, g.Sum(p => p.N)))
                  .OrderByDescending(x => x.Item2).Take(take).ToList();

        /// Місця пуску шахедів цієї ночі (з хвиль); якщо хвиль немає — усі відомі.
        public List<string> DroneSites()
        {
            var s = Waves.SelectMany(w => w.Items.Select(i => i.Origin))
                         .Where(o => LaunchFeed.SiteCoords(o) is not null).Distinct().ToList();
            // Офіційні напрямки запуску БпЛА зі звіту ПС ЗСУ — головне джерело.
            if (Official is { DroneSites.Count: > 0 } o) return o.DroneSites.Union(s).ToList();
            return s.Count > 0 ? s : LaunchFeed.AllSites.ToList();
        }
    }

    static bool IsDrone(ThreatKind k) => k is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Unknown;

    /// Склеює точки в траєкторії: та сама «родина» (дрони / крилаті), наступне місто
    /// ≤110 км від попереднього і ≤50 хв пізніше. Початок треку дронів чіпляємо до
    /// найправдоподібнішого місця пуску (близько + «позаду» за напрямком руху).
    public static List<Track> BuildTracks(Data d)
    {
        var tracks = new List<Track>();
        var sites = d.DroneSites();
        foreach (var fam in new[] { true, false }) // true = дрони, false = крилаті ракети
        {
            // Без «центрів області» (місто не знайдено) — інакше лінії хибно сходяться
            // «зірками» в Київ/Полтаву/Миколаїв.
            var pts = d.Points.Where(p => !p.Approx && (fam ? IsDrone(p.Kind) : p.Kind == ThreatKind.Missile))
                              .OrderBy(p => p.At).ToList();
            var chains = new List<List<Point>>();
            foreach (var p in pts)
            {
                // Те саме місце знову (наступний «шматок» поста) — лише оновлюємо час.
                var same = chains.FirstOrDefault(c => c[^1].Place == p.Place && p.At - c[^1].At <= TimeSpan.FromMinutes(50));
                if (same != null) { same[^1] = same[^1] with { At = p.At }; continue; }
                List<Point>? best = null; double bd = double.MaxValue;
                foreach (var c in chains)
                {
                    var last = c[^1];
                    var dt = p.At - last.At;
                    if (dt < TimeSpan.Zero || dt > TimeSpan.FromMinutes(50) || c.Count >= 40) continue;
                    var km = ThreatGeo.Km((last.Lat, last.Lon), (p.Lat, p.Lon));
                    if (km < 3 || km > 110) continue;
                    if (km < bd) { bd = km; best = c; }
                }
                if (best != null) best.Add(p); else chains.Add(new() { p });
            }

            foreach (var c in chains)
            {
                var line = c.Select(p => (p.Lat, p.Lon)).ToList();
                string? site = null;
                if (fam)
                {
                    // Найкраще місце пуску: близько до початку і «позаду» руху (кут між
                    // site→перша точка і першим відрізком треку). Далі 750 км — не чіпляємо.
                    double bestScore = double.MaxValue;
                    foreach (var s in sites)
                    {
                        if (LaunchFeed.SiteCoords(s) is not { } sc) continue;
                        var km = ThreatGeo.Km(sc, line[0]);
                        if (km > 750) continue;
                        double angle = 0;
                        if (line.Count >= 2)
                        {
                            var a1 = ThreatGeo.Bearing(sc, line[0]);
                            var a2 = ThreatGeo.Bearing(line[0], line[1]);
                            angle = Math.Abs(((a1 - a2 + 540) % 360) - 180);
                        }
                        var score = km * (1 + angle / 90);
                        if (score < bestScore) { bestScore = score; site = s; }
                    }
                    if (site != null) line.Insert(0, LaunchFeed.SiteCoords(site)!.Value);
                }
                if (line.Count >= 2) tracks.Add(new(fam ? ThreatKind.Drone : ThreatKind.Missile, line, site));
            }
        }
        return tracks;
    }

    /// Вікно «ночі» = 18:00 → 08:00. Після 08:00 — остання ЗАВЕРШЕНА ніч (учора 18:00 →
    /// сьогодні 08:00); до 08:00 — ніч, що ще триває (учора 18:00 → зараз).
    public static (DateTime from, DateTime to) Window(DateTime now)
    {
        var from = now.Date.AddDays(-1).AddHours(18);
        return now.Hour < 8 ? (from, now) : (from, now.Date.AddHours(8));
    }

    // Історія каналу у вікні [from, to] (листаємо сторінками по 100, максимум ~2000 постів).
    static async Task<List<Message>> History(WTelegram.Client client, Channel ch, DateTime from, DateTime to)
    {
        var all = new List<Message>();
        int offset = 0;
        for (int page = 0; page < 20; page++)
        {
            var res = await client.Messages_GetHistory(ch, offset_id: offset, limit: 100);
            var msgs = res.Messages.OfType<Message>().ToList();
            if (msgs.Count == 0) break;
            all.AddRange(msgs);
            offset = msgs.Min(m => m.id);
            if (msgs.Min(m => m.date.ToLocalTime()) < from) break;
        }
        return all.Where(m => !string.IsNullOrWhiteSpace(m.message))
                  .Where(m => m.date.ToLocalTime() >= from && m.date.ToLocalTime() <= to)
                  .OrderBy(m => m.date).ToList();
    }

    // Балістика з тексту: «пуск балістики з Брянська на Київ», «Балістика з Курська!».
    // Прогнози («загроза балістики…», «можлива») — не пуск, пропускаємо.
    // Діагностика: усі пости про балістику за ніч (пишемо в night_ball.log).
    public static readonly List<string> BallLog = new();

    // Ціль із назви: місто («Київ», «Ічню (Чернігівщина)») або область («Сумщині»).
    static (double lat, double lon, string name)? TargetOf(string raw)
    {
        var t = Regex.Replace(raw, @"\(.*?\)", " ").Trim().TrimEnd('.', '!', ',');
        var w = Regex.Split(t, @"\s+(?:від|з|із|через|повз|курс)\b", RegexOptions.IgnoreCase)[0].Trim();
        if (w.Length < 3) return null;
        if (ThreatGeo.Resolve(w) is { } g) return (g.lat, g.lon, g.name);
        // Короткі назви у знахідному відмінку («на Ічню» → Ічня, «на Суму»): пробуємо -я/-а.
        if (w.Length >= 4 && "юуі".Contains(char.ToLowerInvariant(w[^1])))
            foreach (var alt in new[] { w[..^1] + "я", w[..^1] + "а" })
                if (ThreatGeo.Resolve(alt) is { } g2) return (g2.lat, g2.lon, g2.name);
        var obl = Geo.Oblasts.FirstOrDefault(kv => w.ToLowerInvariant().StartsWith(kv.Key[..^1])).Value;
        return ThreatGeo.ResolveOblast(obl) is { } o ? (o.lat, o.lon, o.name + " (обл.)") : null;
    }

    // Балістика з постів радара: «2х Балістики на Київ», «Ще 3х Балістики на Київ»,
    // «Швидкісна ціль на Харків», «Балістика на Павлоград від Лозової». Кожен рядок окремо.
    // «Загроза застосування…»/«Відбій…» — не пуск. Місце пуску: з тексту, інакше орієнтовно.
    static void BallisticFrom(string text, DateTime at, bool kyivLocal, List<Ballistic> outp)
    {
        var low = text.ToLowerInvariant();
        if (!Regex.IsMatch(low, @"балісти|баллист|іскандер|кинджал|кн-?23|швидкісн")) return;
        lock (BallLog) BallLog.Add($"[{at:dd.MM HH:mm}]{(kyivLocal ? " К" : "")} {text.Replace("\n", " ⏎ ")}");
        var origins = LaunchFeed.OriginsIn(text).Where(o => LaunchFeed.SiteCoords(o) is not null).ToList();
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var ll = line.ToLowerInvariant();
            if (Regex.IsMatch(ll, @"загроз|можлив|ймовірн|імовірн|відбій|чисто|прогноз|рсзв")) continue;
            var m = Regex.Match(line,
                @"(?:(\d{1,2})\s*[хx×]\s*)?(?:балістик\w*|баллист\w*|іскандер\w*|кинджал\w*|кн-?23|швидкісн\w*\s+ціл\w*)\s+(?:на|в бік|у бік|по)\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            int n = int.TryParse(m.Groups[1].Value, out var k) && k is > 0 and < 60 ? k : 1;
            if (TargetOf(m.Groups[2].Value) is not { } tg) continue;
            string site; bool guessed = false;
            if (origins.Count > 0) site = origins[0];
            else
            {   // найближчий до цілі район пусків балістики
                site = BallisticSites.OrderBy(s => ThreatGeo.Km(LaunchFeed.SiteCoords(s)!.Value, (tg.lat, tg.lon))).First();
                guessed = true;
            }
            var sc = LaunchFeed.SiteCoords(site)!.Value;
            outp.Add(new(site, sc.lat, sc.lon, tg.lat, tg.lon, tg.name, at, n, guessed));
        }
    }

    public static async Task<Data> Collect(WTelegram.Client client,
        IEnumerable<Channel> radars, IEnumerable<Channel> kyivLocal, IEnumerable<Channel> launch, IEnumerable<Channel> officialChs,
        DateTime from, DateTime to)
    {
        var points = new List<Point>();
        var impacts = new List<Impact>();
        var waves = new List<LaunchBatch>();
        var ball = new List<Ballistic>();      // з радара — основне джерело
        var ballOther = new List<Ballistic>(); // Київ/канал пусків — лише якщо радар мовчав (інакше дублі)
        // Один канал може бути в діалогах двічі (так з @rozvidkaneba) — інакше все подвоюється.
        radars = radars.DistinctBy(c => c.ID).ToList(); kyivLocal = kyivLocal.DistinctBy(c => c.ID).ToList(); launch = launch.DistinctBy(c => c.ID).ToList();
        int maxAir = 0;

        // Радари: «Область:\nN× ТИП курсом на Місто» → точки.
        foreach (var ch in radars)
            foreach (var m in await Safe(() => History(client, ch, from, to)))
            {
                var at = m.date.ToLocalTime();
                BallisticFrom(m.message, at, false, ball);
                foreach (var ev in ThreatParser.Parse(m.message))
                {
                    if (ev.Kind is ThreatKind.AllClear or ThreatKind.Ballistic) continue;
                    var g = ThreatGeo.Resolve(ev.City);
                    bool approx = g is null;
                    g ??= ThreatGeo.ResolveOblast(ev.Oblast);
                    if (g is not { } gp) continue;
                    points.Add(new(gp.lat, gp.lon, ev.Kind, Math.Max(1, ev.Count), gp.name, at, approx));
                }
            }

        // Київ: цілі по районах + вибухи/прильоти + балістика на Київ.
        foreach (var ch in kyivLocal)
            foreach (var m in await Safe(() => History(client, ch, from, to)))
            {
                var at = m.date.ToLocalTime();
                BallisticFrom(m.message, at, true, ballOther);
                var e = KyivMonitor.Analyze(m.message);
                var low = m.message.ToLowerInvariant();
                var places = e.Places.Select(p => (p, c: ThreatGeo.Of(p))).Where(x => x.c is not null).ToList();
                bool boom = Regex.IsMatch(low, @"вибух|приліт|прил[её]т|влучанн|прильот|гучно")
                            && !Regex.IsMatch(low, @"можлив|можуть бути|загроза");
                if (boom)
                {
                    if (places.Count == 0 && ThreatGeo.Of("Київ") is { } kc) places.Add(("Київ", kc));
                    foreach (var (p, c) in places) impacts.Add(new(c!.Value.lat, c.Value.lon, p, at));
                }
                else if (e.Signal == KhSignal.Threat && e.Kind != ThreatKind.Ballistic)
                    foreach (var (p, c) in places)
                        points.Add(new(c!.Value.lat, c.Value.lon, e.Kind, 1, p, at));
            }

        // Пуски й «у повітрі» (+ балістика, якщо канал її називає).
        foreach (var ch in launch)
            foreach (var m in await Safe(() => History(client, ch, from, to)))
            {
                BallisticFrom(m.message, m.date.ToLocalTime(), false, ballOther);
                if (LaunchFeed.ParseLaunch(m.message) is { } b) waves.Add(b);
                else if (LaunchFeed.ParseAirborne(m.message) is int a) maxAir = Math.Max(maxAir, a);
            }

        // Дубль-вибухи (кілька каналів про той самий) — один на місце в межах 20 хв.
        impacts = impacts.GroupBy(i => (i.Place, Slot: i.At.Ticks / TimeSpan.FromMinutes(20).Ticks))
                         .Select(g => g.First()).ToList();

        // Ті самі пуски пишуть кілька каналів → рахуємо з радара; інші — тільки як запасний варіант.
        var ballistics = ball.Count > 0 ? ball : ballOther;

        // Ранковий звіт ПС ЗСУ виходить ~09:00 — шукаємо до +6 год після кінця вікна.
        OfficialReport? official = null;
        foreach (var ch in officialChs.DistinctBy(c => c.ID))
            foreach (var m in await Safe(() => History(client, ch, to.AddHours(-3), to.AddHours(6))))
                if (OfficialReports.Parse(m.message) is { } rep) { official = rep; break; }
        // Офіційний звіт ПС ЗСУ знайдено → райони пуску ракет переназначаємо на найближчий
        // з НАЗВАНИХ у звіті (а не з загального списку).
        if (official is { MissileSites.Count: > 0 })
            ballistics = ballistics.Select(b =>
            {
                if (!b.Guessed) return b;
                var site = official.MissileSites.OrderBy(s => ThreatGeo.Km(LaunchFeed.SiteCoords(s)!.Value, (b.TLat, b.TLon))).First();
                var sc = LaunchFeed.SiteCoords(site)!.Value;
                return b with { Origin = site, OLat = sc.lat, OLon = sc.lon };
            }).ToList();

        return new(from, to, points, impacts, waves, maxAir, ballistics, official);
    }

    static async Task<List<Message>> Safe(Func<Task<List<Message>>> f)
    {
        try { return await f(); } catch { return new(); }
    }

    public static string Period(Data d) =>
        d.From.Date == d.To.Date ? $"{d.From:dd.MM} {d.From:HH:mm}–{d.To:HH:mm}"
                                 : $"{d.From:dd.MM HH:mm} → {d.To:dd.MM HH:mm}";

    /// Текст підсумку (HTML для каналу / plain для пушу).
    public static string Caption(Data d, bool html)
    {
        string B(string s) => html ? $"<b>{H(s)}</b>" : s;
        string T(string s) => html ? H(s) : s;
        var sb = new StringBuilder();
        sb.AppendLine("🌙 " + B("Підсумок ночі") + " · " + T(Period(d)));
        if (d.Official is { } o)
        {
            sb.AppendLine("🛡 " + B("За даними Повітряних Сил:"));
            if (o.Missiles > 0)
                sb.AppendLine($"🔴 Ракет: {o.Missiles}" + (o.MissileTypes.Length > 0 ? $" ({T(o.MissileTypes)})" : "")
                    + (o.MissileSites.Count > 0 ? " — з: " + T(string.Join(", ", o.MissileSites)) : ""));
            if (o.Drones > 0)
                sb.AppendLine($"🟡 БпЛА: {o.Drones}" + (o.Reactive > 0 ? $" (з них реактивних {o.Reactive})" : "")
                    + (o.DroneSites.Count > 0 ? " — з: " + T(string.Join(", ", o.DroneSites)) : ""));
            if (o.Down > 0) sb.AppendLine($"✅ Збито/подавлено: {o.Down}");
            if (o.Hits > 0) sb.AppendLine($"💥 Влучання: {o.Hits} локацій");
            sb.AppendLine();
        }
        if (d.Launched > 0)
            sb.AppendLine($"🚀 Запущено шахедів: ~{d.Launched} (хвиль: {d.Waves.Count}"
                + (d.Waves.Sum(w => w.Items.Where(i => i.Reactive).Sum(i => i.N)) is > 0 and var r ? $", реактивних ~{r}" : "") + ")");
        var sites = d.Waves.SelectMany(w => w.Items).GroupBy(i => i.Origin)
                     .OrderByDescending(g => g.Sum(i => i.N)).Take(5).Select(g => $"{g.Key} {g.Sum(i => i.N)}");
        if (d.Launched > 0) sb.AppendLine("🛫 Звідки: " + T(string.Join(", ", sites)));
        if (d.Ballistics.Count > 0)
        {
            var byT = d.Ballistics.GroupBy(b => b.Target).OrderByDescending(g => g.Sum(b => b.N))
                       .Take(6).Select(g => $"{g.Key} {g.Sum(b => b.N)}");
            sb.AppendLine($"🔴 Балістика: {d.Ballistics.Sum(b => b.N)} — " + T(string.Join(", ", byT))
                + (!d.Ballistics.Any(b => b.Guessed) ? ""
                   : d.Official is { MissileSites.Count: > 0 } ? " (райони пуску — за звітом ПС ЗСУ)"
                   : " (місця пуску орієнтовні)"));
        }
        if (d.MaxAir > 0) sb.AppendLine($"🛸 Максимум одночасно в повітрі: ~{d.MaxAir}");
        var top = d.TopOblasts(5);
        if (top.Count > 0) sb.AppendLine("📍 Куди летіло найбільше: " + T(string.Join(", ", top.Select(t => t.oblast))));
        if (d.Impacts.Count > 0)
        {
            var where = d.Impacts.GroupBy(i => i.Place).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key);
            sb.AppendLine($"💥 Вибухи (Київ): {d.Impacts.Count} — " + T(string.Join(", ", where)));
        }
        if (d.Launched == 0 && d.Points.Count == 0 && d.Impacts.Count == 0) sb.AppendLine("😌 Ніч була відносно спокійною.");
        sb.Append(html ? "\n🤖 WarAlarm · за даними моніторів, звіряйтесь з офіційними" : "\n🤖 WarAlarm");
        return sb.ToString();
    }

    static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// Для застосунку (state.night).
    public static string StateJson(Data d)
    {
        static string J(string x) => x.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var ts = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
        var top = string.Join(", ", d.TopOblasts(5).Select(t => t.oblast));
        var boomAt = string.Join(", ", d.Impacts.GroupBy(i => i.Place).OrderByDescending(g => g.Count()).Take(5).Select(g => g.Key));
        return $"{{\"ts\":{ts},\"period\":\"{J(Period(d))}\",\"launched\":{d.Launched},\"waves\":{d.Waves.Count},"
             + $"\"maxAir\":{d.MaxAir},\"top\":\"{J(top)}\",\"impacts\":{d.Impacts.Count},\"impactAt\":\"{J(boomAt)}\","
             + $"\"ballistic\":{d.Ballistics.Sum(b => b.N)}}}";
    }
}
