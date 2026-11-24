using System.Text.Json;

// ── 🔮 Аудит прогнозу груп пуску по журналу (оффлайн):
//   dotnet run -- fcast journal/2026-11-22.jsonl journal/2026-11-23.jsonl … [-v]
// Пости каналу пусків (@ukraine_pyxx) створюють групи, як наживо; пости джерел ідуть через
// хаб і трекер (справжні цілі забирають дрони з груп). Кожні 5 хв для кожної групи на карті:
// де вона, чи там офіційна тривога (з @UkraineAlarmSignal), і де найближча справжня ціль-дрон.
// Прогноз, що летить по області без тривоги або за сотні км від справжніх цілей, — помилка.
public static class ForecastAudit
{
    public const string LaunchChannel = "ukraine_pyxx";

    sealed class Stat
    {
        public required LaunchGroup G;
        public DateTime First, Last;
        public int Shown, InUa, NoAlarm, NoReal;
        public readonly List<double> Near = new();
    }

    public static void Run(List<string> files, bool verbose)
    {
        if (files.Count == 0) return;
        AreaLog.Backfill(Path.GetDirectoryName(Path.GetFullPath(files[0]))!);
        var posts = new List<(DateTime at, string ch, string text)>();
        foreach (var file in files)
        {
            var day = DateTime.TryParse(Path.GetFileNameWithoutExtension(file), out var dd) ? dd : DateTime.Today;
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"kind\":\"post\"")) continue;
                try
                {
                    var e = JsonDocument.Parse(line).RootElement;
                    posts.Add((day + TimeSpan.Parse(e.GetProperty("t").GetString()!), e.GetProperty("ch").GetString()!, e.GetProperty("text").GetString() ?? ""));
                }
                catch { }
            }
        }
        posts.Sort((a, b) => a.at.CompareTo(b.at));
        var hub = new SourceHub();
        var tr = new ThreatTracker((50.0, 36.3));
        var stats = new Dictionary<string, Stat>();
        DateTime next = posts.Count > 0 ? posts[0].at : default;
        DateTime simNow = next;
        LaunchGroups.InAlarm = (la, lo) => AlarmAt((la, lo), simNow);

        void Sample(DateTime now)
        {
            simNow = now;
            LaunchGroups.Json(now); // прибирає розібрані / долетілі
            var real = tr.Active.Where(x => x.Kind is ThreatKind.Drone or ThreatKind.Target && !x.Launch && !x.Down && x.Zone == "UA"
                                            && !x.Approx && now - x.LastUpdate < TimeSpan.FromMinutes(15) && x.DrawPos(now) is not null)
                                .Select(x => x.DrawPos(now)!.Value).ToList();
            foreach (var g in LaunchGroups.Active)
            {
                if (!stats.TryGetValue(g.Id, out var s)) stats[g.Id] = s = new Stat { G = g, First = now };
                s.Last = now; s.Shown++;
                var p = g.PosAt(now);
                bool inUa = LaunchGroups.Free(p); // окупована — як Росія: тривог там не оголошують
                if (!inUa) continue;
                s.InUa++;
                bool alarm = AlarmAt(p, now);
                if (!alarm) s.NoAlarm++;
                double near = real.Count == 0 ? 9999 : real.Min(r => ThreatGeo.Km(r, p));
                if (near > 150) s.NoReal++;
                s.Near.Add(near);
                if (verbose)
                    Console.WriteLine($"{now:dd.MM HH:mm} {g.Id,-26} ×{g.Left,-3} {OblastGeo.PolyName(p.lat, p.lon),-16} {(alarm ? "🔴" : "⚪")} ближча ціль {near,5:0} км{(g.HoldSince is { } hs ? $" · чекає на межі {(now - hs).TotalMinutes:0} хв" : "")}");
            }
        }

        foreach (var (at, ch, text) in posts)
        {
            while (next <= at) { tr.Tick(next, hub.ListCaps(next), hub.LastListAt); Sample(next); next = next.AddMinutes(5); }
            simNow = at;
            tr.Tick(at, hub.ListCaps(at), hub.LastListAt);
            if (ch == LaunchChannel)
            {
                if (LaunchFeed.ParseLaunch(text) is { } b)
                {
                    LaunchGroups.Add(b, at);
                    if (verbose) Console.WriteLine($"{at:dd.MM HH:mm} 🚀 {string.Join(", ", b.Items.Select(i => $"{i.Origin} {i.N}{(i.Reactive ? "р" : "")}"))}");
                }
                continue;
            }
            if (ch is "nebo_raketa" or "chyste_neboo")
            {
                var ke = KyivMonitor.Analyze(text);
                if (ke.Signal == KhSignal.Threat && !(ke.Kind == ThreatKind.Ballistic && Slang.BallisticThreat(ke.Raw)))
                    tr.Ingest(ke, "Київ", at);
                continue;
            }
            if (SourceCatalog.Get(ch) is not { } si) continue;
            var r = hub.Process(si, text, at);
            foreach (var st in r.Statuses)
                _ = st.Circling ? tr.MarkCircling(st.Pos, st.Kind, at) : tr.MarkShot(st.Pos, st.Kind, st.Shot, at, ch);
            foreach (var e in r.Confirmed) tr.IngestUa(e.Ev, at, (e.Place, e.Pos.lat, e.Pos.lon));
        }

        Console.WriteLine($"\n{"група",-28} {"з",-6} {"до",-6} {"×",3} {"знімків",7} {"в Укр",5} {"без трив.",9} {"далеко",6} {"медіана км",10}");
        int all = 0, ua = 0, noAl = 0, noReal = 0;
        foreach (var s in stats.Values.OrderBy(s => s.First))
        {
            var med = s.Near.Count == 0 ? double.NaN : s.Near.OrderBy(x => x).ElementAt(s.Near.Count / 2);
            Console.WriteLine($"{s.G.Id + " " + s.G.Via,-28} {s.First:HH:mm} {s.Last:HH:mm} {s.G.N,3} {s.Shown,7} {s.InUa,5} {s.NoAlarm,9} {s.NoReal,6} {med,10:0}");
            all += s.Shown; ua += s.InUa; noAl += s.NoAlarm; noReal += s.NoReal;
        }
        Console.WriteLine($"\nУсього знімків {all}: в Україні {ua}, з них над місцем без тривоги {noAl}, за 150+ км від справжніх цілей {noReal}");
    }

    /// Офіційна тривога (червона чи жовта) у точці в момент [now] — за історією @UkraineAlarmSignal.
    public static bool AlarmAt((double lat, double lon) p, DateTime now) =>
        AreaLog.AlarmsAt(p.lat, p.lon, now.AddHours(-13), 25).Any(a => a.start <= now && a.end >= now);
}
