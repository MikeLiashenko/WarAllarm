using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// ── WarAlarm · Хаб джерел ────────────────────────────────────────────────────
// Усі моніторингові канали з neptun.in.ua/dzherela (82 шт.) в одній системі:
//
//   1. КАТАЛОГ    — рівень довіри кожного каналу (офіційні / розвідка / регіональні
//                   / агрегатори) і його область(і).
//   2. ОЧИСТКА    — прибираємо підписи, рекламу, збори, новини про минулі удари.
//   3. КЛАСИФІКАЦІЯ — ціль / відбій / балістика / вибух / шум.
//   4. РОЗБІР     — ThreatParser з контекстом області каналу + «голі» назви
//                   («Оболонь», «Коцюбинське, Ірпінь») у межах регіону каналу.
//   5. ГЕО        — точний довідник ThreatGeo, далі повний довідник Cities
//                   (~5300 н.п.) з урахуванням відмінків і найближчості до регіону.
//   6. ЗВІРКА     — одна й та сама ціль з різних каналів склеюється в «кластер»;
//                   на карту йде лише ПІДТВЕРДЖЕНЕ:
//                     · офіційні / розвідка — одразу;
//                     · регіональний канал про СВІЙ регіон — одразу (місцеві бачать);
//                     · агрегатор або «чужий» регіон — потрібне 2-ге незалежне джерело.
//   7. СТАТИСТИКА — скільки кожен канал дав, скільки першим, скільки шуму.

public enum SourceTier { Official, Intel, Regional, Aggregator }

public sealed record SourceInfo(string User, SourceTier Tier, string[] Oblasts)
{
    public string Label => Tier switch
    {
        SourceTier.Official => "офіційне",
        SourceTier.Intel => "розвідка",
        SourceTier.Regional => "регіональне",
        _ => "агрегатор",
    };
}

public static class SourceCatalog
{
    static readonly string[] West =
        { "Львівська", "Волинська", "Рівненська", "Тернопільська", "Івано-Франківська", "Закарпатська", "Хмельницька", "Чернівецька" };
    static readonly string[] South = { "Одеська", "Миколаївська", "Херсонська" };
    static readonly string[] North = { "Чернігівська", "Сумська", "Київська" };
    static readonly string[] East = { "Харківська", "Донецька" };

    static SourceInfo R(string u, params string[] obl) => new(u, SourceTier.Regional, obl);
    static SourceInfo A(string u, params string[] obl) => new(u, SourceTier.Aggregator, obl);

