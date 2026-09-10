using System.Text;
using System.Text.Json;

// ── WarAlarm · Telegram-бот (персональний) ───────────────────────────────────
// Кожен користувач задає СВОЄ місто (пошук по великому списку ~5330 міст, як у
// застосунку). Далі /status, /risk і тривоги — по його місту. Бот сам шле
// сповіщення в особисті, коли загроза поруч із містом користувача.
// Токен — secrets.local.txt (telegram_bot_token=...). Окремий від каналу.

// Що вміє ядро (Program передає): усе відносно точки (lat,lon).
public sealed record BotFeatures(
    Func<double, double, string> StatusAt,
    Func<double, double, string> RiskAt,
    Func<double, double, (bool alarm, string text)> AlarmAt,
    Func<Task<byte[]?>> MapPng,
    Func<Task<string>> Ai,
    Func<string> Stats,
    Func<Task<string>> Alerts);

public sealed class TelegramBot
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    const string SubsFile = "bot_subscribers.json";

    sealed record Place(string Name, double Lat, double Lon);

    readonly string _api;
    readonly BotFeatures _f;
    readonly Dictionary<long, Place?> _users = new();      // chat → місто (null = ще не задано)
    readonly Dictionary<long, bool> _lastAlarm = new();    // для фронта тривоги
    long _offset;

    public TelegramBot(string token, BotFeatures features)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _f = features;
        LoadSubs();
    }

    public int SubscriberCount => _users.Count;

    static readonly object Keyboard = new
    {
        keyboard = new[]
        {
            new[] { "📊 Статус", "🗺 Карта" },
            new[] { "🎯 Ризик", "🏠 Укриття" },
            new[] { "📍 Моє місто", "📈 Аналітика" },
            new[] { "🤖 ШІ", "🚨 Тривоги" },
            new[] { "❓ Довідка" },
        },
        resize_keyboard = true,
    };

    // Inline-кнопки під тривогою: «чи було чути вибух?» (краудсорс точності).
    static readonly object BoomKb = new
    {
        inline_keyboard = new[] { new object[]
        {
            new { text = "💥 Чув вибух", callback_data = "boom_yes" },
            new { text = "🤫 Тихо",      callback_data = "boom_no" },
        }}
    };

    // ── Фонові цикли ──────────────────────────────────────────────────────────
    public async Task RunAsync(CancellationToken ct = default)
    {
        try { await InitOffset(); } catch { }
        _ = RunUserAlarms(ct);                 // персональні тривоги в особисті
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var u in await GetUpdates(ct))
                {
                    _offset = u.id + 1;
                    if (u.cbData != null) await HandleCallback(u.chat, u.cbData, u.cbId!);
                    else if (u.text != null) await Handle(u.chat, u.text.Trim());
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
                if (alarm && !last)
                    try { await SendInline(chat,
                        $"🔴 ТРИВОГА поруч — {place.Name}!\n{text}\n➤ В укриття! Подалі від вікон, правило двох стін.\n\nЯк мине — познач, чи було чути вибух:",
                        BoomKb); } catch { }
                else if (!alarm && last)
                    try { await Send(chat, $"🟢 Відбій — {place.Name}. Наразі поруч чисто."); } catch { }
                _lastAlarm[chat] = alarm;
            }
        }
    }

    // ── Команди ───────────────────────────────────────────────────────────────
    async Task Handle(long chat, string text)
    {
        var cmd = text.Split(' ', '\n')[0].Split('@')[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/start":
                bool isNew = !_users.ContainsKey(chat);
                if (isNew) _users[chat] = null;
                SaveSubs();
                await Send(chat,
                    "🛡 Вітаю у WarAlarm!\n\n"
                    + "Напиши назву свого міста (напр. «Полтава» або «Бровари») — і я "
                    + "сповіщатиму, коли загроза буде поруч, та показуватиму ризик саме по ньому.",
                    withKeyboard: true);
                return;

            case "/stop":
                _users.Remove(chat); _lastAlarm.Remove(chat);
                SaveSubs();
                await Send(chat, "Відписано. /start — щоб знову.");
                return;

            case "/help":
                await Send(chat,
                    "WarAlarm — тривоги повітряної небезпеки по ТВОЄМУ місту.\n\n"
                    + "📍 Моє місто — задати/змінити місто\n"
                    + "📊 Статус — цілі + ймовірність по твоєму місту\n"
                    + "🎯 Ризик — детально по типах\n🏠 Укриття — найближчі укриття/метро\n🗺 Карта — карта України\n"
                    + "🚨 Тривоги — офіційні по областях\n📈 Аналітика — статистика\n"
                    + "🤖 ШІ — розумний аналіз\n/stop — відписатися",
                    withKeyboard: true);
                return;
        }

        // Кнопки-«вкладки».
        if (Match(text, cmd, "/map", "🗺", "карта"))
        {
            await Send(chat, "🗺 Малюю карту…");
            var png = await _f.MapPng();
            if (png != null) await SendPhoto(chat, png, "🗺 Карта тривог"); else await Send(chat, "Не вдалося.");
            return;
        }
        if (Match(text, cmd, "/stats", "📈", "аналітика")) { await Send(chat, _f.Stats()); return; }
        if (Match(text, cmd, "/alerts", "🚨", "тривоги")) { await Send(chat, "🚨 Перевіряю…"); await Send(chat, await _f.Alerts()); return; }
        if (Match(text, cmd, "/ai", "🤖", "ші", "ии")) { await Send(chat, "🤖 Аналізую…"); await Send(chat, await _f.Ai()); return; }
        if (Match(text, cmd, "/place", "📍", "моє місто", "мое место", "місто", "город"))
        {
            await Send(chat, "Напиши назву свого міста (напр. «Львів», «Ірпінь», «Ізюм»):");
            return;
        }

        bool hasCity = _users.TryGetValue(chat, out var pl) && pl is not null;
        if (Match(text, cmd, "/status", "📊", "статус"))
        {
            if (!hasCity) { await Send(chat, "Спершу напиши назву свого міста 📍"); return; }
            await Send(chat, $"📍 {pl!.Name}\n" + _f.StatusAt(pl.Lat, pl.Lon));
            return;
        }
        if (Match(text, cmd, "/risk", "🎯", "ризик"))
        {
            if (!hasCity) { await Send(chat, "Спершу напиши назву свого міста 📍"); return; }
            await Send(chat, $"📍 {pl!.Name}\n" + _f.RiskAt(pl.Lat, pl.Lon));
            return;
        }
        if (Match(text, cmd, "/shelter", "🏠", "укриття", "укрытие"))
        {
            if (!hasCity) { await Send(chat, "Спершу напиши назву свого міста 📍"); return; }
            await Send(chat, "🏠 Шукаю найближчі укриття…");
            await Send(chat, await Shelters.Near(pl!.Lat, pl.Lon));
            return;
        }

        // Інакше — вважаємо це назвою міста → шукаємо у великому списку.
        var hit = Cities.Find(text);
        if (hit is { } c)
        {
            _users[chat] = new Place(c.name, c.lat, c.lon);
            _lastAlarm.Remove(chat);
            SaveSubs();
            await Send(chat, $"✅ Твоє місто: {c.name}.\nТепер /status і тривоги — по ньому. Змінити — просто напиши іншу назву.", withKeyboard: true);
            return;
        }
        var sug = Cities.Suggest(text);
        await Send(chat, sug.Count > 0
            ? "Не знайшов точно. Може, одне з цих?\n" + string.Join("\n", sug)
            : "Не знайшов такого міста. Спробуй іншу назву (українською).");
    }

    static bool Match(string text, string cmd, params string[] keys)
    {
        var low = text.ToLowerInvariant();
        foreach (var k in keys) if (cmd == k || low.Contains(k)) return true;
        return false;
    }

    // ── Bot API ──────────────────────────────────────────────────────────────
    async Task InitOffset()
    {
        using var doc = await GetJson($"{_api}/getUpdates?timeout=0");
        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
            _offset = Math.Max(_offset, u.GetProperty("update_id").GetInt64() + 1);
    }

    async Task<List<(long id, long chat, string? text, string? cbData, string? cbId)>> GetUpdates(CancellationToken ct)
    {
        var outp = new List<(long, long, string?, string?, string?)>();
        using var doc = await GetJson($"{_api}/getUpdates?timeout=25&offset={_offset}&allowed_updates=[\"message\",\"callback_query\"]", ct);
        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var id = u.GetProperty("update_id").GetInt64();
            if (u.TryGetProperty("callback_query", out var cq) &&
                cq.TryGetProperty("message", out var cm) && cm.TryGetProperty("chat", out var cch))
            {
                outp.Add((id, cch.GetProperty("id").GetInt64(), null,
                    cq.TryGetProperty("data", out var d) ? d.GetString() : "",
                    cq.GetProperty("id").GetString()));
            }
            else if (u.TryGetProperty("message", out var m) &&
                     m.TryGetProperty("text", out var t) && m.TryGetProperty("chat", out var ch))
            {
                outp.Add((id, ch.GetProperty("id").GetInt64(), t.GetString() ?? "", null, null));
            }
            else { _offset = Math.Max(_offset, id + 1); }
        }
        return outp;
    }

    async Task Send(long chat, string text, bool withKeyboard = false)
    {
        object payload = withKeyboard
            ? new { chat_id = chat, text, disable_web_page_preview = true, reply_markup = Keyboard }
            : new { chat_id = chat, text, disable_web_page_preview = true };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync($"{_api}/sendMessage", content);
        if (!resp.IsSuccessStatusCode && (int)resp.StatusCode == 403) { _users.Remove(chat); SaveSubs(); }
    }

    async Task SendInline(long chat, string text, object markup)
    {
        var payload = JsonSerializer.Serialize(new { chat_id = chat, text, disable_web_page_preview = true, reply_markup = markup });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await Http.PostAsync($"{_api}/sendMessage", content);
    }

    async Task AnswerCallback(string cbId, string text)
    {
        var payload = JsonSerializer.Serialize(new { callback_query_id = cbId, text });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        try { await Http.PostAsync($"{_api}/answerCallbackQuery", content); } catch { }
    }

    // Натиснули «чи було чути вибух?» → пишемо у журнал (краудсорс реальних прильотів).
    async Task HandleCallback(long chat, string data, string cbId)
    {
        if (data is "boom_yes" or "boom_no")
        {
            var p = _users.TryGetValue(chat, out var pl) ? pl : null;
            LogBoom(chat, p, data == "boom_yes");
            await AnswerCallback(cbId, data == "boom_yes" ? "Дякую! Бережи себе 🇺🇦" : "Дякую за відгук!");
        }
        else await AnswerCallback(cbId, "");
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
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chat.ToString()), "chat_id" },
            { new StringContent(caption), "caption" },
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
