using System.Text;

// ── WarAlarm · Міст до Firebase ──────────────────────────────────────────────
// Публікує живий стан (той самий JSON, що MapWriter будує для map.html) у
// Firebase Realtime Database, щоб застосунок на Flutter читав РЕАЛЬНІ дані.
// Raw HTTP через HttpClient — без зайвих залежностей (як ntfy та AiAdvisor).
//
// Налаштування у secrets.local.txt:
//   firebase_db_secret=...   (Legacy database secret з консолі Firebase — дає право запису)
//   firebase_db_url=...      (необовʼязково; за замовч. — база проєкту waralarm-c8c1f)

public sealed class FirebasePublisher
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly string _baseUrl;
    readonly string? _secret;
    DateTime _lastWarn = DateTime.MinValue;

    public FirebasePublisher(string baseUrl, string? secret)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _secret = secret;
    }

    /// PUT <base>/<node>.json — замінює вузол свіжим станом. Помилки не критичні.
    public async Task Publish(string node, string json)
    {
        try
        {
            var url = $"{_baseUrl}/{node}.json";
            if (!string.IsNullOrEmpty(_secret)) url += $"?auth={Uri.EscapeDataString(_secret)}";

            using var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                // Не спамимо консоль — попередження не частіше разу на хвилину.
                if (DateTime.Now - _lastWarn > TimeSpan.FromMinutes(1))
                {
                    _lastWarn = DateTime.Now;
                    var body = await resp.Content.ReadAsStringAsync();
                    Console.Error.WriteLine(
                        $"⚠ Firebase publish: {(int)resp.StatusCode} {body[..Math.Min(200, body.Length)]}");
                }
            }
        }
        catch (Exception ex)
        {
            if (DateTime.Now - _lastWarn > TimeSpan.FromMinutes(1))
            {
                _lastWarn = DateTime.Now;
                Console.Error.WriteLine("⚠ Firebase publish не вдався: " + ex.Message);
            }
        }
    }
}