    /// Джерела з neptun.in.ua/dzherela, розкладені за рівнем і областю.
    public static readonly Dictionary<string, SourceInfo> All = new(StringComparer.OrdinalIgnoreCase)
    {
        // Офіційні
        ["kpszsu"] = new("kpszsu", SourceTier.Official, Array.Empty<string>()),
        ["UkraineAlarmSignal"] = new("UkraineAlarmSignal", SourceTier.Official, Array.Empty<string>()),
        // Розвідувальні
        ["rozvidkaneba"] = new("rozvidkaneba", SourceTier.Intel, Array.Empty<string>()),
        ["Ukrainian_Intelligence"] = new("Ukrainian_Intelligence", SourceTier.Intel, Array.Empty<string>()),
        // Регіональні спостерігачі
        ["zahidnimonitoring"] = R("zahidnimonitoring", West),
        ["vin_info_ppo"] = R("vin_info_ppo", "Вінницька"),
        ["dnepr_nagladach"] = R("dnepr_nagladach", "Дніпропетровська"),
        ["dnipro_alerts"] = R("dnipro_alerts", "Дніпропетровська"),
        ["Radar_Dnipra"] = R("Radar_Dnipra", "Дніпропетровська"),
        ["nikalert"] = R("nikalert", "Миколаївська"),
        ["shovnebi"] = R("shovnebi"),
        ["kramatorsk_radar"] = R("kramatorsk_radar", "Донецька"),
        ["operatyvnyi_donbas"] = R("operatyvnyi_donbas", "Донецька"),
        ["korostentoday"] = R("korostentoday", "Житомирська"),
        ["eyes_everywhere_ua"] = R("eyes_everywhere_ua"),
        ["gnilayachereha"] = R("gnilayachereha"),
        ["info_zp"] = R("info_zp", "Запорізька"),
        ["operatyvnohlep"] = R("operatyvnohlep", "Запорізька"),
        ["zaborzp"] = R("zaborzp", "Запорізька"),
        ["Northern_Sich_ukr"] = R("Northern_Sich_ukr", North),
        ["kievinform_ua1"] = R("kievinform_ua1", "Київська"),
        ["kyiv_nebo"] = R("kyiv_nebo", "Київська"),
        ["krop_vanek"] = R("krop_vanek", "Кіровоградська"),
        ["pivden_FPV"] = R("pivden_FPV", South),
        ["korabely_media"] = R("korabely_media", "Миколаївська"),
        ["Shturval_media"] = R("Shturval_media", "Миколаївська"),
        ["odessa_inform"] = R("odessa_inform", "Одеська"),
        ["odessa_knight"] = R("odessa_knight", "Одеська"),
        ["odessaveter"] = R("odessaveter", "Одеська"),
        ["ReniHub"] = R("ReniHub", "Одеська"),
        ["temporis_odesa"] = R("temporis_odesa", "Одеська"),
        ["kremen_sv"] = R("kremen_sv", "Полтавська"),
        ["PoltavaRadar"] = R("PoltavaRadar", "Полтавська"),
        ["PoltavaRanger"] = R("PoltavaRanger", "Полтавська"),
        ["ppopoltavanightfighters"] = R("ppopoltavanightfighters", "Полтавська"),
        ["pivden_varta"] = R("pivden_varta", South),
        ["krolevetsnews"] = R("krolevetsnews", "Сумська"),
        ["rdsprostir"] = R("rdsprostir", "Сумська"),
        ["sumygo"] = R("sumygo", "Сумська"),
        ["sumyregion"] = R("sumyregion", "Сумська"),
        ["svessainfo"] = R("svessainfo", "Сумська"),
        ["cxidua"] = R("cxidua", East),
        ["Karkivw"] = R("Karkivw", "Харківська"),
        ["kharkivlife"] = R("kharkivlife", "Харківська"),
        ["kharkov_media"] = R("kharkov_media", "Харківська"),
        ["kuptg"] = R("kuptg", "Харківська"),
        ["monitor1654"] = R("monitor1654", "Харківська"),
        ["Nablydatel_Dozor"] = R("Nablydatel_Dozor", "Харківська"),
        ["place_kharkiv"] = R("place_kharkiv", "Харківська"),
        ["tlknewsua"] = R("tlknewsua", "Харківська"),
        ["kherson_monitoring"] = R("kherson_monitoring", "Херсонська"),
        ["kherson_non_drone"] = R("kherson_non_drone", "Херсонська"),
        ["cherkasy_nebbo"] = R("cherkasy_nebbo", "Черкаська"),
        ["horizon_of_war_Official"] = R("horizon_of_war_Official"),
        ["my_safety_Chernigiv"] = R("my_safety_Chernigiv", "Чернігівська"),
        ["PhantomChe"] = R("PhantomChe", "Чернігівська"),
        ["RadarChernihiv"] = R("RadarChernihiv", "Чернігівська"),
        ["ShahedChernihiv"] = R("ShahedChernihiv", "Чернігівська"),
        ["taktychna_rukavuchkaa"] = R("taktychna_rukavuchkaa"),
        ["kyiv_airdef"] = R("kyiv_airdef", "Київська"),
        ["monitoring_kyiv"] = R("monitoring_kyiv", "Київська"),
        ["shahedradar"] = R("shahedradar", "Київська"),
        // Загальнонаціональні агрегатори
        ["eRadarrua"] = A("eRadarrua", "Київська"), // пише райони столиці («Теличка», «Оболонь»)
        ["kudy_letyt"] = A("kudy_letyt"),
        ["monikppy"] = A("monikppy"),
        ["monitor_ukr"] = A("monitor_ukr"),
        ["newspn"] = A("newspn"),
        ["povitryanatrivogaaa"] = A("povitryanatrivogaaa"),
        ["radar_raketaa"] = A("radar_raketaa"),
        ["radar_top_ua"] = A("radar_top_ua"),
        ["raketa_trevoga"] = A("raketa_trevoga"),
        ["sectorv666"] = A("sectorv666"),
        ["totallzrada"] = A("totallzrada"),
        ["tryvoga_chomu"] = A("tryvoga_chomu"),
        ["UkraineRadar_24_7"] = A("UkraineRadar_24_7"),
        ["vanek_nikolaev"] = A("vanek_nikolaev", "Миколаївська"),
        ["veselyy_pivden"] = A("veselyy_pivden", South),
        ["vibuxaviasia"] = A("vibuxaviasia"),
        ["war_monitor"] = A("war_monitor"),
        ["monitoring_zaxid"] = A("monitoring_zaxid", West),
        ["rocket_danger_lviv"] = A("rocket_danger_lviv", "Львівська"),
    };

