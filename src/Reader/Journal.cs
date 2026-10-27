using System.Text.Json;

// ── WarAlarm · журнал постів каналів ─────────────────────────────────────────
// Кожен пост із каналів + що ми з нього зрозуміли (ціль/місце/«місце не знайдено»), по
// рядку JSON у journal/РРРР-ММ-ДД.jsonl. Щоб виправляти точність не наосліп: після ночі
// видно кожну помилку з її справжнім текстом, і виправлення перевіряються на тих самих
// постах (`dotnet run -- journal [дата]` — звіт).

public static class Journal
{
    const string Dir = "journal";
    static readonly object Gate = new();
    static readonly JsonSerializerOptions Opt = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static void Write(object row)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, $"{DateTime.Now:yyyy-MM-dd}.jsonl"), JsonSerializer.Serialize(row, Opt) + "\n");
            }
        }
        catch { /* журнал не критичний */ }
    }

    /// Сирий пост каналу (усі канали, до будь-якого розбору).
    public static void Post(string ch, int id, DateTime at, string text) =>
        Write(new { t = at.ToString("HH:mm:ss"), ch, id, kind = "post", text });

    /// Що хаб зрозумів із поста: вердикт, примітка, цілі з місцями.
    public static void Hub(string ch, int id, HubResult r) =>
        Write(new
        {
            t = DateTime.Now.ToString("HH:mm:ss"), ch, id, kind = "hub", verdict = r.Verdict.ToString(), note = r.Note,
            targets = r.Confirmed.Select(e => new { k = e.Ev.Kind.ToString(), place = e.Place, lat = Math.Round(e.Pos.lat, 3), lon = Math.Round(e.Pos.lon, 3), n = e.Ev.Count, toward = e.Ev.Toward }),
        });

    /// Прогнати пости журналу через ТЕПЕРІШНІЙ хаб (з реальним часом постів): кожен пост,
    /// що він зрозумів тоді і що розуміє зараз. Щоб правки сортувальника перевірялись на
    /// справжній ночі, а не на вигаданих прикладах.   `dotnet run -- replay [дата|файл] [вихід.txt]`
    public static void Replay(string? date, string? outPath)
    {
        var file = date is { } d && File.Exists(d) ? d : Path.Combine(Dir, $"{date ?? DateTime.Now.ToString("yyyy-MM-dd")}.jsonl");
        if (!File.Exists(file)) { Console.WriteLine($"Немає журналу {file}"); return; }
        var day = DateTime.TryParse(Path.GetFileNameWithoutExtension(file), out var dd) ? dd : DateTime.Today;
        var posts = new List<(string ch, int id, DateTime at, string text)>();
        var was = new Dictionary<(string, int), string>();
        foreach (var line in File.ReadLines(file))
        {
            try
            {
                var e = JsonDocument.Parse(line).RootElement;
                var ch = e.GetProperty("ch").GetString()!; var id = e.GetProperty("id").GetInt32();
                if (e.GetProperty("kind").GetString() == "post")
                    posts.Add((ch, id, day + TimeSpan.Parse(e.GetProperty("t").GetString()!), e.GetProperty("text").GetString() ?? ""));
                else was[(ch, id)] = e.GetProperty("verdict").GetString() ?? "";
            }
            catch { }
        }
        var hub = new SourceHub();
        var sb = new System.Text.StringBuilder();
        var now = new Dictionary<HubVerdict, int>();
        int changed = 0;
        foreach (var (ch, id, at, text) in posts.OrderBy(p => p.at))
        {
            if (SourceCatalog.Get(ch) is not { } si) continue;
            // Лише пости, що й наживо йшли в хаб (харківські/київські канали має свій парсер).
            if (was.Count > 0 && !was.ContainsKey((ch, id))) continue;
            var r = hub.Process(si, text, at);
            now[r.Verdict] = now.GetValueOrDefault(r.Verdict) + 1;
            var old = was.GetValueOrDefault((ch, id), "?");
            if (old != r.Verdict.ToString()) changed++;
            var what = r.Verdict switch
            {
                HubVerdict.Confirmed => string.Join("; ", r.Confirmed.Select(e => $"{e.Ev.Kind} {e.Place}×{e.Ev.Count}")),
                HubVerdict.AllClear => "відбій " + (r.ClearOblast ?? ""),
                _ => r.Note,
            };
            sb.AppendLine($"{at:HH:mm:ss}\t@{ch}\t{old}→{r.Verdict}\t{what}\t{text.Replace("\n", " ⏎ ")}");
        }
        if (outPath != null) File.WriteAllText(outPath, sb.ToString()); else Console.Write(sb);
        Console.WriteLine($"📒 {file}: постів каталогу {now.Values.Sum()}, вердикт змінився у {changed}");
        foreach (var (v, n) in now.OrderByDescending(p => p.Value)) Console.WriteLine($"   {v,-10} {n}");
        Console.WriteLine("\n" + hub.Report(15));
    }

    /// Звіт за день: скільки постів, що не розібрано (з текстами), які місця найчастіші.
    public static void Report(string? date)
    {
        var file = Path.Combine(Dir, $"{date ?? DateTime.Now.ToString("yyyy-MM-dd")}.jsonl");
        if (!File.Exists(file)) { Console.WriteLine($"Немає журналу {file}"); return; }
        var posts = new Dictionary<(string, int), string>();
        var rows = new List<JsonElement>();
        foreach (var line in File.ReadLines(file))
        {
            try
            {
                var e = JsonDocument.Parse(line).RootElement.Clone();
                if (e.GetProperty("kind").GetString() == "post")
                    posts[(e.GetProperty("ch").GetString()!, e.GetProperty("id").GetInt32())] = e.GetProperty("text").GetString() ?? "";
                else rows.Add(e);
            }
            catch { }
        }
        Console.WriteLine($"📒 {file}: постів {posts.Count}, розборів хабу {rows.Count}");
        foreach (var g in rows.GroupBy(r => r.GetProperty("verdict").GetString()).OrderByDescending(g => g.Count()))
            Console.WriteLine($"   {g.Key,-10} {g.Count()}");
        Console.WriteLine("\n❓ «Місце не знайдено» (останні 25, з текстом):");
        foreach (var r in rows.Where(r => r.GetProperty("verdict").GetString() == "Unplaced").TakeLast(25))
        {
            var key = (r.GetProperty("ch").GetString()!, r.GetProperty("id").GetInt32());
            var text = posts.TryGetValue(key, out var t) ? t.Replace("\n", " ⏎ ") : "?";
            Console.WriteLine($"  [{r.GetProperty("t").GetString()}] @{key.Item1}: {(text.Length > 140 ? text[..140] + "…" : text)}");
        }
        Console.WriteLine("\n📍 Найчастіші місця цілей:");
        foreach (var g in rows.SelectMany(r => r.GetProperty("targets").EnumerateArray())
                              .GroupBy(x => x.GetProperty("place").GetString()).OrderByDescending(g => g.Count()).Take(20))
            Console.WriteLine($"   {g.Count(),4} × {g.Key}");
    }
}
