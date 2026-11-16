using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ── WarAlarm · 📈 Історія тривог по районах і громадах (аналітика «моїх місць») ──
// Журнал alarms.csv знає лише власні зони Reader (Харків/Київ), тому аналітика в застосунку
// й боті не бачила інших міст. Тут — історія офіційних тривог для БУДЬ-ЯКОГО місця:
//   • джерело — канал @UkraineAlarmSignal (кожна тривога / відбій по району й громаді
//     з областю в дужках + сповіщення громад про загрозу: 🛸 БпЛА, 💣 КАБ, 💥 вибухи…);
//   • перший запуск — відновлюємо історію з журналу постів (journal/*.jsonl);
//   • area_log.json — стан на диску; Firebase areaLog/<область>/<день> — застосунку
//     (лише змінені дні, не частіше разу на 5 хв: трафік мізерний).
// Застосунок / бот беруть для місця тривоги його району (+ громади поруч) і загрози поруч.
public static class AreaLog
{
    public const string Channel = "UkraineAlarmSignal";
    const string FileName = "area_log.json";
    public const int KeepDays = 60;

    public sealed class Span
    {
        public required string Obl;   // англ. назва полігона (Kharkiv, Kiev City…)
        public required string Unit;  // «Харківський район» / «Харків» (громада)
        public double? Lat, Lon;      // для громади — координати; район — null (за назвою)
        public DateTime Start;        // локальний (Київ)
        public DateTime? End;         // null — триває
        public int Level;             // 2 червона, 1 жовта
    }

    /// Сповіщення громади: загроза БпЛА / КАБ / ракет / обстріл, або «вибухи».
    public sealed record Note(string Obl, DateTime At, string Kind, string Place, double Lat, double Lon);

    static readonly object Gate = new();
    static readonly List<Span> Spans = new();
    static readonly Dictionary<string, Span> Open = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<Note> Notes = new();
    static readonly HashSet<(string obl, string day)> Dirty = new();
    static bool _changed, _loaded, _backfill;
    // Історія з журналу: пропущений відбій не перевірити фідом — тривога понад 12 год там
    // майже завжди пропуск. Наживо фід закриває/відкриває сам, тож межа — лише запобіжник.
    static TimeSpan MaxSpan => _backfill ? TimeSpan.FromHours(12) : TimeSpan.FromHours(30);
    static DateTime _lastAt; // час останнього поста каналу (пропуск у журналі — Reader не працював)
    static readonly TimeSpan Gap = TimeSpan.FromHours(3);

    // ── Розбір поста ─────────────────────────────────────────────────────────
    public enum State { Off, Yellow, Red }
    public sealed record Unit(string Obl, string Name, double? Lat, double? Lon, State State);

    static readonly Regex Paren = new(@"^(?<name>.+?)\s*\((?<inner>[^()]*)\)\s*$");

