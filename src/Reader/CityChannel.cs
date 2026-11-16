using System.Net;
using System.Text;
using System.Text.Json;

// ── WarAlarm · міські канали (Київ @kyiv_waralarm, Харків @Kh_WarAlarm) ──────
// Поруч з основним всеукраїнським каналом. Кожен міський канал — свій ChannelPoster
// (свій LastId і захист від дублів) і пише про СВОЮ ЗОНУ — місто й області навколо:
//   Харків — Харківщина; Київ — Київщина, Черкащина, Чернігівщина, Полтавщина.
//   • офіційна тривога / відбій по точці центру міста (відбій — після двох опитувань поспіль);
//   • зведення раз на 8 хв, поки в зоні є цілі: карта зони + коротко «Шахед на Люботин»
//     (Program.cs → RunCityDigest); кінець нальоту — тихе редагування останнього зведення;
//   • загроза балістики — одразу (секунди вирішують), не частіше разу на 5 хв;
//   • світло (єСвітло) — лише Київ.
// Ключі в secrets.local.txt: telegram_channel_kyiv=@kyiv_waralarm, telegram_channel_kharkiv=@Kh_WarAlarm;
// токен — telegram_channel_<key>_bot_token або спільний telegram_channel_bot_token.
public sealed class CityChannel
{
    /// [Name] — зона моніторів (укр.), [Ru] — канал російською, [Title] — назва в постах,
    /// [Oblasts] — області зони (англ. назви полігонів, як ключі OfficialAlerts.ByPolygon),
    /// [Towns] — міста області для підписів на карті зони.
    public readonly record struct City(string Key, string Name, double Lat, double Lon, string[] Oblasts, string[] Towns,
        bool Ru = false, string? TitleRu = null)
    {
        public string Title => Ru ? TitleRu ?? Name : Name;
    }

    /// Підтримувані міста: ключ у командах/секретах, назва (= зона моніторів), центр, зона.
    /// Харківський канал — російською (як місцеві монітори).
    public static readonly City[] Known =
    {
        new("kyiv", "Київ", 50.4501, 30.5234,
            new[] { "Kiev", "Kiev City", "Cherkasy", "Chernihiv", "Poltava" },
            new[] { "Бровари", "Бориспіль", "Біла Церква", "Фастів", "Ірпінь", "Вишгород", "Обухів", "Яготин",
                    "Ніжин", "Прилуки", "Бахмач", "Новгород-Сіверський", "Умань", "Сміла", "Золотоноша", "Звенигородка",
                    "Кременчук", "Миргород", "Лубни", "Горішні Плавні", "Карлівка", "Гадяч" }),
        new("kharkiv", "Харків", 49.9935, 36.2304,
            new[] { "Kharkiv" },
            new[] { "Люботин", "Чугуїв", "Ізюм", "Куп'янськ", "Лозова", "Балаклія", "Богодухів", "Вовчанськ",
                    "Золочів", "Красноград", "Дергачі", "Мерефа", "Валки", "Барвінкове", "Зміїв", "Первомайський",
                    "Великий Бурлук", "Шевченкове" },
            Ru: true, TitleRu: "Харьков"),
    };

    public static City? Find(string s) =>
        Known.Cast<City?>().FirstOrDefault(c => c!.Value.Key.Equals(s, StringComparison.OrdinalIgnoreCase)
                                             || c.Value.Name.Equals(s, StringComparison.OrdinalIgnoreCase));

    public City Info { get; }
    public string Key => Info.Key;
    public string Name => Info.Name;
    public string Title => Info.Title;
    public bool Ru => Info.Ru;
    string T(string ua, string ru) => Ru ? ru : ua;
    public (double lat, double lon) Center => (Info.Lat, Info.Lon);
    public string Handle { get; }
    /// null — сухий прогон (citysim): пости лише друкуються в консоль.
    public ChannelPoster? Poster { get; }

    public CityChannel(City city, string handle, ChannelPoster? poster)
    {
        Info = city; Handle = handle; Poster = poster;
    }