    public static SourceInfo? Get(string? user) =>
        user is { Length: > 0 } && All.TryGetValue(user.TrimStart('@'), out var s) ? s : null;

    /// Довіра до повідомлення про місце: 1.0 — підтверджує саме по собі.
    public static double Weight(SourceInfo s, string? placeOblast) => s.Tier switch
    {
        SourceTier.Official => 1.0,
        SourceTier.Intel => 0.9,
        SourceTier.Regional when placeOblast != null && s.Oblasts.Contains(placeOblast, StringComparer.OrdinalIgnoreCase) => 0.85,
        SourceTier.Regional => 0.45,
        _ => 0.5,
    };
}

/// Пошук місця за назвою з поста: відмінки («Борисполем», «Білої Церкви»,
/// «Ірпеня»), апострофи, і вибір серед тезок — найближчого до регіону каналу.
public static class PlaceFinder
{
    const string Vowels = "аеєиіїоуюяь";

    // Розмовні орієнтири моніторів, яких немає в довіднику населених пунктів.
    static readonly (string re, string? name, double lat, double lon)[] Aliases =
    {
        (@"^(?:київське )?(?:водосховищ|море\b)", "Київське водосховище", 50.82, 30.47),
        (@"^столиц", "Київ", 50.4547, 30.5238),
    };

