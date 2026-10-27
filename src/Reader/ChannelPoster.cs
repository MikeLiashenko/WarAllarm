using System.Text;
using System.Text.Json;

// ── WarAlarm · постер новинного каналу ───────────────────────────────────────
// Окремий бот (свій токен від @BotFather), доданий адміном у канал. Публікує
// новини про офіційні тривоги по Україні + карту. Токен і канал — з
// secrets.local.txt: telegram_channel_bot_token=... , telegram_channel=@name
//
// Проти спаму: Post повертає id повідомлення, щоб «живі» пости (дошка цілей, карта)
// РЕДАГУВАТИ, а не постити заново; silent=true — без звуку (рутинні оновлення);
// той самий текст підряд (≤10 хв) не публікуємо вдруге.
public sealed class ChannelPoster
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    readonly string _api;
    readonly string _channel;
    string _lastText = "";
    DateTime _lastAt = DateTime.MinValue;

    public ChannelPoster(string token, string channel)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _channel = channel;
    }

    /// id найсвіжішого поста в каналі (щоб знати, чи «живий» пост уже засипало іншими).
    public long? LastId { get; private set; }

    public async Task<long?> Post(string text, byte[]? png, bool silent = false)
    {
        if (text == _lastText && DateTime.Now - _lastAt < TimeSpan.FromMinutes(10)) return LastId; // дубль
        try
        {
            var id = png is not null ? await SendPhoto(text, png, silent) : await SendText(text, silent);
            if (id is not null) { LastId = id; _lastText = text; _lastAt = DateTime.Now; }
            return id;
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ channel: " + ex.Message); return null; }
    }

    /// Редагує текст поста (photo=true — підпис під картою). false — не вдалося (пост видалено тощо).
    public async Task<bool> Edit(long id, string text, bool photo = false)
    {
        try
        {
            object payload = photo
                ? new { chat_id = _channel, message_id = id, caption = Trim(text), parse_mode = "HTML" }
                : new { chat_id = _channel, message_id = id, text, parse_mode = "HTML", disable_web_page_preview = true };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{_api}/{(photo ? "editMessageCaption" : "editMessageText")}", content);
            return resp.IsSuccessStatusCode || (await resp.Content.ReadAsStringAsync()).Contains("not modified");
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ channel edit: " + ex.Message); return false; }
    }

    /// Замінює картинку й підпис поста-карти (оновлена карта без нового поста).
    public async Task<bool> EditPhoto(long id, byte[] png, string caption)
    {
        try
        {
            var media = JsonSerializer.Serialize(new { type = "photo", media = "attach://map", caption = Trim(caption), parse_mode = "HTML" });
            using var form = new MultipartFormDataContent
            {
                { new StringContent(_channel), "chat_id" },
                { new StringContent(id.ToString()), "message_id" },
                { new StringContent(media), "media" },
            };
            var photo = new ByteArrayContent(png);
            photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(photo, "map", "map.png");
            var resp = await Http.PostAsync($"{_api}/editMessageMedia", form);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ channel map edit: " + ex.Message); return false; }
    }

    async Task<long?> SendText(string text, bool silent)
    {
        var payload = JsonSerializer.Serialize(new { chat_id = _channel, text, parse_mode = "HTML", disable_web_page_preview = true, disable_notification = silent });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        return await MessageId(await Http.PostAsync($"{_api}/sendMessage", content));
    }

    // Одним постом: карта з підписом (Telegram обмежує підпис ~1024 симв.).
    async Task<long?> SendPhoto(string caption, byte[] png, bool silent)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(_channel), "chat_id" },
            { new StringContent(Trim(caption)), "caption" },
            { new StringContent("HTML"), "parse_mode" },
            { new StringContent(silent ? "true" : "false"), "disable_notification" },
        };
        var photo = new ByteArrayContent(png);
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(photo, "photo", "map.png");
        return await MessageId(await Http.PostAsync($"{_api}/sendPhoto", form));
    }

    // Обрізаємо на межі рядка (наші HTML-теги не переносяться), щоб не розрізати <b>…</b>.
    static string Trim(string caption)
    {
        if (caption.Length <= 1000) return caption;
        var cut = caption.LastIndexOf('\n', 999);
        return cut > 0 ? caption[..cut] : caption[..1000];
    }

    static async Task<long?> MessageId(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"⚠ channel: {(int)resp.StatusCode} {body}");
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("message_id", out var id)
            ? id.GetInt64() : null;
    }
}