    /// Підпис постів міського каналу (аналог ChannelFoot).
    public string Foot => T($"<i>🤖 WarAlarm {Title} · дані приблизні</i>", $"<i>🤖 WarAlarm {Title} · данные приблизительные</i>");
    /// Рядок «оновлюється» під живою дошкою.
    public string LiveFoot() => T($"<i>🕒 {DateTime.Now:HH:mm} · оновлюється · 🤖 WarAlarm {Title}</i>",
                                  $"<i>🕒 {DateTime.Now:HH:mm} · обновляется · 🤖 WarAlarm {Title}</i>");
    /// Фінальний рядок дошки, коли небо чисте.
    public string CloseText => T("Цілей більше не видно", "Целей больше не видно");
    /// Логотип міста для карт (assets/logo_<key>.png).
    public string LogoFile => $"logo_{Key}.png";

    public async Task<long?> Post(string text, byte[]? png = null, bool silent = false)
    {
        if (Poster is not null) return await Poster.Post(text, png, silent);
        Console.WriteLine($"📤 [{Name}]{(silent ? " 🔕" : " 🔔")}{(png is not null ? " 🖼" : "")}\n{Plain(text)}\n");
        return null;
    }

    // ── 1. Офіційна тривога / відбій по місту ────────────────────────────────
    bool? _alarm;           // null — ще не бачили жодного знімка (перший — без поста)
    int _miss;
    DateTime? _alarmAt;     // коли оголосили (null — тривога вже йшла на старті)

    /// Крок по знімку API: рівень у точці центру міста → текст поста або null.
    /// Збій API (!snap.Ok) сюди не передаємо — це не відбій.
    public string? AlertStep(string? level, DateTime now)
    {
        bool red = level == "red";
        if (_alarm is null) { _alarm = red; _alarmAt = null; return null; }
        if (red)
        {
            _miss = 0;
            if (_alarm == true) return null;
            _alarm = true; _alarmAt = now;
            return T($"🔴 <b>Повітряна тривога — {H(Title)}</b>\nУсім в укриття.\n\n{Foot}",
                     $"🔴 <b>Воздушная тревога — {H(Title)}</b>\nВсем в укрытие.\n\n{Foot}");
        }
        if (_alarm == false) return null;
        if (++_miss < 2) return null;    // зникла вперше — ще тривога (як у RunAlertsLoop)
        _alarm = false; _miss = 0;
        var dur = _alarmAt is { } at ? T($"\nТривала {Dur(now - at)}.", $"\nДлилась {Dur(now - at)}.") : "";
        _alarmAt = null;
        return $"🟢 <b>{T("Відбій", "Отбой")} — {H(Title)}</b>{dur}\n\n{Foot}";
    }

    string Dur(TimeSpan d) => d.TotalMinutes < 60 ? $"{Math.Max(1, (int)d.TotalMinutes)} {T("хв", "мин")}"
        : $"{(int)d.TotalHours} {T("год", "ч")} {d.Minutes} {T("хв", "мин")}";

    // ── 2. Короткі пости одразу (з локальних моніторів міста): «🛵 Шахед на Люботин» ─────
    // Ціль біля міста (≤40 км), «💥 Гучно», «🎯 Мінус», «🛡 Працює ППО», загроза балістики.
    // Решта області — у зведенні з картою раз на 8 хв.
    readonly Dictionary<string, DateTime> _recent = new();
    DateTime _ppoAt = DateTime.MinValue, _ballAt = DateTime.MinValue;

