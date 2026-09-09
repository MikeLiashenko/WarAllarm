using System.Text;
using System.Text.Json;

// ── WarAlarm · Telegram-бот ──────────────────────────────────────────────────
// Особистий бот тривог: люди пишуть /start і підписуються, бот сам надсилає
// сповіщення, коли поруч із домом зʼявляється загроза, і відповідає на /status
// (поточні цілі + ймовірність удару). Токен — з secrets.local.txt
// (telegram_bot_token=...). Це БОТ (Bot API), окремий від акаунта-читача каналів.
public sealed class TelegramBot : INotifier
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    const string SubsFile = "bot_subscribers.json";

    readonly string _api;
    readonly Func<string> _buildStatus;
    readonly HashSet<long> _subs = new();
    long _offset;

    public TelegramBot(string token, Func<string> buildStatus)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _buildStatus = buildStatus;
        LoadSubs();
    }

    public int SubscriberCount => _subs.Count;

    // ── INotifier: розсилка тривоги/відбою всім підписникам ──────────────────
    public async Task Notify(AlarmInfo a)
    {
        if (_subs.Count == 0) return;
        string msg = a.Clear
            ? $"✅ ВІДБІЙ — можна виходити з укриття.\n{a.Headline}\n🕑 {a.Time}"
            : $"🔴 ПОВІТРЯНА ТРИВОГА\n{a.Headline}\n➤ В укриття! Подалі від вікон, правило двох стін.\n🕑 {a.Time} · {a.Source}";
        foreach (var chat in _subs.ToArray())
            try { await Send(chat, msg); } catch { /* один невдалий чат не спиняє інших */ }
    }

    // ── Фоновий цикл: тягне команди через long-polling ───────────────────────
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Пропускаємо backlog, щоб не відповідати на старі команди після перезапуску.
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
        // /start@BotName → беремо тільки команду.
        var cmd = text.Split(' ', '\n')[0].Split('@')[0].ToLowerInvariant();
        switch (cmd)
        {
            case "/start":
                var isNew = _subs.Add(chat);
                SaveSubs();
                await Send(chat,
                    "🛡 Вітаю у WarAlarm!\n\n"
                    + (isNew ? "Ти підписаний — я сповіщу, коли поруч буде загроза.\n\n" : "Ти вже підписаний.\n\n")
                    + "Команди:\n/status — поточна обстановка й ймовірність\n"
                    + "/stop — відписатися\n/help — довідка");
                await Send(chat, _buildStatus());
                break;

            case "/stop":
                _subs.Remove(chat);
                SaveSubs();
                await Send(chat, "Відписано. Напиши /start, щоб знову отримувати тривоги.");
                break;

            case "/status":
            case "/risk":
                await Send(chat, _buildStatus());
                break;

            case "/help":
                await Send(chat,
                    "WarAlarm — тривоги повітряної небезпеки.\n\n"
                    + "/start — підписатися\n/status — поточні цілі + ймовірність удару\n"
                    + "/stop — відписатися\n\nБот сам надсилає сповіщення при загрозі поруч.");
                break;

            default:
                await Send(chat, "Не зрозумів. Напиши /status або /help.");
                break;
        }
    }

    // ── Bot API ──────────────────────────────────────────────────────────────
    async Task InitOffset()
    {
        using var doc = await GetJson($"{_api}/getUpdates?timeout=0");
        var arr = doc.RootElement.GetProperty("result");
        foreach (var u in arr.EnumerateArray())
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
            if (!u.TryGetProperty("message", out var m)) { _offset = Math.Max(_offset, id + 1); continue; }
            if (!m.TryGetProperty("text", out var t) || !m.TryGetProperty("chat", out var c)) { _offset = Math.Max(_offset, id + 1); continue; }
            outp.Add((id, c.GetProperty("id").GetInt64(), t.GetString() ?? ""));
        }
        return outp;
    }

    async Task Send(long chat, string text)
    {
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = chat,
            text,
            disable_web_page_preview = true,
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync($"{_api}/sendMessage", content);
        if (!resp.IsSuccessStatusCode)
        {
            // 403 = користувач заблокував бота → прибираємо з підписок.
            if ((int)resp.StatusCode == 403 && _subs.Remove(chat)) SaveSubs();
        }
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
