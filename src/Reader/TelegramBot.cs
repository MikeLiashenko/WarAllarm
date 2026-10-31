using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ── WarAlarm · Telegram-бот (персональний) ───────────────────────────────────
// Кожен користувач задає СВОЄ місто (пошук по великому списку ~5330 міст, як у
// застосунку, кнопкою або геолокацією). Далі статус, ризик, карта, тривоги — по його
// місту. Бот сам шле сповіщення в особисті, коли загроза поруч із містом користувача.
// Токен — secrets.local.txt (telegram_bot_token=...). Окремий від каналу.
//
// Усі відповіді — Telegram HTML (parse_mode=HTML): тексти від Program уже екрановані.
// Проти спаму: «🔄 Оновити» і стеження за ціллю РЕДАГУЮТЬ повідомлення, а не шлють нові.

// Що вміє ядро (Program передає): усе відносно точки (lat,lon). Тексти — HTML.
public sealed record BotFeatures(
    Func<double, double, string> StatusAt,
    Func<double, double, string> RiskAt,
    Func<double, double, (bool alarm, string text)> AlarmAt, // text — простий текст («Шахед ~12 км»)
    Func<double, double, string, Task<byte[]?>> MapPng,      // карта з містом користувача
    Func<Task<string>> Ai,
    Func<string> Stats,
    Func<double, double, Task<string>> Alerts,              // офіційні тривоги: твоя область + країна
    Func<Task<(byte[] png, string caption)>>? Night = null, // 🌙 підсумок ночі
    Func<string>? Sources = null, // 📡 які канали що дали (SourceHub)
    Func<string?>? Outlook = null, // 🔮 чого чекати цієї ночі (null — оцінки ще нема)
    Func<double, double, int, List<(string id, string text)>>? TargetsNear = null, // 🛰 найближчі цілі (короткі рядки)
    Func<string, double, double, (string text, string stage)?>? TargetInfo = null); // 🛰 картка цілі (null — зникла)