    /// Подія локального монітора → короткий пост (або null).
    /// [u] — результат tracker.Ingest (для загроз), [hadCityThreat] — чи були цілі біля міста ДО події
    /// (нова загроза місту → зі звуком, решта — тихо).
    public (string text, bool silent)? EventPost(KhEvent e, TrackUpdate? u, bool hadCityThreat, string ballisticText, DateTime now)
    {
        string? line = null;
        bool loud = false;
        if (e.Signal == KhSignal.News) { }
        else if (e.Kind == ThreatKind.Ballistic && Slang.BallisticThreat(e.Raw)) { } // загроза балістики — BallisticPost (Program.BallisticWatch)
        else if (Slang.ShotCount(e.Raw) is { } n)
        {   // «мінус» без цілей біля міста — не про нас (або запізнілий перепост)
            if (hadCityThreat) line = $"🎯 {T("Мінус", "Минус")}" + (n > 1 ? $" {n}" : "");
        }
        else if (e.Signal == KhSignal.Threat && u is { Threat: { Recon: false, Pos: { } p } t } && ThreatGeo.Km(p, Center) <= 40
                 // без власного місця («Над вами!», «ховайтеся») — лише щойно вибух / над районом, інакше це шум
                 && (e.Places.Count > 0 || (u.Value.StageChanged && t.Stage is TrackStage.Impact or TrackStage.Overhead)))
        {
            line = ThreatLine(e, t, u.Value.StageChanged, Ru);
            loud = !hadCityThreat;
        }

        if (Ppo(e.Raw) && !Slang.EnemyPpoMention(e.Raw) && now - _ppoAt > TimeSpan.FromMinutes(3)) // вороже ППО — EnemyPpoPost
        {
            _ppoAt = now;
            var ppoLine = T("🛡 Працює ППО", "🛡 Работает ПВО");
            line = line is null ? ppoLine : line + "\n" + ppoLine;
        }
        if (line is null) return null;

        // Той самий рядок (перепост іншим каналом) ≤3 хв — не дублюємо.
        if (_recent.TryGetValue(line, out var was) && now - was < TimeSpan.FromMinutes(3)) return null;
        _recent[line] = now;
        if (_recent.Count > 200)
            foreach (var k in _recent.Where(kv => now - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList()) _recent.Remove(k);

        return ($"{H(line)}\n\n{Foot}", !loud);
    }

    // ── ☄ Загроза балістики / 🛡 вороже ППО (Program.BallisticWatch) ───────────────
    DateTime _enemyPpoAt = DateTime.MinValue;
    static readonly Dictionary<string, string> RuSite = new()
    {
        ["Брянськ"] = "Брянск", ["Бєлгород"] = "Белгород", ["Курськ"] = "Курск", ["Крим"] = "Крым", ["Орел"] = "Орёл",
        ["Липецьк"] = "Липецк", ["Чорне море"] = "Чёрное море", ["Смоленськ"] = "Смоленск", ["Енгельс"] = "Энгельс",
        ["Донецьк"] = "Донецк", ["Луганськ"] = "Луганск", ["Маріуполь"] = "Мариуполь", ["Бердянськ"] = "Бердянск",
        ["Міллерово"] = "Миллерово", ["Каспій"] = "Каспий",
    };

    /// Загроза балістики, що щойно почалась у [oblasts] → пост (зі звуком) або null (не наша зона / ≤5 хв тому).
    public string? BallisticPost(string? from, IReadOnlyCollection<string> oblasts, DateTime now)
    {
        var mine = oblasts.Intersect(Info.Oblasts).ToList();
        if (mine.Count == 0 || now - _ballAt < TimeSpan.FromMinutes(5)) return null;
        _ballAt = now;
        // Лише своя область міста — без переліку; інакше — де саме («Чернігівщина»).
        bool own = mine.All(o => o == Info.Oblasts[0] || o == "Kiev City");
        var where = own ? "" : " — " + BallisticAlert.Names(mine);
        var site = from is null ? "" : T($"\nРайон пуску: {from}.", $"\nРайон пуска: {(RuSite.TryGetValue(from, out var r) ? r : from)}.");
        return T($"☄️ <b>Загроза балістики</b>{H(where)}{H(site)}\nПуску ще немає — будь ближче до укриття.\n\n{Foot}",
                 $"☄️ <b>Угроза баллистики</b>{H(where)}{H(site)}\nПуска ещё нет — будь ближе к укрытию.\n\n{Foot}");
    }

    /// «Працює вороже ППО» → пост (тихо); [cleared] — у зоні щойно зняли загрозу балістики.
    public string? EnemyPpoPost(bool cleared, DateTime now)
    {
        if (now - _enemyPpoAt < TimeSpan.FromMinutes(cleared ? 2 : 5)) return null;
        _enemyPpoAt = now;
        return cleared
            ? T($"🛡 <b>Працює вороже ППО</b>\nЗагрозу балістики знято — схоже, це не пуски по нас.\nДо відбою — уважно.\n\n{Foot}",
                $"🛡 <b>Работает вражеское ПВО</b>\nУгрозу баллистики сняли — похоже, это не пуски по нам.\nДо отбоя — внимательно.\n\n{Foot}")
            : T($"🛡 <b>Працює вороже ППО</b>\nСхоже, це не пуски по нас. До відбою — уважно.\n\n{Foot}",
                $"🛡 <b>Работает вражеское ПВО</b>\nПохоже, это не пуски по нам. До отбоя — внимательно.\n\n{Foot}");
    }

    public string BallisticClearPost() =>
        T($"🟢 <b>Відбій загрози балістики</b>\n\n{Foot}", $"🟢 <b>Отбой угрозы баллистики</b>\n\n{Foot}");

    // [fresh] — стадія змінилась саме цією подією (інакше «💥» від минулого вибуху липне до нових постів).
    static string ThreatLine(KhEvent e, Threat t, bool fresh, bool ru)
    {
        var places = e.Places.Count > 0 ? string.Join(", ", e.Places) : t.LastPlace;
        if (ru) places = RuPlace(places);
        var icon = e.Kind switch
        {
            ThreatKind.Drone => "🛵",
            ThreatKind.Missile => "🚀",
            ThreatKind.Ballistic => "☄️",
            ThreatKind.Kab => "💣",
            ThreatKind.Aviation => "✈️",
            ThreatKind.Fpv => "🪖",
            _ => "🎯",
        };
        if (e.Extra is { } ex && (ex.Contains("fpv", StringComparison.OrdinalIgnoreCase) || ex.Contains("фпв", StringComparison.OrdinalIgnoreCase))) icon = "🪖";
        var label = e.Extra is { Length: > 0 } x ? char.ToUpper(x[0]) + x[1..] : e.Kind switch
        {
            ThreatKind.Drone => "Шахед",
            ThreatKind.Missile => "Ракета",
            ThreatKind.Ballistic => ru ? "Баллистика" : "Балістика",
            ThreatKind.Kab => "КАБ",
            ThreatKind.Aviation => ru ? "Авиация" : "Авіація",
            ThreatKind.Fpv => ru ? "FPV" : "ФПВ",
            _ => ru ? "Цель" : "Ціль",
        };
        if (ru) label = RuWord(label);
        var stage = fresh ? t.Stage : TrackStage.Approaching;
        var boom = ru ? "💥 Громко" : "💥 Гучно";
        if (stage == TrackStage.Impact) return places.Length > 0 ? $"{boom} — {places}" : boom;
        var what = $"{icon} {label}";
        if (places.Length == 0) return what;
        return stage switch
        {
            TrackStage.Overhead => $"⚠️ {what} над {places}",
            TrackStage.Imminent => ru ? $"❗ {what} на подлёте — {places}" : $"❗ {what} на підльоті — {places}",
            _ => $"{what} на {places}",
        };
    }

    static bool Ppo(string s) => System.Text.RegularExpressions.Regex.IsMatch(s.ToLowerInvariant(), @"\bппо\b|протиповітрян|зенітн");

    // ── 3. Зведення раз на 8 хв: карта зони + «Шахед на Люботин» ────────────────
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(8);
    public enum DigestAction { None, Post, Close }
    DateTime _digestAt = DateTime.MinValue, _activeAt = DateTime.MinValue;
    string _digestCore = "";
    bool _open;                 // зведення про наліт уже йдуть (наступні — без звуку)
    /// Id останнього зведення (закриваємо його редагуванням підпису).
    public long? DigestId { get; set; }
    /// Звук лише на першому зведенні нальоту.
    public bool DigestLoud => !_open;

    /// Ціль у зоні каналу (точка всередині однієї з областей зони).
    public bool InScope(Threat t) => t.Pos is { } p && !t.Down
        && OblastGeo.PolyName(p.lat, p.lon) is { } en && Info.Oblasts.Contains(en);

    /// Крок розкладу: [core] — рядки цілей у зоні ("" — нікого). Post — не частіше ніж раз на 8 хв
    /// і лише коли картина змінилась; Close — 3 хв тиші після нальоту (редагування, без поста).
    public DigestAction DigestStep(string core, DateTime now)
    {
        if (core.Length == 0)
        {
            if (!_open || now - _activeAt < TimeSpan.FromMinutes(3)) return DigestAction.None;
            _open = false; _digestCore = "";
            return DigestAction.Close;
        }
        _activeAt = now;
        if (now - _digestAt < Every || core == _digestCore) return DigestAction.None;
        return DigestAction.Post;
    }

    /// Після відправки зведення (після DigestStep → Post).
    public void DigestSent(string core, long? id, DateTime now)
    {
        _digestAt = now; _digestCore = core; _open = true;
        DigestId = id;
    }

    /// Рядки зведення: «Шахед на Люботин», «Шахед ×3 на Чугуїв» — найближчі до міста першими.
    /// [all] — усі цілі (щоб невідому «ціль» назвати так само, як на карті).
    public string DigestCore(IReadOnlyList<Threat> inScope, IReadOnlyList<Threat> all, int max = 12)
    {
        var rows = inScope
            .GroupBy(t => (cat: MapImage.Category(t, all), place: Place(t)))
            .Select(g => (label: $"{Icon(g.Key.cat)} {Label(g.Key.cat)}", g.Key.place, n: g.Sum(t => Math.Max(1, t.Amount)),
                          km: g.Min(t => ThreatGeo.Km(t.Pos!.Value, Center))))
            .OrderBy(r => r.km).ToList();
        var sb = new StringBuilder();
        foreach (var r in rows.Take(max))
            sb.Append(H(r.label)).Append(r.n > 1 ? $" ×{r.n}" : "").Append(T(" на ", " на ")).AppendLine(H(r.place));
        if (rows.Count > max) sb.AppendLine(T($"…і ще {rows.Count - max}", $"…и ещё {rows.Count - max}"));
        return sb.ToString();
    }

    /// Повний підпис зведення: рядки + області зони під тривогою + час.
    /// [whole] — тривога на всю область, [any] — тривога хоч у частині районів (ByPolygon).
    public string DigestText(string core, IReadOnlyDictionary<string, string>? whole, IReadOnlyDictionary<string, string>? any, DateTime now)
    {
        var sb = new StringBuilder(core);
        var red = new List<string>();
        foreach (var en in Info.Oblasts.Where(en => en != "Kiev City"))
        {
            bool w = whole is not null && whole.TryGetValue(en, out var wl) && wl == "red";
            bool a = any is not null && any.TryGetValue(en, out var al) && al == "red";
            if (!w && !a) continue;
            var n = Ru ? RuWord(OblastGeo.Short(en)) : OblastGeo.Short(en);
            red.Add(w ? n : n + T(" (частково)", " (частично)"));
        }
        if (red.Count > 0) sb.AppendLine().Append(T("🔴 Тривога: ", "🔴 Тревога: ")).AppendLine(H(string.Join(", ", red)));
        sb.AppendLine().Append($"<i>🕒 {now:HH:mm} · 🤖 WarAlarm {Title}</i>");
        return sb.ToString();
    }

    /// Підпис останнього зведення, коли наліт скінчився.
    public string ClosedText(string lastText, DateTime now) =>
        lastText + T($"\n✅ <b>Цілей більше не видно</b> · {now:HH:mm}", $"\n✅ <b>Целей больше не видно</b> · {now:HH:mm}");

    static string Icon(string cat) => cat switch
    {
        "shahed" or "shahed_jet" => "🛵",
        "missile" or "banderol" => "🚀",
        "balist" => "☄️",
        "kab" => "💣",
        "fpv" => "🪖",
        "avia" => "✈️",
        _ => "🎯",
    };

    string Label(string cat) => cat switch
    {
        "shahed" => "Шахед",
        "shahed_jet" => T("Реактивний шахед", "Реактивный шахед"),
        "missile" => "Ракета",
        "banderol" => "Бандероль",
        "balist" => T("Балістика", "Баллистика"),
        "kab" => "КАБ",
        "fpv" => T("ФПВ", "FPV"),
        "avia" => T("Авіація", "Авиация"),
        _ => T("Ціль", "Цель"),
    };

    /// Куди: місце цілі; обласна згадка («Харківщина (обл.)») → «Харківщину».
    string Place(Threat t)
    {
        var p = t.LastPlace.Trim();
        if (p.EndsWith("(обл.)")) p = p[..^"(обл.)".Length].Trim();
        if (p.Length == 0 && t.Pos is { } pos) p = OblastGeo.Region(pos.lat, pos.lon);
        if (Ru) p = RuPlace(p);
        return p.EndsWith("щина") ? p[..^1] + "у" : p;
    }

    /// Місто [Towns] → координати з довідника (перше з такою назвою в зоні).
    public IReadOnlyList<(string name, double lon, double lat)> TownPoints() => _towns ??= Info.Towns
        .Select(n => TownExtra.TryGetValue(n, out var x) ? (name: n, x.lat, x.lon) : Cities.All.FirstOrDefault(c => c.name == n
            && OblastGeo.PolyName(c.lat, c.lon) is { } en && Info.Oblasts.Contains(en)))
        .Where(c => c.name is not null).Select(c => (c.name, c.lon, c.lat)).ToList();
    /// Міст, яких немає в довіднику Cities (або там лише тезки з інших областей).
    static readonly Dictionary<string, (double lat, double lon)> TownExtra = new()
    {
        ["Куп'янськ"] = (49.7106, 37.6156), ["Красноград"] = (49.3794, 35.4413), ["Первомайський"] = (49.3880, 36.2150),
    };
    IReadOnlyList<(string name, double lon, double lat)>? _towns;

    /// Рамка карти зони.
    public MapImage.View MapView => _view ??= MapImage.ViewOf(Info.Oblasts, marginDeg: 0.12, minAspect: 0.9, maxAspect: 1.4);
    MapImage.View? _view;
    /// Заголовок карти зони.
    public string MapTitle => Info.Key == "kyiv" ? "WarAlarm · Київ і область" : T("WarAlarm · Харківщина", "WarAlarm · Харьковщина");

    // ── Російська для харківського каналу: місця й слова з моніторів (укр.) ──────
    // Спершу словник (райони й місця Харкова та області), далі — грубі правила транслітерації.
    static readonly Dictionary<string, string> RuDict = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Харків"] = "Харьков", ["Київ"] = "Киев", ["Харківщина"] = "Харьковщина", ["Бєлгород"] = "Белгород",
        ["Шевченківський"] = "Шевченковский", ["Київський"] = "Киевский", ["Салтівський"] = "Салтовский",
        ["Немишлянський"] = "Немышлянский", ["Індустріальний"] = "Индустриальный", ["Слобідський"] = "Слободской",
        ["Основ'янський"] = "Основянский", ["Основʼянський"] = "Основянский", ["Холодногірський"] = "Холодногорский",
        ["Новобаварський"] = "Новобаварский",
        ["Салтівка"] = "Салтовка", ["Північна Салтівка"] = "Северная Салтовка", ["Олексіївка"] = "Алексеевка",
        ["Дергачі"] = "Дергачи", ["Циркуни"] = "Циркуны", ["Кутузівка"] = "Кутузовка", ["Слатине"] = "Слатино",
        ["Козача Лопань"] = "Казачья Лопань", ["Васищеве"] = "Васищево", ["Безлюдівка"] = "Безлюдовка",
        ["Кулиничі"] = "Кулиничи", ["П'ятихатки"] = "Пятихатки", ["Чугуїв"] = "Чугуев", ["Мерефа"] = "Мерефа",
        ["Пісочин"] = "Песочин", ["Липці"] = "Липцы", ["Золочів"] = "Золочев", ["Вовчанськ"] = "Волчанск",
        ["Ізюм"] = "Изюм", ["Куп'янськ"] = "Купянск", ["Лозова"] = "Лозовая", ["Балаклія"] = "Балаклея",
        ["Богодухів"] = "Богодухов", ["Люботин"] = "Люботин", ["Рогань"] = "Рогань", ["Валки"] = "Валки",
        ["Сортувальна"] = "Сортировка", ["Основа"] = "Основа", ["Нові Будинки"] = "Новые Дома",
        ["Павлове Поле"] = "Павлово Поле", ["Шишківка"] = "Шишковка", ["Журавлівка"] = "Журавлевка",
        ["Велика Данилівка"] = "Большая Даниловка", ["Мала Данилівка"] = "Малая Даниловка",
        ["Приколотне"] = "Приколотное", ["Барвінкове"] = "Барвенково", ["Близнюки"] = "Близнецы",
        ["Печеніги"] = "Печенеги", ["Краснопавлівка"] = "Краснопавловка", ["Зміїв"] = "Змиев", ["Красноград"] = "Красноград",
        ["Первомайський"] = "Первомайский", ["Великий Бурлук"] = "Великий Бурлук", ["Шевченкове"] = "Шевченково",
        ["Суми"] = "Сумы", ["Полтава"] = "Полтава", ["Дніпро"] = "Днепр", ["Запоріжжя"] = "Запорожье", ["Луганськ"] = "Луганск",
        ["Донецьк"] = "Донецк", ["Краматорськ"] = "Краматорск", ["Слов'янськ"] = "Славянск", ["Сєвєродонецьк"] = "Северодонецк",
        ["Курськ"] = "Курск", ["Вороніж"] = "Воронеж", ["Кропивницький"] = "Кропивницкий", ["Чернігів"] = "Чернигов",
        ["Миргород"] = "Миргород", ["Гадяч"] = "Гадяч", ["Карлівка"] = "Карловка", ["Лебедин"] = "Лебедин", ["Охтирка"] = "Ахтырка",
        ["Нова Водолага"] = "Новая Водолага", ["Сахновщина"] = "Сахновщина", ["Оскіл"] = "Оскол", ["Борова"] = "Боровая",
        ["Коломак"] = "Коломак", ["Кегичівка"] = "Кегичевка", ["Старий Мерчик"] = "Старый Мерчик", ["Бабаї"] = "Бабаи",
        ["Балістика"] = "Баллистика", ["Молнія"] = "Молния", ["Бандероль"] = "Бандероль",
        ["Реакт. шахед"] = "Реакт. шахед", ["Розвідник"] = "Разведчик", ["Шахед"] = "Шахед",
    };

