using System.Text.Json;
using System.Text.RegularExpressions;

// ── Офіційні тривоги в часі — для офлайн-аудиту (звідки знати, де був відбій у минулому).
// Журнал сервера живого фіду тривог не пише, тож беремо пости @UkraineAlarmSignal з того ж
// журналу (t.me/s): «🔴 Червоний рівень тривоги • X район (Y обл.)», «🟢 Марганець (…) Відбій тривоги».
// Рівень району, громади (рахується на її район), «• Київ», «• … область» — на всю область.
public sealed class AlarmTimeline
{
    readonly List<(DateTime at, string key, string? level)> _events = new();
    int _next;
    readonly Dictionary<string, string> _raion = new();   // Napo(«X район») → red/yellow
    readonly Dictionary<string, string> _whole = new();   // полігон → red/yellow
    readonly Dictionary<string, (string raion, string level)> _hromada = new();
    public int Events => _events.Count;
    public int Unresolved { get; private set; }

    static string Napo(string s) => s.Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'');

    static readonly Regex Entity = new(@"^(?:(?:•|🔴|🟡|🟢)\uFE0F?\s*)(?<n>[^()\n]+?)\s*(?:\((?<alt>[^()]+)\)\s*)?(?:\((?<o>[^()]+?)\s+обл\.?\))?\s*$");

    public static AlarmTimeline FromJournals(IEnumerable<string> files)
    {
        var tl = new AlarmTimeline();
        foreach (var file in files.Where(File.Exists))
        {
            var day = DateTime.TryParse(Path.GetFileNameWithoutExtension(file), out var dd) ? dd : DateTime.Today;
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"UkraineAlarmSignal\"")) continue;
                try
                {
                    var e = JsonDocument.Parse(line).RootElement;
                    if (e.GetProperty("ch").GetString() != "UkraineAlarmSignal") continue;
                    tl.Parse(day + TimeSpan.Parse(e.GetProperty("t").GetString()!), e.GetProperty("text").GetString() ?? "");
                }
                catch { }
            }
        }
        tl._events.Sort((a, b) => a.at.CompareTo(b.at));
        return tl;
    }

    void Parse(DateTime at, string text)
    {
        if (text.StartsWith("Мапа тривог")) return;
        string? section = null; bool sectionSet = false;
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (l.Length == 0) continue;
            string? lv = l.StartsWith("🔴") ? "red" : l.StartsWith("🟡") ? "yellow" : l.StartsWith("🟢") ? "" : null;
            // Заголовок розділу: «🔴 Червоний рівень тривоги», «🟢 Відбій тривоги», «🟡 Відбій червоної тривоги».
            if (lv is not null && !l.Contains('(') && l.Contains("тривог"))
            { section = lv; sectionSet = true; continue; }
            if (l.StartsWith("•") && sectionSet) Add(at, l, section!);
            else if (lv is not null && i == 0) Add(at, l, lv);   // «🟢 Марганець (Дніпропетровська обл.)» + рядок пояснення
        }
    }

    void Add(DateTime at, string line, string level)
    {
        var m = Entity.Match(line);
        if (!m.Success) { Unresolved++; return; }
        var name = m.Groups["n"].Value.Trim();
        var obl = m.Groups["o"].Success ? m.Groups["o"].Value.Trim() + " область" : null;
        string? lvl = level.Length == 0 ? null : level;
        if (name == "Київ" || name == "м. Київ") { _events.Add((at, "W:Kiev City", lvl)); return; }
        if (name.EndsWith(" область") && OfficialAlerts.UaToEn.TryGetValue(name, out var wen)) { _events.Add((at, "W:" + wen, lvl)); return; }
        if (name.EndsWith(" район")) { _events.Add((at, "R:" + Napo(name), lvl)); return; }
        // Громада / місто: беремо точку з довідника міст у цій області → її район.
        string? en = obl is not null && OfficialAlerts.UaToEn.TryGetValue(obl, out var e2) ? e2 : null;
        foreach (var c in Cities.All)
        {
            if (Cities.Norm(c.name) != Cities.Norm(name)) continue;
            if (en is not null && OblastGeo.PolyName(c.lat, c.lon) != en) continue;
            if (OblastGeo.RaionAt(c.lat, c.lon) is { } rn) { _events.Add((at, $"H:{name}|{Napo(rn)}", lvl)); return; }
        }
        Unresolved++;
    }

    /// Прокрутити стан до моменту [now].
    public void AdvanceTo(DateTime now)
    {
        while (_next < _events.Count && _events[_next].at <= now)
        {
            var (_, key, lv) = _events[_next++];
            var k = key[2..];
            switch (key[0])
            {
                case 'W': if (lv is null) _whole.Remove(k); else _whole[k] = lv; break;
                case 'R': if (lv is null) _raion.Remove(k); else _raion[k] = lv; break;
                case 'H':
                    if (lv is null) _hromada.Remove(k); else _hromada[k] = (k[(k.IndexOf('|') + 1)..], lv);
                    break;
            }
        }
    }

    /// Тривога в цій точці (область цілком / район / громада в цьому районі).
    public bool InAlarm(double lat, double lon)
    {
        if (OblastGeo.PolyName(lat, lon) is not { } poly) return false;
        if (_whole.ContainsKey(poly)) return true;
        if (OblastGeo.RaionAt(lat, lon) is not { } r) return false;
        var nr = Napo(r);
        return _raion.ContainsKey(nr) || _hromada.Values.Any(h => h.raion == nr);
    }

    /// Області (полігони), де десь є тривога — як ByPolygon живого фіду.
    public HashSet<string> ActivePolys()
    {
        var set = new HashSet<string>(_whole.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var r in _raion.Keys.Concat(_hromada.Values.Select(h => h.raion)))
            if (OblastGeo.RaionCenter(r) is { } c && OblastGeo.PolyName(c.lat, c.lon) is { } p) set.Add(p);
        return set;
    }

    public string Describe() => $"цілком: {_whole.Count} обл., районів: {_raion.Count}, громад: {_hromada.Count}";
}
