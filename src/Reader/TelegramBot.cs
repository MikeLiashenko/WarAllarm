using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ── WarAlarm · Telegram-бот (персональний) ───────────────────────────────────
// Кожен користувач задає СВОЄ місто (пошук по великому списку ~5330 міст, як у
// застосунку, кнопкою або геолокацією; мікрорайон чи адресу — через OSM, див. Geocode). Далі статус, ризик, карта, тривоги — по його
// місту. Бот сам шле сповіщення в особисті, коли загроза поруч із містом користувача.
// Токен — secrets.local.txt (telegram_bot_token=...). Окремий від каналу.
//
// Усі відповіді — Telegram HTML (parse_mode=HTML): тексти від Program уже екрановані.
// Проти спаму: «🔄 Оновити» і стеження за ціллю РЕДАГУЮТЬ повідомлення, а не шлють нові.

// Що вміє ядро (Program передає): усе відносно точки (lat,lon). Тексти — HTML.
public sealed record BotFeatures(
    Func<double, double, string> StatusAt,
    Func<double, double, string> RiskAt,
    Func<double, double, double, (bool alarm, string text)> AlarmAt, // (lat, lon, радіус км); text — простий текст («Шахед ~12 км»)
    Func<double, double, string, Task<byte[]?>> MapPng,      // карта з містом користувача
    Func<Task<string>> Ai,
    Func<double, double, string, string> Stats, // 📈 аналітика тривог у місці користувача
    Func<double, double, Task<string>> Alerts,              // офіційні тривоги: твоя область + країна
    Func<Task<(byte[] png, string caption)>>? Night = null, // 🌙 підсумок ночі
    Func<string>? Sources = null, // 📡 які канали що дали (SourceHub)
    Func<string?>? Outlook = null, // 🔮 чого чекати цієї ночі (null — оцінки ще нема)
    Func<double, double, int, List<(string id, string text)>>? TargetsNear = null, // 🛰 найближчі цілі (короткі рядки)
    Func<string, double, double, (string text, string stage)?>? TargetInfo = null, // 🛰 картка цілі (null — зникла)
    Func<double, double, bool>? PpoCleared = null, // 🛡 загрозу балістики в точці щойно зняли через вороже ППО
    Func<double, double, DangerStars.Result>? Stars = null); // ⭐ небезпека надворі в точці (0–5)

