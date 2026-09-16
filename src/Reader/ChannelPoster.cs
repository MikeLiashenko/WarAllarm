using System.Text;
using System.Text.Json;

// ── WarAlarm · постер новинного каналу ───────────────────────────────────────
// Окремий бот (свій токен від @BotFather), доданий адміном у канал. Публікує
// новини про офіційні тривоги по Україні + карту. Токен і канал — з
// secrets.local.txt: telegram_channel_bot_token=... , telegram_channel=@name
public sealed class ChannelPoster
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    readonly string _api;
    readonly string _channel;

    public ChannelPoster(string token, string channel)
    {
        _api = $"https://api.telegram.org/bot{token}";
        _channel = channel;
    }

    public async Task Post(string text, byte[]? png)
    {
        try
        {
            if (png is not null) await SendPhoto(text, png);
            else await SendText(text);
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ channel: " + ex.Message); }
    }

    async Task SendText(string text)
    {
        var payload = JsonSerializer.Serialize(new { chat_id = _channel, text, parse_mode = "HTML", disable_web_page_preview = true });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await Http.PostAsync($"{_api}/sendMessage", content);
    }

    // Одним постом: карта з підписом (Telegram обмежує підпис ~1024 симв.).
    async Task SendPhoto(string caption, byte[] png)
    {
        // Обрізаємо на межі рядка (наші HTML-теги не переносяться), щоб не розрізати <b>…</b>.
        if (caption.Length > 1000)
        {
            var cut = caption.LastIndexOf('\n', Math.Min(1000, caption.Length - 1));
            caption = cut > 0 ? caption[..cut] : caption[..1000];
        }
        using var form = new MultipartFormDataContent
        {
            { new StringContent(_channel), "chat_id" },
            { new StringContent(caption), "caption" },
            { new StringContent("HTML"), "parse_mode" },
        };
        var photo = new ByteArrayContent(png);
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(photo, "photo", "map.png");
        await Http.PostAsync($"{_api}/sendPhoto", form);
    }
}