    /// «Жмеринський район (Вінницька обл.)» / «Космічний (Запоріжжя, Запорізька обл.)» /
    /// «Жуляни (Київ)» / «Київ» → (область, назва, координати громади або null).
    static (string obl, string name, double? lat, double? lon)? Place(string s)
    {
        s = s.Trim().TrimEnd('.', '!', ' ');
        if (s.Length == 0) return null;
        string name, obl;
        string? city = null;
        var m = Paren.Match(s);
        if (m.Success)
        {
            name = m.Groups["name"].Value.Trim();
            var parts = m.Groups["inner"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var last = parts.Length > 0 ? parts[^1] : "";
            if (last.Equals("Київ", StringComparison.OrdinalIgnoreCase)) { obl = "Kiev City"; city = "Київ"; }
            else
            {
                var canon = Regex.Replace(last, @"\s*обл\.?$", "").Trim();
                if (OblastGeo.EnName(canon) is not { } en) return null;
                obl = en;
                if (parts.Length > 1) city = parts[0];
            }
        }
        else if (s.Equals("Київ", StringComparison.OrdinalIgnoreCase) || s.Equals("м. Київ", StringComparison.OrdinalIgnoreCase))
        {
            name = "Київ"; obl = "Kiev City";
        }
        else return null;
        if (Regex.IsMatch(name, @"\sрайон$", RegexOptions.IgnoreCase)) return (obl, name, null, null);
        // Громада / мікрорайон: координати міста (для мікрорайону — його міста).
        var at = Locate(city ?? name, obl) ?? (city is not null ? Locate(name, obl) : null);
        return (obl, name, at?.lat, at?.lon);
    }

    static Dictionary<string, List<(double lat, double lon)>>? _byName;
    static (double lat, double lon)? Locate(string name, string obl)
    {
        if (_byName is null)
        {
            var d = new Dictionary<string, List<(double, double)>>();
            foreach (var c in Cities.All)
            {
                var k = Cities.Norm(c.name);
                if (!d.TryGetValue(k, out var l)) d[k] = l = new();
                l.Add((c.lat, c.lon));
            }
            _byName = d;
        }
        var key = Cities.Norm(Regex.Replace(name, @"^(м\.|смт|с\.|селище)\s*", ""));
        if (!_byName.TryGetValue(key, out var list)) return null;
        foreach (var p in list)
        {
            var o = OblastGeo.PolyName(p.lat, p.lon);
            if (o == obl || (obl == "Kiev City" && o is "Kiev" or "Kiev City")) return p;
        }
        return null;
    }

    static readonly string[] OnWords = { "тривог", "рівень" };

    /// Пост каналу → зміни тривог по районах/громадах і сповіщення про загрози.
    public static (List<Unit> units, List<(string obl, string kind, string place, double lat, double lon)> notes) Parse(string text)
    {
        var units = new List<Unit>();
        var notes = new List<(string, string, string, double, double)>();
        var lines = text.Split('\n', StringSplitOptions.TrimEntries);
        State? cur = null;
        string low = text.ToLowerInvariant();
        string noteKind = low.Contains("вибух") ? "impact"
            : low.Contains("каб") ? "Kab"
            : low.Contains("високошвидкісн") || low.Contains("баліст") ? "Ballistic"
            : low.Contains("ракет") ? "Missile"
            : low.Contains("бпла") || low.Contains("дрон") || low.Contains("шахед") ? "Drone"
            : low.Contains("обстріл") || low.Contains("артилер") ? "Shelling"
            : "Unknown";
        foreach (var raw in lines)
        {
            if (raw.Length == 0) continue;
            State? mark = raw.StartsWith("🔴") ? State.Red : raw.StartsWith("🟡") ? State.Yellow : raw.StartsWith("🟢") ? State.Off : null;
            if (mark is { } st)
            {
                var rest = raw[2..].Trim();
                var pl = Place(rest);
                if (pl is null && OnWords.Any(w => rest.Contains(w, StringComparison.OrdinalIgnoreCase)) || rest.StartsWith("Відбій", StringComparison.OrdinalIgnoreCase))
                {
                    cur = st; // заголовок списку: «🔴 Червоний рівень тривоги», «🟢 Відбій тривоги»
                    continue;
                }
                if (pl is { } p) { units.Add(new Unit(p.obl, p.name, p.lat, p.lon, st)); cur = st; }
                continue;
            }
            if (raw.StartsWith("•") && cur is { } c)
            {
                if (Place(raw.TrimStart('•').Trim()) is { } p) units.Add(new Unit(p.obl, p.name, p.lat, p.lon, c));
                continue;
            }
            // Сповіщення громади: «🛸 Березанка (Миколаївська обл.)», «💥 Микільське (…)».
            if (raw.Length > 2 && !char.IsLetterOrDigit(raw[0]) && raw[0] != '•' && raw[0] != '(')
            {
                var rest = Regex.Replace(raw, @"^[^\p{L}\d]+", "").Trim();
                if (rest.Contains('(') && Place(rest) is { lat: { } la, lon: { } lo } p)
                {
                    var kind = raw.StartsWith("💥") ? "impact" : raw.StartsWith("💣") ? "Kab" : raw.StartsWith("🛸") ? "Drone"
                        : raw.StartsWith("🚀") && noteKind is "Unknown" ? "Missile" : noteKind;
                    notes.Add((p.obl, kind, p.name, la, lo));
                }
            }
        }
        return (units, notes);
    }

    /// Живий пост або рядок журналу.
    public static void Ingest(string text, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var (units, notes) = Parse(text);
        if (units.Count == 0 && notes.Count == 0) return;
        lock (Gate) Apply(units, notes, at);
    }

    static string Day(DateTime t) => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static void Apply(List<Unit> units, List<(string obl, string kind, string place, double lat, double lon)> notes, DateTime at)
    {
        // Довга тиша каналу = Reader не працював і міг пропустити відбої: закриваємо, що
        // було відкрите, на момент останнього поста (інакше тривога «висіла» б добу).
        if (_lastAt != default && at - _lastAt > Gap) CloseAll(_lastAt);
        if (at > _lastAt) _lastAt = at;
        foreach (var u in units)
        {
            var key = u.Obl + "|" + u.Name;
            if (u.State == State.Off)
            {
                if (Open.Remove(key, out var sp))
                {
                    sp.End = at < sp.Start ? sp.Start : at;
                    Dirty.Add((sp.Obl, Day(sp.Start)));
                    _changed = true;
                }
                continue;
            }
            int lvl = u.State == State.Red ? 2 : 1;
            if (Open.TryGetValue(key, out var open))
            {
                if (lvl > open.Level) { open.Level = lvl; Dirty.Add((open.Obl, Day(open.Start))); _changed = true; }
                continue;
            }
            var s = new Span { Obl = u.Obl, Unit = u.Name, Lat = u.Lat, Lon = u.Lon, Start = at, Level = lvl };
            Spans.Add(s);
            Open[key] = s;
            Dirty.Add((s.Obl, Day(at)));
            _changed = true;
        }
        foreach (var n in notes)
        {
            // Той самий пост двічі (редагування / повтор у журналі) — один запис.
            if (Notes.Any(x => x.Obl == n.obl && x.Place == n.place && x.Kind == n.kind && Math.Abs((x.At - at).TotalMinutes) < 10)) continue;
            Notes.Add(new Note(n.obl, at, n.kind, n.place, n.lat, n.lon));
            Dirty.Add((n.obl, Day(at)));
            _changed = true;
        }
        // Тривога, що «висить» понад добу без відбою (пропустили пост) — закриваємо: інакше
        // аналітика рахувала б тиждень без перерви.
        foreach (var (k, sp) in Open.Where(kv => at - kv.Value.Start > MaxSpan).ToList())
        {
            sp.End = sp.Start + MaxSpan;
            Open.Remove(k);
            Dirty.Add((sp.Obl, Day(sp.Start)));
        }
    }

    static void CloseAll(DateTime at)
    {
        foreach (var sp in Open.Values)
        {
            sp.End = at < sp.Start ? sp.Start : at;
            Dirty.Add((sp.Obl, Day(sp.Start)));
        }
        if (Open.Count > 0) _changed = true;
        Open.Clear();
    }

    static string Napo(string s) => s.Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'').ToLowerInvariant();