public sealed class TelegramBot
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    const string SubsFile = "bot_subscribers.json";

    sealed record Place(string Name, double Lat, double Lon);
    // Live — трансляція геопозиції («Поки не вимкну»): до LiveUntil; LiveStop — трансляцію зупинили.
    sealed record Upd(long Id, long Chat, string? Text, string? CbData, string? CbId, long MsgId, double? Lat, double? Lon,
                      DateTime? LiveUntil = null, bool LiveStop = false);
    // 🛰 Жива геопозиція: де людина зараз (з трансляції Telegram) — тривоги рахуються тут, а не для «свого міста».
    sealed record Live(double Lat, double Lon, string Name, DateTime Until, DateTime Seen, double AnnLat, double AnnLon);

    readonly string _api;
    readonly BotFeatures _f;
    readonly ConcurrentDictionary<long, Place?> _users = new();      // chat → місто (null = ще не задано)
    readonly ConcurrentDictionary<long, Live> _live = new();         // chat → де зараз (трансляція геопозиції)
    // Телефон шле точку лише коли рухається; стоїть добу без жодної — вважаємо, що трансляція згасла.
    static readonly TimeSpan LiveStale = TimeSpan.FromHours(24);
    readonly ConcurrentDictionary<long, int> _radius = new();        // 📏 радіус зони тривоги, км (нема — типовий, як у застосунку)
    public const int RadiusDefault = 25, RadiusMin = 5, RadiusMax = 100;
    static readonly int[] RadiusChoices = { 5, 10, 15, 20, 25, 30, 40, 50, 70, 100 };
    int RadiusOf(long chat) => _radius.TryGetValue(chat, out var r) ? r : RadiusDefault;
    readonly ConcurrentDictionary<long, bool> _lastAlarm = new();    // для фронта тривоги
    readonly ConcurrentDictionary<long, DateTime> _calmSince = new(); // відбій — лише після ≥60 с тиші (без «тривога-відбій-тривога»)
    // 🛰 Стеження: chat → (id цілі, останній опис, стадія, повідомлення, яке редагуємо).
    // Ключі кнопок — короткі (callback_data ≤ 64 байти, а id цілі кирилицею довший).
    readonly ConcurrentDictionary<long, (string id, string last, string stage, long msg)> _tracking = new();
    readonly ConcurrentDictionary<string, string> _trackKeys = new();
    // 📍 Кілька збігів адреси — чекають на вибір кнопкою «geo:N» (у callback_data не влазить назва).
    readonly ConcurrentDictionary<long, List<Geocode.Hit>> _geoPending = new();
    long _offset;

    // ── 📣 Поділитися: особисте посилання-запрошення t.me/<бот>?start=r<chat> ──────
    // Хто кого привів — окремий файл (не bot_subscribers.json): /stop і новий /start не накручують
    // лічильник, а друг, що вже колись був у боті, не рахується вдруге.
    const string RefFile = "bot_referrals.json";
    /// Ім'я бота без «@» (з getMe) — для посилань у каналах і в «Поділитися». null — ще не відомо.
    public static string? Username { get; private set; }
    readonly ConcurrentDictionary<long, long> _refBy = new();      // запрошений → хто запросив
    readonly ConcurrentDictionary<string, int> _fromTag = new();   // мітка посилання (?start=ch_kyiv) → скільки прийшло
    readonly ConcurrentDictionary<long, bool> _everSeen = new();   // хто будь-коли запускав бота (щоб не рахувати двічі)

    public TelegramBot(string token, BotFeatures features)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _f = features;
        LoadSubs();
        LoadRefs();
        foreach (var id in _users.Keys) _everSeen[id] = true;
    }

    /// Посилання на бота з міткою, звідки прийшли (канали, сайт): t.me/<бот>?start=<tag>.
    public static string? Link(string? tag = null) =>
        Username is { } u ? $"https://t.me/{u}" + (tag is { Length: > 0 } ? "?start=" + tag : "") : null;

    public int ReferralCount => _refBy.Count;

    public int SubscriberCount => _users.Count;

    // ── Клавіатури ───────────────────────────────────────────────────────────
    // Головне — завжди під рукою; решта — у «☰ Ще» (inline-меню), щоб не було 10 кнопок.
    static readonly object MainKb = new
    {
        keyboard = new[]
        {
            new[] { "📊 Статус", "🛰 Цілі" },
            new[] { "🗺 Карта", "🚨 Тривоги" },
            new[] { "🏠 Укриття", "☰ Ще" },
        },
        resize_keyboard = true,
        is_persistent = true,
        input_field_placeholder = "Місто, мікрорайон чи адреса",
    };

    static object Btn(string text, string data) => new { text, callback_data = data };
    static object Inline(params object[][] rows) => new { inline_keyboard = rows };

    // Жива карта як міні-застосунок: та сама сторінка, що й сайт. Місто — у фрагменті URL.
    // ?v=… — нова адреса повз кеш Telegram (він довго тримає стару сторінку); далі сторінка сама підтягує свіжу версію.
    const string MiniAppUrl = "https://mikeliashenko.github.io/WarAllarm/site/?v=2";
    static object? LiveMapKb(long chat, Place? pl)
    {
        if (chat < 0) return null; // кнопки web_app працюють лише в особистих чатах
        var url = pl is null ? MiniAppUrl
            : MiniAppUrl + "#home=" + Uri.EscapeDataString(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{pl.Lat:0.###},{pl.Lon:0.###},{pl.Name}"));
        return Inline(new object[] { new { text = "🗺 Відкрити живу карту", web_app = new { url } } });
    }

    static readonly object MoreKb = Inline(
        new[] { Btn("🛰 Стежити за мною (геопозиція)", "cmd:/live") },
        new[] { Btn("🔮 Що вночі", "cmd:/tonight"), Btn("🌙 Підсумок ночі", "cmd:/night") },
        new[] { Btn("🎯 Ризик", "cmd:/risk") },
        new[] { Btn("📈 Аналітика", "cmd:/stats"), Btn("📡 Джерела", "cmd:/sources") },
        new[] { Btn("🤖 ШІ-аналіз", "cmd:/ai"), Btn("📍 Змінити місто", "cmd:/place") },
        new[] { Btn("📏 Радіус тривоги", "cmd:/radius"), Btn("❓ Довідка", "cmd:/help") },
        new[] { Btn("📣 Поділитися з друзями", "cmd:/share") });

    // 📏 Вибір радіусу: поточний позначено ✅.
    static object RadiusKb(int cur) => Inline(
        RadiusChoices.Take(5).Select(r => Btn((r == cur ? "✅ " : "") + r + " км", "rad:" + r)).ToArray(),
        RadiusChoices.Skip(5).Select(r => Btn((r == cur ? "✅ " : "") + r + " км", "rad:" + r)).ToArray());

    static string RadiusText(int r, Place? p) =>
        $"📏 <b>Радіус тривоги: {r} км</b>\n\n"
        + $"Щойно ціль залетить ближче ніж <b>{r} км</b> до " + (p is { } pl ? $"<b>{H(pl.Name)}</b>" : "твого міста")
        + " — надішлю 🔴 тривогу, а коли вилетить і буде тихо — 🟢 відбій.\n"
        + "Менше — лише коли зовсім поруч; більше — попереджу раніше.\n\n"
        + "Обери 👇 або напиши, напр., <code>/radius 35</code> (від 5 до 100 км).";

    static readonly object CityKb = Inline(
        new[] { Btn("Київ", "city:Київ"), Btn("Харків", "city:Харків"), Btn("Дніпро", "city:Дніпро") },
        new[] { Btn("Одеса", "city:Одеса"), Btn("Львів", "city:Львів"), Btn("Запоріжжя", "city:Запоріжжя") });

    // Тимчасова клавіатура вибору міста: геолокація одним дотиком.
    static readonly object LocationKb = new
    {
        keyboard = new object[]
        {
            new object[] { new { text = "📍 Надіслати геолокацію", request_location = true } },
            new object[] { "⬅️ Скасувати" },
        },
        resize_keyboard = true,
        one_time_keyboard = true,
    };

    static readonly object StatusKb = Inline(
        new[] { Btn("🔄 Оновити", "upd:st"), Btn("🎯 Ризик", "cmd:/risk"), Btn("🛰 Цілі", "cmd:/targets") });
    static readonly object RiskKb = Inline(new[] { Btn("🔄 Оновити", "upd:rk"), Btn("📊 Статус", "cmd:/status") });
    static readonly object AlertsKb = Inline(new[] { Btn("🔄 Оновити", "upd:al"), Btn("🗺 Карта", "cmd:/map") });
    static readonly object AlarmKb = Inline(new[] { Btn("🛰 Що летить", "cmd:/targets"), Btn("🏠 Укриття", "cmd:/shelter") });
    // Під відбоєм: «чи було чути вибух?» (краудсорс точності).
    static readonly object BoomKb = Inline(new[] { Btn("💥 Чув вибух", "boom_yes"), Btn("🤫 Було тихо", "boom_no") });
    static readonly object UntrackKb = Inline(new[] { Btn("⏹ Не стежити", "untrk") });
    // 🏙 Ціль у 3D над справжнім містом (Ukraine-3D: кожен будинок з OSM). go/ сам знаходить ціль за id
    // у живому стані й відкриває потрібну область; сторінка веде модель цілі разом із ботом/застосунком.
    const string City3dUrl = "https://mikeliashenko.github.io/Ukraine-3D/go/?id=";
    static object TrackKb(long chat, string id) => chat < 0 ? UntrackKb : Inline(
        new object[] { new { text = "🏙 Ціль у 3D над містом", web_app = new { url = City3dUrl + Uri.EscapeDataString(id) } } },
        new object[] { Btn("⏹ Не стежити", "untrk") });
    static readonly object NoKb = new { inline_keyboard = Array.Empty<object[]>() };

    // ── Фонові цикли ──────────────────────────────────────────────────────────
    // Бот не має права вмерти. Що вже траплялося:
    //  • 2026-11-04: одна зависла команда тримала весь цикл — бот мовчав годинами;
    //  • 2026-11-07: Telegram відповів 429/502, наступний запит вийшов за таймаут, а таймаут
    //    HttpClient — це OperationCanceledException, і цикл вирішив, що його зупиняють: break,
    //    бот мертвий до перезапуску контейнера.
    // Тепер: опитування лише читає й ставить у чергу (ніщо його не блокує); будь-яка помилка —
    // пауза й далі (429 — скільки просить Telegram); сторож перезапускає опитування, якщо
    // воно мовчить 3 хв; команди виконує окремий обробник по черзі, ≤60 с кожна, а повторні
    // натискання тієї ж кнопки, поки перша ще в черзі чи в роботі, не дублюються.
    readonly System.Threading.Channels.Channel<Upd> _queue =
        System.Threading.Channels.Channel.CreateBounded<Upd>(new System.Threading.Channels.BoundedChannelOptions(300)
            { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    readonly HashSet<string> _pending = new(); // chat|команда — уже в черзі або виконується
    DateTime _lastPoll = DateTime.UtcNow;

    static string KeyOf(Upd u) => $"{u.Chat}|{u.CbData ?? u.Text ?? $"{u.Lat},{u.Lon}{(u.LiveUntil is null ? "" : "|live")}{(u.LiveStop ? "|stop" : "")}"}";
    static string Short(string s) => s.Length > 40 ? s[..40] + "…" : s;

    public async Task RunAsync(CancellationToken ct = default)
    {
        try { await InitOffset(); } catch { }
        try { await InitMe(); } catch { }
        try { await SetCommands(); } catch { }
        _ = RunUserAlarms(ct);                 // персональні тривоги в особисті
        _ = Task.Run(() => Worker(ct));
        while (!ct.IsCancellationRequested)
        {
            using var loop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _lastPoll = DateTime.UtcNow;
            var poll = Task.Run(() => PollLoop(loop.Token));
            while (!poll.IsCompleted && !ct.IsCancellationRequested)
            {
                await Task.WhenAny(poll, Task.Delay(TimeSpan.FromSeconds(20)));
                if (DateTime.UtcNow - _lastPoll > TimeSpan.FromMinutes(3))
                {
                    Console.Error.WriteLine("⚠ bot: опитування мовчить 3 хв — перезапускаю");
                    loop.Cancel();
                    break;
                }
            }
            if (poll.IsFaulted) Console.Error.WriteLine("⚠ bot: опитування впало — " + poll.Exception?.GetBaseException().Message);
            try { await Task.Delay(2000, ct); } catch { break; }
        }
    }

    async Task PollLoop(CancellationToken ct)
    {
        var errors = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ups = await GetUpdates(ct);
                _lastPoll = DateTime.UtcNow;
                errors = 0;
                foreach (var u in ups)
                {
                    _offset = u.Id + 1;
                    var what = u.CbData ?? u.Text ?? "геолокація";
                    bool dup;
                    lock (_pending) dup = !_pending.Add(KeyOf(u));
                    if (dup)
                    {
                        Console.WriteLine($"🤖 бот ← {Short(what)} (повтор, уже виконую)");
                        if (u.CbId != null) _ = Call("answerCallbackQuery", new { callback_query_id = u.CbId });
                        continue;
                    }
                    Console.WriteLine($"🤖 бот ← {Short(what)}");
                    _queue.Writer.TryWrite(u);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _lastPoll = DateTime.UtcNow; // живий, просто Telegram відповідає помилкою
                errors++;
                // 409 = хтось інший читає цього ж бота (локальний Reader з тим самим токеном):
                // тоді частина повідомлень іде йому, і бот «не відповідає».
                Console.Error.WriteLine(ex.Message.Contains("409")
                    ? "⚠ bot: 409 — цього бота читає ще один Reader з тим самим токеном (вимкни локальний!)"
                    : "⚠ bot: " + ex.Message);
                var m = Regex.Match(ex.Message, @"retry after (\d+)");
                var wait = m.Success ? int.Parse(m.Groups[1].Value) + 1 : Math.Min(30, 3 * errors);
                try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); } catch { return; }
            }
        }
    }

    async Task Worker(CancellationToken ct)
    {
        try
        {
            await foreach (var u in _queue.Reader.ReadAllAsync(ct))
            {
                var what = u.CbData ?? u.Text ?? "геолокація";
                var key = KeyOf(u);
                var work = Task.Run(async () =>
                {
                    try
                    {
                        if (u.CbData != null) await HandleCallback(u.Chat, u.CbData, u.CbId!, u.MsgId);
                        else if (u.LiveStop) await EndLive(u.Chat, "вимкнено в Telegram");
                        else if (u.Lat is { } la && u.Lon is { } lo && u.LiveUntil is { } lu) await OnLiveLocation(u.Chat, la, lo, lu);
                        else if (u.Lat is { } la2 && u.Lon is { } lo2) await SetPlaceByLocation(u.Chat, la2, lo2);
                        else if (u.Text != null) await Handle(u.Chat, u.Text.Trim());
                    }
                    catch (Exception ex) { Console.Error.WriteLine("⚠ bot handle: " + ex.Message); }
                });
                // Повільна команда лишається «в роботі», доки не закінчиться: повтори не множаться.
                _ = work.ContinueWith(_ => { lock (_pending) _pending.Remove(key); });
                if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(60), ct)) != work)
                    Console.Error.WriteLine($"⚠ bot: «{Short(what)}» не відповіла за 60 с — далі без неї");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Обробник теж не має вмирати: перезапускаємо його.
            Console.Error.WriteLine("⚠ bot worker: " + ex.Message);
            if (!ct.IsCancellationRequested) _ = Task.Run(() => Worker(ct));
        }
    }

    // ⭐ «Небезпека надворі: ★★★★☆ 4/5 — дуже висока» під тривогою (як у 📊 Статусі й застосунку).
    string StarsLine(Place place)
    {
        try
        {
            if (_f.Stars?.Invoke(place.Lat, place.Lon) is not { Stars: > 0 } r) return "";
            return $"⭐ Небезпека надворі: {DangerStars.Bar(r.Stars)} {r.Stars}/5 — {r.Level.ToLowerInvariant()}\n";
        }
        catch { return ""; }
    }

    // ── 🛰 Жива геопозиція ────────────────────────────────────────────────────
    /// Де рахувати тривоги: там, де людина зараз (жива трансляція), інакше — її місто.
    Place? AlarmPlace(long chat)
    {
        if (_live.TryGetValue(chat, out var lv))
        {
            if (DateTime.UtcNow < lv.Until && DateTime.UtcNow - lv.Seen < LiveStale) return new Place(lv.Name, lv.Lat, lv.Lon);
            _ = EndLive(chat, DateTime.UtcNow >= lv.Until ? "час трансляції минув" : "телефон давно не надсилав геопозицію");
        }
        return _users.TryGetValue(chat, out var p) ? p : null;
    }

    static string NearName(double lat, double lon)
    {
        var best = Cities.All.MinBy(c => ThreatGeo.Km((c.lat, c.lon), (lat, lon)));
        return ThreatGeo.Km((best.lat, best.lon), (lat, lon)) <= 15 ? best.name : $"біля {best.name}";
    }

    async Task OnLiveLocation(long chat, double lat, double lon, DateTime until)
    {
        var name = NearName(lat, lon);
        bool fresh = !_live.TryGetValue(chat, out var was);
        if (!_users.ContainsKey(chat)) { _users[chat] = null; SaveSubs(); }
        _live[chat] = fresh ? new Live(lat, lon, name, until, DateTime.UtcNow, lat, lon)
                            : was! with { Lat = lat, Lon = lon, Name = name, Until = until, Seen = DateTime.UtcNow };
        if (fresh)
        {
            _lastAlarm.Remove(chat); _calmSince.Remove(chat);
            var home = _users.TryGetValue(chat, out var h) && h is not null ? h.Name : null;
            await Send(chat,
                "🛰 <b>Стежу за тобою наживо</b>\n"
                + $"📍 Зараз: <b>{H(name)}</b>\n"
                + $"🔔 Тривоги — там, де ти є, у зоні <b>{RadiusOf(chat)} км</b>. Працює й коли Telegram закритий.\n"
                + (home is null ? "" : $"Зупиниш трансляцію — повернусь до «{H(home)}».\n")
                + (until < DateTime.MaxValue.AddYears(-1) ? $"⏳ Трансляція до {until.ToLocalTime():HH:mm} — для «завжди» обери «Поки не вимкну».\n" : ""), MainKb);
            if (AlarmPlace(chat) is { } np) await SendStatus(chat, np);
            return;
        }
        // Переїхав в інше місце (≥15 км від того, про яке казали) — тихо кажемо, де тепер стежимо.
        if (ThreatGeo.Km((was!.AnnLat, was.AnnLon), (lat, lon)) >= 15)
        {
            _live[chat] = _live[chat] with { AnnLat = lat, AnnLon = lon };
            await Send(chat, $"📍 Тепер стежу біля <b>{H(name)}</b> (зона {RadiusOf(chat)} км).", null, silent: true);
        }
    }

    async Task EndLive(long chat, string why)
    {
        if (!_live.TryRemove(chat, out _)) return;
        _lastAlarm.Remove(chat); _calmSince.Remove(chat);
        var home = _users.TryGetValue(chat, out var h) ? h : null;
        try
        {
            await Send(chat, $"🛰 <b>Трансляцію геопозиції зупинено</b> ({H(why)}).\n"
                + (home is not null ? $"🔔 Тривоги знову для «{H(home.Name)}»." : "📍 Напиши своє місто, щоб тривоги приходили далі.")
                + "\nУвімкнути знову — /live", MainKb);
        }
        catch { }
    }

    const string LiveHelp =
        "🛰 <b>Стежити за мною (геопозиція)</b>\n\n"
        + "Тривоги приходитимуть туди, де ти <b>зараз</b> — удома, в дорозі, в іншому місті. Телефон сам надсилає "
        + "геопозицію у фоні: працює навіть коли Telegram і бот закриті.\n\n"
        + "<b>Як увімкнути</b> (один раз):\n"
        + "1. У цьому чаті натисни 📎 (скріпка)\n"
        + "2. «Геопозиція» → <b>«Транслювати геопозицію»</b>\n"
        + "3. Обери <b>«Поки не вимкну»</b>\n\n"
        + "Вимкнути — «Зупинити трансляцію» вгорі чату. Тоді тривоги знову для твого міста.\n"
        + "<i>Геопозицію бачить лише бот, ніде не зберігаю — тільки щоб рахувати відстань до цілей.</i>";

    // Кожні ~10 с: для кожного користувача з містом — тривога поруч → сповіщення.
    async Task RunUserAlarms(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
            foreach (var chat in _users.Keys.Union(_live.Keys).ToArray())
            {
                if (AlarmPlace(chat) is not { } place) continue;
                int rad = RadiusOf(chat);
                var (alarm, text) = _f.AlarmAt(place.Lat, place.Lon, rad);
                bool last = _lastAlarm.TryGetValue(chat, out var l) && l;
                if (alarm) _calmSince.Remove(chat);
                if (alarm && !last)
                {
                    bool ball = text.StartsWith('☄');
                    var stars = StarsLine(place);
                    try { await Send(chat, ball
                        ? $"☄️ <b>ЗАГРОЗА БАЛІСТИКИ — {H(place.Name)}</b>\n"
                          + (text.Contains('·') ? $"🚀 {H(text[(text.IndexOf('·') + 1)..].Trim())}\n" : "")
                          + stars
                          + "Пуску ще немає, але ракета летить лічені хвилини.\n"
                          + "\n➤ <b>Будь ближче до укриття</b>; почуєш «пуск» чи вибухи — одразу туди."
                        : $"🔴 <b>ТРИВОГА поруч — {H(place.Name)}</b>\n"
                          + (text.Length > 0 ? $"🎯 {H(text)}\n" : "")
                          + $"📏 Зона тривоги — {rad} км\n"
                          + stars
                          + "\n➤ <b>В укриття!</b> Подалі від вікон, правило двох стін.", AlarmKb); } catch { }
                    _lastAlarm[chat] = true;
                }
                else if (!alarm && last)
                {
                    // Ціль на межі зони «блимає» — відбій лише після хвилини тиші.
                    if (!_calmSince.TryGetValue(chat, out var since)) { _calmSince[chat] = DateTime.Now; continue; }
                    if (DateTime.Now - since < TimeSpan.FromSeconds(60)) continue;
                    _calmSince.Remove(chat);
                    bool ppo = _f.PpoCleared?.Invoke(place.Lat, place.Lon) == true;
                    try { await Send(chat, ppo
                        ? $"🛡 <b>Працює вороже ППО — {H(place.Name)}</b>\nЗагрозу балістики знято: схоже, це не пуски по нас.\nДо офіційного відбою — уважно."
                        : $"🟢 <b>Відбій — {H(place.Name)}</b>\nНаразі поруч чисто.\n\nЧи було чути вибух?", ppo ? null : BoomKb); } catch { }
                    _lastAlarm[chat] = false;
                }
            }
            // 🛰 Стеження: ціль рухається → РЕДАГУЄМО ту саму картку; змінилась стадія
            // (наближається → на підльоті → над районом) — нове повідомлення, щоб дзенькнуло.
            if (_f.TargetInfo is { } ti)
                foreach (var (chat, tr) in _tracking.ToArray())
                {
                    var pl = AlarmPlace(chat);
                    if (pl is null) { _tracking.Remove(chat); continue; }
                    var info = ti(tr.id, pl.Lat, pl.Lon);
                    if (info is not { } inf)
                    {
                        _tracking.Remove(chat);
                        try
                        {
                            await EditMarkup(chat, tr.msg, NoKb);
                            await Send(chat, "🛰 <b>Ціль зникла з радарів</b> — збита, долетіла або відбій.\nСтеження завершено.");
                        }
                        catch { }
                        continue;
                    }
                    // Рядок «оновлено HH:mm:ss» не рахуємо зміною — лише рух/стадію.
                    static string Core(string x) => string.Join("\n", x.Split('\n').Where(l => !l.Contains("🕒")));
                    if (Core(inf.text) == Core(tr.last)) continue;
                    try
                    {
                        if (inf.stage != tr.stage)
                        {
                            await EditMarkup(chat, tr.msg, NoKb);
                            var id = await Send(chat, $"🛰 <b>Ціль: {H(inf.stage)}</b>\n\n" + inf.text, TrackKb(chat, tr.id));
                            _tracking[chat] = (tr.id, inf.text, inf.stage, id ?? tr.msg);
                        }
                        else
                        {
                            await Edit(chat, tr.msg, "👁 <b>Стежу за ціллю</b> · оновлюється\n\n" + inf.text, TrackKb(chat, tr.id));
                            _tracking[chat] = (tr.id, inf.text, inf.stage, tr.msg);
                        }
                    }
                    catch { }
                }
        }
    }

    // ── Команди ───────────────────────────────────────────────────────────────
    async Task Handle(long chat, string text)
    {
        var cmd = text.Split(' ', '\n')[0].Split('@')[0].ToLowerInvariant();
        // Трансляція геопозиції увімкнена — 📊 Статус, 🛰 Цілі, 🏠 Укриття… для того місця, де людина зараз.
        var pl = AlarmPlace(chat);
        bool hasCity = pl is not null;

        switch (cmd)
        {
            case "/start":
                // «/start r123» — прийшов за запрошенням, «/start ch_kyiv» — з каналу / сайту.
                await OnStartPayload(chat, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault());
                if (!_users.ContainsKey(chat)) _users[chat] = null;
                SaveSubs();
                if (hasCity)
                {
                    await Send(chat, $"👋 <b>З поверненням!</b>\nТвоє місто — <b>{H(pl!.Name)}</b>. Сповіщення увімкнені 🔔", MainKb);
                    await SendStatus(chat, pl);
                    return;
                }
                await Send(chat,
                    "👋 <b>Вітаю у WarAlarm!</b>\n\n"
                    + "Я стежу за повітряними загрозами й попереджаю, коли щось летить до <b>твого</b> міста.\n\n"
                    + "<b>Обери місто</b> 👇 кнопкою, надішли геолокацію 📍 або просто напиши назву (напр. «Бровари»), "
                    + "мікрорайон чи адресу (напр. «Харків, 603-й мікрорайон», «Київ, вул. Хрещатик 22»).",
                    CityKb);
                await Send(chat, "📍 Або поділись геолокацією — так точніше:", LocationKb);
                return;

            case "/stop":
                _live.Remove(chat);
                _users.Remove(chat); _lastAlarm.Remove(chat); _tracking.Remove(chat); _calmSince.Remove(chat); _radius.Remove(chat);
                SaveSubs();
                await Send(chat, "🔕 <b>Сповіщення вимкнено.</b>\nПовернутись — /start", new { remove_keyboard = true });
                return;
        }

        if (Match(text, cmd, "/more", "☰", "ще", "меню"))
        {
            await Send(chat, "☰ <b>Ще можливості</b>", MoreKb);
            return;
        }
        if (Match(text, cmd, "/share", "📣", "поділитися", "поделиться", "запросити"))
        {
            await SendShare(chat);
            return;
        }
        if (Match(text, cmd, "/live", "🛰 стежити за мною", "стежити за мною", "геопозиція", "де я"))
        {
            await Send(chat, (_live.TryGetValue(chat, out var lv)
                ? $"🛰 <b>Уже стежу за тобою наживо</b> · зараз: {H(lv.Name)}\n\n" : "") + LiveHelp, MainKb);
            return;
        }
        if (Match(text, cmd, "/radius", "📏", "радіус", "радиус", "радіус тривоги"))
        {
            var arg = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault()?.Replace("км", "");
            if (arg != null && int.TryParse(arg, out var want)) { await SetRadius(chat, want, pl); return; }
            await Send(chat, RadiusText(RadiusOf(chat), pl), RadiusKb(RadiusOf(chat)));
            return;
        }
        if (Match(text, cmd, "/help", "❓", "довідка", "допомога"))
        {
            await Send(chat, HelpText(pl, RadiusOf(chat)), MainKb);
            return;
        }
        if (Match(text, cmd, "⬅️", "скасувати"))
        {
            await Send(chat, hasCity ? "👌 Гаразд." : "👌 Гаразд. Місто можна обрати будь-коли — просто напиши назву чи адресу.", MainKb);
            return;
        }
        if (Match(text, cmd, "/place", "📍", "моє місто", "мое место", "місто", "город"))
        {
            await Send(chat,
                (hasCity ? $"📍 Зараз: <b>{H(pl!.Name)}</b>\n\n" : "")
                + "Напиши назву міста (напр. «Львів», «Ірпінь», «Ізюм»), мікрорайон чи адресу "
                + "(«Харків, 603-й мікрорайон», «Дніпро, просп. Яворницького 50») або обери 👇", CityKb);
            await Send(chat, "…чи поділись геолокацією:", LocationKb);
            return;
        }

        // Кнопки-«вкладки».
        if (Match(text, cmd, "/map", "🗺", "карта"))
        {
            await Action(chat, "upload_photo");
            var png = await _f.MapPng(pl?.Lat ?? 0, pl?.Lon ?? 0, pl?.Name ?? ""); // без міста — карта країни
            if (png != null) await SendPhoto(chat, png, $"🗺 <b>Карта загроз</b> · {DateTime.Now:HH:mm}\n🔴 тривога · 🟡 жовтий рівень · силуети — цілі за курсом", LiveMapKb(chat, pl));
            else await Send(chat, "😔 Картинку зараз не вдалося намалювати, але жива карта працює.", LiveMapKb(chat, pl));
            return;
        }
        if (_f.Outlook is { } outlook && Match(text, cmd, "/tonight", "🔮", "що вночі", "прогноз"))
        {
            await Send(chat, outlook() ?? "🔮 Вечірньої оцінки загроз ще немає — монітори зазвичай пишуть її близько 21:00.");
            return;
        }
        if (_f.Night is { } night && Match(text, cmd, "/night", "🌙", "ніч", "ночь", "підсумок"))
        {
            var wait = await Send(chat, "🌙 Збираю підсумок ночі з каналів… (до хвилини)");
            await Action(chat, "upload_photo");
            try
            {
                var (png, caption) = await night();
                await SendPhoto(chat, png, caption);
                if (wait is { } w) await Delete(chat, w);
            }
            catch (Exception ex) { if (wait is { } w) await Edit(chat, w, "😔 Не вдалося зібрати підсумок: " + H(ex.Message)); }
            return;
        }
        if (Match(text, cmd, "/untrack", "⏹", "не стежити"))
        {
            await Send(chat, _tracking.Remove(chat) ? "⏹ Стеження вимкнено." : "Ти ні за чим не стежиш. Обери ціль у 🛰 Цілі.");
            return;
        }
        if (Match(text, cmd, "/stats", "📈", "аналітика"))
        {
            if (!hasCity) { await Send(chat, "📍 <b>Спершу обери своє місто</b> — напиши назву або натисни 👇", CityKb); return; }
            await Send(chat, _f.Stats(pl!.Lat, pl.Lon, pl.Name));
            return;
        }
        if (_f.Sources is { } srcs && Match(text, cmd, "/sources", "📡", "джерела")) { await Send(chat, srcs()); return; }
        if (Match(text, cmd, "/ai", "🤖", "ші", "ии"))
        {
            var wait = await Send(chat, "🤖 Аналізую обстановку…");
            await Action(chat, "typing");
            var reply = await _f.Ai();
            if (wait is not { } w || !await Edit(chat, w, reply)) await Send(chat, reply);
            return;
        }

        bool needsCity = Match(text, cmd, "/status", "📊", "статус") || Match(text, cmd, "/risk", "🎯", "ризик")
            || Match(text, cmd, "/shelter", "🏠", "укриття", "укрытие") || Match(text, cmd, "/alerts", "🚨", "тривоги")
            || Match(text, cmd, "/targets", "🛰", "цілі");
        if (needsCity && !hasCity)
        {
            await Send(chat, "📍 <b>Спершу обери своє місто</b> — напиши назву або натисни 👇", CityKb);
            return;
        }
        if (Match(text, cmd, "/status", "📊", "статус")) { await SendStatus(chat, pl!); return; }
        if (Match(text, cmd, "/risk", "🎯", "ризик")) { await Send(chat, RiskText(pl!), RiskKb); return; }
        if (Match(text, cmd, "/alerts", "🚨", "тривоги")) { await Send(chat, await _f.Alerts(pl!.Lat, pl.Lon), AlertsKb); return; }
        if (Match(text, cmd, "/shelter", "🏠", "укриття", "укрытие"))
        {
            var wait = await Send(chat, "🏠 Шукаю укриття поруч…");
            var res = await Shelters.Near(pl!.Lat, pl.Lon);
            if (wait is not { } w || !await Edit(chat, w, res)) await Send(chat, res);
            return;
        }
        if (_f.TargetsNear is { } near && Match(text, cmd, "/targets", "🛰", "цілі"))
        {
            await SendTargets(chat, pl!, near);
            return;
        }

        if (cmd.StartsWith('/'))
        {
            await Send(chat, "🤔 Не знаю такої команди. Ось що я вмію 👇\n\n" + HelpText(pl, RadiusOf(chat)), MainKb);
            return;
        }
        // Інакше — вважаємо це назвою міста → шукаємо у великому списку.
        await SetPlaceByName(chat, text);
    }

    static string HelpText(Place? place, int radius = RadiusDefault) =>
        "❓ <b>Як користуватись WarAlarm</b>\n\n"
        + "<b>Головне</b>\n"
        + "📊 Статус — ⭐ наскільки небезпечно надворі (0–5 зірок) і що летить біля твого міста\n"
        + "🛰 Цілі — найближчі цілі, за кожною можна 👁 стежити\n"
        + "🗺 Карта — карта тривог і цілей\n"
        + "🚨 Тривоги — офіційні: твоя область і країна\n"
        + "🏠 Укриття — найближчі укриття й метро\n\n"
        + "<b>☰ Ще</b>\n"
        + "🎯 Ризик · 🔮 Що вночі · 🌙 Підсумок ночі · 📈 Аналітика · 📡 Джерела · 🤖 ШІ-аналіз\n\n"
        + "<b>Налаштування</b>\n"
        + "📍 /place — змінити місто (можна мікрорайон чи адресу)" + (place is { } p ? $" (зараз: {H(p.Name)})" : "") + "\n"
        + "📏 /radius — радіус тривоги: ціль ближче — 🔴 тривога (5–100 км)\n"
        + "🛰 /live — стежити за мною: тривоги там, де ти зараз (трансляція геопозиції)\n"
        + "📣 /share — поділитися ботом з друзями\n"
        + "🔕 /stop — вимкнути сповіщення\n\n"
        + "☄️ Загроза балістики у твоїй області — теж тривога; «працює вороже ППО» її знімає.\n"
        + $"🔔 Сповіщення приходять самі: ціль ближче {radius} км → 🔴 тривога, потім 🟢 відбій.\n"
        + "<i>Бот — допоміжний. Завжди зважай на офіційну тривогу.</i>";

    string StatusText(Place p) => $"📊 <b>{H(p.Name)}</b> · {DateTime.Now:HH:mm}\n\n" + _f.StatusAt(p.Lat, p.Lon).TrimEnd();
    string RiskText(Place p) => $"🎯 <b>{H(p.Name)}</b> · {DateTime.Now:HH:mm}\n\n" + _f.RiskAt(p.Lat, p.Lon).TrimEnd();
    Task SendStatus(long chat, Place p) => Send(chat, StatusText(p), StatusKb);

    async Task SendTargets(long chat, Place p, Func<double, double, int, List<(string id, string text)>> near)
    {
        var list = near(p.Lat, p.Lon, 5);
        if (list.Count == 0)
        {
            await Send(chat, $"🛰 <b>Поруч із {H(p.Name)} цілей немає</b>\nУ радіусі 250 км тихо 🟢", StatusKb);
            return;
        }
        var sb = new StringBuilder($"🛰 <b>Найближчі цілі · {H(p.Name)}</b> · {DateTime.Now:HH:mm}\n\n");
        var rows = new List<object[]>();
        for (int i = 0; i < list.Count; i++)
        {
            sb.Append(i + 1).Append(". ").AppendLine(list[i].text).AppendLine();
            rows.Add(new[] { TrackBtn(list[i].id, $"👁 Стежити за №{i + 1}") });
        }
        sb.Append("<i>Натисни 👁 — і я оновлюватиму картку цілі, поки вона летить.</i>");
        await Send(chat, sb.ToString(), new { inline_keyboard = rows });
    }

    async Task SetPlaceByName(long chat, string text)
    {
        text = text.Trim();
        if (Cities.FindExact(text) is { } exact) { await SetPlace(chat, new Place(exact.name, exact.lat, exact.lon)); return; }
        // Назва міста (частково) — як раніше; адресу/мікрорайон не підміняємо першим-ліпшим містом.
        bool addr = Geocode.LooksLikeAddress(text);
        if (!addr && Cities.Find(text) is { } c) { await SetPlace(chat, new Place(c.name, c.lat, c.lon)); return; }

        // Мікрорайон, вулиця, будинок — OpenStreetMap.
        await Action(chat, "find_location");
        var geo = text.Length >= 3 ? await Geocode.Search(text) : new List<Geocode.Hit>();
        if (geo.Count == 1) { await SetPlace(chat, new Place(geo[0].Label, geo[0].Lat, geo[0].Lon)); return; }
        if (geo.Count > 1)
        {
            _geoPending[chat] = geo;
            await Send(chat, $"📍 Знайшов кілька місць для «{H(text)}». Яке твоє?",
                new { inline_keyboard = geo.Select((g, i) => new[] { Btn(g.Label, $"geo:{i}") }).ToArray() });
            return;
        }
        if (addr && Cities.Find(text) is { } c2) { await SetPlace(chat, new Place(c2.name, c2.lat, c2.lon)); return; }

        var sug = Cities.Suggest(text).Where(n => Encoding.UTF8.GetByteCount("city:" + n) <= 64).Take(6).ToList();
        if (sug.Count > 0)
            await Send(chat, $"🤔 Не знайшов «{H(text)}» точно. Може, одне з цих?",
                new { inline_keyboard = sug.Chunk(2).Select(r => r.Select(n => Btn(n, "city:" + n)).ToArray()).ToArray() });
        else
            await Send(chat, $"🤔 Не знайшов «{H(text)}».\n"
                + "Спробуй інакше — українською, з містом: «Біла Церква», «Харків, 603-й мікрорайон», "
                + "«Львів, вул. Городоцька 100» — або надішли геолокацію 📍");
    }

    async Task SetPlaceByLocation(long chat, double lat, double lon)
    {
        // Назва — найближчий населений пункт зі списку; координати — точні, як надіслав.
        var best = Cities.All.MinBy(c => ThreatGeo.Km((c.lat, c.lon), (lat, lon)));
        var km = ThreatGeo.Km((best.lat, best.lon), (lat, lon));
        await SetPlace(chat, new Place(km <= 15 ? best.name : $"біля {best.name}", lat, lon));
    }

    async Task SetRadius(long chat, int km, Place? p, long? editMsg = null)
    {
        km = Math.Clamp(km, RadiusMin, RadiusMax);
        if (km == RadiusDefault) _radius.Remove(chat); else _radius[chat] = km;
        if (!_users.ContainsKey(chat)) _users[chat] = null;
        SaveSubs();
        var txt = RadiusText(km, p) + "\n\n✅ Збережено.";
        if (editMsg is { } m && await Edit(chat, m, txt, RadiusKb(km))) return;
        await Send(chat, txt, RadiusKb(km));
    }

    async Task SetPlace(long chat, Place p)
    {
        _users[chat] = p;
        _lastAlarm.Remove(chat);
        _calmSince.Remove(chat);
        SaveSubs();
        await Send(chat,
            $"✅ <b>Готово! Твоє місто — {H(p.Name)}</b>\n"
            + $"🔔 Напишу, щойно ціль залетить у зону <b>{RadiusOf(chat)} км</b>, і дам відбій, коли мине (📏 /radius — змінити).\n"
            + "Змінити місто — просто напиши іншу назву чи адресу.\n\n"
            + "📣 Корисно? Поділись із рідними й друзями — /share", MainKb);
        await SendStatus(chat, p);
    }

    // Команда / кнопка: «/status», «📊 Статус» (кнопка, і старої клавіатури теж) або слово
    // окремо («статус»). Слово — лише ЦІЛИМ повідомленням: інакше місто «Карташівка»
    // відкривало карту, а «Решетилівка» — ШІ.
    static bool Match(string text, string cmd, params string[] keys)
    {
        var low = text.ToLowerInvariant().Trim();
        var word = Regex.Replace(low, @"^[^\p{L}/]+", "").Trim(); // без емодзі на початку
        foreach (var k in keys)
        {
            if (cmd == k) return true;
            if (k[0] != '/' && !char.IsLetter(k[0]) ? low.StartsWith(k) : word == k) return true;
        }
        return false;
    }

    static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── Bot API ──────────────────────────────────────────────────────────────
    async Task InitOffset()
    {
        using var doc = await GetJson($"{_api}/getUpdates?timeout=0");
        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
            _offset = Math.Max(_offset, u.GetProperty("update_id").GetInt64() + 1);
    }

    // Меню команд у Telegram (кнопка «/» біля поля вводу).
    async Task InitMe()
    {
        using var doc = await GetJson($"{_api}/getMe");
        if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("username", out var un)) Username = un.GetString();
        Console.WriteLine($"🤖 бот @{Username} · запрошень: {_refBy.Count}"
            + (_fromTag.IsEmpty ? "" : " · з посилань: " + string.Join(", ", _fromTag.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))));
    }

    const string ShareText = "🛡 WarAlarm — попереджає, коли щось летить саме до твого міста чи району: шахеди, ракети, "
        + "балістика. ⭐ Наскільки небезпечно надворі зараз, укриття поруч, жива карта. Безкоштовно.";

    async Task SendShare(long chat)
    {
        if (Link("r" + chat) is not { } link)
        {
            await Send(chat, "📣 Поділитися поки не вийшло — спробуй за хвилину.");
            return;
        }
        int mine = _refBy.Values.Count(v => v == chat);
        var share = "https://t.me/share/url?url=" + Uri.EscapeDataString(link) + "&text=" + Uri.EscapeDataString(ShareText);
        await Send(chat,
            "📣 <b>Поділись WarAlarm з тими, за кого хвилюєшся</b>\n\n"
            + "Кожен отримає тривогу саме для свого міста чи району — і зірки небезпеки надворі.\n\n"
            + $"🔗 Твоє посилання:\n{link}\n\n"
            + (mine > 0 ? $"🎉 За твоїм запрошенням уже приєдналось: <b>{mine}</b>. Дякую!" : "Натисни кнопку нижче й обери, кому надіслати 👇"),
            Inline(new object[] { new { text = "📤 Надіслати друзям", url = share } }));
    }

    // Мітка з посилання. Запрошення рахуємо лише для нової людини й не самому собі.
    async Task OnStartPayload(long chat, string? payload)
    {
        bool fresh = _everSeen.TryAdd(chat, true);
        if (!fresh) return;
        if (payload is not { Length: > 0 }) { SaveRefs(); return; }
        if (Regex.Match(payload, @"^r(\d{1,15})$") is { Success: true } rm && long.TryParse(rm.Groups[1].Value, out var by) && by != chat)
        {
            if (!_refBy.TryAdd(chat, by)) return;
            SaveRefs();
            int n = _refBy.Values.Count(v => v == by);
            Console.WriteLine($"📣 бот: запрошення від {by} → усього {n}");
            if (_users.ContainsKey(by))
                await Send(by, $"🎉 За твоїм запрошенням до WarAlarm приєднався ще хтось — усього <b>{n}</b>. Дякую, що ділишся!", silent: true);
            return;
        }
        if (Regex.IsMatch(payload, @"^[A-Za-z0-9_\-]{1,40}$"))
        {
            _fromTag.AddOrUpdate(payload, 1, (_, v) => v + 1);
            SaveRefs();
        }
    }

    void LoadRefs()
    {
        try
        {
            if (!File.Exists(RefFile)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(RefFile));
            if (doc.RootElement.TryGetProperty("by", out var by))
                foreach (var p in by.EnumerateObject())
                    if (long.TryParse(p.Name, out var id) && p.Value.TryGetInt64(out var r)) _refBy[id] = r;
            if (doc.RootElement.TryGetProperty("tags", out var tags))
                foreach (var p in tags.EnumerateObject())
                    if (p.Value.TryGetInt32(out var n)) _fromTag[p.Name] = n;
            if (doc.RootElement.TryGetProperty("seen", out var seen))
                foreach (var v in seen.EnumerateArray())
                    if (v.TryGetInt64(out var id)) _everSeen[id] = true;
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ " + RefFile + ": " + ex.Message); }
    }

    readonly object _refLock = new();
    void SaveRefs()
    {
        lock (_refLock) try
        {
            var json = JsonSerializer.Serialize(new
            {
                by = _refBy.ToDictionary(x => x.Key.ToString(), x => x.Value),
                tags = _fromTag.ToDictionary(x => x.Key, x => x.Value),
                seen = _everSeen.Keys.OrderBy(x => x).ToArray(),
            });
            File.WriteAllText(RefFile + ".tmp", json);
            File.Move(RefFile + ".tmp", RefFile, true);
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ " + RefFile + ": " + ex.Message); }
    }

    async Task SetCommands()
    {
        var cmds = new (string c, string d)[]
        {
            ("status", "📊 Що летить біля мого міста"), ("targets", "🛰 Найближчі цілі"),
            ("map", "🗺 Карта тривог"), ("alerts", "🚨 Офіційні тривоги"), ("shelter", "🏠 Укриття поруч"),
            ("risk", "🎯 Ризик по типах"), ("tonight", "🔮 Чого чекати цієї ночі"), ("night", "🌙 Підсумок ночі"), ("stats", "📈 Аналітика"),
            ("sources", "📡 Джерела"), ("ai", "🤖 ШІ-аналіз"), ("place", "📍 Змінити місто"), ("radius", "📏 Радіус тривоги"),
            ("live", "🛰 Стежити за мною (геопозиція)"), ("share", "📣 Поділитися з друзями"), ("help", "❓ Довідка"), ("stop", "🔕 Вимкнути сповіщення"),
        };
        await Call("setMyCommands", new { commands = cmds.Select(x => new { command = x.c, description = x.d }) });
        // Кнопка меню біля поля вводу відкриває живу карту.
        await Call("setChatMenuButton", new { menu_button = new { type = "web_app", text = "🗺 Карта", web_app = new { url = MiniAppUrl } } });
    }

    async Task<List<Upd>> GetUpdates(CancellationToken ct)
    {
        var outp = new List<Upd>();
        using var doc = await GetJson($"{_api}/getUpdates?timeout=25&offset={_offset}&allowed_updates=[\"message\",\"edited_message\",\"callback_query\"]", ct);
        if (doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            throw new Exception($"{(doc.RootElement.TryGetProperty("error_code", out var ec) ? ec.GetInt32() : 0)} "
                + (doc.RootElement.TryGetProperty("description", out var de) ? de.GetString() : ""));
        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var id = u.GetProperty("update_id").GetInt64();
            if (u.TryGetProperty("callback_query", out var cq) &&
                cq.TryGetProperty("message", out var cm) && cm.TryGetProperty("chat", out var cch))
            {
                outp.Add(new Upd(id, cch.GetProperty("id").GetInt64(), null,
                    cq.TryGetProperty("data", out var d) ? d.GetString() : "",
                    cq.GetProperty("id").GetString(),
                    cm.TryGetProperty("message_id", out var mid) ? mid.GetInt64() : 0, null, null));
            }
            else if ((u.TryGetProperty("message", out var m) || u.TryGetProperty("edited_message", out m)) && m.TryGetProperty("chat", out var ch)
                     && m.TryGetProperty("location", out var loc))
            {
                // 🛰 Трансляція: «live_period» є, поки транслюють (0x7FFFFFFF — «Поки не вимкну»); оновлення приходять
                // як edited_message. Відредаговане без live_period — трансляцію зупинили. Разова точка — нове «моє місто».
                bool edited = u.TryGetProperty("edited_message", out _);
                DateTime? until = null;
                if (loc.TryGetProperty("live_period", out var lp) && lp.TryGetInt64(out var secs))
                {
                    var sent = DateTimeOffset.FromUnixTimeSeconds(m.TryGetProperty("date", out var dt) ? dt.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
                    until = secs >= 0x7FFFFFFF ? DateTime.MaxValue : sent.AddSeconds(secs);
                }
                outp.Add(new Upd(id, ch.GetProperty("id").GetInt64(), null, null, null, 0,
                    loc.GetProperty("latitude").GetDouble(), loc.GetProperty("longitude").GetDouble(),
                    until, LiveStop: edited && until is null));
            }
            else if (u.TryGetProperty("message", out var m2) &&
                     m2.TryGetProperty("text", out var t) && m2.TryGetProperty("chat", out var ch2))
            {
                outp.Add(new Upd(id, ch2.GetProperty("id").GetInt64(), t.GetString() ?? "", null, null, 0, null, null));
            }
            else { _offset = Math.Max(_offset, id + 1); }
        }
        return outp;
    }

    async Task<HttpResponseMessage> Call(string method, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        for (var attempt = 0; ; attempt++)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{_api}/{method}", content);
            // 429: Telegram каже, скільки чекати — чекаємо (≤30 с) і пробуємо ще раз, а не губимо відповідь.
            if ((int)resp.StatusCode != 429 || attempt >= 2) return resp;
            var body = await resp.Content.ReadAsStringAsync();
            var m = Regex.Match(body, @"""retry_after""\s*:\s*(\d+)");
            resp.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, m.Success ? int.Parse(m.Groups[1].Value) + 1 : 3)));
        }
    }

    // Надсилає HTML-повідомлення; повертає його id (для редагування) або null.
    async Task<long?> Send(long chat, string text, object? markup = null, bool silent = false)
    {
        var resp = await Call("sendMessage", markup is null
            ? new { chat_id = chat, text, parse_mode = "HTML", disable_web_page_preview = true, disable_notification = silent }
            : (object)new { chat_id = chat, text, parse_mode = "HTML", disable_web_page_preview = true, disable_notification = silent, reply_markup = markup });
        if ((int)resp.StatusCode == 403) { _users.Remove(chat); SaveSubs(); return null; } // заблокував бота
        if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"⚠ bot send: {await resp.Content.ReadAsStringAsync()}"); return null; }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("result").GetProperty("message_id").GetInt64();
    }

    // Редагує текст повідомлення. «Не змінилось» теж успіх.
    async Task<bool> Edit(long chat, long msg, string text, object? markup = null)
    {
        var resp = await Call("editMessageText", markup is null
            ? new { chat_id = chat, message_id = msg, text, parse_mode = "HTML", disable_web_page_preview = true }
            : (object)new { chat_id = chat, message_id = msg, text, parse_mode = "HTML", disable_web_page_preview = true, reply_markup = markup });
        return resp.IsSuccessStatusCode || (await resp.Content.ReadAsStringAsync()).Contains("not modified");
    }

    async Task EditMarkup(long chat, long msg, object markup)
    {
        try { await Call("editMessageReplyMarkup", new { chat_id = chat, message_id = msg, reply_markup = markup }); } catch { }
    }

    async Task Delete(long chat, long msg)
    {
        try { await Call("deleteMessage", new { chat_id = chat, message_id = msg }); } catch { }
    }

    // «друкує… / надсилає фото…» замість зайвих «⏳ Зачекай» повідомлень.
    async Task Action(long chat, string action)
    {
        try { await Call("sendChatAction", new { chat_id = chat, action }); } catch { }
    }

    async Task AnswerCallback(string cbId, string text)
    {
        try { await Call("answerCallbackQuery", new { callback_query_id = cbId, text }); } catch { }
    }

    object TrackBtn(string id, string label)
    {
        var key = "trk:" + ((uint)id.GetHashCode()).ToString("x");
        _trackKeys[key] = id;
        return Btn(label, key);
    }

    async Task HandleCallback(long chat, string data, string cbId, long msg)
    {
        var p = _users.TryGetValue(chat, out var up) ? up : null;
        if (data.StartsWith("cmd:"))
        {
            await AnswerCallback(cbId, "");
            await Handle(chat, data[4..]);
            return;
        }
        if (data.StartsWith("rad:") && int.TryParse(data[4..], out var rkm))
        {
            await AnswerCallback(cbId, $"📏 {Math.Clamp(rkm, RadiusMin, RadiusMax)} км");
            await SetRadius(chat, rkm, p, msg);
            return;
        }
        if (data.StartsWith("city:"))
        {
            await AnswerCallback(cbId, "");
            await SetPlaceByName(chat, data[5..]);
            return;
        }
        if (data.StartsWith("geo:"))
        {
            if (_geoPending.TryGetValue(chat, out var hits) && int.TryParse(data[4..], out var gi) && gi >= 0 && gi < hits.Count)
            {
                _geoPending.Remove(chat);
                await AnswerCallback(cbId, "");
                await EditMarkup(chat, msg, NoKb);
                await SetPlace(chat, new Place(hits[gi].Label, hits[gi].Lat, hits[gi].Lon));
            }
            else await AnswerCallback(cbId, "Список застарів — напиши адресу ще раз");
            return;
        }
        if (data.StartsWith("upd:"))
        {
            if (p is null) { await AnswerCallback(cbId, "Спершу обери місто 📍"); return; }
            var (text, kb) = data switch
            {
                "upd:rk" => (RiskText(p), RiskKb),
                "upd:al" => (await _f.Alerts(p.Lat, p.Lon), AlertsKb),
                _        => (StatusText(p), StatusKb),
            };
            await Edit(chat, msg, text, kb);
            await AnswerCallback(cbId, $"🔄 Оновлено о {DateTime.Now:HH:mm}");
            return;
        }
        if (data.StartsWith("trk:") && _trackKeys.TryGetValue(data, out var tid))
        {
            var info = p is null ? null : _f.TargetInfo?.Invoke(tid, p.Lat, p.Lon);
            if (info is not { } inf) { await AnswerCallback(cbId, "Ця ціль уже зникла з радарів"); return; }
            if (_tracking.TryGetValue(chat, out var old)) await EditMarkup(chat, old.msg, NoKb);
            await AnswerCallback(cbId, "👁 Стежу");
            var id = await Send(chat, "👁 <b>Стежу за ціллю</b> · оновлюється\n\n" + inf.text, TrackKb(chat, tid));
            if (id is { } m) _tracking[chat] = (tid, inf.text, inf.stage, m);
            return;
        }
        if (data == "untrk")
        {
            _tracking.Remove(chat);
            await EditMarkup(chat, msg, NoKb);
            await AnswerCallback(cbId, "⏹ Стеження вимкнено");
            return;
        }
        if (data is "boom_yes" or "boom_no")
        {
            LogBoom(chat, p, data == "boom_yes"); // у журнал (краудсорс реальних прильотів)
            await EditMarkup(chat, msg, NoKb);    // відповідь одна — кнопки прибираємо
            await AnswerCallback(cbId, data == "boom_yes" ? "Дякую! Бережи себе 🇺🇦" : "Дякую за відгук!");
            return;
        }
        await AnswerCallback(cbId, "");
    }

    void LogBoom(long chat, Place? p, bool heard)
    {
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var line = $"{DateTime.Now:yyyy-MM-dd},{DateTime.Now:HH:mm:ss},{chat}," +
                       $"{p?.Name},{(p != null ? p.Lat.ToString(inv) : "")},{(p != null ? p.Lon.ToString(inv) : "")}," +
                       $"{(heard ? "boom" : "quiet")}";
            File.AppendAllText("boom_feedback.csv", line + "\n");
        }
        catch { }
    }

    async Task SendPhoto(long chat, byte[] png, string caption, object? markup = null)
    {
        // Підпис ≤1024: ріжемо на межі рядка, щоб не розірвати <b>…</b>.
        if (caption.Length > 1000) { var cut = caption.LastIndexOf('\n', 999); caption = cut > 0 ? caption[..cut] : caption[..1000]; }
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chat.ToString()), "chat_id" },
            { new StringContent(caption), "caption" },
            { new StringContent("HTML"), "parse_mode" },
        };
        var photo = new ByteArrayContent(png);
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(photo, "photo", "map.png");
        if (markup is not null) form.Add(new StringContent(JsonSerializer.Serialize(markup)), "reply_markup");
        await Http.PostAsync($"{_api}/sendPhoto", form);
    }

    async Task<JsonDocument> GetJson(string url, CancellationToken ct = default)
    {
        var resp = await Http.GetAsync(url, ct);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    // ── Підписники (chat → місто), локальний файл у .gitignore ────────────────
    void LoadSubs()
    {
        try
        {
            if (!File.Exists(SubsFile)) return;
            var json = File.ReadAllText(SubsFile).TrimStart();
            if (json.StartsWith("["))
            {
                // Старий формат: масив id — переносимо без міста.
                foreach (var id in JsonSerializer.Deserialize<long[]>(json) ?? Array.Empty<long>())
                    _users[id] = null;
                return;
            }
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!long.TryParse(p.Name, out var id)) continue;
                var v = p.Value;
                if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("r", out var rr) && rr.TryGetInt32(out var rv))
                    _radius[id] = Math.Clamp(rv, RadiusMin, RadiusMax);
                if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var nm))
                    _users[id] = new Place(nm.GetString() ?? "", v.GetProperty("lat").GetDouble(), v.GetProperty("lon").GetDouble());
                else
                    _users[id] = null;
            }
        }
        catch { }
    }

    readonly object _saveLock = new();
    void SaveSubs()
    {
        lock (_saveLock) try
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var (id, p) in _users)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(id).Append("\":");
                var r = _radius.TryGetValue(id, out var rk) ? rk : 0;
                if (p is null) sb.Append(r > 0 ? $"{{\"r\":{r}}}" : "null");
                else sb.Append($"{{\"name\":{JsonSerializer.Serialize(p.Name)},\"lat\":{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lon\":{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}" + (r > 0 ? $",\"r\":{r}" : "") + "}");
            }
            sb.Append('}');
            File.WriteAllText(SubsFile, sb.ToString());
        }
        catch { }
    }
}

// Бот обробляє команди й фонові тривоги паралельно — словники користувачів спільні.
static class ConcurrentDictExt
{
    public static bool Remove<TK, TV>(this ConcurrentDictionary<TK, TV> d, TK key) where TK : notnull => d.TryRemove(key, out _);
}