    /// «Салтівка, Немишлянський» → «Салтовка, Немышлянский».
    public static string RuPlace(string s) => string.Join(", ", s.Split(", ").Select(RuWord));

    static string RuWord(string w)
    {
        if (w.Length == 0) return w;
        if (RuDict.TryGetValue(w, out var hit)) return hit;
        // Правила: закінчення, потім літери (и→ы раніше за і→и).
        var r = w;
        foreach (var (a, b) in new[] { ("ський", "ский"), ("цький", "цкий"), ("івка", "овка"), ("ївка", "евка"), ("ичі", "ичи"), ("ів", "ов") })
            if (r.EndsWith(a)) { r = r[..^a.Length] + b; break; }
        var sb = new StringBuilder();
        for (int i = 0; i < r.Length; i++)
        {
            char c = r[i], lo = char.ToLower(c);
            string o = lo switch
            {
                'и' => (i > 0 && "цжшчщкгх".Contains(char.ToLower(r[i - 1]))) || (i + 1 < r.Length && r[i + 1] == 'й') ? "и" : "ы",
                'і' => "и", 'ї' => "йи", 'є' => "е", 'ґ' => "г", '\'' or 'ʼ' or '’' => "", _ => lo.ToString(),
            };
            sb.Append(char.IsUpper(c) && o.Length > 0 ? char.ToUpper(o[0]) + o[1..] : o);
        }
        return sb.ToString();
    }