    /// Звірка з офіційним API (кожне опитування): район, якого там уже немає, — відбій; район
    /// під тривогою, а в нас ні (пропустили пост) — відкриваємо. Громади (їх у фіді немає)
    /// закриваємо, лише коли в області тривоги немає зовсім.
    public static void Sync(OfficialAlerts.Snapshot snap, DateTime now)
    {
        if (!snap.Ok || !_loaded) return;
        var raions = new HashSet<string>((snap.ByRaion ?? new()).Keys.Select(Napo));
        lock (Gate)
        {
            foreach (var (k, sp) in Open.ToList())
            {
                if (now - sp.Start < TimeSpan.FromMinutes(5)) continue; // фід запізнюється
                bool whole = snap.ByOblastWhole?.ContainsKey(sp.Obl) == true;
                bool active = sp.Lat is null ? whole || raions.Contains(Napo(sp.Unit)) : snap.ByPolygon.ContainsKey(sp.Obl);
                if (active) { Miss.Remove(k); continue; }
                // Один порожній опит API — ще не відбій (фід буває неповним): лише двічі поспіль.
                Miss[k] = Miss.GetValueOrDefault(k) + 1;
                if (Miss[k] < 2) continue;
                Miss.Remove(k);
                sp.End = now;
                Open.Remove(k);
                Dirty.Add((sp.Obl, Day(sp.Start)));
                _changed = true;
            }
            var seenNow = new HashSet<string>();
            foreach (var (raion, lvl) in snap.ByRaion ?? new())
            {
                if (snap.RaionObl?.TryGetValue(raion, out var obl) != true || obl is null) continue;
                if (Open.Keys.Any(k => k.StartsWith(obl + "|", StringComparison.OrdinalIgnoreCase) && Napo(k[(obl.Length + 1)..]) == Napo(raion))) continue;
                // Тривоги нема в каналі, але є в API — відкриваємо, коли видно двічі поспіль
                // (пост каналу зазвичай приходить раніше; час початку — перший опит).
                var key = obl + "|" + raion;
                seenNow.Add(key);
                if (!Seen.TryGetValue(key, out var first)) { Seen[key] = now; continue; }
                var s = new Span { Obl = obl, Unit = raion, Start = first, Level = lvl == "red" ? 2 : 1 };
                Spans.Add(s);
                Open[obl + "|" + raion] = s;
                Seen.Remove(key);
                Dirty.Add((obl, Day(first)));
                _changed = true;
            }
            foreach (var k in Seen.Keys.Where(k => !seenNow.Contains(k)).ToList()) Seen.Remove(k);
            foreach (var k in Miss.Keys.Where(k => !Open.ContainsKey(k)).ToList()) Miss.Remove(k);
        }
    }
    static readonly Dictionary<string, int> Miss = new();        // відкрита тривога, якої нема в API: скільки опитів поспіль
    static readonly Dictionary<string, DateTime> Seen = new();   // район є в API, а в нас — ні: коли вперше побачили

