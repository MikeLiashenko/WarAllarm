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
        ["nikalert"] = R("nikalert", "Дніпропетровська"), // Нікополь (не Миколаїв!): Марганецька, Покровська ТГ, Мар'ївка
        ["shovnebi"] = R("shovnebi"),
        ["kramatorsk_radar"] = R("kramatorsk_radar", "Донецька"),
        ["operatyvnyi_donbas"] = R("operatyvnyi_donbas", "Донецька"),
        ["korostentoday"] = R("korostentoday", "Житомирська"),
        ["eyes_everywhere_ua"] = R("eyes_everywhere_ua", "Запорізька"), // Вільнянськ, Річне, Таврійське
        ["gnilayachereha"] = R("gnilayachereha", "Запорізька"), // Кушугум, Балабине, «Космос» (не Космач у Карпатах)
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
        ["horizon_of_war_Official"] = R("horizon_of_war_Official", "Чернігівська"),
        ["my_safety_Chernigiv"] = R("my_safety_Chernigiv", "Чернігівська"),
        ["PhantomChe"] = R("PhantomChe", "Чернігівська"),
        ["RadarChernihiv"] = R("RadarChernihiv", "Чернігівська"),
        ["ShahedChernihiv"] = R("ShahedChernihiv", "Чернігівська"),
        ["taktychna_rukavuchkaa"] = R("taktychna_rukavuchkaa", "Чернігівська"),
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
        // Місцевий сленг: «Троя» = Троєщина (Київ), «Амур» = Амур-Нижньодніпровський (Дніпро),
        // «Хортицький» = район Запоріжжя, ТЕЦ-6 — біля Троєщини.
        (@"^тро[яюєї]\b", "Троєщина", 50.516, 30.605),
        (@"^тец[- ]?6\b", "ТЕЦ-6 (Київ)", 50.524, 30.655),
        (@"^амур\b", "Амур (Дніпро)", 48.505, 35.085),
        (@"^хортицьк", "Хортицький р-н (Запоріжжя)", 47.820, 35.030),
        // Нема в довіднику — без них «Космос» ставав Космачем у Карпатах, «Лісопарк» — Лиском на
        // Полтавщині (координати — OpenStreetMap). «Космос» = Космічний р-н Запоріжжя.
        (@"^космос\b|^космічн", "Космос (Запоріжжя)", 47.800, 35.210),
        (@"^любицьк", "Любицьке", 47.770, 35.941),
        (@"^терсянк", "Терсянка", 47.885, 35.893),
        (@"^ручаївк", "Ручаївка", 47.879, 34.884),
        (@"^(?:михайло[- ])?лукашев", "Михайло-Лукашеве", 47.958, 35.592),
        (@"^малишівк", "Малишівка", 47.939, 34.992),
        (@"^кринівк", "Кринівка", 47.866, 35.867),
        (@"^заливн", "Заливне", 47.862, 35.893),
        (@"^лісопарк", "Лісопарк (Харків)", 50.058, 36.241),
        (@"^нов(?:і|ые) будинк", "Нові Будинки (Харків)", 49.959, 36.332),
        (@"^львов(?:а|у|і|ом)$", "Львів", 49.84, 24.03), // «на Львова» — місто, а не село Львове на Херсонщині
        (@"^канатов", "Канатове", 48.580, 32.376),
        (@"^шпитьк", "Шпитьки", 50.415, 30.120),
    };

    // Нормовані назви довідника — рахуємо раз (раніше Norm для кожного з ~5300 імен на
    // кожен запит: десятки тисяч Regex на пост на слабкому сервері).
    static readonly string[] CityNorm = Cities.All.Select(c => Norm(c.name)).ToArray();

    /// Назва ОБЛАСТІ замість точки: «Харківщині», «Київщину», «Запорізьку область».
    /// Лише справжні обласні «-щина/-ччина» — раніше відкидалось будь-яке «щин», і
    /// Троєщина (масив Києва) ніколи не потрапляла на карту.
    public static readonly Regex OblastWord = new(
        @"(?:харків|київ|сум|полтав|чернігів|черка|житомир|оде|миколаїв|херсон|дніпропетров|луган|кіровоград|тернопіль|львів|рівнен|франків|слобожан)щин|(?:вінни|доне|хмельни)ччин|област|\bобл\b",
        RegexOptions.IgnoreCase);

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

    // Назва й запит — те саме слово з точністю до відмінкового закінчення
    // («Новоселівка» / «Новоселівку» / «Новоселівці»), а не лише «схожі приголосні».
    static string Core(string w) => Regex.Replace(w, "[аеєиіїоуюяь]+$", "");
    public static bool Literal(string name, string q) => LiteralN(Norm(name), Norm(q));
    static bool LiteralN(string name, string q)
    {
        var nw = name.Split(' ', '-'); var qw = q.Split(' ', '-');
        if (nw.Length != qw.Length) return false;
        for (int i = 0; i < nw.Length; i++)
        {
            var a = Core(nw[i]); var b = Core(qw[i]);
            if (a.Length < 2) { if (nw[i] != qw[i]) return false; continue; }
            // «Новоселівка/Новоселівці»: основа збігається, у відмінку може змінитись 1 приголосна (к→ц).
            if (a != b && !(a.Length == b.Length && a.Length >= 5 && a[..^1] == b[..^1])) return false;
        }
        return true;
    }

    static int Lcp(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    /// Чи названо місце <paramref name="name"/> у фразі <paramref name="q"/> (з відмінком).
    /// Повертає «якість» збігу (0 — ні).
    public static int Score(string name, string q) => ScoreN(Norm(name), Norm(q));
    static int ScoreN(string name, string q)
    {
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
            // Назва збіглась не до кінця — у запиті після спільного початку має лишатись лише
            // відмінкове закінчення («Новоселівці»), а не інше слово: «Лісопарк» ≠ «Лісок».
            // Прикметник від назви («Русанівські Сади» ← Русанівка) — теж збіг.
            if (l < a.Length && b.Length - l > 3 && !Regex.IsMatch(b[l..], @"^[сц]\*к")) return 0; // скелет: «ськ» → «с*к»
            // Недозбіг назви — штраф: «Харковом» ближче до «Харків», ніж до «Харківці».
            total += Math.Max(1, l * 10 - Math.Abs(nw[i].Length - qw[i].Length) - 15 * (a.Length - l));
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
        // Область замість міста («маневрує на Харківщині») — це не точка. Крім «Запоріжжя»:
        // монітори так кличуть МІСТО («На Запоріжжя»), а не область.
        if (Geo.Canonical(q) is not null && q != "запоріжжя" || OblastWord.IsMatch(q)) return null;
        // «Херсонський район» (офіційні районні тривоги: «дронова загроза») → центр району.
        if (RaionGeo.Find(q, region) is { } rg) return rg;
        // Норма прибирає цифри («ТЕЦ-6» → «тец-»), тож орієнтири звіряємо й із сирим текстом.
        var rawLow = raw.Trim().ToLowerInvariant();
        // Орієнтир — лише якщо він у регіоні каналу (сільські назви повторюються по країні).
        if (Aliases.FirstOrDefault(a => (Regex.IsMatch(q, a.re) || Regex.IsMatch(rawLow, a.re))
                                        && !(RegionKm((a.lat, a.lon), region) > maxKm)) is { name: not null } al)
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

        // Назва СПРАВЖНЯ (є такий н.п. хоч десь) — тоді «схоже за приголосними» інше село не
        // підставляємо: «Новоселівка» ≠ «Новосілки» (а то Новоселівка з Чернігівщини ставала
        // Новосілками під Києвом і вмикала київську тривогу). Краще «місце не знайдено».
        bool real = CityNorm.Any(c => LiteralN(c, q));
        if (real && cur is { } c0 && !Literal(c0.name, q)) { cur = null; curScore = 0; }

        // 2) Повний довідник: усі збіги, найкращий за (якість, близькість до регіону, розмір).
        (string name, double lat, double lon)? best = null;
        double bestKey = double.MinValue;
        for (int i = 0; i < Cities.All.Length; i++)
        {
            var c = Cities.All[i];
            int sc = ScoreN(CityNorm[i], q);
            if (sc == 0) continue;
            if (real && !LiteralN(CityNorm[i], q)) continue;
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

    /// Назва без тезок по всій країні (усі однойменні н.п. в межах 40 км) — тоді її можна
    /// ставити й без регіону каналу: «курсом на Єзупіль» від київського агрегатора.
    public static (string name, double lat, double lon)? Unique(string raw)
    {
        var q = Norm(raw);
        if (q.Length < 4 || Geo.Canonical(q) is not null && q != "запоріжжя" || OblastWord.IsMatch(q)) return null;
        var hits = new List<int>();
        for (int i = 0; i < CityNorm.Length; i++)
            if (LiteralN(CityNorm[i], q)) hits.Add(i);
        if (hits.Count == 0) return null;
        var f = Cities.All[hits[0]];
        if (hits.Any(i => ThreatGeo.Km((Cities.All[i].lat, Cities.All[i].lon), (f.lat, f.lon)) > 40)) return null;
        ThreatGeo.Learn(f.name, f.lat, f.lon);
        return f;
    }

    /// Скорочена назва з крапкою («Червоногригор. ТГ») — н.п. регіону, що з неї починається.
    public static (string name, double lat, double lon)? Prefix(string abbr, IReadOnlyCollection<string> region, double maxKm)
    {
        var q = Norm(abbr);
        if (q.Length < 6 || q.Contains(' ') || region.Count == 0) return null;
        (string name, double lat, double lon)? best = null;
        double bestKm = maxKm;
        for (int i = 0; i < CityNorm.Length; i++)
        {
            if (!CityNorm[i].StartsWith(q)) continue;
            var c = Cities.All[i];
            if (RegionKm((c.lat, c.lon), region) is { } km && km <= bestKm) { bestKm = km; best = c; }
        }
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
        @"@\w|https?://|t\.me/|підписат|подписат|підпиш|подпиш|надіслати новину|зворотн|alerts live|skyops|#реклама|whatsapp|instagram|tiktok|threads|написати нам|чуєте бпла|крам радар|темный рыцарь",
        RegexOptions.IgnoreCase);
    static readonly Regex Fundraise = new(@"monobank|банк[аи]|картк|донат|збір|зібрали|реквізит|paypal|грн\b", RegexOptions.IgnoreCase);
    static readonly Regex AfterNews = new(
        @"наслідк|поранен|загинул|поміран|постраждал|пошкоджен|зруйнов|госпіталізов|ворог (?:атакував|наніс|завдав)|завдали удару|удар по|влучив|влучанн[яю] (?:в|у) |— суспільне|, — \w+|isw|за даними|повідомив|повідомила|уражен|за тиждень|за добу|протягом \d|прогноз погоди|поїзд|результати тижня|ворог здійснив|обстріл",
        RegexOptions.IgnoreCase);
    static readonly Regex Impact = new(@"удар по|приліт|прилет|влучанн|загибл|загинул|поранен|за попередньою інформацією|—\s*терехов", RegexOptions.IgnoreCase);
    static readonly Regex Clear = new(
        @"^(?:✅|⚪️|❎|🟢)?\s*(?:відбій|отбой)|(?:область|київ|харків|місто|небо)\s*[—:-]?\s*чисто|наразі чисто|локаційно чисто|^\W*\p{L}+(?:щина|ччина)\s*[—:-]\s*чисто|над \w+ наразі чисто|^чисто\b|^[А-ЯІЇЄҐ][\w'ʼ’-]*\s+чисто\W*$",
        RegexOptions.IgnoreCase);
    // Відбій по району/громаді («🟢 Конотопський р-н ⏎ Відбій повітряної тривоги», «Чугуївський район
    // - відбій») — не ціль у центрі району і не відбій по всій області каналу.
    static readonly Regex ClearWord = new(@"\b(?:відбій|отбой)\b", RegexOptions.IgnoreCase);
    static readonly Regex RaionWord = new(@"р-н|район|громад|\bтг\b", RegexOptions.IgnoreCase);
    // Районна «дронова загроза» («🟡 Корюківський район — повітряна тривога, жовтий рівень: Дронова
    // загроза», «🟡 Миколаївський район - Дронова загроза») — ФПВ біля центру району.
    static readonly Regex RaionDrone = new(
        @"(?m)^\W*([\p{L}'ʼ’-]+\s+(?:район|р-н))\b[^\n]*?дронова загроза", RegexOptions.IgnoreCase);
    // Офіційні оголошення загроз і рівнів тривоги («🛸 Вишгород (Київська обл.) ⏎ Загроза
    // застосування БПЛА», «Ракетна загроза (червоний рівень)», «Желтый уровень тревоги ⏎ • Березовский
    // район») — це тривоги, а не ціль у точці; вони й так є з офіційного API.
    static readonly Regex AlertNotice = new(
        @"загроза застосування|ракетна загроза|(?:рівень|уровень)\s+(?:тривоги|тревоги)|(?:жовтий|червоний|желтый|красный)\s+(?:рівень|уровень)",
        RegexOptions.IgnoreCase);
    static readonly Regex AlarmOnly = new(@"повітряна тривога|воздушная тревога|оголошена .*тривога|^\W*тривога\W*$|загроза (?:шахедів|бпла|реактивних)", RegexOptions.IgnoreCase);
    // Балістика: «балістика», рос. «баллистика/балистика», сленг «бали», «Іскандер», «швидкісна
    // (ціль) на Харків», офіційне «загроза застосування високошвидкісних цілей». Але не
    // «швидкісні шахеди» (то реактивні дрони).
    static readonly Regex Ballistic = new(
        @"балісти|баллист|балист|іскандер|искандер|(?<!\p{L})бали(?!\p{L})|високошвидкісн|высокоскоростн|(?<!\p{L})(?:швидк|скоростн)\w*\s+(?:ціл|цел)|(?<!\p{L})швидкісн\w*\s+(?:на|в|у|курс)\s",
        RegexOptions.IgnoreCase);
    static readonly Regex NotBallistic = new(@"шахед|бпла|дрон|реактив|мопед|шаболд", RegexOptions.IgnoreCase);
    // Явно крилата ракета — не перекласифікуємо в балістику навіть одразу після «загрози балістики».
    static readonly Regex Cruise = new(@"крилат|крылат|калібр|калибр|бандерол|х-?101|х-?55|х-?59|х-?69|х-?22|х-?32|циркон|онікс|оникс", RegexOptions.IgnoreCase);
    // Офіційне: «🚀 Харків (Харківська обл.) ⏎ Загроза застосування високошвидкісних цілей» — ціль = місто.
    static readonly Regex OfficialFast = new(
        @"^\W*(?<place>[^\n(]{3,40}?)\s*(?:\((?<obl>[^)]*)\))?\s*\n\s*загроза застосування (?:високошвидкісних|балістичн)", RegexOptions.IgnoreCase);
    static readonly Regex Memorial = new(@"прощаєт|прощаєм|з героєм|навіки в строю|вічна пам|загинув|загинула|співчутт", RegexOptions.IgnoreCase);
    // Подяки за донати, поради «уникати районів», зведення «мапа тривог», «повідомлення від читачів».
    static readonly Regex Chatter = new(
        @"донат|дякую за підтримк|загрози не станов|не становить загроз|рекоменд\w*\s+(?:тимчасово\s+)?уника|мапа тривог|карта (?:повітряних )?тривог|сообщение от читател|цитируем текст",
        RegexOptions.IgnoreCase);
    // Тип із самого рядка, коли парсер дав лише «ціль»: «Ситуация по реактивным: 1 курсом на …».
    static readonly Regex DroneWord = new(@"реактив|шахед|шаболд|бпла|дрон|мопед|ударн", RegexOptions.IgnoreCase);
    // «Далі/Далее Мерефа» — продовження руху. КАБ не «летить далі»: це дрон або ракета.
    static readonly Regex Continue = new(@"^\W*(?:(?:і|и|та|а)\s+)?(?:далі|далее|дальше|потім|потом|знову|опять|снова|продовжує|продолжает)\b", RegexOptions.IgnoreCase);

    // Останній тип цілі по каналу: «Оболонь» після «реактивні на Київ» — теж реактивний.
    // Окремо — останній дрон/ракета: після «КАБ на Вовчанськ» рядок «Далее Мерефа» — це шахед,
    // що летів до того, а не КАБ (раніше десятки «КАБів» висіли над містом).
    readonly Dictionary<string, (ThreatKind kind, string? extra, DateTime at)> _ctx = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (ThreatKind kind, string? extra, DateTime at)> _ctxAir = new(StringComparer.OrdinalIgnoreCase);

    // Остання «загроза балістики» (будь-який канал) і звідки: «Ракета на Харків» за хвилину
    // після «балістика з Бєлгорода» — це та сама балістика, а не крилата, що летить на Київ.
    DateTime _ballAt = DateTime.MinValue;
    string? _ballFrom;

    static TimeSpan CtxTtl(ThreatKind k) => k switch
    {
        ThreatKind.Kab => TimeSpan.FromMinutes(2),       // КАБ летить ~1–2 хв
        ThreatKind.Missile => TimeSpan.FromMinutes(5),   // крилата перетинає область за кілька хвилин
        _ => TimeSpan.FromMinutes(15),
    };

    /// Тип для рядка без типу («Далі Мерефа», «Оболонь»): з контексту каналу.
    (ThreatKind kind, string? extra)? CtxKind(string user, string raw, DateTime at)
    {
        bool cont = Continue.IsMatch(raw);
        if (_ctx.TryGetValue(user, out var cx) && at - cx.at < CtxTtl(cx.kind) && !(cx.kind == ThreatKind.Kab && cont))
            return (cx.kind, cx.extra);
        if (_ctxAir.TryGetValue(user, out var ca) && at - ca.at < CtxTtl(ca.kind))
            return (ca.kind, ca.extra);
        return null;
    }

    void RememberKind(string user, ThreatKind kind, string? extra, DateTime at)
    {
        if (kind is not (ThreatKind.Drone or ThreatKind.Missile or ThreatKind.Kab)) return;
        _ctx[user] = (kind, extra, at);
        if (kind != ThreatKind.Kab) _ctxAir[user] = (kind, extra, at);
    }

    static bool IsBallisticText(string clean) =>
        (Ballistic.IsMatch(clean) || WeaponCatalog.Match(clean)?.Kind == ThreatKind.Ballistic)
        && !NotBallistic.IsMatch(clean) && clean.Length < 160
        && !Regex.IsMatch(clean, @"відбій|отбой|оцінка|чисто|ніч|ночі|за добу|випустил|застосувал|атакувал|фото|відео|вирв|після|наслідк|влуч|прил[іе]т|удар\w* по", RegexOptions.IgnoreCase);

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
            // «🗞 …» — рубрика новин (@nikalert); «удар по Новобаварському району — Терехов» — постфактум.
            // «⚡️Прилет Новобаварском районе», «є загиблі. — Терехов» — постфактум навіть коротко: ціль
            // уже не летить, а «Терехов» ставав селом Терни.
            if (text.TrimStart().StartsWith("🗞") || Impact.IsMatch(clean) && clean.Length < 200
                || AfterNews.IsMatch(clean) && clean.Length > 70)
            { st.News++; return new(HubVerdict.News, [], "новина (не жива загроза)"); }

            // Відбій від місцевого спостерігача свого регіону / офіційного.
            if (Clear.IsMatch(clean) && clean.Length < 90)
            {
                string? obl = src.Oblasts.Length == 1 ? src.Oblasts[0] : null;
                if (src.Tier == SourceTier.Official || RaionWord.IsMatch(clean)) obl = null;
                return new(HubVerdict.AllClear, [], "відбій", obl ?? (src.Tier == SourceTier.Official ? "*" : null));
            }
            if (Memorial.IsMatch(clean))
            { st.News++; return new(HubVerdict.News, [], "памʼять/новина"); }
            if (Chatter.IsMatch(clean))
            { st.Noise++; return new(HubVerdict.Noise, [], "подяка/порада/зведення"); }
            // Балістика — лише коротке живе попередження (довга «оцінка загроз на ніч» — ні).
            // Запам'ятовуємо час і район пуску: наступна «ракета на Харків» — це вона.
            bool ballisticText = IsBallisticText(clean);
            if (at - _ballAt > TimeSpan.FromMinutes(10)) _ballFrom = null;
            if (ballisticText)
            {
                _ballAt = at;
                if (ThreatParser.FromOf(clean) is { } bf && LaunchSites.Resolve(bf) is not null) _ballFrom = bf;
            }
            if (!ballisticText && Regex.IsMatch(clean, @"^очікуємо|^чекаємо", RegexOptions.IgnoreCase))
            { st.Noise++; return new(HubVerdict.Noise, [], "очікування"); }

            // ── розбір ──
            string? dflt = src.Oblasts.Length == 1 ? src.Oblasts[0] : null;
            bool recentBall = at - _ballAt < TimeSpan.FromMinutes(4);
            var events = ThreatParser.Parse(clean, dflt)
                .Where(e => e.Kind is not (ThreatKind.AllClear or ThreatKind.Aviation))
                .Select(e =>
                {
                    // «Балістика на Харків», «Швидкісна на Харків!» — ціль-балістика (не крилата/«ціль»).
                    if (ballisticText && e.Kind is ThreatKind.Missile or ThreatKind.Target or ThreatKind.Unknown)
                        e = e with { Kind = ThreatKind.Ballistic };
                    // «Ракета курсом на Харків» за хвилину після «загрози балістики з Бєлгорода» — балістика.
                    if (recentBall && e.Kind == ThreatKind.Missile && !Cruise.IsMatch(e.Raw))
                        e = e with { Kind = ThreatKind.Ballistic };
                    if (e.Kind != ThreatKind.Ballistic) return e;
                    // «Загроза балістики з Бєлгорода» — Бєлгород не ціль, а район пуску.
                    if (e.City is { } bc && LaunchSites.FromText(bc) is not null) e = e with { City = null, From = e.From ?? bc };
                    return e.From is null && _ballFrom is { } bfr ? e with { From = bfr } : e;
                })
                // Балістика з названим містом («Іскандер-М на Харків») — на карту; без міста — попередження.
                .Where(e => !(e.Kind == ThreatKind.Ballistic && e.City == null))
                .ToList();
            // Офіційне «🚀 Харків (Харківська обл.) ⏎ Загроза застосування високошвидкісних цілей».
            if (events.Count == 0 && OfficialFast.Match(clean) is { Success: true } of)
                events.Add(new ThreatEvent(ThreatKind.Ballistic, OblastHint(of.Groups["obl"].Value) ?? dflt,
                    of.Groups["place"].Value.Trim(), 1, null, clean, _ballFrom));
            if (ballisticText && events.All(e => e.City == null))
                return new(HubVerdict.Ballistic, [], clean.Replace("\n", " "));
            bool bare = false;
            if (events.All(e => e.City == null && e.Toward == null))
            {
                var rd = RaionDrone.Matches(clean);
                if (ClearWord.IsMatch(clean) && clean.Length < 200)
                    return new(HubVerdict.AllClear, [], "відбій по району/громаді");
                if (rd.Count > 0)
                    events = rd.Select(m => new ThreatEvent(ThreatKind.Fpv, dflt, m.Groups[1].Value, 1, "дронова загроза", m.Value)).ToList();
                // «🔴 Прилуцький район — Повітряна тривога» — тривога, а не ціль у центрі району.
                else if ((AlertNotice.IsMatch(clean) || AlarmOnly.IsMatch(clean)) && clean.Length < 250)
                    return new(HubVerdict.Noise, [], "оголошення загрози/тривоги (є з офіційного API)");
                else if (src.Oblasts.Length > 0 && BareNames(clean, src, at) is { Count: > 0 } b)
                { events = b; bare = true; }
            }
            if (events.Count == 0)
            {
                if (AlarmOnly.IsMatch(clean)) return new(HubVerdict.Noise, [], "оголошення тривоги (є з офіційного API)");
                st.Noise++;
                return new(HubVerdict.Noise, [], "без цілі");
            }

            var outp = new List<HubEvent>();
            var missed = new List<string>();
            int placed = 0;
            foreach (var ev in events)
            {
                (string name, double lat, double lon)? g;
                // «Реактивний від Березни» (міста не названо) — ціль зараз біля Березни. Але не
                // район пуску («з Курська», «з Криму») і не балістика/ракети (вони ще далеко).
                var phrase = ev.City ?? (ev.Kind is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Fpv
                                         && ev.From is { } fr && LaunchSites.FromText(fr) is null ? fr : null);
                if (ev.City is null && ev.Toward is not null && ev.Oblast is not null && OblastGeo.PartPoint(ev.Oblast, ev.Side) is { } part)
                    // «на півночі Сумщини, курсом на Полтаву» — точка в цій частині області.
                    g = (OblastGeo.Region(part.lat, part.lon) + " (обл.)", part.lat, part.lon);
                else if (phrase is null) continue;
                else
                {
                    // «Зпр: Ще Реактив на ТОТ Веселе з Криму» — область з початку рядка поста.
                    var rawObl = Regex.Match(ev.Raw, @"^\W*([\p{L}'\-]{2,20})\s*:") is { Success: true } rh ? OblastHint(rh.Groups[1].Value) : null;
                    g = Locate(phrase, rawObl ?? ev.Oblast, src, bare, out bool hadName);
                    if (!hadName) continue; // «Збили», «Транзит», «Центр» — статус, а не місце
                }
                if (g is not { } gp) { st.Unplaced++; missed.Add(phrase ?? ev.Toward ?? "?"); continue; }
                placed++;
                var pos = (gp.lat, gp.lon);
                var placeObl = Geo.Canonical(OblastGeo.Region(gp.lat, gp.lon)) ?? ev.Oblast;
                var kind = ev.Kind;
                var extra = ev.Extra;
                if (kind == ThreatKind.Target && DroneWord.IsMatch(ev.Raw)) kind = ThreatKind.Drone;
                else if (kind == ThreatKind.Target && CtxKind(src.User, ev.Raw, at) is { } cx)
                { kind = cx.kind; extra ??= cx.extra; }
                // «Далее Мерефа» без типу, а контексту дрона нема — краще «ціль», ніж вигаданий КАБ.
                if (kind == ThreatKind.Kab && ev.Kind != ThreatKind.Kab && Continue.IsMatch(ev.Raw)) kind = ThreatKind.Target;
                RememberKind(src.User, kind, extra, at);
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

                // Балістика летить 1–3 хв — друге джерело чекати нема коли.
                bool nowConfirmed = c.Score >= 0.8 || c.Votes.Count >= 2 || bestKind == ThreatKind.Ballistic;
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
            // Балістику не вдалось прив'язати до місця — лишається попередженням (пуш «в укриття»).
            if (placed == 0 && ballisticText)
                return new(HubVerdict.Ballistic, [], clean.Replace("\n", " "));
            if (placed == 0 && missed.Count == 0)
            { st.Noise++; return new(HubVerdict.Noise, [], "без місця"); }
            if (placed == 0) return new(HubVerdict.Unplaced, [], "місце не знайдено: " + string.Join(" | ", missed));
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

    // ── місце з фрази ───────────────────────────────────────────────────────
    // Монітори пишуть місце як завгодно: «Далі Федорівка», «на півночі від н.п. Буялик на Одещині»,
    // «Лідне/Пісочин», «Вишгород (Київська обл.)», «Зпр: Водяне», «Червоногригор. ТГ»,
    // «Новые Дома», «2500-2700м Спасское». Тут — варіанти назви по черзі, до першого знайденого.

    // Скорочення @nikalert: «Зпр:», «Мгнц:», «Хрсн:», «Dнпр:».
    static readonly Dictionary<string, string> AbbrOblast = new(StringComparer.OrdinalIgnoreCase)
    {
        ["зпр"] = "Запорізька", ["зп"] = "Запорізька", ["мгнц"] = "Дніпропетровська", ["нкп"] = "Дніпропетровська",
        ["днпр"] = "Дніпропетровська", ["dнпр"] = "Дніпропетровська", ["хрсн"] = "Херсонська",
    };

    /// «Київська обл.», «Франківська», «Миколаївщина», «Зпр» → канонічна область (або null).
    static string? OblastHint(string w)
    {
        w = w.Trim().TrimEnd('.', ':', ' ');
        if (w.Length < 2) return null;
        if (Geo.Canonical(w) is { } c) return c;
        if (AbbrOblast.TryGetValue(w, out var a)) return a;
        var adj = Regex.Replace(w, @"\s*(?:обл\.?|область|області)$", "", RegexOptions.IgnoreCase).Trim();
        if (adj.Length < 4) return null;
        return Geo.Oblasts.Values.Distinct().FirstOrDefault(v => v.Equals(adj, StringComparison.OrdinalIgnoreCase)
                                                             || v.EndsWith("-" + adj, StringComparison.OrdinalIgnoreCase));
    }

    // Слова перед назвою: напрямки, прийменники, «н.п.», дієслова руху.
    static readonly Regex LeadWord = new(
        @"^(?:далі|далее|дальше|потім|знову|опять|снова|ще|і|и|та|на|в|у|до|над|под|під|біля|возле|около|поблизу|неподалік|недалеко|навпроти|напротив|повз|мимо|через|бік|сторону|напрямку|направлении|напрямок|курс|курсом|сектор|районі|районе|район|р-ні|р-н|н\.п|нп|с|смт|м|сел|село|селище|місто|мікрорайоні|мікрорайону|мікрорайон|микрорайон|масив|околиці|околицях|краю|підльоті|траса|трасі|трасу|проходить|пройшов|проходит|прошел|розворот|повернув|повертає|пішов|йде|іде|летить|летит|рух|рухається|змістилась|змістився|заходить|залітає|залетів|від|із|з|зі|межі|кордоні|тот|\w+(?:ніше|нее)|півночі|півдні|сході|заході|центрі)$",
        RegexOptions.IgnoreCase);
    // Засоби й службові слова — не частина назви (розривають «Кушугум Балабине ФПВ»).
    static readonly Regex NotName = new(
        @"^(?:бпла|бп|рбпла|дрон\w*|шахед\w*|шахід\w*|шохєд\w*|герань\w*|геран\w*|молні\w*|ланцет\w*|фпв|fpv|каб\w*|ракет\w*|крилат\w*|бандерол\w*|реактив\w*|мопед\w*|італмас\w*|ціл[ьі]\w*|розвід\w*|ударн\w*|звич\w*|р|тг|отг|обл|область|області|район|р-н"
        + @"|центр|центрі|центру|центр\w+ села|загроз\w*|угроз\w*|город|города|городу|транзит\w*|збил\w*|збит\w*|збиття|впал\w*|впав\w*|падає|вибух\w*|взрыв\w*|зник\w*|чути|чую|тиша|чисто|укрил\w*|увага|тривога|ситуац\w*|оновлен\w*|відбій|не|без|все|всі|пуск\w*|груп\w*|місто|штук\w*|висота|захід|схід|північ|південь|база|базар|україн\w*|країн\w*|росі\w*|рф|європ\w*|зала|орлан|мавік|червоним|жовтим|зеленим|синім|білим|помаранчевим)$",
        RegexOptions.IgnoreCase);
    // Прикметник-частина назви («Нові», «Велика») сам по собі — не місце.
    static readonly Regex AdjPart = new(@"^(?:нов|стар|вел|мал|верх|нижн|біл|чорн|черв|зелен|середн|північн|південн|східн|західн)\w*$", RegexOptions.IgnoreCase);

    /// Варіанти назви з фрази, по черзі. <paramref name="hint"/> — область з самої фрази
    /// («(Київська обл.)», «Зпр:», «на Одещині»), <paramref name="hromada"/> — «… ТГ».
    static List<string> PlaceVariants(string phrase, out string? hint, out bool hromada, bool split = false)
    {
        hint = null;
        var p = phrase.Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'');
        // Дужки: область/район — підказка; інша назва («Траса Слов'янськ/Ізюм (Хрестище)») — точніше місце.
        var inner = new List<string>();
        foreach (Match m in Regex.Matches(p, @"\(([^)]*)\)"))
        {
            var pr = m.Groups[1].Value.Trim();
            if (OblastHint(pr) is { } h) hint = h;
            else if (RaionGeo.Find(PlaceFinder.Norm(pr), Array.Empty<string>()) is { } rg)
                hint = Geo.Canonical(OblastGeo.Region(rg.lat, rg.lon)) ?? hint;
            // Але не стара назва вулиці («вулиця Бориса Мозолевського (Фрунзе)») — то не село Фрунзе.
            else if (pr.Length > 2 && char.IsUpper(pr[0]) && !Regex.IsMatch(p, @"вулиц|проспект|площ|бульвар|провулок|\bул\.", RegexOptions.IgnoreCase))
                inner.Add(pr);
        }
        p = Regex.Replace(p, @"\([^)]*\)", " ");
        // «Зпр: …», «Франківська: …», «Чернігівщина: …»
        var mh = Regex.Match(p, @"^\W*([\p{L}'\-]{2,20}(?:\s+обл\.?)?)\s*:\s*");
        if (mh.Success && OblastHint(mh.Groups[1].Value) is { } h2) { hint = h2; p = p[mh.Length..]; }
        // Хвіст «на Одещині» — підказка; «з Тернопільщини», «в області» — просто відрізаємо.
        var tail = Regex.Match(p, @"\s+(?:(на|в|у)|з|із|зі|від|по)\s+([\p{L}'\-]+(?:щин|ччин)\w*)\b.*$|\s+(?:на|в|у)\s+(?:\S+\s+)?област\w*.*$", RegexOptions.IgnoreCase);
        if (tail.Success)
        {
            if (tail.Groups[1].Success && Geo.Canonical(tail.Groups[2].Value) is { } th) hint ??= th;
            p = p[..tail.Index];
        }
        hromada = Regex.IsMatch(p, @"\b(?:ТГ|ОТГ|громад\w*)\b", RegexOptions.IgnoreCase);

        var res = new List<string>();
        foreach (var alt in Regex.Split(p, @"\s*/\s*"))
        {
            var a = alt.Trim();
            // «Херсонський район», «Нікопольський р-н» — район цілком (RaionGeo).
            if (Regex.Match(a, @"^\W*([\p{L}'\-]+\s+(?:район\w*|р-н\w*))\W*$", RegexOptions.IgnoreCase) is { Success: true } rm)
            { res.Add(rm.Groups[1].Value); continue; }
            var toks = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Regex.Replace(t, @"^[^\p{L}\d]+|[^\p{L}\d.]+$", ""))
                .Select(t => Regex.Replace(t, @"[-/](?:район|р-н)$", "", RegexOptions.IgnoreCase))
                .Where(t => t.Length > 0).ToList();
            int i = 0;
            while (i < toks.Count && (LeadWord.IsMatch(toks[i].TrimEnd('.')) || char.IsDigit(toks[i][0]) || NotName.IsMatch(toks[i].TrimEnd('.'))))
                i++;
            if (i >= toks.Count) continue;
            // Назва — до 3 слів з великої («Біла Церква», «Нові Санжари»); з малої — лише
            // останнє слово одразу після «курс»/«на» («Жабки курс осняг»).
            bool lowOk = i == toks.Count - 1 && toks[i].Length >= 5
                         && (toks.Count == 1 || Regex.IsMatch(toks[i - 1], "^(?:курс|курсом|на|бік|напрямку|до)$", RegexOptions.IgnoreCase));
            var run = new List<string>();
            for (int j = i; j < toks.Count && run.Count < 3; j++)
            {
                var t = toks[j];
                if (NotName.IsMatch(t.TrimEnd('.')) || !char.IsUpper(t[0]) && !lowOk) break;
                run.Add(t);
            }
            if (run.Count == 0) continue;
            res.Add(string.Join(' ', run).TrimEnd('.'));
            // «Кушугум Балабине» → «Кушугум», «Балабине»; але не «Нові» з «Нові Будинки».
            if (run.Count > 1 && split)
                foreach (var t in run)
                    if (t.TrimEnd('.').Length >= 6 && !AdjPart.IsMatch(t)) res.Add(t.TrimEnd('.'));
            // «Червоногригор.» — скорочення.
            if (run[^1].EndsWith('.')) res.Add(run[^1]);
        }
        res.AddRange(inner); // уточнення в дужках («(Хрестище)») — коли основну назву не знайшли
        // «Червоногригорівська ТГ» → Червоногригорівка; «Марганецька» → Марганець; «Покровська» → Покровське.
        if (hromada)
            foreach (var c in res.ToList())
                if (Regex.Match(c, @"^(.*?)(ськ|цьк)(?:а|ої|ій|у)$") is { Success: true } hm)
                {
                    var stem = hm.Groups[1].Value;
                    res.AddRange(hm.Groups[2].Value == "цьк" ? new[] { stem + "ць", stem + "цьке" } : new[] { stem + "ка", stem + "ське", stem + "ськ" });
                }
        // Російською: «Изюм», «Ольшаны», «Игрень», «Березовка» → українське написання.
        foreach (var c in res.ToList())
            if (RuToUk(c) is var u && u != c) res.Add(u);
        return res.Distinct().ToList();
    }

    // Російські назви, що не перекладаються по літерах.
    static readonly Dictionary<string, string> RuNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Киев"] = "Київ", ["Киева"] = "Київ", ["Харьков"] = "Харків", ["Харькова"] = "Харків", ["Днепр"] = "Дніпро",
        ["Одесса"] = "Одеса", ["Одессу"] = "Одеса", ["Николаев"] = "Миколаїв", ["Запорожье"] = "Запоріжжя",
        ["Чернигов"] = "Чернігів", ["Сумы"] = "Суми", ["Кривой Рог"] = "Кривий Ріг", ["Песочин"] = "Пісочин",
        ["Волчанск"] = "Вовчанськ", ["Чугуев"] = "Чугуїв", ["Купянск"] = "Куп'янськ", ["Лозовая"] = "Лозова",
    };

    static string RuToUk(string s)
    {
        if (RuNames.TryGetValue(s, out var known)) return known;
        if (!Regex.IsMatch(s, @"[ыэёъЫЭ]|^[Ии]|ск(?:ое)?$|ая$|ое$|[ое]вка$")) return s; // схоже на українське — не чіпаємо
        // Спершу «и»→«і», потім «ы»→«и» (інакше «Ольшаны» ставали б «Ольшані»).
        var t = s.Replace('и', 'і').Replace('И', 'І')
                 .Replace('ы', 'и').Replace('Ы', 'И').Replace('э', 'е').Replace('Э', 'Е').Replace('ё', 'е').Replace('ъ', '\'');
        t = Regex.Replace(t, @"с?ское$", "ське"); t = Regex.Replace(t, @"с?ск$", "ськ"); // «Черкасское» → «Черкаське»
        t = Regex.Replace(t, @"ая$", "а"); t = Regex.Replace(t, @"ое$", "е");
        t = Regex.Replace(t, @"[ое]вка$", "івка"); t = Regex.Replace(t, @"ій$", "ий");
        return t;
    }

    // «Місто» в пості регіонального каналу — його обласний центр (Донеччина — Краматорськ).
    static readonly Dictionary<string, string> Capitals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Харківська"] = "Харків", ["Київська"] = "Київ", ["Сумська"] = "Суми", ["Дніпропетровська"] = "Дніпро",
        ["Запорізька"] = "Запоріжжя", ["Полтавська"] = "Полтава", ["Чернігівська"] = "Чернігів", ["Одеська"] = "Одеса",
        ["Миколаївська"] = "Миколаїв", ["Херсонська"] = "Херсон", ["Донецька"] = "Краматорськ", ["Черкаська"] = "Черкаси",
        ["Кіровоградська"] = "Кропивницький", ["Вінницька"] = "Вінниця", ["Житомирська"] = "Житомир",
        ["Хмельницька"] = "Хмельницький", ["Рівненська"] = "Рівне", ["Львівська"] = "Львів", ["Волинська"] = "Луцьк",
        ["Тернопільська"] = "Тернопіль", ["Івано-Франківська"] = "Івано-Франківськ", ["Закарпатська"] = "Ужгород",
        ["Чернівецька"] = "Чернівці",
    };

    /// Знайти місце з фрази поста. <paramref name="hadName"/> = false — у фразі взагалі
    /// немає назви («Збили», «Транзит»), тобто це не «не знайдено», а «без місця».
    (string name, double lat, double lon)? Locate(string phrase, string? oblast, SourceInfo src, bool bare, out bool hadName)
    {
        hadName = true;
        // «на схід Кіровоградщини з Дніпропетровщини», «північ Чернігівщини» — частина області.
        var pm = Regex.Match(phrase, @"^\W*(?:на\s+)?(півн\w*|півд\w*|сх[іо]д\w*|зах[іо]д\w*|центр\w*)(?:[\s-]+(сх\w*|зах\w*))?\s+([\p{L}'\-]+(?:щин|ччин)\w*)", RegexOptions.IgnoreCase);
        if (pm.Success && Geo.Canonical(pm.Groups[3].Value) is { } po
            && OblastGeo.PartPoint(po, pm.Groups[1].Value + " " + pm.Groups[2].Value) is { } pp)
            return (OblastGeo.Region(pp.lat, pp.lon) + " (обл.)", pp.lat, pp.lon);

        // «Ціль на місто», «Цель на город» — місто КАНАЛУ (обласний центр), а не село Городне.
        if (Regex.IsMatch(phrase, @"^\W*(?:на\s+|в\s+|над\s+)?(?:город|городу|міст[оау]|місті)(?!\p{L})", RegexOptions.IgnoreCase))
            return src.Oblasts.Length > 0 && Capitals.TryGetValue(src.Oblasts[0], out var cap) ? ThreatGeo.Resolve(cap) : null;

        var vars = PlaceVariants(phrase, out var hint, out bool hromada, split: bare);
        if (vars.Count == 0) { hadName = false; return null; }
        IReadOnlyCollection<string> region = hint is { } h ? new[] { h } : oblast is { } o ? new[] { o } : src.Oblasts;
        double maxKm = bare ? 160 : 260;
        bool kharkiv = region.Contains("Харківська", StringComparer.OrdinalIgnoreCase);
        if (kharkiv && KharkivMonitor.PlaceName(phrase, multiWord: true) is { } km && PlaceFinder.Find(km, region, maxKm) is { } kmg) return kmg;
        foreach (var v in vars)
        {
            // «Червоногригор.» — н.п. регіону, що з цього починається.
            if (v.EndsWith('.'))
            {
                if (PlaceFinder.Prefix(v.TrimEnd('.'), region, maxKm) is { } pf) return pf;
                continue;
            }
            if (PlaceFinder.Find(v, region, maxKm) is { } g) return g;
            // Масиви Харкова й російські назви з місцевих каналів («Новые Дома», «Алексеевку»).
            if (kharkiv && KharkivMonitor.PlaceName(v) is { } kn && PlaceFinder.Find(kn, region, maxKm) is { } kg) return kg;
        }
        // Громада без повної назви («Червоногригор ТГ») — н.п., що з неї починається.
        if (hromada && PlaceFinder.Prefix(vars[0], region, maxKm) is { } hp) return hp;
        // Агрегатор чи місце в «чужому» регіоні: назва без тезок по всій країні («курсом на Єзупіль»).
        if (!bare)
            foreach (var v in vars)
                if (!v.EndsWith('.') && PlaceFinder.Unique(v) is { } u) return u;
        return null;
    }

    // «Голі» назви від місцевих: «Оболонь», «1 Оболонь, 1 Боярка», «Коцюбинське, Ірпінь»,
    // «2 в районі Ірпеня», «Другий Білогородка», «Знову Оболонь», «Далі Федорівка», «Зпр: 1 Водяне».
    List<ThreatEvent> BareNames(string clean, SourceInfo src, DateTime at)
    {
        var res = new List<ThreatEvent>();
        // Довгий текст — це не «голий» список місць. Крім списків-«маркерів» (@nikalert:
        // «◼️ 12:16 с. Мар'ївка ⏎ ◻️ 12:16 Марганецька ТГ»).
        bool bullets = Regex.IsMatch(clean, "[◼◻▪•▫■□]");
        if (clean.Length > (bullets ? 400 : 120)) return res;
        var kind = ThreatKind.Target; string? extra = null;
        // Тип, названий У САМОМУ пості («Бандероль — Оболонь», «2 бандеролі: Дарниця»), важливіший
        // за тип попереднього поста каналу: раніше бандероль після «реактивні» ставала реактивним дроном.
        if (WeaponCatalog.Match(clean) is { } own) { kind = own.Kind; extra = WeaponCatalog.DisplayName(own); }
        else if (CtxKind(src.User, clean, at) is { } cx) { kind = cx.kind; extra = cx.extra; }
        string? obl = src.Oblasts.Length == 1 ? src.Oblasts[0] : null;
        foreach (var seg0 in Regex.Split(clean, @"[,;\n]+"))
        {
            var seg = Regex.Replace(seg0.Trim(), @"\s*/\s*", "/");
            // «◼️ 12:16 с. Мар'ївка», «◻️ Марганецька ТГ», «🟡 Херсонський район — повітряна тривога»
            seg = Regex.Replace(seg, @"^[^\p{L}\d]+", "");                            // маркери / емодзі
            seg = Regex.Replace(seg, @"^\d{1,2}[:.]\d{2}\s*", "");                   // час
            // «Зпр: 1 Новосергіївка», «Франківська: Галич/Більшівці» — область для цього й наступних рядків.
            var hm = Regex.Match(seg, @"^([\p{L}'\-]{2,20})\s*:\s*");
            if (hm.Success && OblastHint(hm.Groups[1].Value) is { } ho) { obl = ho; seg = Regex.Replace(seg[hm.Length..], @"^[^\p{L}\d]+", ""); }
            // «Херсонський район — повітряна тривога» → до тире; але «Бандероль — Оболонь» — місце ПІСЛЯ тире.
            if (!Regex.IsMatch(seg, @"^(?:бандерол|ракет|крилат|ціл[ьі]|каб|шахед|реактив|рбпла|бпла)", RegexOptions.IgnoreCase))
                seg = Regex.Split(seg, @"\s+[—–-]\s+")[0].Trim();
            seg = Regex.Replace(seg, @"^(?:с\.|с-ще|смт\.?|сел\.|м\.|село|селище|місто)\s*", "", RegexOptions.IgnoreCase);
            bool hromada = Regex.IsMatch(seg, @"\s+(?:тг|отг|громада|територіальна громада)\.?$", RegexOptions.IgnoreCase);
            seg = Regex.Replace(seg, @"\s+(?:тг|отг|громада|територіальна громада)\.?$", "", RegexOptions.IgnoreCase);
            seg = Regex.Replace(seg, @"^\d{3,5}(?:\s*-\s*\d{3,5})?\s*м\b\s*", "");    // висота «2500-2700м Спасское»
            int count = 1;
            var m = Regex.Match(seg, @"^(\d+)\s*[хx×]?\s+");
            if (m.Success) { int.TryParse(m.Groups[1].Value, out count); seg = seg[m.Length..]; }
            // Назва засобу перед місцем: «бандероль — Оболонь», «бандеролі: Дарниця», «ракета Бровари».
            var wm = Regex.Match(seg, @"^(?:бандерол\w*|ракет\w*|крилат\w*|ціл[ьі]\w*|каб\w*|шахед\w*|реактив\w*|рбпла|бпла)\s*[-—–:]?\s*", RegexOptions.IgnoreCase);
            if (wm.Success) { seg = seg[wm.Length..]; if (seg.Length == 0) continue; }
            seg = Regex.Replace(seg, @"^(?:і\s+)?(?:ще|знову|другий|третій|перший|один|два|три|в районі|у районі|район|рбпла|бпла|шахед\w*|реактив\w*)\s+", "", RegexOptions.IgnoreCase);
            // «Далі Федорівка», «Далее Балашовка», «На Яворницьке», «Розворот На Ховми».
            for (Match lw; (lw = Regex.Match(seg, @"^(\S+)\s+")).Success && LeadWord.IsMatch(lw.Groups[1].Value);) seg = seg[lw.Length..];
            // Хвіст після назви: «Мар'ївка ФПВ», «Степуки курс до вас», «Кушугум Балабине ФПВ низько…».
            seg = Regex.Replace(seg, @"\s+(?:низько|високо|приблизно|йде|летить|прямо|зараз|червоний|жовтий|жовтогарячий|жовтогорячий|помаранчевий|зелений|курс\w*|в бік|у бік|через|повз|фпв|fpv|бпла|молні\w*|ланцет\w*|шахед\w*|реактив\w*|акустика|кружля\w*)\b.*$", "", RegexOptions.IgnoreCase).Trim(' ', '.', '!', '?');
            // Голий список — це 1–2 слова назви («Оболонь», «Біла Церква»), не речення.
            var words = Regex.Replace(seg, @"\([^)]*\)", "").Split('/')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (seg.Length < 3 || words.Length is 0 or > 2) continue;
            if (!char.IsUpper(seg[0]) && !Regex.IsMatch(seg0, @"^\d")) continue; // назва з великої
            res.Add(new ThreatEvent(kind, obl, hromada ? seg + " ТГ" : seg, count, extra, seg0));
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

    /// Найкорисніші канали (бот «📡 Джерела»): хто дав найбільше підтверджених цілей.
    public List<(string user, Stat s)> Top(int top = 8)
    {
        lock (_gate)
            return _stats.Where(p => p.Value.Threats > 0)
                .OrderByDescending(p => p.Value.Confirmed).ThenByDescending(p => p.Value.Threats)
                .Take(top).Select(p => (p.Key, p.Value)).ToList();
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
