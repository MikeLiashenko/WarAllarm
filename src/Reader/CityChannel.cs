using System.Net;
using System.Text;
using System.Text.Json;

// ── WarAlarm · міські канали (Київ @kyiv_waralarm, Харків @Kh_WarAlarm) ──────
// Поруч з основним всеукраїнським каналом. Кожен міський канал — свій ChannelPoster
// (свій LastId і захист від дублів) і пише ЛИШЕ про своє місто:
//   • офіційна тривога / відбій по точці центру міста (відбій — після двох опитувань поспіль);
//   • жива дошка цілей біля міста (Program.cs → RunLiveBoard з фільтром міста);
//   • короткі пости з локальних моніторів (KyivMonitor/KharkivMonitor): «🛵 Шахед → Троєщина»;
//   • світло (єСвітло) — лише Київ.
// Ключі в secrets.local.txt: telegram_channel_kyiv=@kyiv_waralarm, telegram_channel_kharkiv=@Kh_WarAlarm;
// токен — telegram_channel_<key>_bot_token або спільний telegram_channel_bot_token.
public sealed class CityChannel
{
    public readonly record struct City(string Key, string Name, double Lat, double Lon);

    /// Підтримувані міста: ключ у командах/секретах, назва (= зона моніторів), центр.
    public static readonly City[] Known =
    {
        new("kyiv", "Київ", 50.4501, 30.5234),
        new("kharkiv", "Харків", 49.9935, 36.2304),
    };

    public static City? Find(string s) =>
        Known.Cast<City?>().FirstOrDefault(c => c!.Value.Key.Equals(s, StringComparison.OrdinalIgnoreCase)
                                             || c.Value.Name.Equals(s, StringComparison.OrdinalIgnoreCase));

    public City Info { get; }
    public string Key => Info.Key;
    public string Name => Info.Name;
    public (double lat, double lon) Center => (Info.Lat, Info.Lon);
    public string Handle { get; }
    /// null — сухий прогон (citysim): пости лише друкуються в консоль.
    public ChannelPoster? Poster { get; }

    public CityChannel(City city, string handle, ChannelPoster? poster)
    {
        Info = city; Handle = handle; Poster = poster;
    }

    /// Підпис постів міського каналу (аналог ChannelFoot).
    public string Foot => FootFor(Name);
    public static string FootFor(string city) => $"<i>🤖 WarAlarm {city} · дані приблизні</i>";
    /// Рядок «оновлюється» під живою дошкою.
    public string LiveFoot() => $"<i>🕒 {DateTime.Now:HH:mm} · оновлюється · 🤖 WarAlarm {Name}</i>";
    /// Логотип міста для карт (assets/logo_<key>.png).
    public string LogoFile => $"logo_{Key}.png";

    public async Task<long?> Post(string text, byte[]? png = null, bool silent = false)
    {
        if (Poster is not null) return await Poster.Post(text, png, silent);
        Console.WriteLine($"📤 [{Name}]{(silent ? " 🔕" : " 🔔")}{(png is not null ? " 🖼" : "")}\n{Plain(text)}\n");
        return null;
    }

    // ── 1. Офіційна тривога / відбій по місту ────────────────────────────────
    bool? _alarm;           // null — ще не бачили жодного знімка (перший — без поста)
    int _miss;
    DateTime? _alarmAt;     // коли оголосили (null — тривога вже йшла на старті)

    /// Крок по знімку API: рівень у точці центру міста → текст поста або null.
    /// Збій API (!snap.Ok) сюди не передаємо — це не відбій.
    public string? AlertStep(string? level, DateTime now)
    {
        bool red = level == "red";
        if (_alarm is null) { _alarm = red; _alarmAt = null; return null; }
        if (red)
        {
            _miss = 0;
            if (_alarm == true) return null;
            _alarm = true; _alarmAt = now;
            return $"🔴 <b>Повітряна тривога — {H(Name)}</b>\nУсім в укриття.\n\n{Foot}";
        }
        if (_alarm == false) return null;
        if (++_miss < 2) return null;    // зникла вперше — ще тривога (як у RunAlertsLoop)
        _alarm = false; _miss = 0;
        var dur = _alarmAt is { } at ? $"\nТривала {Dur(now - at)}." : "";
        _alarmAt = null;
        return $"🟢 <b>Відбій — {H(Name)}</b>{dur}\n\n{Foot}";
    }

