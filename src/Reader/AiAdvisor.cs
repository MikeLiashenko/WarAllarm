using System.Text;
using System.Text.Json;

// ── WarAlarm · Справжній ІІ ──────────────────────────────────────────────────
// Надсилає поточну обстановку у модель і повертає коротку пораду українською.
// Три формати API (визначається за префіксом ключа або ai_provider):
//   • Google Gemini (native)  → ai_api_key=AIza… або AQ…   (x-goog-api-key)
//   • Anthropic Claude        → ai_api_key=sk-ant-…        (x-api-key, /v1/messages)
//   • OpenAI-сумісні          → NVIDIA nvapi-… / Groq gsk_ / OpenRouter sk-or- …
// Ключ — у secrets.local.txt (ai_api_key=, ai_model=, опц. ai_provider=/ai_base_url=).
// Raw HTTP через HttpClient — без зайвих залежностей (як і ntfy).

public sealed class AiAdvisor
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    enum Mode { Anthropic, Google, OpenAi }

    readonly string _key;
    readonly string _model;
    readonly Mode _mode;
    readonly string _url;

    public AiAdvisor(string apiKey, string? model = null, string? provider = null, string? baseUrl = null)
    {
        _key = apiKey.Trim();
        provider = provider?.Trim().ToLowerInvariant();

        // Автовизначення провайдера за префіксом ключа, якщо не вказано явно.
        if (string.IsNullOrEmpty(provider))
            provider = _key.StartsWith("AIza") || _key.StartsWith("AQ") ? "google"
                     : _key.StartsWith("nvapi-") ? "nvidia"
                     : _key.StartsWith("gsk_") ? "groq"
                     : _key.StartsWith("sk-or-") ? "openrouter"
                     : _key.StartsWith("sk-ant") ? "anthropic"
                     : "openai"; // невідомий префікс → OpenAI-сумісний (потрібен ai_base_url)

        _mode = provider is "anthropic" or "claude" ? Mode.Anthropic
              : provider is "google" or "gemini" ? Mode.Google
              : Mode.OpenAi;

        _model = !string.IsNullOrWhiteSpace(model) ? model!
               : _mode == Mode.Anthropic ? "claude-opus-5"
               : _mode == Mode.Google ? "gemini-3.6-flash"
               : "meta/llama-3.3-70b-instruct";

        _url = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl!.Trim()
             : _mode switch
             {
                 Mode.Anthropic => "https://api.anthropic.com/v1/messages",
                 Mode.Google    => $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent",
                 _ => provider switch
                 {
                     "nvidia"     => "https://integrate.api.nvidia.com/v1/chat/completions",
                     "groq"       => "https://api.groq.com/openai/v1/chat/completions",
                     "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
                     "mistral"    => "https://api.mistral.ai/v1/chat/completions",
                     _            => "https://integrate.api.nvidia.com/v1/chat/completions",
                 },
             };
    }

    const string SystemPrompt =
        "Ти — асистент застосунку повітряної тривоги WarAlarm для мешканця України. " +
        "Тобі дають зведення про поточні повітряні загрози: тип (шахед/ракета/КАБ/балістика), " +
        "місце, відстань до дому користувача, орієнтовний час підльоту (ETA), стадію та напрямок, " +
        "а також коротку історію. Дай КОРОТКУ пораду (2–4 речення) українською: наскільки небезпечно " +
        "саме для користувача зараз, що робити ЗАРАЗ (укриття / стежити / спокій) і чи наближається загроза. " +
        "Пиши прямо, спокійно і по суті, як досвідчений фахівець із безпеки. Без вступів, без markdown, без переліків. " +
        "Ніколи не вигадуй загроз, яких немає у зведенні.";

    // Возвращает совет модели или null (ошибка/пусто). Ошибки не критичны.
    public async Task<string?> Analyze(string situation)
    {
        try
        {
            string payload = _mode switch
            {
                Mode.Anthropic => JsonSerializer.Serialize(new
                {
                    model = _model, max_tokens = 400, system = SystemPrompt,
                    messages = new[] { new { role = "user", content = situation } },
                }),
                Mode.Google => JsonSerializer.Serialize(new
                {
                    systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
                    contents = new[] { new { role = "user", parts = new[] { new { text = situation } } } },
                    generationConfig = new { maxOutputTokens = 400, temperature = 0.3 },
                }),
                _ => JsonSerializer.Serialize(new
                {
                    model = _model, max_tokens = 400, temperature = 0.3,
                    messages = new[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user",   content = situation },
                    },
                }),
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _url);
            switch (_mode)
            {
                case Mode.Anthropic:
                    req.Headers.Add("x-api-key", _key);
                    req.Headers.Add("anthropic-version", "2023-06-01");
                    break;
                case Mode.Google:
                    req.Headers.Add("x-goog-api-key", _key);
                    break;
                default:
                    req.Headers.Add("Authorization", "Bearer " + _key);
                    break;
            }
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"⚠ ІІ: {(int)resp.StatusCode} {body[..Math.Min(260, body.Length)]}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string text;
            if (_mode == Mode.Anthropic)
            {
                var sb = new StringBuilder();
                foreach (var block in root.GetProperty("content").EnumerateArray())
                    if (block.TryGetProperty("type", out var ty) && ty.GetString() == "text"
                        && block.TryGetProperty("text", out var tx))
                        sb.Append(tx.GetString());
                text = sb.ToString().Trim();
            }
            else if (_mode == Mode.Google)
            {
                var sb = new StringBuilder();
                foreach (var part in root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray())
                    if (part.TryGetProperty("text", out var tx)) sb.Append(tx.GetString());
                text = sb.ToString().Trim();
            }
            else
            {
                text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
            }
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("⚠ ІІ виклик не вдався: " + ex.Message);
            return null;
        }
    }
}
