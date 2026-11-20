using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// ── WarAlarm · ☄ загроза балістики по областях ───────────────────────────────
// «Загроза балістики з Брянська» — ракети ще немає, але секунди вирішують: тривога в
// областях, яких це стосується (бот, застосунок, міські й новинний канали), доки:
//   • «Відбій загрози балістики» / «по балістиці чисто»;
//   • «працює вороже ППО» — це ППО РФ збиває наші дрони, а не пуск по нас: тривогу знімаємо
//     й кажемо про це (🛡 Працює вороже ППО);
//   • [Ttl] без нових постів.
// Області — англ. назви полігонів (як ключі OfficialAlerts.ByPolygon і OblastLookup у застосунку).
public static class BallisticAlert
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(20);
    /// Скільки показувати «🛡 Працює вороже ППО — загрозу знято».
    public static readonly TimeSpan PpoShow = TimeSpan.FromMinutes(10);
    /// Після відбою / ворожого ППО запізнілі перепости «загрози» не піднімають тривогу знову.
    static readonly TimeSpan Quiet = TimeSpan.FromMinutes(3);
    /// Загальнонаціональний канал, район пуску не з таблиці — області в межах цієї досяжності.
    const double ReachKm = 420;
    /// Місцевий канал пише про загрозу «з X»: його область — лише якщо X не надто далеко.
    const double LocalReachKm = 560;

    sealed record Entry(DateTime Since, DateTime Last, string? From, string Source);
    static readonly object Gate = new();
    static readonly Dictionary<string, Entry> Active = new();
    static readonly Dictionary<string, DateTime> CancelledAt = new();
    // Остання відміна через вороже ППО: які області й коли.
    static DateTime _ppoAt = DateTime.MinValue;
    static string[] _ppoOblasts = Array.Empty<string>();

    // Куди зазвичай б'ють із району пуску (де тоді вмикають тривогу); інші — за [ReachKm].
    static readonly string[] North = { "Chernihiv", "Sumy", "Kiev", "Kiev City" };
    static readonly string[] NorthEast = { "Sumy", "Chernihiv", "Kharkiv", "Poltava", "Kiev", "Kiev City" };
    static readonly string[] East = { "Kharkiv", "Sumy", "Poltava", "Dnipropetrovs'k" };
    static readonly string[] EastFar = { "Kharkiv", "Sumy", "Poltava", "Luhans'k", "Donets'k", "Dnipropetrovs'k" };
    static readonly string[] SouthEast = { "Donets'k", "Zaporizhzhya", "Dnipropetrovs'k", "Kharkiv" };
    static readonly string[] South = { "Kherson", "Mykolayiv", "Odessa", "Zaporizhzhya", "Dnipropetrovs'k" };
    static readonly Dictionary<string, string[]> SiteReach = new()
    {
        ["Брянськ"] = North, ["Смоленськ"] = North, ["Орел"] = North, ["Курськ"] = NorthEast, ["Липецьк"] = NorthEast,
        ["Бєлгород"] = East, ["Воронеж"] = EastFar,
        ["Ростов"] = SouthEast, ["Таганрог"] = SouthEast, ["Міллерово"] = SouthEast, ["Донецьк"] = SouthEast,
        ["Луганськ"] = SouthEast, ["Маріуполь"] = SouthEast, ["Бердянськ"] = SouthEast,
        ["Крим"] = South, ["Джанкой"] = South, ["Саки"] = South, ["Севастополь"] = South, ["Чорне море"] = South,
    };

    // ── Що це за пост ────────────────────────────────────────────────────────────
    public enum Kind { None, Threat, Clear, EnemyPpo }
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    // «По балістиці без загроз», «загрозу балістики знято», «загроза минула».
    static readonly Regex ClearMore = new(@"(?:балісти|баллист|швидкісн)\w*\W{0,3}(?:\w+\s+){0,2}?без\s+загроз|без\s+загроз\w*\s+(?:по\s+)?балісти"
        + @"|загроз\w*\s+(?:\w+\s+){0,2}?(?:минул|знят|скасован|зникл)|(?:знят|скасован)\w*\s+загроз\w*\s+(?:\w+\s+)?балісти", O);
    // «По балістиці ймовірно ППО» — це те саме, що «вороже ППО».
    static readonly Regex PpoOfBallistic = new(@"(?:балісти|баллист)\w*\W{0,3}(?:\w+\s+){0,2}?(?:ппо|пво)(?!\p{L})", O);
    // Не живі сигнали: оцінки, підсумки, поради на вечір.
    // «уважно найближчу годину-дві до загрози балістики», «Починаючи з 01:30 підвищена загроза» — прогноз, не сигнал.
    static readonly Regex NotLive = new(@"оцінка|за добу|минулої ночі|цієї ночі|вночі|ймовірність застосування|запас|закликаєм|рекоменду|не перебувати"
        + @"|найближч\w*\s+(?:годин|час)|годину-дві|починаючи\s+з", O);

    public static Kind Classify(string text)
    {
        if (Slang.EnemyPpo(text)) return Kind.EnemyPpo;
        if (!Slang.BallisticMention(text)) return Kind.None;
        if (PpoOfBallistic.IsMatch(text) && text.Length <= 220 && !text.Contains('?')) return Kind.EnemyPpo;
        if (ThreatTracker.BallisticClearNow(text) || ClearMore.IsMatch(text)) return Kind.Clear;
        if (ThreatTracker.BallisticClear.IsMatch(text)) return Kind.None; // «буде відбій, якщо…» — ще ні те, ні те
        return text.Length <= 400 && Slang.BallisticThreat(text) && !NotLive.IsMatch(text) ? Kind.Threat : Kind.None;
    }

    /// Район пуску з поста («з Таганрогу», «з району Брянської області»).
    public static (string name, double lat, double lon)? SiteOf(string text) => LaunchSites.FromText(ThreatParser.FromOf(text) ?? text);

    // Названі в пості області: «(Харківська обл.)», «Харківщина», «у Києві».
    static string[] Mentioned(string text)
    {
        var low = text.ToLowerInvariant();
        var set = new HashSet<string>();
        foreach (var (stem, canon) in Geo.Oblasts)
            if (low.Contains(stem[..Math.Max(4, stem.Length - 3)]) && OblastGeo.EnName(canon) is { } en) set.Add(en);
        foreach (Match m in Regex.Matches(text, @"([А-ЯІЇЄҐ][\p{L}'’-]+ськ)(?:а|ої|ій|у)\s+обл", O))
            foreach (var canon in Geo.Oblasts.Values.Distinct())
                if (canon.StartsWith(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase) && OblastGeo.EnName(canon) is { } en) set.Add(en);
        if (Regex.IsMatch(low, @"(?<!\p{L})(?:києв|київ(?!щ|ськ)|киев(?!щ|ск))")) { set.Add("Kiev City"); set.Add("Kiev"); }
        set.Remove("Crimea");
        return set.ToArray();
    }

    // «з півдня», «з півночі», «зі сходу» (офіційні: «Загроза застосування балістичного озброєння з півдня»).
    static string[]? Direction(string text)
    {
        var l = text.ToLowerInvariant();
        if (Regex.IsMatch(l, @"(?:з|із|зі|с)\s+(?:північного\s+сходу|северо-востока)")) return NorthEast.Union(East).ToArray();
        if (Regex.IsMatch(l, @"(?:з|із|зі|с)\s+(?:півночі|севера)")) return North.Union(NorthEast).ToArray();
        if (Regex.IsMatch(l, @"(?:з|із|зі|с)\s+(?:півдня|юга)")) return South;
        if (Regex.IsMatch(l, @"(?:з|із|зі|с)\s+(?:сходу|востока)")) return EastFar.Union(SouthEast).ToArray();
        return null;
    }

    static List<string> Local(IEnumerable<string> localCanon)
    {
        var local = localCanon.Select(c => c == "Київ" ? "Kiev City" : OblastGeo.EnName(c)).OfType<string>().Distinct().ToList();
        if (local.Count > 4) local.Clear(); // «уся країна» — не місцевий
        if (local.Contains("Kiev") && !local.Contains("Kiev City")) local.Add("Kiev City");
        return local;
    }

    static (double lat, double lon)? Center(string en) =>
        OblastGeo.Centers.FirstOrDefault(c => c.en == en) is { en: not null } c ? (c.lat, c.lon) : null;

    /// Області загрози: названі в пості; інакше місцевий канал — його області (якщо район пуску
    /// досяжний); інакше — куди б'ють із названого району пуску / напрямку. Порожньо — невідомо.
    public static string[] ScopeOf(IEnumerable<string> localCanon, string text)
    {
        var said = Mentioned(text);
        if (said.Length > 0) return said;
        var site = SiteOf(text);
        var local = Local(localCanon);
        if (local.Count > 0)
            return site is { } s0
                ? local.Where(o => Center(o) is not { } c || ThreatGeo.Km(c, (s0.lat, s0.lon)) <= LocalReachKm).ToArray()
                : local.ToArray();
        if (site is { } s)
            return SiteReach.TryGetValue(s.name, out var known) ? known : OblastGeo.Centers
                .Where(c => c.en is not ("Crimea" or "Sevastopol'") && ThreatGeo.Km((c.lat, c.lon), (s.lat, s.lon)) <= ReachKm)
                .Select(c => c.en).ToArray();
        return Direction(text) ?? Array.Empty<string>();
    }

    /// Що знімає відбій / вороже ППО: названі чи свої області; «вороже ППО в Курську» — загрози з
    /// Курська; загальне «працює вороже ППО» / офіційний відбій — усі. Порожньо = усі.
    public static string[] CancelScope(IEnumerable<string> localCanon, string text)
    {
        var said = Mentioned(text);
        if (said.Length > 0) return said;
        var local = Local(localCanon);
        if (local.Count > 0) return local.ToArray();
        if (SiteOf(text) is { } s)
        {
            lock (Gate)
                return Active.Where(kv => kv.Value.From == s.name).Select(kv => kv.Key)
                    .Concat(SiteReach.TryGetValue(s.name, out var r) ? r : Array.Empty<string>()).Distinct().ToArray() is { Length: > 0 } hit
                    ? hit : new[] { "-" }; // нічого з того району — нічого й не знімаємо
        }
        return Array.Empty<string>();
    }

    /// Нова/повторна загроза → області, де вона щойно ПОЧАЛАСЬ (для постів і сповіщень).
    public static string[] Raise(IReadOnlyCollection<string> oblasts, string? from, string source, DateTime now)
    {
        var fresh = new List<string>();
        lock (Gate)
        {
            Expire(now);
            foreach (var o in oblasts)
            {
                if (Active.TryGetValue(o, out var e)) Active[o] = e with { Last = now, From = from ?? e.From };
                else if (CancelledAt.TryGetValue(o, out var c) && now - c < Quiet) continue; // перепост після відбою
                else { Active[o] = new Entry(now, now, from, source); fresh.Add(o); }
            }
            if (fresh.Count > 0) _ppoOblasts = _ppoOblasts.Except(fresh).ToArray();
        }
        return fresh.ToArray();
    }

    /// Відбій / вороже ППО → області, де загрозу ЗНЯЛИ. [oblasts] порожній — усі.
    public static string[] Cancel(IReadOnlyCollection<string> oblasts, bool enemyPpo, DateTime now)
    {
        lock (Gate)
        {
            Expire(now);
            var gone = Active.Keys.Where(k => oblasts.Count == 0 || oblasts.Contains(k)).ToArray();
            foreach (var k in gone) { Active.Remove(k); CancelledAt[k] = now; }
            if (enemyPpo && gone.Length > 0) { _ppoAt = now; _ppoOblasts = _ppoOblasts.Union(gone).ToArray(); }
            return gone;
        }
    }

    static void Expire(DateTime now)
    {
        foreach (var k in Active.Where(kv => now - kv.Value.Last > Ttl).Select(kv => kv.Key).ToList()) Active.Remove(k);
        if (now - _ppoAt > PpoShow) _ppoOblasts = Array.Empty<string>();
    }

    /// Чи діє загроза в точці: (так, звідки) або null.
    public static (bool on, string? from) At(double lat, double lon, DateTime now)
    {
        var en = OblastGeo.PolyName(lat, lon);
        if (en is null) return (false, null);
        lock (Gate)
        {
            Expire(now);
            return Active.TryGetValue(en, out var e) ? (true, e.From) : (false, null);
        }
    }

    /// Щойно (≤ [PpoShow]) загрозу в цій точці зняли через вороже ППО.
    public static bool PpoCleared(double lat, double lon, DateTime now)
    {
        var en = OblastGeo.PolyName(lat, lon);
        lock (Gate) { Expire(now); return en != null && _ppoOblasts.Contains(en); }
    }

    public static bool AnyIn(IEnumerable<string> oblasts, DateTime now)
    {
        lock (Gate) { Expire(now); return oblasts.Any(Active.ContainsKey); }
    }

    /// «Київщина, Чернігівщина, Сумщина» (Київ окремо не називаємо, коли є Київщина).
    public static string Names(IEnumerable<string> oblasts)
    {
        var l = oblasts.ToList();
        if (l.Contains("Kiev")) l.Remove("Kiev City");
        return string.Join(", ", l.Select(OblastGeo.Short));
    }

    /// Для застосунку (state.ballThreat): {"active":[{"o":"Kharkiv","from":"Бєлгород","at":ms}],"ppo":{"at":ms,"o":[...]}}.
    public static string Json(DateTime now)
    {
        static long Ms(DateTime t) => ((DateTimeOffset)t.ToUniversalTime()).ToUnixTimeMilliseconds();
        static string Q(string s) => System.Text.Json.JsonSerializer.Serialize(s);
        lock (Gate)
        {
            Expire(now);
            var sb = new StringBuilder("{\"active\":[");
            sb.AppendJoin(',', Active.OrderBy(kv => kv.Key).Select(kv =>
                $"{{\"o\":{Q(kv.Key)},\"from\":{(kv.Value.From is { } f ? Q(f) : "null")},\"at\":{Ms(kv.Value.Since).ToString(CultureInfo.InvariantCulture)}}}"));
            sb.Append(']');
            if (_ppoOblasts.Length > 0)
                sb.Append($",\"ppo\":{{\"at\":{Ms(_ppoAt).ToString(CultureInfo.InvariantCulture)},\"o\":[{string.Join(',', _ppoOblasts.Order().Select(Q))}]}}");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