    static string Dur(TimeSpan d) => d.TotalMinutes < 60 ? $"{Math.Max(1, (int)d.TotalMinutes)} хв" : $"{(int)d.TotalHours} год {d.Minutes} хв";

    // ── 3. Пости з локальних моніторів ───────────────────────────────────────
    readonly Dictionary<string, DateTime> _recent = new();
    DateTime _ppoAt = DateTime.MinValue, _ballAt = DateTime.MinValue;

    /// Подія локального монітора → короткий пост (або null).
    /// [u] — результат tracker.Ingest (для загроз), [hadCityThreat] — чи були цілі біля міста ДО події
    /// (новa загроза місту → зі звуком, решта — тихо).
    public (string text, bool silent)? EventPost(KhEvent e, TrackUpdate? u, bool hadCityThreat, string ballisticText, DateTime now)
    {
        string? line = null;
        bool loud = false;
        if (e.Signal == KhSignal.News) { }
        else if (e.Kind == ThreatKind.Ballistic && Slang.BallisticThreat(e.Raw))
        {
            if (now - _ballAt > TimeSpan.FromMinutes(5)) { line = ballisticText; loud = true; _ballAt = now; }
        }
        else if (Slang.ShotCount(e.Raw) is { } n)
        {   // «мінус» без цілей біля міста — не про нас (або запізнілий перепост)
            if (hadCityThreat) line = n > 1 ? $"🎯 Мінус {n}" : "🎯 Мінус";
        }
        else if (e.Signal == KhSignal.Threat && u is { Threat: { Recon: false, Pos: { } p } t } && ThreatGeo.Km(p, Center) <= 40
                 // без власного місця («Над вами!», «ховайтеся») — лише щойно вибух / над районом, інакше це шум
                 && (e.Places.Count > 0 || (u.Value.StageChanged && t.Stage is TrackStage.Impact or TrackStage.Overhead)))
        {
            line = ThreatLine(e, t, u.Value.StageChanged);
            loud = !hadCityThreat;
        }

        if (Ppo(e.Raw) && now - _ppoAt > TimeSpan.FromMinutes(3))
        {
            _ppoAt = now;
            line = line is null ? "🛡 Працює ППО" : line + "\n🛡 Працює ППО";
        }
        if (line is null) return null;

        // Той самий рядок (перепост іншим каналом) ≤3 хв — не дублюємо.
        if (_recent.TryGetValue(line, out var was) && now - was < TimeSpan.FromMinutes(3)) return null;
        _recent[line] = now;
        if (_recent.Count > 200)
            foreach (var k in _recent.Where(kv => now - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList()) _recent.Remove(k);

        return ($"{H(line)}\n\n{Foot}", !loud);
    }

    // [fresh] — стадія змінилась саме цією подією (інакше «💥» від минулого вибуху липне до нових постів).
    static string ThreatLine(KhEvent e, Threat t, bool fresh)
    {
        var places = e.Places.Count > 0 ? string.Join(", ", e.Places) : t.LastPlace;
        var icon = e.Kind switch
        {
            ThreatKind.Drone => "🛵",
            ThreatKind.Missile => "🚀",
            ThreatKind.Ballistic => "☄",
            ThreatKind.Kab => "💣",
            ThreatKind.Aviation => "✈",
            ThreatKind.Fpv => "🪖",
            _ => "🎯",
        };
        if (e.Extra is { } ex && (ex.Contains("fpv", StringComparison.OrdinalIgnoreCase) || ex.Contains("фпв", StringComparison.OrdinalIgnoreCase))) icon = "🪖";
        var label = e.Extra is { Length: > 0 } x ? char.ToUpper(x[0]) + x[1..] : e.Kind switch
        {
            ThreatKind.Drone => "Шахед",
            ThreatKind.Missile => "Ракета",
            ThreatKind.Ballistic => "Балістика",
            ThreatKind.Kab => "КАБ",
            ThreatKind.Aviation => "Авіація",
            ThreatKind.Fpv => "ФПВ",
            _ => "Ціль",
        };
        var stage = fresh ? t.Stage : TrackStage.Approaching;
        if (stage == TrackStage.Impact) return places.Length > 0 ? $"💥 Гучно — {places}" : "💥 Гучно";
        var what = $"{icon} {label}";
        if (places.Length == 0) return what;
        return stage switch
        {
            TrackStage.Overhead => $"⚠️ {what} над {places}",
            TrackStage.Imminent => $"❗ {what} на підльоті — {places}",
            _ => $"{what} → {places}",
        };
    }

    static bool Ppo(string s) => System.Text.RegularExpressions.Regex.IsMatch(s.ToLowerInvariant(), @"\bппо\b|протиповітрян|зенітн");

    static string H(string s) => WebUtility.HtmlEncode(s);
    /// HTML поста → текст для консолі (citysim).
    public static string Plain(string html) =>
        WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));

    // ── Оформлення каналу через Bot API (channelsetup) ───────────────────────
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// Перевіряє права бота й ставить аватар + опис. Повертає список того, що треба ввімкнути руками.
    public static async Task<List<string>> Setup(string token, string channel, City city, string? avatarPath)
    {
        var todo = new List<string>();
        var api = $"https://api.telegram.org/bot{token}";
        async Task<JsonElement?> Call(string method, object payload)
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{api}/{method}", content);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return doc.RootElement.GetProperty("result").Clone();
            Console.WriteLine($"   ⚠ {method}: {(doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : body)}");
            return null;
        }

        if (await Call("getMe", new { }) is not { } me) { todo.Add("Токен бота каналу не працює — перевір telegram_channel_bot_token"); return todo; }
        var botId = me.GetProperty("id").GetInt64();
        var botName = me.TryGetProperty("username", out var u) ? "@" + u.GetString() : botId.ToString();
        Console.WriteLine($"🤖 Бот: {botName}");

        if (await Call("getChat", new { chat_id = channel }) is not { } chat)
        {
            todo.Add($"Бот {botName} не бачить {channel}: додай його в канал адміном");
            return todo;
        }
        Console.WriteLine($"📢 Канал: «{(chat.TryGetProperty("title", out var tt) ? tt.GetString() : channel)}» ({channel})");

        bool canInfo = false;
        if (await Call("getChatMember", new { chat_id = channel, user_id = botId }) is { } mem)
        {
            var status = mem.GetProperty("status").GetString();
            bool Right(string k) => status == "creator" || (mem.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True);
            if (status is not ("administrator" or "creator"))
                todo.Add($"Зроби {botName} адміністратором {channel} (зараз: {status})");
            else
            {
                if (!Right("can_post_messages")) todo.Add($"{channel} → Адміністратори → {botName}: увімкни «Публікувати повідомлення»");
                if (!Right("can_edit_messages")) todo.Add($"{channel} → Адміністратори → {botName}: увімкни «Редагувати повідомлення інших»");
                canInfo = Right("can_change_info");
                if (!canInfo) todo.Add($"{channel} → Адміністратори → {botName}: увімкни «Змінювати інформацію каналу» (для аватара й опису)");
            }
            Console.WriteLine($"   права: статус {status}, публікувати {Right("can_post_messages")}, редагувати {Right("can_edit_messages")}, інфо {Right("can_change_info")}");
        }
        if (!canInfo) return todo;

        // Аватар
        if (avatarPath is null || !File.Exists(avatarPath))
            todo.Add($"Не знайдено design/channels/{city.Key}_avatar.png — аватар не встановлено");
        else
        {
            using var form = new MultipartFormDataContent { { new StringContent(channel), "chat_id" } };
            var img = new ByteArrayContent(await File.ReadAllBytesAsync(avatarPath));
            img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(img, "photo", Path.GetFileName(avatarPath));
            var resp = await Http.PostAsync($"{api}/setChatPhoto", form);
            var body = await resp.Content.ReadAsStringAsync();
            Console.WriteLine(resp.IsSuccessStatusCode ? $"🖼 Аватар встановлено ({Path.GetFileName(avatarPath)})" : $"   ⚠ setChatPhoto: {body}");
            if (!resp.IsSuccessStatusCode) todo.Add("Аватар не встановився — див. помилку вище");
        }

        // Опис (≤255 симв.)
        var desc = $"Повітряні тривоги та загрози — {city.Name}. Тривога/відбій, цілі біля міста, дані локальних моніторів"
                 + (city.Key == "kyiv" ? ", світло" : "") + ". Дані приблизні — слухай офіційну тривогу. 🤖 WarAlarm";
        var r = await Call("setChatDescription", new { chat_id = channel, description = desc });
        // «description is not modified» — теж ок
        Console.WriteLine(r is not null ? "📝 Опис встановлено:\n   " + desc : "   (якщо вище «not modified» — опис уже такий самий)");
        return todo;
    }

    /// Шукає файл дизайну вгору від поточної теки (запуск з src/Reader або з кореня репо).
    public static string? FindDesign(string file)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 5 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "design", "channels", file);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
