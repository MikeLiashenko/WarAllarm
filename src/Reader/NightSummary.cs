using System.Text;
using System.Text.RegularExpressions;
using TL;

// ── WarAlarm · «Підсумок ночі» ───────────────────────────────────────────────
// Відтворює ніч ІЗ ІСТОРІЇ каналів (а не з пам'яті бота — тож працює і заднім числом):
//   • радари (Розвідка неба)  → де літали дрони/ракети (точки по містах);
//   • локальні канали Києва   → де були вибухи/прильоти;
//   • @ukraine_pyxx           → скільки запустили (хвилі) і максимум «у повітрі».
// Далі MapImage.RenderNight малює карту, Caption — текст для каналу/пушу.

public static class NightSummary
{
    public record Point(double Lat, double Lon, ThreatKind Kind, int N, string Place, DateTime At);
    public record Impact(double Lat, double Lon, string Place, DateTime At);
    public record Data(DateTime From, DateTime To, List<Point> Points, List<Impact> Impacts,
                       List<LaunchBatch> Waves, int MaxAir)
    {
        public int Launched => Waves.Sum(w => w.Total);
        /// Області за кількістю згадок цілей (сума ×N по постах) — «куди летіло найбільше».
        public List<(string oblast, int n)> TopOblasts(int take = 6) =>
            Points.GroupBy(p => OblastGeo.Region(p.Lat, p.Lon))
                  .Where(g => g.Key != "Україна")
                  .Select(g => (g.Key, g.Sum(p => p.N)))
                  .OrderByDescending(x => x.Item2).Take(take).ToList();
    }

    /// Вікно «ночі»: після 16:00 — від сьогоднішніх 16:00 до зараз (ніч триває);
    /// до 12:00 — від учорашніх 16:00 до зараз; вдень (12–16) — минула ніч 16:00→09:00.
    public static (DateTime from, DateTime to) Window(DateTime now)
    {
        if (now.Hour >= 16) return (now.Date.AddHours(16), now);
        if (now.Hour < 12) return (now.Date.AddDays(-1).AddHours(16), now);
        return (now.Date.AddDays(-1).AddHours(16), now.Date.AddHours(9));
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

    public static async Task<Data> Collect(WTelegram.Client client,
        IEnumerable<Channel> radars, IEnumerable<Channel> kyivLocal, IEnumerable<Channel> launch,
        DateTime from, DateTime to)
    {
        var points = new List<Point>();
        var impacts = new List<Impact>();
        var waves = new List<LaunchBatch>();
        int maxAir = 0;

        // Радари: «Область:\nN× ТИП курсом на Місто» → точки.
        foreach (var ch in radars)
            foreach (var m in await Safe(() => History(client, ch, from, to)))
            {
                var at = m.date.ToLocalTime();
                foreach (var ev in ThreatParser.Parse(m.message))
                {
                    if (ev.Kind == ThreatKind.AllClear) continue;
                    var g = ThreatGeo.Resolve(ev.City) ?? ThreatGeo.ResolveOblast(ev.Oblast);
                    if (g is not { } gp) continue;
                    points.Add(new(gp.lat, gp.lon, ev.Kind, Math.Max(1, ev.Count), gp.name, at));
                }
            }

        // Київ: цілі по районах + вибухи/прильоти.
        foreach (var ch in kyivLocal)
            foreach (var m in await Safe(() => History(client, ch, from, to)))
            {
                var at = m.date.ToLocalTime();
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
                else if (e.Signal == KhSignal.Threat)
                    foreach (var (p, c) in places)
                        points.Add(new(c!.Value.lat, c.Value.lon, e.Kind, 1, p, at));
            }

        // Пуски й «у повітрі».
        foreach (var ch in launch)
            foreach (var m in await Safe(() => History(client, ch, from, to)))
            {
                if (LaunchFeed.ParseLaunch(m.message) is { } b) waves.Add(b);
                else if (LaunchFeed.ParseAirborne(m.message) is int a) maxAir = Math.Max(maxAir, a);
            }

        // Дубль-вибухи (кілька каналів про той самий) — один на місце в межах 20 хв.
        impacts = impacts.GroupBy(i => (i.Place, Slot: i.At.Ticks / TimeSpan.FromMinutes(20).Ticks))
                         .Select(g => g.First()).ToList();
        return new(from, to, points, impacts, waves, maxAir);
    }

    static async Task<List<Message>> Safe(Func<Task<List<Message>>> f)
    {
        try { return await f(); } catch { return new(); }
    }

    public static string Period(Data d) =>
        d.From.Date == d.To.Date ? $"{d.From:dd.MM} {d.From:HH:mm}–{d.To:HH:mm}"
                                 : $"{d.From:dd.MM HH:mm} → {d.To:dd.MM HH:mm}";

    /// Текст підсумку (HTML для каналу; для пушу — той самий, теги зрізаються ntfy не треба — шлемо plain).
    public static string Caption(Data d, bool html)
    {
        string B(string s) => html ? $"<b>{H(s)}</b>" : s;
        string T(string s) => html ? H(s) : s;
        var sb = new StringBuilder();
        sb.AppendLine("🌙 " + B("Підсумок ночі") + " · " + T(Period(d)));
        if (d.Launched > 0)
            sb.AppendLine($"🚀 Запущено шахедів: ~{d.Launched} (хвиль: {d.Waves.Count}"
                + (d.Waves.Sum(w => w.Items.Where(i => i.Reactive).Sum(i => i.N)) is > 0 and var r ? $", реактивних ~{r}" : "") + ")");
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
             + $"\"maxAir\":{d.MaxAir},\"top\":\"{J(top)}\",\"impacts\":{d.Impacts.Count},\"impactAt\":\"{J(boomAt)}\"}}";
    }
}
