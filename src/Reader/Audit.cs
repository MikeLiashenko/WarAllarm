using System.Text.Json;

// ── 🔎 Аудит карти по журналу (оффлайн):  dotnet run -- audit journal/2026-11-15.jsonl [HH:mm HH:mm]
// Прогін постів каналів-джерел через хаб і трекер у віртуальному часі (як HandleHub наживо).
// Щохвилини шукаємо помилки, які видно на карті: дві мітки однієї цілі поруч (дубль),
// ракета й балістика над тим самим місцем, балістика без слова «балістика» в постах.
public static class Audit
{
    public static void Run(string file, TimeSpan? from = null, TimeSpan? to = null, bool verbose = false)
    {
        var day = DateTime.TryParse(Path.GetFileNameWithoutExtension(file), out var dd) ? dd : DateTime.Today;
        var posts = new List<(DateTime at, string ch, string text)>();
        foreach (var line in File.ReadLines(file))
        {
            if (!line.Contains("\"kind\":\"post\"")) continue;
            try
            {
                var e = JsonDocument.Parse(line).RootElement;
                var at = day + TimeSpan.Parse(e.GetProperty("t").GetString()!);
                if (from is { } f && at.TimeOfDay < f || to is { } t && at.TimeOfDay > t) continue;
                posts.Add((at, e.GetProperty("ch").GetString()!, e.GetProperty("text").GetString() ?? ""));
            }
            catch { }
        }
        posts.Sort((a, b) => a.at.CompareTo(b.at));
        var hub = new SourceHub();
        var tr = new ThreatTracker((50.0, 36.3));
        var why = new Dictionary<string, string>(); // id цілі → пост, що її створив
        var reported = new HashSet<string>();
        int dupPairs = 0, mixPairs = 0, n = 0;
        var kinds = new Dictionary<string, int>();
        DateTime nextCheck = posts.Count > 0 ? posts[0].at : day;
        void Check(DateTime now)
        {
            tr.Tick(now);
            var act = tr.Active.Where(x => x.Zone == "UA" && !x.Launch && !x.Down && x.DrawPos(now) is not null
                                           && now - x.LastUpdate < TimeSpan.FromMinutes(10)).ToList();
            for (int i = 0; i < act.Count; i++)
                for (int j = i + 1; j < act.Count; j++)
                {
                    var a = act[i]; var b = act[j];
                    double km = ThreatGeo.Km(a.DrawPos(now)!.Value, b.DrawPos(now)!.Value);
                    if (km > 4) continue;
                    bool mix = a.Kind != b.Kind && IsRocket(a.Kind) && IsRocket(b.Kind);
                    bool dup = !mix && (a.Kind == b.Kind || Air(a.Kind) && Air(b.Kind)) && !(a.Approx && b.Approx);
                    if (!mix && !dup) continue;
                    var key = string.CompareOrdinal(a.Id, b.Id) < 0 ? a.Id + "|" + b.Id : b.Id + "|" + a.Id;
                    if (!reported.Add(key)) continue;
                    if (mix) mixPairs++; else dupPairs++;
                    var tag = $"{a.Kind}+{b.Kind}";
                    kinds[(mix ? "MIX " : "DUP ") + tag] = kinds.GetValueOrDefault((mix ? "MIX " : "DUP ") + tag) + 1;
                    Console.WriteLine($"\n{now:HH:mm} {(mix ? "⚠ РАКЕТА/БАЛІСТИКА" : "⚠ ДУБЛЬ")} {km:0.0} км: {Desc(a)}  ≈  {Desc(b)}");
                    Console.WriteLine($"     A ← {why.GetValueOrDefault(a.Id, "?")}");
                    Console.WriteLine($"     B ← {why.GetValueOrDefault(b.Id, "?")}");
                }
        }
        foreach (var (at, ch, text) in posts)
        {
            while (nextCheck <= at) { Check(nextCheck); nextCheck = nextCheck.AddMinutes(1); }
            if (SourceCatalog.Get(ch) is not { } si) continue;
            var r = hub.Process(si, text, at);
            foreach (var s in r.Statuses)
                if (s.Circling) tr.MarkCircling(s.Pos, s.Kind, at); else tr.MarkShot(s.Pos, s.Kind, s.Shot, at, si.User, s.Lost);
            if (r.MissilesDone) tr.SweepWhere(t => t.Kind == ThreatKind.Missile && at - t.LastUpdate > TimeSpan.FromMinutes(1));
            if (r.Verdict == HubVerdict.AllClear && r.ClearOblast is { } co && co != "*")
                tr.SweepWhere(t => t.Zone == "UA" && t.Oblast != null && string.Equals(t.Oblast, co, StringComparison.OrdinalIgnoreCase) && at - t.LastUpdate > TimeSpan.FromMinutes(2));
            if (r.Verdict != HubVerdict.Confirmed) continue;
            foreach (var he in r.Confirmed)
            {
                if (he.Ev.Kind != ThreatKind.Target)
                    tr.SweepWhere(t => t.Zone == "UA" && t.Kind == ThreatKind.Target && t.LastPlace == he.Place);
                if (tr.IngestUa(he.Ev, at, (he.Place, he.Pos.lat, he.Pos.lon)) is not { } up) continue;
                n++;
                var one = text.Replace("\n", " ⏎ ");
                var note = $"{at:HH:mm:ss} @{ch}: {one[..Math.Min(140, one.Length)]}";
                if (!why.ContainsKey(up.Threat.Id)) why[up.Threat.Id] = note;
                if (verbose) Console.WriteLine($"{at:HH:mm:ss} {he.Ev.Kind} {he.Place} → {up.Threat.Id}  @{ch}: {one[..Math.Min(100, one.Length)]}");
                if (he.Ev.Kind == ThreatKind.Ballistic && !Slang.BallisticMention(text))
                    Console.WriteLine($"\n{at:HH:mm} ☄? балістика без слова «балістика»: {he.Place}  ← {note}");
            }
        }
        Console.WriteLine($"\n═══ {Path.GetFileName(file)}: цілей на карту {n}, дублів {dupPairs}, ракета/балістика разом {mixPairs}");
        foreach (var (k, v) in kinds.OrderByDescending(p => p.Value)) Console.WriteLine($"   {k,-28} {v}");
    }

    static bool IsRocket(ThreatKind k) => k is ThreatKind.Missile or ThreatKind.Ballistic;
    static bool Air(ThreatKind k) => k is ThreatKind.Drone or ThreatKind.Target;
    static string Desc(Threat t) => $"{ThreatTracker.Label(t)} «{t.LastPlace}» ×{t.Amount} [{t.Id}]";
}
