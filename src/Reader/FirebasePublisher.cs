using System.Text;
using System.Text.Json;

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

    // ── Живий стан: економно ────────────────────────────────────────────────
    // Було: увесь стан (~70 КБ) PUT-ом на КОЖЕН пост каналу (~раз на 1–2 с), і Firebase
    // відсилав його назад у відповідь — ~40 ГБ туди й ~36 ГБ назад за тиждень на платному
    // вихідному трафіку Google Cloud, плюс ті ж 70 КБ кожному відкритому застосунку.
    // Тепер: не частіше ніж раз на 4 с (береться найсвіжіший стан), лише змінені поля
    // (PUT state/<поле> — старі версії застосунку це розуміють), відповідь без тіла
    // (print=silent), повний стан — раз на 10 хв як страховка.
    static readonly TimeSpan StateEvery = TimeSpan.FromSeconds(4), FullEvery = TimeSpan.FromMinutes(10);
    readonly object _gate = new();
    string? _pending;
    bool _sending;
    readonly Dictionary<string, string> _sent = new();
    DateTime _fullAt = DateTime.MinValue;

    public void PublishState(string json)
    {
        lock (_gate)
        {
            _pending = json;
            if (_sending) return;
            _sending = true;
        }
        _ = Task.Run(StateLoop);
    }

    async Task StateLoop()
    {
        while (true)
        {
            string json;
            lock (_gate)
            {
                if (_pending is null) { _sending = false; return; }
                json = _pending;
                _pending = null;
            }
            var started = DateTime.Now;
            await SendState(json);
            var wait = StateEvery - (DateTime.Now - started);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
        }
    }

    async Task SendState(string json)
    {
        Dictionary<string, string> now;
        try
        {
            using var doc = JsonDocument.Parse(json);
            now = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
        }
        catch { await Publish("state", json); return; }

        if (DateTime.Now - _fullAt > FullEvery)
        {
            if (await Publish("state", json)) { _fullAt = DateTime.Now; _sent.Clear(); foreach (var kv in now) _sent[kv.Key] = kv.Value; }
            return;
        }
        var jobs = new List<Task<bool>>();
        var keys = new List<(string key, string? raw)>();
        foreach (var (k, v) in now)
            if (!_sent.TryGetValue(k, out var old) || old != v) keys.Add((k, v));
        foreach (var k in _sent.Keys.Where(k => !now.ContainsKey(k)).ToList()) keys.Add((k, null));
        foreach (var (k, v) in keys) jobs.Add(Publish($"state/{k}", v ?? "null"));
        var ok = await Task.WhenAll(jobs);
        for (int i = 0; i < keys.Count; i++)
        {
            if (!ok[i]) { _fullAt = DateTime.MinValue; continue; } // збій — наступного разу повний стан
            if (keys[i].raw is { } r) _sent[keys[i].key] = r; else _sent.Remove(keys[i].key);
        }
    }

    /// PUT <base>/<node>.json — замінює вузол. Помилки не критичні; true = записано.
    public async Task<bool> Publish(string node, string json)
    {
        try
        {
            // print=silent: Firebase відповідає 204 без тіла, а не відсилає записане назад.
            var url = $"{_baseUrl}/{node}.json?print=silent";
            if (!string.IsNullOrEmpty(_secret)) url += $"&auth={Uri.EscapeDataString(_secret)}";

            using var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                await Warn(resp);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (DateTime.Now - _lastWarn > TimeSpan.FromMinutes(1))
            {
                _lastWarn = DateTime.Now;
                Console.Error.WriteLine("⚠ Firebase publish не вдався: " + ex.Message);
            }
            return false;
        }
    }

    // Не спамимо консоль — попередження не частіше разу на хвилину.
    async Task Warn(HttpResponseMessage resp)
    {
        if (DateTime.Now - _lastWarn <= TimeSpan.FromMinutes(1)) return;
        _lastWarn = DateTime.Now;
        var body = await resp.Content.ReadAsStringAsync();
        Console.Error.WriteLine($"⚠ Firebase publish: {(int)resp.StatusCode} {body[..Math.Min(200, body.Length)]}");
    }
}
