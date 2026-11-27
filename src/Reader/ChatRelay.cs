using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// ── WarAlarm · Чат застосунку ─────────────────────────────────────────────────
// Користувачі застосунку не мають облікових записів Firebase, а база відкрита лише на читання стану.
// Тож пише в чат тільки сервер: застосунок кладе запит у закриту «скриньку» chat/in/<uid>/<id>
// (писати може будь-хто, читати — ніхто, крім сервера з секретом), сервер його перевіряє й публікує:
//   • ключ пристрою (перший запит uid закріплює ключ; далі — лише з ним: чужим ім'ям не напишеш);
//   • профіль із підтвердженням віку 13+ (без нього повідомлення не приймаються);
//   • ліміт частоти, розмір, тип файлу (справжній JPEG/PNG/WebP, аудіо M4A/AAC/OGG), бан;
//   • «видалити своє», скарга (3 різні скарги — повідомлення сховане), адміністратори (chat_admins).
// Публічно читаються chat/msgs (повідомлення), chat/media/<id> (фото, голосові), chat/profiles, chat/ava/<uid>.
// Правила доступу до вузла chat сервер дописує в Firebase сам (легасі-секрет має на це право).
public sealed class ChatRelay
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly string _base, _secret;
    readonly HashSet<string> _admins;
    readonly ConcurrentDictionary<string, string> _keys = new();
    readonly ConcurrentDictionary<string, JsonObject> _profiles = new();
    readonly ConcurrentDictionary<string, bool> _bans = new();
    // Дозвіл батьків: телефони дітей до 13, яким адміністратор відкрив чат (за ID пристрою, не за датою).
    readonly ConcurrentDictionary<string, bool> _kids = new();
    readonly string[] _cfgKids;
    readonly ConcurrentDictionary<string, Queue<DateTime>> _rate = new();
    readonly ConcurrentDictionary<string, HashSet<string>> _reports = new();
    readonly ConcurrentDictionary<string, (string uid, string? mid)> _msgs = new();
    readonly ConcurrentQueue<(string uid, string id, DateTime at)> _res = new();
    readonly SemaphoreSlim _one = new(1, 1);
    const int KeepMsgs = 400, MaxText = 2000, MaxMediaB64 = 480_000, MaxAvatarB64 = 120_000;

    public ChatRelay(string baseUrl, string secret, IEnumerable<string> admins, IEnumerable<string>? kids = null)
    {
        _cfgKids = (kids ?? []).Where(k => k.Length is >= 16 and <= 40).ToArray();
        _base = baseUrl.TrimEnd('/'); _secret = secret;
        _admins = new HashSet<string>(admins.Where(a => a.Length > 0), StringComparer.Ordinal);
    }

    string U(string path, string q = "") => $"{_base}/{path}.json?auth={Uri.EscapeDataString(_secret)}{q}";

    public void Start() => _ = Task.Run(async () =>
    {
        try { await EnsureRules(); } catch (Exception ex) { Console.Error.WriteLine("⚠ чат · правила: " + ex.Message); }
        try { await Load(); } catch (Exception ex) { Console.Error.WriteLine("⚠ чат · завантаження: " + ex.Message); }
        _ = Task.Run(TrimLoop);
        while (true)
        {
            try { await Listen(); } catch (Exception ex) { Console.Error.WriteLine("⚠ чат · потік: " + ex.Message); }
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    });

    // ── Правила: дописуємо вузол chat, решту не чіпаємо ────────────────────────
    static readonly string ChatRules = """
    {
      ".read": false, ".write": false,
      "msgs": { ".read": true, ".indexOn": ["ts"] },
      "media": { ".read": true },
      "profiles": { ".read": true },
      "ava": { ".read": true },
      "res": { "$uid": { ".read": true } },
      "kids": { "$uid": { ".read": true } },
      "in": {
        "$uid": {
          ".validate": "$uid.length >= 16 && $uid.length <= 40",
          "$req": {
            ".write": "!data.exists() && newData.exists()",
            ".validate": "newData.hasChildren(['k', 't', 'ts']) && newData.child('k').isString() && newData.child('k').val().length >= 24 && newData.child('k').val().length <= 64 && newData.child('t').isString() && newData.child('ts').isNumber() && newData.child('ts').val() <= now",
            "img": { ".validate": "newData.isString() && newData.val().length < 480000" },
            "voice": { ".validate": "newData.isString() && newData.val().length < 480000" },
            "ava": { ".validate": "newData.isString() && newData.val().length < 120000" },
            "text": { ".validate": "newData.isString() && newData.val().length <= 2000" }
          }
        }
      }
    }
    """;

    async Task EnsureRules()
    {
        var raw = await Http.GetStringAsync(U(".settings/rules"));
        var root = JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?.AsObject()
                   ?? throw new Exception("порожні правила");
        if (root["rules"] is not JsonObject rules) throw new Exception("нема rules");
        var want = JsonNode.Parse(ChatRules)!;
        if (rules["chat"] is JsonNode have && JsonNode.DeepEquals(have, want)) return;
        rules["chat"] = want;
        using var res = await Http.PutAsync(U(".settings/rules"), new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json"));
        Console.WriteLine(res.IsSuccessStatusCode ? "💬 Чат: правила доступу оновлено" : $"⚠ чат · правила: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    async Task Load()
    {
        async Task<JsonObject?> Get(string p) => JsonNode.Parse(await Http.GetStringAsync(U(p))) as JsonObject;
        if (await Get("chat/keys") is { } keys) foreach (var (k, v) in keys) if (v?.GetValue<string>() is { } s) _keys[k] = s;
        if (await Get("chat/bans") is { } bans) foreach (var (k, _) in bans) _bans[k] = true;
        if (await Get("chat/kids") is { } kids) foreach (var (k, _) in kids) _kids[k] = true;
        foreach (var k in _cfgKids.Where(k => !_kids.ContainsKey(k)))
        {
            _kids[k] = true; await Send(HttpMethod.Put, $"chat/kids/{k}", JsonValue.Create(true));
        }
        if (_kids.Count > 0) Console.WriteLine($"💬 Чат: дозвіл батьків для {_kids.Count} пристр.");
        if (await Get("chat/profiles") is { } pr) foreach (var (k, v) in pr) if (v is JsonObject o) _profiles[k] = (JsonObject)o.DeepClone();
        if (await Get("chat/msgs") is { } ms)
            foreach (var (k, v) in ms) if (v is JsonObject o) _msgs[k] = (o["uid"]?.GetValue<string>() ?? "", o["mid"]?.GetValue<string>());
        await Send(HttpMethod.Delete, "chat/res", null);
        Console.WriteLine($"💬 Чат: користувачів {_profiles.Count}, повідомлень {_msgs.Count}");
    }

    // ── Потік скриньки (SSE) ──────────────────────────────────────────────────
    async Task Listen()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, U("chat/in"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var sse = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var res = await sse.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        using var rd = new StreamReader(await res.Content.ReadAsStreamAsync());
        string? ev = null;
        while (await rd.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("event:")) ev = line[6..].Trim();
            else if (line.StartsWith("data:") && ev is "put" or "patch")
            {
                var d = JsonNode.Parse(line[5..]) as JsonObject;
                var path = d?["path"]?.GetValue<string>() ?? "/";
                var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var data = d?["data"];
                // «/» — уся скринька, «/uid» — усі запити користувача, «/uid/id» — один запит.
                if (parts.Length == 0 && data is JsonObject all) { foreach (var (uid, reqs) in all) if (reqs is JsonObject ro) foreach (var (id, r) in ro) await Handle(uid, id, r as JsonObject); }
                else if (parts.Length == 1 && data is JsonObject ur) { foreach (var (id, r) in ur) await Handle(parts[0], id, r as JsonObject); }
                else if (parts.Length == 2) await Handle(parts[0], parts[1], data as JsonObject);
            }
            else if (ev is "cancel" or "auth_revoked") throw new Exception(ev);
        }
    }

    async Task Handle(string uid, string id, JsonObject? r)
    {
        if (r is null) return;
        await _one.WaitAsync();
        try
        {
            string? why = null;
            try { why = await Process(uid, id, r); } catch (Exception ex) { why = "помилка: " + ex.Message; }
            if (why is not null)
            {
                Console.WriteLine($"💬 чат · {uid[..Math.Min(6, uid.Length)]}…/{id} відхилено: {why}");
                // Застосунок бачить причину у chat/res/<uid>/<id> (читати може лише той, хто знає свій uid).
                await Send(HttpMethod.Put, $"chat/res/{uid}/{id}", new JsonObject { ["why"] = why, ["ts"] = Now });
                _res.Enqueue((uid, id, DateTime.UtcNow));
            }
            await Send(HttpMethod.Delete, $"chat/in/{uid}/{id}", null);
        }
        finally { _one.Release(); }
    }

    static string? Str(JsonObject r, string k) => r[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static double Num(JsonObject r, string k) => r[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    async Task<string?> Process(string uid, string id, JsonObject r)
    {
        var key = Str(r, "k") ?? "";
        if (_keys.TryGetValue(uid, out var known)) { if (known != key) return "Цей пристрій не впізнано — перевстанови чат у налаштуваннях"; }
        else { _keys[uid] = key; await Send(HttpMethod.Put, $"chat/keys/{uid}", JsonValue.Create(key)); }
        if (_bans.ContainsKey(uid)) return "Тебе заблоковано в чаті за порушення правил";
        bool admin = _admins.Contains(uid);
        switch (Str(r, "t"))
        {
            case "profile": return await Profile(uid, r, admin);
            case "msg": return await Message(uid, id, r);
            case "del":
            {
                var mid = Str(r, "id") ?? "";
                if (!_msgs.TryGetValue(mid, out var m)) return "Повідомлення вже немає";
                if (m.uid != uid && !admin) return "Це не твоє повідомлення";
                await Remove(mid); return null;
            }
            case "report":
            {
                var mid = Str(r, "id") ?? "";
                if (!_msgs.ContainsKey(mid)) return "Повідомлення вже немає";
                var set = _reports.GetOrAdd(mid, _ => new HashSet<string>());
                lock (set) set.Add(uid);
                if (admin || set.Count >= 3) await Send(HttpMethod.Put, $"chat/msgs/{mid}/hidden", JsonValue.Create(true));
                Console.WriteLine($"🚩 чат · скарга на {mid} ({set.Count}){(Str(r, "why") is { } w ? ": " + w : "")}");
                return null;
            }
            case "react":
            {
                var mid = Str(r, "id") ?? "";
                var e = Str(r, "e") ?? "";
                if (!_msgs.ContainsKey(mid)) return "Повідомлення вже немає";
                if (!Reactions.Contains(e)) return "Невідома реакція";
                var path = $"chat/msgs/{mid}/rx/{e}/{uid}";
                var had = await Http.GetStringAsync(U(path)) is "true";
                if (had) await Send(HttpMethod.Delete, path, null); else await Send(HttpMethod.Put, path, JsonValue.Create(true));
                return null;
            }
            case "edit":
            {
                var mid = Str(r, "id") ?? "";
                if (!_msgs.TryGetValue(mid, out var m)) return "Повідомлення вже немає";
                if (m.uid != uid) return "Це не твоє повідомлення";
                var text = (Str(r, "text") ?? "").Trim();
                if (text.Length > MaxText) text = text[..MaxText];
                if (text.Length == 0 && m.mid is null) return "Порожнє повідомлення";
                await Send(HttpMethod.Patch, $"chat/msgs/{mid}", new JsonObject { ["text"] = text, ["edited"] = Now });
                return null;
            }
            case "kid" when admin:
            {
                var who = Str(r, "uid") ?? "";
                if (who.Length is < 16 or > 40 || !Regex.IsMatch(who, "^[0-9a-f]+$")) return "Не той ID — скопіюй його з телефону дитини";
                var on = r["on"] is not JsonValue ov || !ov.TryGetValue<bool>(out var o) || o;
                if (on) { _kids[who] = true; await Send(HttpMethod.Put, $"chat/kids/{who}", JsonValue.Create(true)); }
                else { _kids.TryRemove(who, out _); await Send(HttpMethod.Delete, $"chat/kids/{who}", null); }
                Console.WriteLine($"👪 чат · дозвіл батьків {(on ? "надано" : "знято")}: {who[..6]}…");
                return null;
            }
            case "ban" when admin:
            {
                var who = Str(r, "uid") ?? "";
                if (who.Length < 16 || _admins.Contains(who)) return "Не той користувач";
                _bans[who] = true; await Send(HttpMethod.Put, $"chat/bans/{who}", JsonValue.Create(true));
                foreach (var (mid, m) in _msgs.Where(x => x.Value.uid == who).ToList()) await Remove(mid);
                Console.WriteLine($"⛔ чат · бан {who}"); return null;
            }
            default: return "Онови застосунок — цей запит він уже не розуміє";
        }
    }

    static readonly HashSet<string> Reactions = new() { "like", "heart", "lol", "wow", "sad", "pray", "fire", "ua" };

    static readonly Regex Impostor = new(@"war\s*alarm|адмін|админ|admin|модератор|moderator|підтримка|support", RegexOptions.IgnoreCase);

    async Task<string?> Profile(string uid, JsonObject r, bool admin)
    {
        var age13 = r["age13"] is JsonValue av && av.TryGetValue<bool>(out var age) && age;
        if (!age13)
        {
            // Дозвіл могли дати й вручну в консолі Firebase (chat/kids/<код> = true) — дивимось свіжий стан бази.
            var allowed = (await Http.GetStringAsync(U($"chat/kids/{uid}"))).Trim() == "true";
            if (allowed) _kids[uid] = true; else _kids.TryRemove(uid, out _);
            if (!allowed) return "Чат — лише з 13 років (або з дозволу батьків)";
        }
        var name = Regex.Replace(Str(r, "name") ?? "", @"[\p{C}]+", " ").Trim();
        name = Regex.Replace(name, @"\s{2,}", " ");
        if (name.Length is < 2 or > 32) return "Ім'я — від 2 до 32 символів";
        if (!admin && Impostor.IsMatch(name)) return "Таке ім'я схоже на адміністрацію — обери інше";
        var bio = Regex.Replace(Str(r, "bio") ?? "", @"[\p{Cc}-[\n]]+", " ").Trim();
        if (bio.Length > 300) bio = bio[..300];
        var city = (Str(r, "city") ?? "").Trim(); if (city.Length > 40) city = city[..40];
        var p = _profiles.TryGetValue(uid, out var old) ? old : new JsonObject { ["since"] = Now };
        p["name"] = name; p["bio"] = bio; p["city"] = city; p["age13"] = true; p["upd"] = Now;
        if (admin) p["adm"] = true; else p.Remove("adm");
        if (Str(r, "ava") is { Length: > 0 } ava)
        {
            if (ava.Length > MaxAvatarB64 || Kind(ava) is not ("image/jpeg" or "image/png" or "image/webp")) return "Аватар має бути фото (JPEG/PNG) до 90 КБ";
            await Send(HttpMethod.Put, $"chat/ava/{uid}", JsonValue.Create(ava));
            p["avaRev"] = Now;
        }
        else if (r["avaDel"] is JsonValue dv && dv.TryGetValue<bool>(out var del) && del)
        {
            await Send(HttpMethod.Delete, $"chat/ava/{uid}", null);
            p.Remove("avaRev");
        }
        _profiles[uid] = p;
        await Send(HttpMethod.Put, $"chat/profiles/{uid}", p.DeepClone());
        return null;
    }

    async Task<string?> Message(string uid, string id, JsonObject r)
    {
        if (!_profiles.TryGetValue(uid, out var prof) || prof["age13"] is null) return "Спершу заповни профіль";
        var now = DateTime.UtcNow;
        var q = _rate.GetOrAdd(uid, _ => new Queue<DateTime>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromMinutes(5)) q.Dequeue();
            if (q.Count >= 30 || q.Count > 0 && now - q.Last() < TimeSpan.FromSeconds(1.2)) return "Забагато повідомлень — зачекай хвилинку";
            q.Enqueue(now);
        }
        var m = new JsonObject { ["uid"] = uid, ["name"] = prof["name"]?.GetValue<string>(), ["ts"] = Now };
        var text = (Str(r, "text") ?? "").Trim();
        if (text.Length > MaxText) text = text[..MaxText];
        string? mediaB64 = null, mime = null;
        if (Str(r, "img") is { Length: > 0 } img)
        {
            mime = Kind(img); if (mime is not ("image/jpeg" or "image/png" or "image/webp") || img.Length > MaxMediaB64) return "Фото не вдалося прийняти (JPEG/PNG до 350 КБ)";
            mediaB64 = img; m["t"] = "img"; m["w"] = Num(r, "w"); m["h"] = Num(r, "h");
        }
        else if (Str(r, "voice") is { Length: > 0 } voice)
        {
            mime = Kind(voice); if (mime is null || !mime.StartsWith("audio/") || voice.Length > MaxMediaB64) return "Голосове не вдалося прийняти";
            var dur = Num(r, "dur"); if (dur is <= 0 or > 180) return "Голосове — до 2 хвилин";
            mediaB64 = voice; m["t"] = "voice"; m["dur"] = Math.Round(dur, 1);
            if (r["wave"] is JsonArray wv && wv.Count <= 64) m["wave"] = wv.DeepClone();
        }
        else if (text.Length == 0) return "Порожнє повідомлення";
        else m["t"] = "text";
        if (text.Length > 0) m["text"] = text;
        if (Str(r, "re") is { Length: > 0 } re && _msgs.ContainsKey(re))
        {
            m["re"] = re;
            if (Str(r, "reName") is { } rn) m["reName"] = rn.Length > 32 ? rn[..32] : rn;
            if (Str(r, "reText") is { } rt) m["reText"] = rt.Length > 120 ? rt[..120] : rt;
        }
        if (_admins.Contains(uid)) m["adm"] = true;
        if (mediaB64 is not null)
        {
            await Send(HttpMethod.Put, $"chat/media/{id}", new JsonObject { ["mime"] = mime, ["data"] = mediaB64 });
            m["mid"] = id;
        }
        await Send(HttpMethod.Put, $"chat/msgs/{id}", m);
        _msgs[id] = (uid, mediaB64 is null ? null : id);
        return null;
    }

    // Справжній тип файлу за першими байтами (не довіряємо підпису від застосунку).
    static string? Kind(string b64)
    {
        byte[] h;
        try { h = Convert.FromBase64String(b64.Length > 64 ? b64[..64] : b64); } catch { return null; }
        bool At(int o, params byte[] s) => h.Length >= o + s.Length && s.Select((b, i) => h[o + i] == b).All(x => x);
        if (At(0, 0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (At(0, 0x89, 0x50, 0x4E, 0x47)) return "image/png";
        if (At(0, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && At(8, (byte)'W', (byte)'E', (byte)'B', (byte)'P')) return "image/webp";
        if (At(0, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && At(8, (byte)'W', (byte)'A', (byte)'V', (byte)'E')) return "audio/wav";
        if (At(4, (byte)'f', (byte)'t', (byte)'y', (byte)'p')) return "audio/mp4";
        if (At(0, (byte)'O', (byte)'g', (byte)'g', (byte)'S')) return "audio/ogg";
        if (h.Length > 1 && h[0] == 0xFF && (h[1] & 0xF6) == 0xF0) return "audio/aac";
        return null;
    }

    async Task Remove(string mid)
    {
        if (_msgs.TryRemove(mid, out var m) && m.mid is { } media) await Send(HttpMethod.Delete, $"chat/media/{media}", null);
        await Send(HttpMethod.Delete, $"chat/msgs/{mid}", null);
    }

    // Старі повідомлення (понад останні 400) і їхні файли — геть: база не росте.
    async Task TrimLoop()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(10));
            try
            {
                while (_res.TryPeek(out var rr) && DateTime.UtcNow - rr.at > TimeSpan.FromMinutes(30) && _res.TryDequeue(out rr))
                    await Send(HttpMethod.Delete, $"chat/res/{rr.uid}/{rr.id}", null);
                var old = _msgs.Keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(KeepMsgs).ToList();
                if (old.Count == 0) continue;
                await _one.WaitAsync();
                try { foreach (var k in old) await Remove(k); } finally { _one.Release(); }
            }
            catch (Exception ex) { Console.Error.WriteLine("⚠ чат · прибирання: " + ex.Message); }
        }
    }

    async Task Send(HttpMethod method, string path, JsonNode? body)
    {
        using var req = new HttpRequestMessage(method, U(path, "&print=silent"));
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) Console.Error.WriteLine($"⚠ чат · {method} {path}: {(int)res.StatusCode}");
    }
}