    public static string Norm(string s)
    {
        var t = s.ToLowerInvariant().Replace('ʼ', '\'').Replace('’', '\'').Replace('`', '\'').Replace('ё', 'е');
        t = Regex.Replace(t, @"[^a-zа-яіїєґ' \-]", " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    // Після перших 2 літер голосні → «*»: «ірпінь»/«ірпеня» → «ір**нь»/«ір**ня»,
    // «київ»/«києвом» → «ки*в»/«ки*в*м» (чергування і/о/е/ї/є у відмінках).
    static string Skel(string w)
    {
        if (w.Length <= 2) return w;
        var sb = new StringBuilder(w[..2]);
        foreach (var ch in w[2..]) sb.Append(Vowels.Contains(ch) ? '*' : ch);
        return sb.ToString();
    }

    static int Lcp(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    /// Чи названо місце <paramref name="name"/> у фразі <paramref name="q"/> (з відмінком).
    /// Повертає «якість» збігу (0 — ні).
    public static int Score(string name, string q)
    {
        name = Norm(name); q = Norm(q);
        if (name.Length < 3 || q.Length < 3) return 0;
        if (name == q) return 1000;
        var nw = name.Split(' ', '-'); var qw = q.Split(' ', '-');
        if (nw.Length != qw.Length) return 0;
        int total = 0;
        for (int i = 0; i < nw.Length; i++)
        {
            var a = Skel(nw[i]); var b = Skel(qw[i]);
            int l = Lcp(a, b);
            int need = Math.Max(3, a.Length - 2);
            if (a.Length >= 6) need = Math.Max(need, (int)Math.Ceiling(a.Length * 0.7));
            if (l < need || Math.Abs(nw[i].Length - qw[i].Length) > 3) return 0;
            total += l * 10 - Math.Abs(nw[i].Length - qw[i].Length);
        }
        return total;
    }

    static (double lat, double lon)? Centre(string oblast) =>
        ThreatGeo.ResolveOblast(oblast) is { } c ? (c.lat, c.lon) : null;

    /// Відстань (км) до найближчого центру з областей регіону (або null без регіону).
    public static double? RegionKm((double lat, double lon) p, IReadOnlyCollection<string> oblasts)
    {
        double? best = null;
        foreach (var o in oblasts)
            if (Centre(o) is { } c)
            {
                var d = ThreatGeo.Km(p, c);
                if (best is null || d < best) best = d;
            }
        return best;
    }

    /// Знайти місце. <paramref name="region"/> — області каналу/поста (для тезок і
    /// фільтра «не за 500 км»). <paramref name="maxKm"/> — наскільки далеко від
    /// регіону може бути місце (для «голих» назв суворіше).
    public static (string name, double lat, double lon)? Find(string raw, IReadOnlyCollection<string> region, double maxKm = 220)
    {
        var q = Norm(raw);
        if (q.Length < 3) return null;
        // Область замість міста («маневрує на Харківщині») — це не точка.
        if (Geo.Canonical(q) is not null || Regex.IsMatch(q, @"щин|област")) return null;
        if (Aliases.FirstOrDefault(a => Regex.IsMatch(q, a.re)) is { name: not null } al)
            return (al.name, al.lat, al.lon);

        // 1) Курований довідник (райони міст, ключові н.п.). Він ловить і префікс фрази
        //    («Гайсин прощається…» → Гайсин), тож перевіряємо, що назва — вся фраза.
        (string name, double lat, double lon)? cur = null;
        int curScore = 0;
        if (ThreatGeo.Resolve(raw) is { } g && Score(g.name, q) is > 0 and var gs)
        {
            var near = RegionKm((g.lat, g.lon), region);
            if (near is null || near <= maxKm) { cur = g; curScore = gs; }
        }

        // 2) Повний довідник: усі збіги, найкращий за (якість, близькість до регіону, розмір).
        (string name, double lat, double lon)? best = null;
        double bestKey = double.MinValue;
        for (int i = 0; i < Cities.All.Length; i++)
        {
            var c = Cities.All[i];
            int sc = Score(c.name, q);
            if (sc == 0) continue;
            var km = RegionKm((c.lat, c.lon), region);
            if (km is { } k && k > maxKm) continue;
            if (km is null && sc < 1000 && q.Length < 5) continue; // без регіону — лише впевнені
            // Список відсортовано від великих до малих: i — «розмір». Близькість важить
            // більше, ніж розмір, коли регіон відомий.
            double key = sc * 1000 - (km ?? 0) * 8 - i * 0.01;
            if (key > bestKey) { bestKey = key; best = c; }
        }
        // Курований виграє, якщо збіг не гірший («Оболонь»); інакше точніший з повного
        // («на Миколаївку» → Миколаївка, а не Миколаїв).
        if (cur is { } cv && (best is null || curScore >= Score(best.Value.name, q))) return cv;
        if (best is { } b) ThreatGeo.Learn(b.name, b.lat, b.lon);
        return best;
    }
}

/// Що зробив хаб із повідомленням.
public enum HubVerdict { Noise, News, AllClear, Ballistic, Pending, Confirmed, Unplaced }

public sealed record HubEvent(ThreatEvent Ev, (double lat, double lon) Pos, string Place, IReadOnlyList<string> Sources, bool Fresh);

public sealed record HubResult(HubVerdict Verdict, IReadOnlyList<HubEvent> Confirmed, string Note, string? ClearOblast = null);

public sealed class SourceHub
{
    // ── статистика джерел ────────────────────────────────────────────────────
    public sealed class Stat
    {
        public int Msgs, Noise, News, Threats, Confirmed, First, Unplaced;
        public DateTime Last;
    }
    readonly Dictionary<string, Stat> _stats = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, Stat> Stats => _stats;

    // ── кластери «одна ціль — кілька каналів» ────────────────────────────────
    sealed class Cluster
    {
        public required string Key;
        public required string Place;
        public (double lat, double lon) Pos;
        public ThreatEvent Ev = null!;
        public DateTime First, Last, LastEmit;
        public readonly Dictionary<string, double> Votes = new(StringComparer.OrdinalIgnoreCase);
        public bool Confirmed;
        public int MaxCount = 1;
        public double Score => Votes.Values.OrderByDescending(v => v).Select((v, i) => v / (1 + i * 0.5)).Sum();
    }
    readonly Dictionary<string, Cluster> _clusters = new();
    static readonly TimeSpan Window = TimeSpan.FromMinutes(6);
    readonly object _gate = new();
    public int Pending { get { lock (_gate) return _clusters.Values.Count(c => !c.Confirmed); } }

    // Сміття, яке не несе живої загрози: підписи, реклама, збори, новини «після».
    static readonly Regex SigLine = new(
        @"@\w|https?://|t\.me/|підписат|подписат|надіслати новину|зворотн|alerts live|skyops|#реклама|whatsapp|instagram|tiktok|threads|написати нам|чуєте бпла",
        RegexOptions.IgnoreCase);
    static readonly Regex Fundraise = new(@"monobank|банк[аи]|картк|донат|збір|зібрали|реквізит|paypal|грн\b", RegexOptions.IgnoreCase);
    static readonly Regex AfterNews = new(
        @"наслідк|поранен|загинул|поміран|постраждал|пошкоджен|зруйнов|госпіталізов|ворог (?:атакував|наніс|завдав)|завдали удару|удар по|влучив|влучанн[яю] (?:в|у) |— суспільне|, — \w+|isw|за даними|повідомив|повідомила",
        RegexOptions.IgnoreCase);
    static readonly Regex Clear = new(
        @"^(?:✅|⚪️|❎|🟢)?\s*(?:відбій|отбой)|(?:область|київ|харків|місто|небо)\s*[—:-]?\s*чисто|наразі чисто|над \w+ наразі чисто|^чисто\b|^[А-ЯІЇЄҐ][\w'ʼ’-]*\s+чисто\W*$",
        RegexOptions.IgnoreCase);
    static readonly Regex AlarmOnly = new(@"повітряна тривога|оголошена .*тривога|^\W*тривога\W*$|загроза (?:шахедів|бпла|реактивних)", RegexOptions.IgnoreCase);
    static readonly Regex Ballistic = new(@"балісти|іскандер", RegexOptions.IgnoreCase);
    static readonly Regex Memorial = new(@"прощаєт|прощаєм|з героєм|навіки в строю|вічна пам|загинув|загинула|співчутт", RegexOptions.IgnoreCase);

    // Останній тип цілі по каналу: «Оболонь» після «реактивні на Київ» — теж реактивний.
    readonly Dictionary<string, (ThreatKind kind, string? extra, DateTime at)> _ctx = new(StringComparer.OrdinalIgnoreCase);

    /// Основний вхід: повідомлення з каналу каталогу.
    public HubResult Process(SourceInfo src, string text, DateTime at)
    {
        lock (_gate)
        {
            var st = _stats.TryGetValue(src.User, out var s0) ? s0 : (_stats[src.User] = new Stat());
            st.Msgs++; st.Last = at;
            Expire(at);

            var clean = Clean(text);
            if (clean.Length == 0 || Fundraise.IsMatch(clean) && clean.Length > 60)
            { st.Noise++; return new(HubVerdict.Noise, [], "шум"); }
            if (AfterNews.IsMatch(clean) && clean.Length > 70)
            { st.News++; return new(HubVerdict.News, [], "новина (не жива загроза)"); }

            // Відбій від місцевого спостерігача свого регіону / офіційного.
            if (Clear.IsMatch(clean) && clean.Length < 90)
            {
                string? obl = src.Oblasts.Length == 1 ? src.Oblasts[0] : null;
                if (src.Tier == SourceTier.Official) obl = null;
                return new(HubVerdict.AllClear, [], "відбій", obl ?? (src.Tier == SourceTier.Official ? "*" : null));
            }
            if (Memorial.IsMatch(clean))
            { st.News++; return new(HubVerdict.News, [], "памʼять/новина"); }
            // Балістика — лише коротке живе попередження (довга «оцінка загроз на ніч» — ні).
            if (Ballistic.IsMatch(clean) && clean.Length < 160 && !Regex.IsMatch(clean, @"відбій|оцінка", RegexOptions.IgnoreCase))
                return new(HubVerdict.Ballistic, [], clean.Replace("\n", " "));
            if (Regex.IsMatch(clean, @"^очікуємо|^чекаємо", RegexOptions.IgnoreCase))
            { st.Noise++; return new(HubVerdict.Noise, [], "очікування"); }

            // ── розбір ──
            string? dflt = src.Oblasts.Length == 1 ? src.Oblasts[0] : null;
            // Балістика з названим містом («Іскандер-М на Харків», «швидкісна ціль на Київ»)
            // — на карту; без міста — лише попередження нижче.
            var events = ThreatParser.Parse(clean, dflt)
                .Where(e => e.Kind is not (ThreatKind.AllClear or ThreatKind.Aviation)
                            && !(e.Kind == ThreatKind.Ballistic && e.City == null))
                .ToList();
            // Балістика без місця — коротке живе попередження (довга «оцінка загроз на ніч» — ні).
            bool ballisticText = (Ballistic.IsMatch(clean) || WeaponCatalog.Match(clean)?.Kind == ThreatKind.Ballistic)
                                 && clean.Length < 160 && !Regex.IsMatch(clean, @"відбій|оцінка", RegexOptions.IgnoreCase);
            if (ballisticText && events.All(e => e.City == null))
                return new(HubVerdict.Ballistic, [], clean.Replace("\n", " "));
            bool bare = false;
            if (events.All(e => e.City == null) && src.Oblasts.Length > 0)
            {
                var b = BareNames(clean, src, at);
                if (b.Count > 0) { events = b; bare = true; }
            }
            if (events.Count == 0)
            {
                if (AlarmOnly.IsMatch(clean)) return new(HubVerdict.Noise, [], "оголошення тривоги (є з офіційного API)");
                st.Noise++;
                return new(HubVerdict.Noise, [], "без цілі");
            }

            var outp = new List<HubEvent>();
            int placed = 0;
            foreach (var ev in events)
            {
                if (ev.City is null) continue;
                var region = ev.Oblast is { } eo ? new[] { eo } : src.Oblasts;
                // «на Запорізьку область», «на Харківщині» — рівень області, не точка.
                if (Regex.IsMatch(ev.City, @"област|щин|обл", RegexOptions.IgnoreCase)) { st.Unplaced++; continue; }
                var g = PlaceFinder.Find(CityCore(ev.City), region, bare ? 160 : 260);
                if (g is not { } gp) { st.Unplaced++; continue; }
                placed++;
                var pos = (gp.lat, gp.lon);
                var placeObl = Geo.Canonical(OblastGeo.Region(gp.lat, gp.lon)) ?? ev.Oblast;
                var kind = ev.Kind;
                var extra = ev.Extra;
                if (kind == ThreatKind.Target && _ctx.TryGetValue(src.User, out var cx) && at - cx.at < TimeSpan.FromMinutes(15))
                { kind = cx.kind; extra ??= cx.extra; }
                if (kind is ThreatKind.Drone or ThreatKind.Missile or ThreatKind.Kab)
                    _ctx[src.User] = (kind, extra, at);
                st.Threats++;

                // Дрони/«ціль» в одному місці — одна ціль; ракети/КАБ — окремо.
                var group = kind is ThreatKind.Drone or ThreatKind.Target ? "air" : kind.ToString();
                var key = $"{group}|{gp.name}";
                bool isNew = !_clusters.TryGetValue(key, out var c);
                if (isNew)
                {
                    c = new Cluster { Key = key, Place = gp.name, Pos = pos, First = at };
                    _clusters[key] = c;
                    st.First++;
                }
                c!.Last = at;
                var w = SourceCatalog.Weight(src, placeObl);
                bool newSrc = !c.Votes.ContainsKey(src.User);
                c.Votes[src.User] = Math.Max(c.Votes.GetValueOrDefault(src.User), w);
                c.MaxCount = Math.Max(c.MaxCount, ev.Count);
                // Тип: конкретніший перемагає «ціль».
                var prevKind = c.Ev?.Kind ?? ThreatKind.Target;
                var bestKind = prevKind == ThreatKind.Target ? kind : prevKind;
                // «Ціль» + відомо, що це (реакт.) шахед → це дрон.
                if (bestKind == ThreatKind.Target && (extra ?? c.Ev?.Extra ?? "").Contains("шахед", StringComparison.OrdinalIgnoreCase))
                    bestKind = ThreatKind.Drone;
                c.Ev = new ThreatEvent(bestKind, placeObl ?? ev.Oblast, gp.name, c.MaxCount, extra ?? c.Ev?.Extra, ev.Raw, ev.From ?? c.Ev?.From);

                bool nowConfirmed = c.Score >= 0.8 || c.Votes.Count >= 2;
                if (nowConfirmed)
                {
                    bool fresh = !c.Confirmed;
                    c.Confirmed = true;
                    st.Confirmed++;
                    // Не засипаємо трекер дублікатами: оновлення раз на 20 с або при новому джерелі.
                    if (fresh || newSrc || at - c.LastEmit > TimeSpan.FromSeconds(20))
                    {
                        c.LastEmit = at;
                        outp.Add(new HubEvent(c.Ev, c.Pos, c.Place, c.Votes.Keys.ToList(), fresh));
                    }
                }
            }
            if (placed == 0) return new(HubVerdict.Unplaced, [], "місце не знайдено");
            return new(outp.Count > 0 ? HubVerdict.Confirmed : HubVerdict.Pending, outp,
                outp.Count > 0 ? "" : "чекаю підтвердження");
        }
    }

    /// Джерела, що бачили ціль у цьому місці (для «підтверджено N»).
    public IReadOnlyList<string> SourcesFor(string place)
    {
        lock (_gate)
            return _clusters.Values.Where(c => c.Place == place).SelectMany(c => c.Votes.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    void Expire(DateTime now)
    {
        foreach (var k in _clusters.Where(p => now - p.Value.Last > Window).Select(p => p.Key).ToList())
            _clusters.Remove(k);
    }

    // Прибрати підписи/емодзі-рамки, лишити суть.
    public static string Clean(string text)
    {
        var lines = text.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !SigLine.IsMatch(l) && !Regex.IsMatch(l, @"^[\p{So}\p{Cs}\p{P}\s‍ㅤ]+$"))
            .Select(l => Regex.Replace(l, @"^[\p{So}\p{Cs}️‍⚠‼️❗️🔴🟡🟠🟥🟧\s]+", "").Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines).Trim();
    }

    // Ядро назви з хвоста фрази: «сектор Ірпінь» → «Ірпінь», «Курисово залетел очередной»
    // → «Курисово», «Біла Церква» лишається (до 3 слів з великої літери).
    static string CityCore(string raw)
    {
        var s = Regex.Replace(raw.Trim(), @"^(?:сектор|район|напрямок|місто|м\.|смт|село|с\.|сел\.)\s+", "", RegexOptions.IgnoreCase);
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1 || !char.IsUpper(words[0][0])) return s;
        var keep = new List<string> { words[0] };
        foreach (var w in words.Skip(1))
        {
            if (keep.Count >= 3 || !(char.IsUpper(w[0]) || w.StartsWith('-'))) break;
            keep.Add(w);
        }
        return string.Join(' ', keep);
    }

    // «Голі» назви від місцевих: «Оболонь», «1 Оболонь, 1 Боярка», «Коцюбинське, Ірпінь»,
    // «2 в районі Ірпеня», «Другий Білогородка», «Знову Оболонь».
    List<ThreatEvent> BareNames(string clean, SourceInfo src, DateTime at)
    {
        var res = new List<ThreatEvent>();
        if (clean.Length > 120) return res; // довгий текст — це не «голий» список місць
        var kind = ThreatKind.Target; string? extra = null;
        // Тип, названий У САМОМУ пості («Бандероль — Оболонь», «2 бандеролі: Дарниця»), важливіший
        // за тип попереднього поста каналу: раніше бандероль після «реактивні» ставала реактивним дроном.
        if (WeaponCatalog.Match(clean) is { } own) { kind = own.Kind; extra = WeaponCatalog.DisplayName(own); }
        else if (_ctx.TryGetValue(src.User, out var cx) && at - cx.at < TimeSpan.FromMinutes(15)) { kind = cx.kind; extra = cx.extra; }
        foreach (var seg0 in Regex.Split(clean, @"[,;\n]+"))
        {
            var seg = seg0.Trim();
            int count = 1;
            var m = Regex.Match(seg, @"^(\d+)\s*[хx×]?\s+");
            if (m.Success) { int.TryParse(m.Groups[1].Value, out count); seg = seg[m.Length..]; }
            // Назва засобу перед місцем: «бандероль — Оболонь», «бандеролі: Дарниця», «ракета Бровари».
            var wm = Regex.Match(seg, @"^(?:бандерол\w*|ракет\w*|крилат\w*|ціл[ьі]\w*|каб\w*|шахед\w*|реактив\w*|рбпла|бпла)\s*[-—–:]?\s*", RegexOptions.IgnoreCase);
            if (wm.Success) { seg = seg[wm.Length..]; if (seg.Length == 0) continue; }
            seg = Regex.Replace(seg, @"^(?:і\s+)?(?:ще|знову|другий|третій|перший|один|два|три|в районі|у районі|район|рбпла|бпла|шахед\w*|реактив\w*)\s+", "", RegexOptions.IgnoreCase);
            seg = Regex.Replace(seg, @"\s+(?:низько|високо|приблизно|йде|летить|прямо|зараз|червоний|жовтий|жовтогарячий|жовтогорячий|помаранчевий|зелений)\b.*$", "", RegexOptions.IgnoreCase).Trim(' ', '.', '!', '?');
            // Голий список — це 1–2 слова назви («Оболонь», «Біла Церква»), не речення.
            if (seg.Length < 3 || seg.Split(' ').Length > 2) continue;
            if (!char.IsUpper(seg[0]) && !Regex.IsMatch(seg0, @"^\d")) continue; // назва з великої
            res.Add(new ThreatEvent(kind, src.Oblasts.Length == 1 ? src.Oblasts[0] : null, seg, count, extra, seg0));
        }
        return res;
    }

    /// Короткий звіт для журналу / бота.
    public string Report(int top = 12)
    {
        lock (_gate)
        {
            var sb = new StringBuilder();
            var act = _stats.Where(p => p.Value.Msgs > 0).OrderByDescending(p => p.Value.Confirmed).ThenByDescending(p => p.Value.Threats).Take(top);
            foreach (var (u, s) in act)
                sb.AppendLine($"@{u}: повідомл. {s.Msgs} · цілей {s.Threats} · підтв. {s.Confirmed} · першим {s.First} · шум {s.Noise + s.News}");
            return sb.ToString();
        }
    }

    /// JSON для state.sources (застосунок/API).
    public string Json(DateTime now)
    {
        lock (_gate)
        {
            string E(string x) => x.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var act = _stats.Where(p => now - p.Value.Last < TimeSpan.FromHours(24)).ToList();
            var sb = new StringBuilder("{");
            sb.Append("\"catalog\":").Append(SourceCatalog.All.Count);
            sb.Append(",\"active\":").Append(act.Count);
            sb.Append(",\"pending\":").Append(_clusters.Values.Count(c => !c.Confirmed));
            sb.Append(",\"confirmed\":").Append(_clusters.Values.Count(c => c.Confirmed));
            sb.Append(",\"top\":[");
            bool first = true;
            foreach (var (u, s) in act.OrderByDescending(p => p.Value.Confirmed).ThenByDescending(p => p.Value.Threats).Take(15))
            {
                if (!first) sb.Append(',');
                first = false;
                var tier = SourceCatalog.Get(u)?.Label ?? "";
                sb.Append("{\"u\":\"").Append(E(u)).Append("\",\"tier\":\"").Append(E(tier))
                  .Append("\",\"msgs\":").Append(s.Msgs).Append(",\"threats\":").Append(s.Threats)
                  .Append(",\"confirmed\":").Append(s.Confirmed).Append(",\"first\":").Append(s.First)
                  .Append(",\"noise\":").Append(s.Noise + s.News).Append('}');
            }
            sb.Append("],\"ts\":").Append(((DateTimeOffset)now.ToUniversalTime()).ToUnixTimeMilliseconds()).Append('}');
            return sb.ToString();
        }
    }
}
