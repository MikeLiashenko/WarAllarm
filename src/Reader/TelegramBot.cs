using System.Text;
using System.Text.Json;

// ── WarAlarm · Telegram-бот ──────────────────────────────────────────────────
// Повноцінний застосунок у боті: підписка, авто-тривоги, і всі функції додатку —
// статус, карта КАРТИНКОЮ (/map), ймовірність, місця, ШІ-аналіз, аналітика,
// офіційні тривоги. Кнопки-меню знизу. Токен — secrets.local.txt
// (telegram_bot_token=...). Це БОТ (Bot API), окремий від акаунта-читача каналів.

// Набір функцій застосунку, які Program передає боту (щоб бот не знав деталей ядра).
public sealed record BotFeatures(
    Func<string> Status,
    Func<byte[]?> MapPng,
    Func<string> Risk,
    Func<string> Places,
    Func<Task<string>> Ai,
    Func<string> Stats,
    Func<Task<string>> Alerts);

public sealed class TelegramBot : INotifier
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    const string SubsFile = "bot_subscribers.json";

    readonly string _api;
    readonly BotFeatures _f;
    readonly HashSet<long> _subs = new();
    long _offset;

    public TelegramBot(string token, BotFeatures features)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _f = features;
        LoadSubs();
    }

    public int SubscriberCount => _subs.Count;

    // Кнопки-меню знизу (як вкладки застосунку).
    static readonly object Keyboard = new
    {
        keyboard = new[]
        {
            new[] { "📊 Статус", "🗺 Карта" },
            new[] { "🎯 Ризик", "📍 Місця" },
            new[] { "🤖 ШІ", "📈 Аналітика" },
            new[] { "🚨 Тривоги", "❓ Довідка" },
        },
        resize_keyboard = true,
    };

    // ── INotifier: розсилка тривоги/відбою всім підписникам ──────────────────
    public async Task Notify(AlarmInfo a)
    {
        if (_subs.Count == 0) return;
        string msg = a.Clear
            ? $"✅ ВІДБІЙ — можна виходити з укриття.\n{a.Headline}\n🕑 {a.Time}"
            : $"🔴 ПОВІТРЯНА ТРИВОГА\n{a.Headline}\n➤ В укриття! Подалі від вікон, правило двох стін.\n🕑 {a.Time} · {a.Source}";
        foreach (var chat in _subs.ToArray())
            try { await Send(chat, msg); } catch { /* один невдалий чат не спиняє інших */ }
        // На тривозі — одразу й свіжа карта.
        if (!a.Clear)
        {
            byte[]? png = null;
            try { png = _f.MapPng(); } catch { }
            if (png is not null)
                foreach (var chat in _subs.ToArray())
                    try { await SendPhoto(chat, png, "🗺 Карта загроз зараз"); } catch { }
        }
    }

    // ── Фоновий цикл: тягне команди через long-polling ───────────────────────
    public async Task RunAsync(CancellationToken ct = default)
    {
        try { await InitOffset(); } catch { /* мережа — спробуємо в циклі */ }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var (updateId, chat, text) in await GetUpdates(ct))
                {
                    _offset = updateId + 1;
                    await Handle(chat, text.Trim());
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

    async Task Handle(long chat, string text)
    {
        // Нормалізуємо: і слеш-команди, і текст кнопок.
        var cmd = text.Split(' ', '\n')[0].Split('@')[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/start":
                var isNew = _subs.Add(chat);
                SaveSubs();
                await Send(chat,
                    "🛡 Вітаю у WarAlarm!\n\n"
                    + (isNew ? "Ти підписаний — я сповіщу, коли поруч буде загроза.\n\n" : "Ти вже підписаний.\n\n")
                    + "Користуйся кнопками знизу або командами:\n"
                    + "/status /map /risk /places /ai /stats /alerts /stop",
                    withKeyboard: true);
                await Send(chat, _f.Status());
                return;

            case "/stop":
                _subs.Remove(chat);
                SaveSubs();
                await Send(chat, "Відписано. Напиши /start, щоб знову отримувати тривоги.");
                return;

            case "/help":
                await Send(chat,
                    "WarAlarm — тривоги повітряної небезпеки.\n\n"
                    + "📊 Статус — поточні цілі + ймовірність\n"
                    + "🗺 Карта — карта загроз картинкою\n"
                    + "🎯 Ризик — детальна ймовірність по типах\n"
                    + "📍 Місця — ризик по твоїх місцях\n"
                    + "🤖 ШІ — розумний аналіз обстановки\n"
                    + "📈 Аналітика — статистика тривог\n"
                    + "🚨 Тривоги — офіційні тривоги по областях\n"
                    + "/stop — відписатися\n\n"
                    + "Бот сам надсилає сповіщення при загрозі поруч.",
                    withKeyboard: true);
                return;
        }

        // Кнопки / слова.
        if (Match(text, cmd, "/status", "📊", "статус")) { await Send(chat, _f.Status()); return; }
        if (Match(text, cmd, "/risk", "🎯", "ризик"))    { await Send(chat, _f.Risk()); return; }
        if (Match(text, cmd, "/places", "📍", "місця"))  { await Send(chat, _f.Places()); return; }
        if (Match(text, cmd, "/stats", "📈", "аналітика")) { await Send(chat, _f.Stats()); return; }
        if (Match(text, cmd, "/help", "❓", "довідка"))  { await Handle(chat, "/help"); return; }

        if (Match(text, cmd, "/map", "🗺", "карта"))
        {
            await Send(chat, "🗺 Малюю карту…");
            byte[]? png = null;
            try { png = _f.MapPng(); } catch (Exception ex) { Console.Error.WriteLine("map: " + ex.Message); }
            if (png is not null) await SendPhoto(chat, png, "🗺 Карта загроз зараз");
            else await Send(chat, "Не вдалося намалювати карту.");
            return;
        }

        if (Match(text, cmd, "/ai", "🤖", "ші", "ии"))
        {
            await Send(chat, "🤖 Аналізую обстановку…");
            string reply;
            try { reply = await _f.Ai(); } catch (Exception ex) { reply = "ІІ недоступний: " + ex.Message; }
            await Send(chat, reply);
            return;
        }

        if (Match(text, cmd, "/alerts", "🚨", "тривоги"))
        {
            await Send(chat, "🚨 Перевіряю офіційні тривоги…");
            string reply;
            try { reply = await _f.Alerts(); } catch (Exception ex) { reply = "Не вдалося отримати: " + ex.Message; }
            await Send(chat, reply);
            return;
        }

        await Send(chat, "Не зрозумів. Тисни кнопки знизу або /help.", withKeyboard: true);
    }

    static bool Match(string text, string cmd, params string[] keys)
    {
        var low = text.ToLowerInvariant();
        foreach (var k in keys)
            if (cmd == k || low.Contains(k)) return true;
        return false;
    }

    // ── Bot API ──────────────────────────────────────────────────────────────
    async Task InitOffset()
    {
        using var doc = await GetJson($"{_api}/getUpdates?timeout=0");
        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
            _offset = Math.Max(_offset, u.GetProperty("update_id").GetInt64() + 1);
    }

    async Task<List<(long updateId, long chat, string text)>> GetUpdates(CancellationToken ct)
    {
        var outp = new List<(long, long, string)>();
        using var doc = await GetJson(
            $"{_api}/getUpdates?timeout=25&offset={_offset}&allowed_updates=[\"message\"]", ct);
        foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var id = u.GetProperty("update_id").GetInt64();
            if (!u.TryGetProperty("message", out var m) ||
                !m.TryGetProperty("text", out var t) ||
                !m.TryGetProperty("chat", out var c))
            { _offset = Math.Max(_offset, id + 1); continue; }
            outp.Add((id, c.GetProperty("id").GetInt64(), t.GetString() ?? ""));
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
        if (!resp.IsSuccessStatusCode && (int)resp.StatusCode == 403 && _subs.Remove(chat)) SaveSubs();
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
        var body = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(body);
    }

    // ── Підписники (локальний файл, у .gitignore) ────────────────────────────
    void LoadSubs()
    {
        try
        {
            if (!File.Exists(SubsFile)) return;
            var ids = JsonSerializer.Deserialize<long[]>(File.ReadAllText(SubsFile));
            if (ids != null) foreach (var id in ids) _subs.Add(id);
        }
        catch { /* немає файлу — почнемо з нуля */ }
    }

    void SaveSubs()
    {
        try { File.WriteAllText(SubsFile, JsonSerializer.Serialize(_subs.ToArray())); }
        catch { /* не критично */ }
    }
}