public sealed class TelegramBot
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    const string SubsFile = "bot_subscribers.json";

    sealed record Place(string Name, double Lat, double Lon);
    sealed record Upd(long Id, long Chat, string? Text, string? CbData, string? CbId, long MsgId, double? Lat, double? Lon);

    readonly string _api;
    readonly BotFeatures _f;
    readonly Dictionary<long, Place?> _users = new();      // chat → місто (null = ще не задано)
    readonly Dictionary<long, bool> _lastAlarm = new();    // для фронта тривоги
    readonly Dictionary<long, DateTime> _calmSince = new(); // відбій — лише після ≥60 с тиші (без «тривога-відбій-тривога»)
    // 🛰 Стеження: chat → (id цілі, останній опис, стадія, повідомлення, яке редагуємо).
    // Ключі кнопок — короткі (callback_data ≤ 64 байти, а id цілі кирилицею довший).
    readonly Dictionary<long, (string id, string last, string stage, long msg)> _tracking = new();
    readonly Dictionary<string, string> _trackKeys = new();
    long _offset;

    public TelegramBot(string token, BotFeatures features)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _f = features;
        LoadSubs();
    }

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
        input_field_placeholder = "Назва міста або кнопка нижче",
    };

    static object Btn(string text, string data) => new { text, callback_data = data };
    static object Inline(params object[][] rows) => new { inline_keyboard = rows };

    static readonly object MoreKb = Inline(
        new[] { Btn("🔮 Що вночі", "cmd:/tonight"), Btn("🌙 Підсумок ночі", "cmd:/night") },
        new[] { Btn("🎯 Ризик", "cmd:/risk") },
        new[] { Btn("📈 Аналітика", "cmd:/stats"), Btn("📡 Джерела", "cmd:/sources") },
        new[] { Btn("🤖 ШІ-аналіз", "cmd:/ai"), Btn("📍 Змінити місто", "cmd:/place") },
        new[] { Btn("❓ Довідка", "cmd:/help") });

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
    static readonly object NoKb = new { inline_keyboard = Array.Empty<object[]>() };

    // ── Фонові цикли ──────────────────────────────────────────────────────────
    public async Task RunAsync(CancellationToken ct = default)
    {
        try { await InitOffset(); } catch { }
        try { await SetCommands(); } catch { }
        _ = RunUserAlarms(ct);                 // персональні тривоги в особисті
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var u in await GetUpdates(ct))
                {
                    _offset = u.Id + 1;
                    try
                    {
                        if (u.CbData != null) await HandleCallback(u.Chat, u.CbData, u.CbId!, u.MsgId);
                        else if (u.Lat is { } la && u.Lon is { } lo) await SetPlaceByLocation(u.Chat, la, lo);
                        else if (u.Text != null) await Handle(u.Chat, u.Text.Trim());
                    }
                    catch (Exception ex) { Console.Error.WriteLine("⚠ bot handle: " + ex.Message); }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine("⚠ bot: " + ex.Message);
                try { await Task.Delay(3000, ct); } catch { break; }
            }
        }
    }

    // Кожні ~10 с: для кожного користувача з містом — тривога поруч → сповіщення.
    async Task RunUserAlarms(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
            foreach (var (chat, place) in _users.ToArray())
            {
                if (place is null) continue;
                var (alarm, text) = _f.AlarmAt(place.Lat, place.Lon);
                bool last = _lastAlarm.TryGetValue(chat, out var l) && l;
                if (alarm) _calmSince.Remove(chat);
                if (alarm && !last)
                {
                    try { await Send(chat,
                        $"🔴 <b>ТРИВОГА поруч — {H(place.Name)}</b>\n"
                        + (text.Length > 0 ? $"🎯 {H(text)}\n" : "")
                        + "\n➤ <b>В укриття!</b> Подалі від вікон, правило двох стін.", AlarmKb); } catch { }
                    _lastAlarm[chat] = true;
                }
                else if (!alarm && last)
                {
                    // Ціль на межі 25 км «блимає» — відбій лише після хвилини тиші.
                    if (!_calmSince.TryGetValue(chat, out var since)) { _calmSince[chat] = DateTime.Now; continue; }
                    if (DateTime.Now - since < TimeSpan.FromSeconds(60)) continue;
                    _calmSince.Remove(chat);
                    try { await Send(chat,
                        $"🟢 <b>Відбій — {H(place.Name)}</b>\nНаразі поруч чисто.\n\nЧи було чути вибух?", BoomKb); } catch { }
                    _lastAlarm[chat] = false;
                }
            }
            // 🛰 Стеження: ціль рухається → РЕДАГУЄМО ту саму картку; змінилась стадія
            // (наближається → на підльоті → над районом) — нове повідомлення, щоб дзенькнуло.
            if (_f.TargetInfo is { } ti)
                foreach (var (chat, tr) in _tracking.ToArray())
                {
                    var pl = _users.TryGetValue(chat, out var up) ? up : null;
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
                            var id = await Send(chat, $"🛰 <b>Ціль: {H(inf.stage)}</b>\n\n" + inf.text, UntrackKb);
                            _tracking[chat] = (tr.id, inf.text, inf.stage, id ?? tr.msg);
                        }
                        else
                        {
                            await Edit(chat, tr.msg, "👁 <b>Стежу за ціллю</b> · оновлюється\n\n" + inf.text, UntrackKb);
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
        bool hasCity = _users.TryGetValue(chat, out var pl) && pl is not null;

        switch (cmd)
        {
            case "/start":
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
                    + "<b>Обери місто</b> 👇 кнопкою, надішли геолокацію 📍 або просто напиши назву (напр. «Бровари»).",
                    CityKb);
                await Send(chat, "📍 Або поділись геолокацією — так точніше:", LocationKb);
                return;

            case "/stop":
                _users.Remove(chat); _lastAlarm.Remove(chat); _tracking.Remove(chat); _calmSince.Remove(chat);
                SaveSubs();
                await Send(chat, "🔕 <b>Сповіщення вимкнено.</b>\nПовернутись — /start", new { remove_keyboard = true });
                return;
        }

        if (Match(text, cmd, "/more", "☰", "ще", "меню"))
        {
            await Send(chat, "☰ <b>Ще можливості</b>", MoreKb);
            return;
        }
        if (Match(text, cmd, "/help", "❓", "довідка", "допомога"))
        {
            await Send(chat, HelpText(pl), MainKb);
            return;
        }
        if (Match(text, cmd, "⬅️", "скасувати"))
        {
            await Send(chat, hasCity ? "👌 Гаразд." : "👌 Гаразд. Місто можна обрати будь-коли — просто напиши назву.", MainKb);
            return;
        }
        if (Match(text, cmd, "/place", "📍", "моє місто", "мое место", "місто", "город"))
        {
            await Send(chat,
                (hasCity ? $"📍 Зараз: <b>{H(pl!.Name)}</b>\n\n" : "")
                + "Напиши назву міста (напр. «Львів», «Ірпінь», «Ізюм») або обери 👇", CityKb);
            await Send(chat, "…чи поділись геолокацією:", LocationKb);
            return;
        }

        // Кнопки-«вкладки».
        if (Match(text, cmd, "/map", "🗺", "карта"))
        {
            await Action(chat, "upload_photo");
            var png = await _f.MapPng(pl?.Lat ?? 0, pl?.Lon ?? 0, pl?.Name ?? ""); // без міста — карта країни
            if (png != null) await SendPhoto(chat, png, $"🗺 <b>Карта загроз</b> · {DateTime.Now:HH:mm}\n🔴 тривога · 🟡 часткова · силуети — цілі за курсом");
            else await Send(chat, "😔 Карту зараз не вдалося намалювати. Спробуй за хвилину.");
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
        if (Match(text, cmd, "/stats", "📈", "аналітика")) { await Send(chat, _f.Stats()); return; }
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
            await Send(chat, "🤔 Не знаю такої команди. Ось що я вмію 👇\n\n" + HelpText(pl), MainKb);
            return;
        }
        // Інакше — вважаємо це назвою міста → шукаємо у великому списку.
        await SetPlaceByName(chat, text);
    }

    static string HelpText(Place? place) =>
        "❓ <b>Як користуватись WarAlarm</b>\n\n"
        + "<b>Головне</b>\n"
        + "📊 Статус — що летить біля твого міста\n"
        + "🛰 Цілі — найближчі цілі, за кожною можна 👁 стежити\n"
        + "🗺 Карта — карта тривог і цілей\n"
        + "🚨 Тривоги — офіційні: твоя область і країна\n"
        + "🏠 Укриття — найближчі укриття й метро\n\n"
        + "<b>☰ Ще</b>\n"
        + "🎯 Ризик · 🔮 Що вночі · 🌙 Підсумок ночі · 📈 Аналітика · 📡 Джерела · 🤖 ШІ-аналіз\n\n"
        + "<b>Налаштування</b>\n"
        + "📍 /place — змінити місто" + (place is { } p ? $" (зараз: {H(p.Name)})" : "") + "\n"
        + "🔕 /stop — вимкнути сповіщення\n\n"
        + "🔔 Сповіщення приходять самі: 🔴 тривога поруч → 🟢 відбій.\n"
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
        var hit = Cities.Find(text);
        if (hit is { } c) { await SetPlace(chat, new Place(c.name, c.lat, c.lon)); return; }
        var sug = Cities.Suggest(text).Where(n => Encoding.UTF8.GetByteCount("city:" + n) <= 64).Take(6).ToList();
        if (sug.Count > 0)
            await Send(chat, $"🤔 Не знайшов «{H(text)}» точно. Може, одне з цих?",
                new { inline_keyboard = sug.Chunk(2).Select(r => r.Select(n => Btn(n, "city:" + n)).ToArray()).ToArray() });
        else
            await Send(chat, $"🤔 Не знайшов міста «{H(text)}».\nСпробуй інакше — українською, напр. «Біла Церква» — або надішли геолокацію 📍");
    }

    async Task SetPlaceByLocation(long chat, double lat, double lon)
    {
        // Назва — найближчий населений пункт зі списку; координати — точні, як надіслав.
        var best = Cities.All.MinBy(c => ThreatGeo.Km((c.lat, c.lon), (lat, lon)));
        var km = ThreatGeo.Km((best.lat, best.lon), (lat, lon));
        await SetPlace(chat, new Place(km <= 15 ? best.name : $"біля {best.name}", lat, lon));
    }

    async Task SetPlace(long chat, Place p)
    {
        _users[chat] = p;
        _lastAlarm.Remove(chat);
        _calmSince.Remove(chat);
        SaveSubs();
        await Send(chat,
            $"✅ <b>Готово! Твоє місто — {H(p.Name)}</b>\n"
            + "🔔 Напишу, щойно загроза буде поруч, і дам відбій, коли мине.\n"
            + "Змінити місто — просто напиши іншу назву.", MainKb);
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
    async Task SetCommands()
    {
        var cmds = new (string c, string d)[]
        {
            ("status", "📊 Що летить біля мого міста"), ("targets", "🛰 Найближчі цілі"),
            ("map", "🗺 Карта тривог"), ("alerts", "🚨 Офіційні тривоги"), ("shelter", "🏠 Укриття поруч"),
            ("risk", "🎯 Ризик по типах"), ("tonight", "🔮 Чого чекати цієї ночі"), ("night", "🌙 Підсумок ночі"), ("stats", "📈 Аналітика"),
            ("sources", "📡 Джерела"), ("ai", "🤖 ШІ-аналіз"), ("place", "📍 Змінити місто"),
            ("help", "❓ Довідка"), ("stop", "🔕 Вимкнути сповіщення"),
        };
        await Call("setMyCommands", new { commands = cmds.Select(x => new { command = x.c, description = x.d }) });
    }

    async Task<List<Upd>> GetUpdates(CancellationToken ct)
    {
        var outp = new List<Upd>();
        using var doc = await GetJson($"{_api}/getUpdates?timeout=25&offset={_offset}&allowed_updates=[\"message\",\"callback_query\"]", ct);
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
            else if (u.TryGetProperty("message", out var m) && m.TryGetProperty("chat", out var ch)
                     && m.TryGetProperty("location", out var loc))
            {
                outp.Add(new Upd(id, ch.GetProperty("id").GetInt64(), null, null, null, 0,
                    loc.GetProperty("latitude").GetDouble(), loc.GetProperty("longitude").GetDouble()));
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
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return await Http.PostAsync($"{_api}/{method}", content);
    }

    // Надсилає HTML-повідомлення; повертає його id (для редагування) або null.
    async Task<long?> Send(long chat, string text, object? markup = null)
    {
        var resp = await Call("sendMessage", markup is null
            ? new { chat_id = chat, text, parse_mode = "HTML", disable_web_page_preview = true }
            : (object)new { chat_id = chat, text, parse_mode = "HTML", disable_web_page_preview = true, reply_markup = markup });
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
        if (data.StartsWith("city:"))
        {
            await AnswerCallback(cbId, "");
            await SetPlaceByName(chat, data[5..]);
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
            var id = await Send(chat, "👁 <b>Стежу за ціллю</b> · оновлюється\n\n" + inf.text, UntrackKb);
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

    async Task SendPhoto(long chat, byte[] png, string caption)
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
                if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("name", out var nm))
                    _users[id] = new Place(nm.GetString() ?? "", v.GetProperty("lat").GetDouble(), v.GetProperty("lon").GetDouble());
                else
                    _users[id] = null;
            }
        }
        catch { }
    }

    void SaveSubs()
    {
        try
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var (id, p) in _users)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(id).Append("\":");
                if (p is null) sb.Append("null");
                else sb.Append($"{{\"name\":{JsonSerializer.Serialize(p.Name)},\"lat\":{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lon\":{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
            }
            sb.Append('}');
            File.WriteAllText(SubsFile, sb.ToString());
        }
        catch { }
    }
}