    // ── Диск ────────────────────────────────────────────────────────────────
    static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();
    static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// Завантажити стан; немає файлу — відновити з журналу постів. true — щойно відновили.
    public static bool Load(string journalDir = "journal")
    {
        lock (Gate)
        {
            if (_loaded) return false;
            _loaded = true;
            if (File.Exists(FileName))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(FileName));
                    foreach (var e in doc.RootElement.GetProperty("spans").EnumerateArray())
                    {
                        var s = new Span
                        {
                            Obl = e[0].GetString()!, Unit = e[1].GetString()!,
                            Lat = e[2].ValueKind == JsonValueKind.Number ? e[2].GetDouble() : null,
                            Lon = e[3].ValueKind == JsonValueKind.Number ? e[3].GetDouble() : null,
                            Start = FromMs(e[4].GetInt64()),
                            End = e[5].ValueKind == JsonValueKind.Number ? FromMs(e[5].GetInt64()) : null,
                            Level = e[6].GetInt32(),
                        };
                        Spans.Add(s);
                        if (s.End is null) Open[s.Obl + "|" + s.Unit] = s;
                    }
                    foreach (var e in doc.RootElement.GetProperty("notes").EnumerateArray())
                        Notes.Add(new Note(e[0].GetString()!, FromMs(e[1].GetInt64()), e[2].GetString()!, e[3].GetString()!, e[4].GetDouble(), e[5].GetDouble()));
                    _lastAt = doc.RootElement.TryGetProperty("last", out var la) ? FromMs(la.GetInt64()) : default;
                    return false;
                }
                catch (Exception ex) { Console.Error.WriteLine("⚠ area_log.json: " + ex.Message); Spans.Clear(); Open.Clear(); Notes.Clear(); }
            }
        }
        Backfill(journalDir);
        return true;
    }

    /// Відновити історію з журналу постів (journal/РРРР-ММ-ДД.jsonl, час — київський).
    public static int Backfill(string journalDir)
    {
        if (!Directory.Exists(journalDir)) return 0;
        int n = 0;
        _backfill = true;
        foreach (var file in Directory.GetFiles(journalDir, "*.jsonl").OrderBy(f => f))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"ch\":\"" + Channel + "\"", StringComparison.OrdinalIgnoreCase) || !line.Contains("\"kind\":\"post\"")) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    var at = day + TimeSpan.Parse(r.GetProperty("t").GetString() ?? "00:00:00", CultureInfo.InvariantCulture);
                    Ingest(r.GetProperty("text").GetString() ?? "", at);
                    n++;
                }
                catch { /* битий рядок */ }
            }
        }
        _backfill = false;
        lock (Gate) _changed = true;
        return n;
    }

    public static void Save()
    {
        string json;
        lock (Gate)
        {
            if (!_changed) return;
            _changed = false;
            Prune(DateTime.Now);
            var sb = new StringBuilder("{\"spans\":[");
            sb.AppendJoin(',', Spans.Select(s =>
                $"[{Q(s.Obl)},{Q(s.Unit)},{(s.Lat is { } la ? N(la) : "null")},{(s.Lon is { } lo ? N(lo) : "null")},{Ms(s.Start)},{(s.End is { } e ? Ms(e).ToString(CultureInfo.InvariantCulture) : "null")},{s.Level}]"));
            sb.Append("],\"notes\":[");
            sb.AppendJoin(',', Notes.Select(x => $"[{Q(x.Obl)},{Ms(x.At)},{Q(x.Kind)},{Q(x.Place)},{N(x.Lat)},{N(x.Lon)}]"));
            sb.Append("],\"last\":").Append(Ms(_lastAt == default ? DateTime.Now : _lastAt)).Append('}');
            json = sb.ToString();
        }
        try { File.WriteAllText(FileName + ".tmp", json); File.Move(FileName + ".tmp", FileName, true); }
        catch (Exception ex) { Console.Error.WriteLine("⚠ area_log.json: " + ex.Message); }
    }

    static void Prune(DateTime now)
    {
        var cut = now.AddDays(-KeepDays);
        Spans.RemoveAll(s => s.End is { } e && e < cut);
        Notes.RemoveAll(n => n.At < cut);
    }

    static string Q(string s) => JsonSerializer.Serialize(s);

    // ── Firebase: areaLog/<область>/<день> ──────────────────────────────────
    // {"a":[[назва, початокХв, кінецьХв|0, рівень, lat?, lon?]], "t":[[хв, тип, місце, lat, lon]]}
    // Хвилини — від епохи Unix (UTC). День — за початком тривоги (київський).
    static string DayJson(string obl, string day)
    {
        static long Min(DateTime t) => Ms(t) / 60000;
        var sb = new StringBuilder("{\"a\":[");
        sb.AppendJoin(',', Spans.Where(s => s.Obl == obl && Day(s.Start) == day).Select(s =>
            $"[{Q(s.Unit)},{Min(s.Start)},{(s.End is { } e ? Min(e) : 0)},{s.Level}"
            + (s.Lat is { } la && s.Lon is { } lo ? $",{N(la)},{N(lo)}" : "") + "]"));
        sb.Append("],\"t\":[");
        sb.AppendJoin(',', Notes.Where(n => n.Obl == obl && Day(n.At) == day).Select(n =>
            $"[{Min(n.At)},{Q(n.Kind)},{Q(n.Place)},{N(n.Lat)},{N(n.Lon)}]"));
        sb.Append("]}");
        return sb.ToString();
    }

    static string Key(string s) => Uri.EscapeDataString(s.Replace('.', '_').Replace('/', '_').Replace('#', '_').Replace('$', '_').Replace('[', '_').Replace(']', '_'));

    static string _prunedDay = "";

    /// Відправити змінені дні (і раз на добу прибрати найстаріший).
    public static async Task Publish(FirebasePublisher fb)
    {
        List<(string obl, string day, string json)> jobs;
        lock (Gate)
        {
            jobs = Dirty.Select(d => (d.obl, d.day, DayJson(d.obl, d.day))).ToList();
            Dirty.Clear();
        }
        foreach (var (obl, day, json) in jobs)
            if (!await fb.Publish($"areaLog/{Key(obl)}/{day}", json))
                lock (Gate) Dirty.Add((obl, day)); // не вдалося — наступного разу
        var today = Day(DateTime.Now);
        if (_prunedDay != today)
        {
            _prunedDay = today;
            var old = Day(DateTime.Now.AddDays(-KeepDays - 1));
            foreach (var en in OblastGeo.Centers.Select(c => c.en).Distinct())
                await fb.Publish($"areaLog/{Key(en)}/{old}", "null");
        }
    }

    /// Усі дні з даними — у Firebase (після відновлення з журналу).
    public static void MarkAllDirty()
    {
        lock (Gate)
        {
            foreach (var s in Spans) Dirty.Add((s.Obl, Day(s.Start)));
            foreach (var n in Notes) Dirty.Add((n.Obl, Day(n.At)));
        }
    }

    /// Фон: завантажити / відновити, далі раз на 5 хв — на диск і в Firebase.
    public static void Start(Func<FirebasePublisher?> fb)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (Load())
                {
                    MarkAllDirty();
                    int days; lock (Gate) days = Spans.Select(s => Day(s.Start)).Distinct().Count();
                    Console.WriteLine($"📈 Історію тривог по районах відновлено з журналу: {Spans.Count} тривог за {days} дн.");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("⚠ area log: " + ex.Message); }
            while (true)
            {
                try
                {
                    Save();
                    if (fb() is { } p) await Publish(p);
                }
                catch (Exception ex) { Console.Error.WriteLine("⚠ area log: " + ex.Message); }
                await Task.Delay(TimeSpan.FromMinutes(5));
            }
        });
    }

    // ── Для бота: аналітика місця ───────────────────────────────────────────
    /// Тривоги в місці: район точки + громади ближче [cityKm] (злиті, без подвійного рахунку).
    public static List<(DateTime start, DateTime end, int level)> AlarmsAt(double lat, double lon, DateTime from, double cityKm = 12)
    {
        var obl = OblastGeo.PolyName(lat, lon);
        if (obl is null) return new();
        var raion = OblastGeo.RaionAt(lat, lon);
        List<Span> mine;
        lock (Gate)
            mine = Spans.Where(s => s.Obl == obl && (s.End ?? DateTime.Now) >= from
                && (s.Lat is { } la && s.Lon is { } lo ? ThreatGeo.Km((la, lo), (lat, lon)) <= cityKm
                    : raion is not null && Napo(s.Unit) == Napo(raion))).ToList();
        var res = new List<(DateTime start, DateTime end, int level)>();
        foreach (var s in mine.OrderBy(s => s.Start))
        {
            var end = s.End ?? DateTime.Now;
            if (res.Count > 0 && s.Start <= res[^1].end)
            {
                var last = res[^1];
                res[^1] = (last.start, end > last.end ? end : last.end, Math.Max(last.level, s.Level));
            }
            else res.Add((s.Start, end, s.Level));
        }
        return res;
    }

    /// Сповіщення поруч із точкою (загрози громад, вибухи).
    public static List<Note> NotesAt(double lat, double lon, DateTime from, double km = 25)
    {
        lock (Gate) return Notes.Where(n => n.At >= from && ThreatGeo.Km((n.Lat, n.Lon), (lat, lon)) <= km).ToList();
    }

    public static bool HasData { get { lock (Gate) return Spans.Count > 0; } }
}