    static string H(string s) => WebUtility.HtmlEncode(s);
    /// HTML поста → текст для консолі (citysim).
    public static string Plain(string html) =>
        WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));

    // ── Оформлення каналу через Bot API (channelsetup) ───────────────────────
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// Перевіряє права бота й ставить аватар + опис. Повертає список того, що треба ввімкнути руками.
    public static async Task<List<string>> Setup(string token, string channel, City city, string? avatarPath)
    {
        var todo = new List<string>();
        var api = $"https://api.telegram.org/bot{token}";
        async Task<JsonElement?> Call(string method, object payload)
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{api}/{method}", content);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return doc.RootElement.GetProperty("result").Clone();
            Console.WriteLine($"   ⚠ {method}: {(doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : body)}");
            return null;
        }

        if (await Call("getMe", new { }) is not { } me) { todo.Add("Токен бота каналу не працює — перевір telegram_channel_bot_token"); return todo; }
        var botId = me.GetProperty("id").GetInt64();
        var botName = me.TryGetProperty("username", out var u) ? "@" + u.GetString() : botId.ToString();
        Console.WriteLine($"🤖 Бот: {botName}");

        if (await Call("getChat", new { chat_id = channel }) is not { } chat)
        {
            todo.Add($"Бот {botName} не бачить {channel}: додай його в канал адміном");
            return todo;
        }
        Console.WriteLine($"📢 Канал: «{(chat.TryGetProperty("title", out var tt) ? tt.GetString() : channel)}» ({channel})");

        bool canInfo = false;
        if (await Call("getChatMember", new { chat_id = channel, user_id = botId }) is { } mem)
        {
            var status = mem.GetProperty("status").GetString();
            bool Right(string k) => status == "creator" || (mem.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True);
            if (status is not ("administrator" or "creator"))
                todo.Add($"Зроби {botName} адміністратором {channel} (зараз: {status})");
            else
            {
                if (!Right("can_post_messages")) todo.Add($"{channel} → Адміністратори → {botName}: увімкни «Публікувати повідомлення»");
                if (!Right("can_edit_messages")) todo.Add($"{channel} → Адміністратори → {botName}: увімкни «Редагувати повідомлення інших»");
                canInfo = Right("can_change_info");
                if (!canInfo) todo.Add($"{channel} → Адміністратори → {botName}: увімкни «Змінювати інформацію каналу» (для аватара й опису)");
            }
            Console.WriteLine($"   права: статус {status}, публікувати {Right("can_post_messages")}, редагувати {Right("can_edit_messages")}, інфо {Right("can_change_info")}");
        }
        if (!canInfo) return todo;

        // Аватар
        if (avatarPath is null || !File.Exists(avatarPath))
            todo.Add($"Не знайдено design/channels/{city.Key}_avatar.png — аватар не встановлено");
        else
        {
            using var form = new MultipartFormDataContent { { new StringContent(channel), "chat_id" } };
            var img = new ByteArrayContent(await File.ReadAllBytesAsync(avatarPath));
            img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(img, "photo", Path.GetFileName(avatarPath));
            var resp = await Http.PostAsync($"{api}/setChatPhoto", form);
            var body = await resp.Content.ReadAsStringAsync();
            Console.WriteLine(resp.IsSuccessStatusCode ? $"🖼 Аватар встановлено ({Path.GetFileName(avatarPath)})" : $"   ⚠ setChatPhoto: {body}");
            if (!resp.IsSuccessStatusCode) todo.Add("Аватар не встановився — див. помилку вище");
        }

        // Опис (≤255 симв.)
        var desc = city.Ru
            ? $"Воздушные тревоги и угрозы — {city.Title} и Харьковщина. Тревога/отбой, каждые 8 мин — карта и цели по области. Данные приблизительные — слушай официальную тревогу. 🤖 WarAlarm"
            : $"Повітряні тривоги та загрози — {city.Title}, Київщина, Черкащина, Чернігівщина, Полтавщина. Тривога/відбій, щовісім хвилин — карта й цілі"
              + (city.Key == "kyiv" ? ", світло" : "") + ". Дані приблизні — слухай офіційну тривогу. 🤖 WarAlarm";
        var r = await Call("setChatDescription", new { chat_id = channel, description = desc });
        // «description is not modified» — теж ок
        Console.WriteLine(r is not null ? "📝 Опис встановлено:\n   " + desc : "   (якщо вище «not modified» — опис уже такий самий)");
        return todo;
    }

    /// Шукає файл дизайну вгору від поточної теки (запуск з src/Reader або з кореня репо).
    public static string? FindDesign(string file)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "design", "channels", file);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
