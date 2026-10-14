using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TL;
using WTelegram;

// ── WarAlarm · Фаза 1 · Живая тревога ────────────────────────────────────────
// Слушает каналы в реальном времени, разбирает угрозы нужным парсером,
// фильтрует по твоему району и воет сиреной, когда цель касается тебя.

try { Console.OutputEncoding = Encoding.UTF8; } catch { /* консоль без юникода — не критично */ }

// ── ТВОИ НАСТРОЙКИ (settings.local.txt — меняй без пересборки) ────────────────
var cfg = LoadConfig("settings.local.txt");
string MyDistrict  = cfg.GetValueOrDefault("my_district", "Салтівський"); // дом: Салтівка, 602-й мкр
string MyOblast    = cfg.GetValueOrDefault("my_oblast", "Харківська");
string MyCity      = cfg.GetValueOrDefault("my_city", "Харків");
bool WatchKharkiv  = cfg.GetValueOrDefault("watch_kharkiv", "true") != "false";
bool WatchKyiv     = cfg.GetValueOrDefault("watch_kyiv", "true") != "false";
int OtboiMinSec    = int.TryParse(cfg.GetValueOrDefault("otboi_min_seconds", "90"), out var _os) ? _os : 90;

// Координаты дома для карты и ETA: my_lat/my_lon из настроек, иначе центр района.
double HomeLat = double.TryParse(cfg.GetValueOrDefault("my_lat", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var _hla)
    ? _hla : (ThreatGeo.Of(MyDistrict)?.lat ?? 50.063);
double HomeLon = double.TryParse(cfg.GetValueOrDefault("my_lon", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var _hlo)
    ? _hlo : (ThreatGeo.Of(MyDistrict)?.lon ?? 36.310);
var tracker = new ThreatTracker((HomeLat, HomeLon));
const string MapPath = "map.html";
// Доп. места для карты: строка settings  places=Назва@широта,довгота;Назва2@...  (необязательно).
List<(string name, double lat, double lon)> ParsePlaces(string raw)
{
    var list = new List<(string, double, double)>();
    foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var at = part.Split('@');
        if (at.Length != 2) continue;
        var ll = at[1].Split(',');
        if (ll.Length == 2
            && double.TryParse(ll[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var la)
            && double.TryParse(ll[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lo))
            list.Add((at[0].Trim(), la, lo));
    }
    return list;
}
var extraPlaces = ParsePlaces(cfg.GetValueOrDefault("places", ""));

// Иконка приложения (щит+тризуб, скруглённая) → data-URI для шапки и favicon карты.
foreach (var ip in new[] { "WarAlarm-rounded.svg", "../../WarAlarm-rounded.svg" })
    if (File.Exists(ip))
    {
        try { MapWriter.IconUri = "data:image/svg+xml;base64," + Convert.ToBase64String(File.ReadAllBytes(ip)); } catch { }
        break;
    }

// Настройки для вкладок «Мої місця»/«Налаштування» на карте (ntfy уточним после логина).
MapWriter.Config = new MapWriter.Cfg(MyDistrict, MyCity, MyOblast, WatchKharkiv, WatchKyiv, OtboiMinSec, false, extraPlaces);

// Локальные каналы Харькова → точность до района (парсер KharkivMonitor).
var khChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{ "kharkivlife", "truexakharkiv", "kharkov_times", "tlknewsua" };
// Локальные каналы Киева → уровень города (парсер KyivMonitor). Ты бываешь в Киеве.
var kyChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{ "nebo_raketa", "chyste_neboo" };
// Всеукраинские каналы-сводки → уровень город/область (парсер ThreatParser).
// По умолчанию @rozvidkaneba («Розвідка неба») — постит ЧАЩЕ (каждые неск. минут во
// время налёта) и СТРОГО в формате «Область:\nNх ТИП курсом на Місто», который наш
// парсер читает нативно; каждый пост — полный срез по областям (идеально под SweepKind).
// Меняется в settings.local.txt:  ua_channels=rozvidkaneba,raketa_trevoga,...
var uaChannels = new HashSet<string>(
    cfg.GetValueOrDefault("ua_channels", "rozvidkaneba")
       .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
       .Select(s => s.TrimStart('@')),
    StringComparer.OrdinalIgnoreCase);

// 🔌 Каналы про свет (отключения/восстановления) → PowerMonitor. По умолчанию
// @esvitlo_kyiv_ua («єСвітло Київ»). Настраивается: power_channels=esvitlo_kyiv_ua,...
var powerChannels = new HashSet<string>(
    cfg.GetValueOrDefault("power_channels", "esvitlo_kyiv_ua")
       .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
       .Select(s => s.TrimStart('@')),
    StringComparer.OrdinalIgnoreCase);

// Внутренние логи WTelegram уводим в файл, чтобы консоль оставалась чистой.
WTelegram.Helpers.Log = (level, message) =>
{
    File.AppendAllText("WTelegram.log", message + Environment.NewLine);
    if (level >= 4) Console.Error.WriteLine("⚠ " + message); // ошибки всё же покажем
};

Dictionary<string, string>? secrets = null;

// ── Настоящий ИИ (Claude API) — если задан ai_api_key в secrets.local.txt ─────
LoadSecrets();
var aiKey = SecretOpt("ai_api_key");
AiAdvisor? advisor = aiKey is { Length: > 0 }
    ? new AiAdvisor(aiKey, SecretOpt("ai_model"), SecretOpt("ai_provider"), SecretOpt("ai_base_url"))
    : null;
MapWriter.Config = MapWriter.Config! with { Ai = advisor is not null };
var aiLast = DateTime.MinValue;

// ── Міст до Firebase: публікуємо живий стан у RTDB для застосунку ─────────────
// Ключ шукаємо в secrets.local.txt АБО settings.local.txt (cfg) — де зручніше.
var fbUrl = SecretOpt("firebase_db_url")
    ?? (cfg.TryGetValue("firebase_db_url", out var fu) && fu.Length > 0 ? fu : null)
    ?? "https://waralarm-c8c1f-default-rtdb.europe-west1.firebasedatabase.app";
var fbSecret = SecretOpt("firebase_db_secret") ?? cfg.GetValueOrDefault("firebase_db_secret");
if (!string.IsNullOrWhiteSpace(fbSecret))
{
    MapWriter.Publisher = new FirebasePublisher(fbUrl, fbSecret);
    Console.WriteLine($"🔥 Firebase ВКЛ → публікую стан у {fbUrl}/state.json (застосунок бачитиме живі дані).\n");
}
else
{
    Console.WriteLine("🔥 Firebase ВИКЛ. Щоб застосунок бачив живі дані — додай  firebase_db_secret=...  у secrets.local.txt або settings.local.txt\n");
}

// Собирает текстовое зведение обстановки для ИИ.
string BuildSituation()
{
    var active = tracker.Active.Where(t => t.DistKm is not null).OrderBy(t => t.DistKm).ToList();
    var sb = new StringBuilder();
    sb.AppendLine($"Дім користувача: {MyDistrict} район, місто {MyCity}.");
    if (active.Count == 0) { sb.AppendLine("Активних повітряних загроз поблизу зараз немає."); return sb.ToString(); }
    sb.AppendLine($"Активні цілі ({active.Count}), від найближчої:");
    foreach (var t in active.Take(12))
        sb.AppendLine($"- {ThreatTracker.Label(t)}: біля {t.LastPlace}, {t.DistKm:0} км від дому, "
            + $"ETA {(t.EtaMin is { } e ? $"~{e:0} хв" : "невідомо")}, стадія «{ThreatTracker.StageLabel(t.Stage)}», напрямок {t.Dir}.");
    return sb.ToString();
}

// Запрашивает ИИ (в фоне, с троттлингом) и кладёт ответ на карту.
Task MaybeAi(bool force)
{
    if (advisor is null) return Task.CompletedTask;
    if (!force && DateTime.Now - aiLast < TimeSpan.FromMinutes(3)) return Task.CompletedTask;
    if (!force && !tracker.Active.Any(t => t.DistKm is { } d && d <= 60)) return Task.CompletedTask; // только близкие цели
    aiLast = DateTime.Now;
    var situation = BuildSituation();
    _ = Task.Run(async () =>
    {
        var reply = await advisor.Analyze(situation);
        if (reply is null) return;
        MapWriter.AiText = reply; MapWriter.AiAt = DateTime.Now;
        MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
        Ui.Line(ConsoleColor.Magenta, "🤖 ІІ: " + reply);
    });
    return Task.CompletedTask;
}

Console.WriteLine("=== WarAlarm · ридер каналов (Фаза 0/1) ===\n");

// ── Оффлайн-разбор целей (без Telegram):  dotnet run -- parse [файл] ─────────
if (args.Length >= 1 && (args[0].Equals("parse", StringComparison.OrdinalIgnoreCase)
                      || args[0].Equals("parsefile", StringComparison.OrdinalIgnoreCase)))
{
    RunParseDemo(args.Length >= 2 ? args[1] : "samples.txt");
    return;
}

// ── Генерація звуку «ПРИЛІТ НАД ДОМОМ» у ассети застосунку:  dotnet run -- gensound
// Пише siren_impact.wav тим самим синтезом, що й у боті — щоб звук збігався.
if (args.Length >= 1 && args[0].Equals("gensound", StringComparison.OrdinalIgnoreCase))
{
    var sounds = new (string name, byte[] data)[]
    {
        ("siren_impact.wav", SirenSound.BuildImpactHome()),
        ("siren_nuclear.wav", SirenSound.BuildNuclear()),
    };
    foreach (var dir in new[] { "../../app/assets", "app/assets" })
        if (Directory.Exists(dir))
            foreach (var (name, data) in sounds)
            {
                var p = Path.Combine(dir, name);
                File.WriteAllBytes(p, data);
                Console.WriteLine($"🔊 Записав {Path.GetFullPath(p)} ({data.Length} байт)");
            }
    return;
}

// ── Харьковский монитор (оффлайн):  dotnet run -- kh [район] [файл] ──────────
if (args.Length >= 1 && args[0].Equals("kh", StringComparison.OrdinalIgnoreCase))
{
    var district = args.Length >= 2 ? args[1] : "Салтівський";
    RunKharkivDemo(args.Length >= 3 ? args[2] : "kharkiv_samples.txt", district);
    return;
}

// ── Симуляция живой тревоги на сэмплах СО ЗВУКОМ:  dotnet run -- sim [район] ──
if (args.Length >= 1 && args[0].Equals("sim", StringComparison.OrdinalIgnoreCase))
{
    RunKharkivSim(args.Length >= 2 ? args[1] : "Салтівський", "kharkiv_samples.txt");
    return;
}

// ── Киевский монитор (оффлайн):  dotnet run -- ky [файл] ────────────────────
if (args.Length >= 1 && args[0].Equals("ky", StringComparison.OrdinalIgnoreCase))
{
    RunKyivDemo(args.Length >= 2 ? args[1] : "kyiv_samples.txt");
    return;
}

// ── Все звуки сирены по типам (оффлайн):  dotnet run -- sounds ──────────────
if (args.Length >= 1 && args[0].Equals("sounds", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("🔊 Звуки тривоги по типах (тримай гучність):\n");
    var demo = new (ThreatKind kind, string name)[]
    {
        (ThreatKind.Ballistic, "Балістика — швидкі панічні пік-пік"),
        (ThreatKind.Missile,   "Ракета/Циркон — середні"),
        (ThreatKind.Kab,       "КАБ — низькі важкі"),
        (ThreatKind.Drone,     "Шахед/БпЛА — класичні"),
        (ThreatKind.Aviation,  "Авіація — низькі"),
    };
    foreach (var (kind, name) in demo)
    {
        Console.WriteLine($"  {ThreatParser.Icon(kind)} {name}");
        SirenNotifier.Play(kind, urgent: false, clear: false);
        Thread.Sleep(4200); // дать мотиву доиграть
    }
    Console.WriteLine("  ✅ Відбій — спокійний акорд");
    SirenNotifier.Play(ThreatKind.Drone, urgent: false, clear: true);
    Thread.Sleep(1900);
    Console.WriteLine("\n✅ Готово. Скажи, який звук підкрутити.");
    return;
}

// ── ДЕМО карты всей Украины стрелками (оффлайн):  dotnet run -- uamap ────────
// Прогоняет типичные посты @raketa_trevoga через парсер и рисует стрелки по стране.
if (args.Length >= 1 && args[0].Equals("uamap", StringComparison.OrdinalIgnoreCase))
{
    // Можно передать файл с РЕАЛЬНЫМ постом:  dotnet run -- uamap post.txt
    if (args.Length >= 2 && File.Exists(args[1]))
    {
        var real = File.ReadAllText(args[1]);
        int drawn = 0;
        foreach (var ev in ThreatParser.Parse(real)) if (tracker.IngestUa(ev, DateTime.Now) is not null) drawn++;
        MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
        Console.WriteLine($"🗺  Карта з твого поста: {Path.GetFullPath(MapPath)}");
        Console.WriteLine($"   Намальовано цілей: {tracker.Active.Count} (розпізнано рядків: {drawn}).");
        foreach (var t in tracker.Active)
            Console.WriteLine($"   • {ThreatTracker.Label(t)} → {t.LastPlace}  ×{t.Amount}  ({t.DistKm:0} км від дому)");
        return;
    }
    // Демо: реальний формат зведення (області без двокрапки, дрібні міста → фолбэк у центр області).
    var post = """
Чернігівщина
2 БпЛА на Славутич

Київщина
1 реактивний БпЛА на Васильків повз Борову

Черкащина
1 реактивний БпЛА на Черкаси

Дніпропетровщина
1 реактивний БпЛА на Тернівку
1 реактивний БпЛА на Губиниху
1 реактивний БпЛА на Царичанку
1 БпЛА на Софіївку

Полтавщина
1 реактивний БпЛА на Нові Санжари
1 реактивний БпЛА на Семенівку

Харківщина
1 реактивний БпЛА на Харків
1 реактивний БпЛА на Нову Водолагу

Миколаївщина
1 реактивний БпЛА на Трихати
2 реактивні БпЛА на Врадіївку
""";
    foreach (var ev in ThreatParser.Parse(post))
        tracker.IngestUa(ev, DateTime.Now);
    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
    Console.WriteLine($"🗺  Карта всієї України зі стрілками: {Path.GetFullPath(MapPath)}");
    Console.WriteLine($"   Намальовано цілей: {tracker.Active.Count}. Відкрий у браузері.");
    Console.WriteLine("   Стрілки: короткі — шахед/БпЛА, довгі — ракети/балістика; напрям — куди летить.");
    return;
}

// ── Тест настоящего ИИ (Claude) на сэмпле:  dotnet run -- ai ─────────────────
if (args.Length >= 1 && args[0].Equals("ai", StringComparison.OrdinalIgnoreCase))
{
    if (advisor is null)
    {
        Console.WriteLine("❌ Немає ключа ІІ. Додай у secrets.local.txt рядок:  ai_api_key=sk-ant-...");
        Console.WriteLine("   (модель за бажанням: ai_model=claude-opus-5  або дешевша  claude-haiku-4-5)");
        return;
    }
    // Реалистичная обстановка: несколько целей по стране + близкая к дому.
    foreach (var p in new[] { "Шахед курсом на Циркуни", "Ракета на Полтаву", "2 БпЛА на Суми" })
        if (p.Contains("Циркун")) tracker.Ingest(KharkivMonitor.Analyze(p, MyDistrict), "Харків", DateTime.Now);
        else foreach (var ev in ThreatParser.Parse(p)) tracker.IngestUa(ev, DateTime.Now);

    Console.WriteLine("🤖 Надсилаю обстановку в ІІ...\n");
    Console.WriteLine(BuildSituation());
    var reply = await advisor.Analyze(BuildSituation());
    if (reply is not null)
    {
        MapWriter.AiText = reply; MapWriter.AiAt = DateTime.Now;
        MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
        Ui.Line(ConsoleColor.Magenta, "🤖 Відповідь ІІ:\n" + reply);
        Console.WriteLine($"\n🗺  map.html оновлено — дивись вкладку ШІ: {Path.GetFullPath(MapPath)}");
    }
    else Console.WriteLine("❌ ІІ не відповів — перевір ключ/мережу (помилка вище).");
    return;
}

// ── Статистика тревог из alarms.csv (оффлайн):  dotnet run -- stats ─────────
if (args.Length >= 1 && args[0].Equals("stats", StringComparison.OrdinalIgnoreCase))
{
    RunStats("alarms.csv");
    return;
}

// ── ДЕМО жизненного цикла угрозы со звуком и картой:  dotnet run -- track ────
// Проигрывает весь путь шахеда к твоему району: далёкие редкие «пики» → чаще →
// вой на подлёте → взрыв → отбой. Параллельно пишет map.html — открой в браузере.
if (args.Length >= 1 && args[0].Equals("track", StringComparison.OrdinalIgnoreCase))
{
    var mapFull = Path.GetFullPath(MapPath);
    Console.WriteLine("🎬 Демо: шахед летить на твій район. Звук + карта.\n");
    Console.WriteLine($"🗺  Відкрий у браузері:  {mapFull}");
    Console.WriteLine("    (сторінка сама оновлюється кожні 3 сек)\n");
    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
    Thread.Sleep(1500);

    void Step(string channel, string post, int pings, int pingGapMs)
    {
        var e = KharkivMonitor.Analyze(post, MyDistrict);
        var up = tracker.Ingest(e, "Харків", DateTime.Now);
        MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
        var note = up?.Note ?? "(пропущено)";
        Ui.Line(ConsoleColor.White, $"📨 {channel}: «{post}»");
        Ui.Line(ConsoleColor.Yellow, $"    → {note}");
        if (up is { } u && u.Threat.Stage == TrackStage.Approaching)
            for (int i = 0; i < pings; i++) { SirenSound.PlayStage(TrackSound.Ping, u.Threat.Kind); Thread.Sleep(pingGapMs); }
        else if (up is { } u2)
        {
            SirenSound.PlayStage(u2.Sound, u2.Threat.Kind);
            Thread.Sleep(3200);
        }
        Console.WriteLine();
    }

    Step("@truexakharkiv", "Шахед курсом на Циркуни",        pings: 3, pingGapMs: 900); // ~9 км, рідко
    Step("@tlknewsua",     "Шахед над Питомником",           pings: 4, pingGapMs: 450); // ~5 км, частіше
    Step("@kharkivlife",   "Шахед над Салтівським районом!", pings: 0, pingGapMs: 0);   // над нами — вой
    Step("@truexakharkiv", "Вибух у Салтівському районі!",   pings: 0, pingGapMs: 0);   // приліт — взрыв

    Ui.Line(ConsoleColor.Green, "✅ Відбій по місту.");
    tracker.Sweep("Харків");
    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
    SirenSound.Play(ThreatKind.Drone, urgent: false, clear: true);
    Thread.Sleep(2000);
    Console.WriteLine($"\n🗺  Карта: {mapFull}\n✅ Демо завершено. Так само працюватиме наживо.");
    return;
}

// ── GPS → район (оффлайн):  dotnet run -- geo <широта> <долгота> ─────────────
//   пример:  dotnet run -- geo 50.05 36.30   → Салтівський
if (args.Length >= 3 && args[0].Equals("geo", StringComparison.OrdinalIgnoreCase)
    && double.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var gLat)
    && double.TryParse(args[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var gLon))
{
    var (name, km) = GeoLocate.Nearest(gLat, gLon);
    if (km > 15)
        Console.WriteLine($"📍 {gLat}, {gLon} → схоже, поза Харковом (~{km:0} км до найближчого району). Обери місто вручну.");
    else
        Console.WriteLine($"📍 {gLat}, {gLon} → твій район: {name}  (~{km:0.0} км до центру району)");
    return;
}

// ── Тестовий пост у канал з картою (оффлайн):  dotnet run -- channeltest ─────
if (args.Length >= 1 && args[0].Equals("channeltest", StringComparison.OrdinalIgnoreCase))
{
    var ct = SecretOpt("telegram_channel_bot_token");
    var cn = SecretOpt("telegram_channel");
    if (string.IsNullOrWhiteSpace(ct) || string.IsNullOrWhiteSpace(cn))
    {
        Console.WriteLine("❌ Немає telegram_channel_bot_token / telegram_channel у secrets.local.txt");
        return;
    }
    Console.WriteLine($"📢 Тестовий пост у {cn} ...");
    var png = await BuildBotMapPng(channel: true);
    var poster = new ChannelPoster(ct!, cn!);
    await poster.Post(
        "🔔 Тест WarAlarm — карта загроз.\nОбласті: 🔴 офіційна тривога · 🟡 часткова. Наші цілі — точками.\n\n🤖 Авто-дані · перевіряйте офіційні джерела",
        png);
    Console.WriteLine(png is null ? "⚠ карта не згенерувалась (надіслано лише текст)." : "✅ Надіслано: карта + текст.");
    return;
}

using var client = new Client(Config);
var me = await client.LoginUserIfNeeded();
Console.WriteLine($"✅ Вошли как: {me.first_name} {me.last_name} (@{me.username})\n");

// Час останньої будь-якої активності від Telegram (апдейт або keepalive). Сторож нижче
// стежить за ним: якщо зв'язок «завмер» — примусово перепідключаємось. Це рятує від
// тихого зависання, коли сокет обірвався, а пасивний слухач не переконектився (баг 12.09).
var lastTgActivity = DateTime.Now;

// ── Каналы оповещения: консоль + сирена (Windows) + push на телефон (ntfy) ────
var notifiers = new List<INotifier> { new ConsoleNotifier() };
if (OperatingSystem.IsWindows()) notifiers.Add(new SirenNotifier());
var ntfyTopic = SecretOpt("ntfy_topic");
MapWriter.Config = MapWriter.Config! with { Ntfy = !string.IsNullOrWhiteSpace(ntfyTopic) };
if (!string.IsNullOrWhiteSpace(ntfyTopic))
{
    notifiers.Add(new NtfyNotifier(ntfyTopic));
    Console.WriteLine($"📲 Push ВКЛ → ntfy topic «{ntfyTopic}». Подпишись на него в приложении ntfy.\n");
}
else
    Console.WriteLine("📲 Push ВЫКЛ. Чтобы телефон звонил — добавь строку  ntfy_topic=...  в secrets.local.txt\n");

// ── Telegram-бот: підписка /start, /status, авто-сповіщення підписникам ───────
var botToken = SecretOpt("telegram_bot_token");
if (!string.IsNullOrWhiteSpace(botToken))
{
    var features = new BotFeatures(
        StatusAt: StatusAt,
        RiskAt: RiskAt,
        AlarmAt: AlarmAt,
        MapPng: () => BuildBotMapPng(),
        Ai: BuildBotAi,
        Stats: BuildBotStats,
        Alerts: BuildBotAlerts);
    var bot = new TelegramBot(botToken, features);
    _ = bot.RunAsync();                 // команди + персональні тривоги по місту
    Console.WriteLine($"🤖 Telegram-бот ВКЛ (підписників: {bot.SubscriberCount}). Напиши боту /start у Telegram.\n");
}
else
    Console.WriteLine("🤖 Telegram-бот ВИКЛ. Додай  telegram_bot_token=...  у secrets.local.txt (токен від @BotFather).\n");

// ── Новинний канал (ОКРЕМИЙ бот) + офіційні тривоги в API ────────────────────
var channelToken = SecretOpt("telegram_channel_bot_token");
var channelName = SecretOpt("telegram_channel");
ChannelPoster? channelPoster =
    (!string.IsNullOrWhiteSpace(channelToken) && !string.IsNullOrWhiteSpace(channelName))
        ? new ChannelPoster(channelToken!, channelName!) : null;
if (channelPoster is not null)
    Console.WriteLine($"📢 Новинний канал ВКЛ → {channelName} (вся Україна, виділення Київ/Харків/Дніпро).\n");
else
    Console.WriteLine("📢 Канал ВИКЛ. Додай  telegram_channel_bot_token=...  і  telegram_channel=@name  у secrets.local.txt.\n");
// Денні лічильники для авто-зведення в канал (оголошення тривог по областях
// + помічені цілі за типом/місцем). Накопичуються в циклах, скидаються після зведення.
var dayRed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var daySeen = new HashSet<string>();
var ppoSeen = new Dictionary<string, DateTime>(); // зона → коли востаннє згадали роботу ППО
DateTime? nuclearUntil = null; // ☢️ доки активна радіаційна/ядерна небезпека (30 хв від згадки)
DateTime? lastLaunch = null;   // 🚀 коли востаннє попереджали про пуски (щоб не спамити)

// Фоновий цикл: щохвилини оновлює офіційні тривоги в state (API) і постить новини в канал.
_ = RunAlertsLoop(channelPoster);
// Живі новини каналу: короткі тексти цілей + карта окремо (раз на 10 хв) + денне зведення.
if (channelPoster is not null)
{
    _ = RunThreatNews(channelPoster);
    _ = RunMapLoop(channelPoster);
    _ = RunDailyDigest(channelPoster);
}

// Карта в канал раз на 10 хв — але лише коли є що показати (цілі або тривоги),
// щоб не постити порожню карту під час повного спокою.
async Task RunMapLoop(ChannelPoster channel)
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(10));
        try
        {
            var snap = await OfficialAlerts.FetchFull();
            int liveN = tracker.Active.Where(t => t.Pos is not null).Sum(t => t.Amount); // сумарно бортів у повітрі
            if (liveN == 0 && snap.RedUa.Count == 0 && snap.YellowUa.Count == 0) continue;
            await channel.Post(MapCaption(snap, liveN), await BuildBotMapPng(channel: true));
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ maploop: " + ex.Message); }
    }
}

// Фоновий цикл офіційних тривог: оновлює API (state.alerts) + новини в канал при змінах.
async Task RunAlertsLoop(ChannelPoster? channel)
{
    var prevRed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var prevActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // області з тривогою (полігони)
    bool seeded = false, activeSeeded = false;
    while (true)
    {
        try
        {
            var snap = await OfficialAlerts.FetchFull();

            // ── Офіційний відбій прибирає цілі (щоб шахеди не висіли після відбою) ──
            // ByPolygon = області з червоною/жовтою тривогою. Коли область виходить із
            // цього набору — офіційно відбій, прибираємо цілі, що фізично в ній.
            var nowActive = new HashSet<string>(snap.ByPolygon.Keys, StringComparer.OrdinalIgnoreCase);
            if (activeSeeded)
            {
                // Тільки «затихлі» цілі (без оновлень >2 хв): жива ціль, яку радар ще
                // веде, лишається — інакше на початку нальоту (тривогу ще не оголосили)
                // ми б стирали свіже раннє попередження.
                bool Quiet(Threat t) => (DateTime.Now - t.LastUpdate) > TimeSpan.FromMinutes(2);
                int removed = 0;
                if (nowActive.Count == 0)
                    removed = tracker.SweepWhere(t => t.Pos is not null && Quiet(t)).Count; // ніде тривоги → знімаємо затихлі
                else
                    foreach (var obl in prevActive.Where(o => !nowActive.Contains(o)))
                        removed += tracker.SweepWhere(t => t.Pos is { } p && Quiet(t) && OblastGeo.PolyName(p.lat, p.lon) == obl).Count;
                if (removed > 0)
                {
                    Ui.Line(ConsoleColor.Green, $"✅ Офіційний відбій — прибрано цілей: {removed}");
                    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
                }
            }
            prevActive = nowActive;
            activeSeeded = true;
            // 1) API: покласти тривоги у state → публікується у Firebase (state/alerts).
            MapWriter.AlertsJson = BuildAlertsJson(snap);
            // Розділення тривог по районах + області, що горять цілком (для карти map.html).
            MapWriter.RaionsJson = snap.ByRaion is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(snap.ByRaion) : "";
            MapWriter.OblastWholeJson = snap.ByOblastWhole is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(snap.ByOblastWhole) : "";
            MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
            // 2) Канал: новини при змінах.
            var nowRed = new HashSet<string>(snap.RedUa, StringComparer.OrdinalIgnoreCase);
            if (!seeded) { prevRed = nowRed; seeded = true; }
            else if (channel is not null)
            {
                var newly = snap.RedUa.Where(r => !prevRed.Contains(r)).ToList();
                var cleared = prevRed.Where(r => !nowRed.Contains(r)).ToList();
                foreach (var r in newly) dayRed[r] = dayRed.GetValueOrDefault(r) + 1; // для зведення
                // Тривога / відбій → перемальовуємо й постимо карту разом із текстом.
                if (newly.Count > 0)
                    await channel.Post(BuildAlarmPost(newly, snap), await BuildBotMapPng(channel: true));
                else if (cleared.Count > 0)
                    await channel.Post(BuildClearPost(cleared, nowRed.Count), await BuildBotMapPng(channel: true));
                prevRed = nowRed;
            }
            else prevRed = nowRed;
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ alerts: " + ex.Message); }
        await Task.Delay(TimeSpan.FromSeconds(20));
    }
}

// Денне авто-зведення в канал — раз на добу, о ~09:00, потім лічильники скидаються.
async Task RunDailyDigest(ChannelPoster channel)
{
    var lastDay = DateTime.Now.Date;
    bool postedToday = DateTime.Now.Hour >= 9; // запуск після 9 → чекаємо завтра
    while (true)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(5)); } catch { }
        var now = DateTime.Now;
        if (now.Date != lastDay) { lastDay = now.Date; postedToday = false; }
        if (!postedToday && now.Hour >= 9)
        {
            postedToday = true;
            try { await channel.Post(BuildDigest(), null); } catch (Exception ex) { Console.Error.WriteLine("⚠ digest: " + ex.Message); }
            dayRed.Clear(); daySeen.Clear();
        }
    }
}

string BuildDigest()
{
    var sb = new StringBuilder();
    sb.AppendLine("📊 <b>Зведення за добу</b>");
    sb.AppendLine();
    int announces = dayRed.Values.Sum();
    sb.AppendLine($"🔴 Тривог по областях: {announces}");
    if (dayRed.Count > 0)
        sb.AppendLine("🔥 Найгарячіше: " + H(string.Join(", ",
            dayRed.OrderByDescending(k => k.Value).Take(3).Select(k => $"{ShortReg(k.Key)} ×{k.Value}"))));
    var byKind = daySeen.GroupBy(x => x.Split('|')[0]).ToDictionary(g => g.Key, g => g.Count());
    if (byKind.Count > 0)
        sb.AppendLine("🛸 Цілей помічено: " + H(string.Join(" · ",
            byKind.OrderByDescending(k => k.Value).Select(k => $"{KindShortEnum(k.Key)} {k.Value}"))));
    if (announces == 0 && byKind.Count == 0) sb.AppendLine("😌 Доба була відносно спокійною.");
    sb.AppendLine();
    sb.Append("🕊 Бережіть себе 🇺🇦 · автозведення WarAlarm");
    return sb.ToString();
}

// HTML-екранування динамічного тексту для постів у канал (parse_mode=HTML).
static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
// Згадка про роботу ППО в пості локального каналу («працює ППО», «робота ППО», зенітки).
static bool PpoMention(string s) => Regex.IsMatch(s.ToLowerInvariant(), @"\bппо\b|протиповітрян|зенітн");

// ☢️ Явна РАДІАЦІЙНА/ЯДЕРНА НЕБЕЗПЕКА — тільки офіційне формулювання сигналу, НЕ новини.
// Найвищий пріоритет → хибний спрацьовує неприпустимий, тож фільтр суворий:
//   • ловимо формальні сигнали: «радіаційна небезпека/тривога», «ядерний удар/вибух/тривога»;
//   • відкидаємо політичну балаканину: «погроза/ризик/заява/навчання/шантаж/риторика».
static bool NuclearMention(string s)
{
    var l = s.ToLowerInvariant();
    bool strong = Regex.IsMatch(l,
        @"радіаційн\w*\s+(небезпек|тривог|загроз)|оголош\w*\s+радіац|ядерн\w*\s+(удар|вибух|тривог)|радіоактивн\w*\s+(хмар|заражен|викид)");
    bool newsy = Regex.IsMatch(l,
        @"погроз|погрож|ризик|заяв|може застосув|навчанн|навчальн|шантаж|риторик|імовірн|ймовірн|аналіт|експерт|сценарі");
    return strong && !newsy;
}

// 🚀 Пуски ракет/КАБ/ШАХЕДІВ, зліт носіїв, ракетна небезпека — окреме попередження.
static bool LaunchMention(string s)
{
    var l = s.ToLowerInvariant();
    bool strong = Regex.IsMatch(l,
          @"ракетн\w*\s+небезпек"
        + @"|(пуск|запуск|запустил|старт|злет|піднял|зафіксован\w*\s+пуск)\w*[^.\n]{0,40}(ракет|каб|крилат|шахед|шахід|бпла|дрон|геран|мопед|ударн|носі|міг|ту-?\d|су-?\d)"
        + @"|(\d{1,3})\s*[^.\n]{0,15}(шахед|шахід|ударн\w*\s+бпла)[^.\n]{0,25}(запуст|пуск|курс на україн|на україн|стартув)");
    bool newsy = Regex.IsMatch(l, @"можлив|ймовірн|імовірн|ризик|погроз|навчанн|аналіт|сценарі|минул|вчора|за добу|за ніч.{0,10}(підсум|збит)|збили|збито|знищено");
    return strong && !newsy;
}

// Кількість із поста про пуск: «~30 шахедів», «30 ударних БпЛА», «пуск 30 …».
static int? LaunchCount(string s)
{
    var l = s.ToLowerInvariant();
    var m = Regex.Match(l, @"(\d{1,3})\s*(?:[a-zа-яїієґ'ʼ\-]+\s+){0,2}(шахед|шахід|бпла|дрон|геран|ціл|борт|од)");
    if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is > 0 and <= 1000) return n;
    var m2 = Regex.Match(l, @"пуск\w*\s*~?\s*(\d{1,3})");
    if (m2.Success && int.TryParse(m2.Groups[1].Value, out var n2) && n2 is > 0 and <= 1000) return n2;
    return null;
}
static string ShortReg(string ua) => ua.Replace(" область", "").Replace("м. ", "").Trim();
static string KindShortEnum(string k) => k switch
{
    "Drone" => "шахеди", "Missile" => "ракети", "Ballistic" => "балістика",
    "Kab" => "КАБи", "Aviation" => "авіація", _ => "цілі",
};

static string BuildAlertsJson(OfficialAlerts.Snapshot s)
{
    static string Q(string x) => "\"" + x.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    string Arr(IEnumerable<string> xs) => "[" + string.Join(",", xs.Select(Q)) + "]";
    var by = new StringBuilder("{");
    bool first = true;
    foreach (var kv in s.ByPolygon)
    {
        if (!first) by.Append(',');
        first = false;
        by.Append(Q(kv.Key)).Append(':').Append(Q(kv.Value));
    }
    by.Append('}');
    var ts = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
    return $"{{\"ts\":{ts},\"redCount\":{s.RedUa.Count},\"yellowCount\":{s.YellowUa.Count},"
         + $"\"red\":{Arr(s.RedUa)},\"yellow\":{Arr(s.YellowUa)},\"byOblast\":{by}}}";
}

static string BigLabel(string ua) =>
    ua.Contains("Київ") ? "КИЇВ" : ua.Contains("Харків") ? "ХАРКІВ" : ua.Contains("Дніпро") ? "ДНІПРО" : ua;

static string BuildAlarmPost(List<string> newly, OfficialAlerts.Snapshot s)
{
    var sb = new StringBuilder();
    if (s.RedUa.Count + s.YellowUa.Count >= 18)
    {
        sb.AppendLine("🚨🚨 <b>МАСШТАБНА ТРИВОГА</b> — майже вся Україна!");
        sb.AppendLine("🛫 Схоже на зліт МіГ-31К або балістику. Усі в укриття!");
        sb.AppendLine();
    }
    var big = newly.Where(r => r.Contains("Київ") || r.Contains("Харків") || r.Contains("Дніпро"))
                   .Select(BigLabel).Distinct().ToList();
    if (big.Count > 0)
        sb.AppendLine("🚨 <b>" + H(string.Join(" · ", big)) + "</b> — тривога!");
    sb.AppendLine("🔴 <b>Повітряна тривога</b>");
    sb.AppendLine("📍 " + H(string.Join(", ", newly.Select(ShortReg))));
    sb.AppendLine();
    sb.AppendLine($"🗺 По країні: 🔴 {s.RedUa.Count} обл · 🟡 {s.YellowUa.Count} обл");
    sb.AppendLine("⚠️ Прямуйте в укриття, бережіть себе 🇺🇦");
    sb.Append("🤖 WarAlarm · звіряйтесь з офіційними джерелами");
    return sb.ToString();
}

// Підпис до карти в канал: пояснює обстановку (а не гола картинка). При масовій
// тривозі майже по всій країні — окреме попередження (типова ознака зльоту МіГ-31К
// чи балістичної загрози).
static string MapCaption(OfficialAlerts.Snapshot s, int targets)
{
    int red = s.RedUa.Count, yel = s.YellowUa.Count;
    bool mass = red + yel >= 18; // майже вся країна під тривогою одночасно
    var sb = new StringBuilder();
    if (mass)
    {
        sb.AppendLine("⚠️ <b>МАСШТАБНА ТРИВОГА</b> — майже вся Україна одночасно.");
        sb.AppendLine("🛫 Так буває при зльоті МіГ-31К або балістиці. Будьте в укритті.");
        sb.AppendLine();
    }
    sb.AppendLine("🗺 <b>Карта тривог</b>");
    sb.AppendLine($"🔴 {red} обл · 🟡 {yel} обл · 🛸 у повітрі: ~{targets}");
    if (red > 0)
        sb.AppendLine("🔥 Тривога: " + H(string.Join(", ", s.RedUa.Take(12).Select(ShortReg)))
            + (red > 12 ? "…" : ""));
    sb.Append("🤖 Авто · звіряйтесь з офіційними джерелами");
    return sb.ToString();
}

static string BuildClearPost(List<string> cleared, int remaining)
{
    var sb = new StringBuilder();
    if (remaining == 0)
    {
        sb.AppendLine("🟢 <b>ВІДБІЙ</b> — по всій країні чисто! 🕊");
        sb.AppendLine("✅ " + H(string.Join(", ", cleared.Select(ShortReg))));
        sb.Append("🤖 WarAlarm · бережіть себе 🇺🇦");
        return sb.ToString();
    }
    sb.AppendLine("🟢 <b>ВІДБІЙ</b>");
    sb.AppendLine("✅ " + H(string.Join(", ", cleared.Select(ShortReg))));
    sb.AppendLine();
    sb.AppendLine($"Ще під тривогою: 🔴 {remaining} обл.");
    sb.Append("🤖 WarAlarm");
    return sb.ToString();
}

// Живі новини: щохвилини дивимось активні цілі; коли картина ЗМІНИЛАСЬ і минуло
// ≥2 хв від минулого посту — публікуємо дайджест (по типах, з місцями) + карту.
async Task RunThreatNews(ChannelPoster channel)
{
    string lastKey = "";
    var lastPost = DateTime.MinValue;
    while (true)
    {
        // Швидко: реактивні цілі перетинають місто за ~30 с, тож перевіряємо
        // кожні 5 с. Перша поява → пост за ≤5 с; під час налёту оновлення ~20 с.
        await Task.Delay(TimeSpan.FromSeconds(5));
        try
        {
            var active = tracker.Active.Where(t => t.Pos is not null).ToList();
            if (active.Count == 0) continue;
            foreach (var t in active) daySeen.Add($"{t.Kind}|{t.LastPlace}"); // для зведення
            // Стадія у ключі: перехід наближається→приліт→далі має дати НОВИЙ пост
            // (для живих коротких оновлень на Київ/Харків: «💥 Гучно», «🇺🇦 Далі на…»).
            var key = string.Join("|", active
                .Select(t => $"{t.Kind}:{t.LastPlace}:{t.Amount}:{t.Stage}")
                .OrderBy(x => x));
            if (key == lastKey) continue;                                  // нічого нового
            if (DateTime.Now - lastPost < TimeSpan.FromSeconds(20)) continue; // не спамимо
            lastKey = key;
            lastPost = DateTime.Now;
            // Налёт на велике місто (Київ/Харків/Дніпро) → короткий формат; інакше — по областях.
            var raidCities = active.Select(NearBigCity).Where(c => c != null).Distinct().ToList();
            // Міста, де щойно (≤3 хв) згадали роботу ППО — для банера «🛡 Працює ППО».
            var ppoNow = new HashSet<string>(
                ppoSeen.Where(kv => DateTime.Now - kv.Value < TimeSpan.FromMinutes(3)).Select(kv => kv.Key));
            var post = raidCities.Count > 0
                ? BuildCityRaidPost(active, raidCities!, ppoNow)
                : BuildOblastPost(active);
            await channel.Post(post, null); // текст; карта — окремо (10 хв)
        }
        catch (Exception ex) { Console.Error.WriteLine("⚠ threatnews: " + ex.Message); }
    }
}

static string KindShort(ThreatKind k) => k switch
{
    ThreatKind.Ballistic => "Балістика",
    ThreatKind.Missile => "Ракета",
    ThreatKind.Kab => "КАБ",
    ThreatKind.Aviation => "Авіація",
    ThreatKind.Drone => "БпЛА",
    _ => "Ціль",
};

// Велике місто (Київ/Харків/Дніпро) у радіусі ~35 км → «режим налёту».
static string? NearBigCity(Threat t)
{
    if (t.Pos is not { } p) return null;
    // ВАЖЛИВО: обласна ціль («Київ (обл.)») прив'язана до центру області (≈ центр
    // міста) і хибно вмикала «КИЇВ — ЗАГРОЗА!». Режим налёту — лише для ТОЧНИХ цілей,
    // не для обласних згадок. Інакше канал кричить фейкову тривогу на місто.
    if (t.LastPlace.EndsWith("(обл.)")) return null;
    var big = new (string name, double lat, double lon)[]
    {
        ("Київ", 50.4501, 30.5234), ("Харків", 49.9935, 36.2304), ("Дніпро", 48.4647, 35.0462),
    };
    foreach (var c in big)
        if (ThreatGeo.Km(p, (c.lat, c.lon)) <= 35) return c.name;
    return null;
}

// Звичайний формат (вся Україна): згруповано по областях —  «🛩 БпЛА на Луцьк».
static string BuildOblastPost(List<Threat> active)
{
    var sb = new StringBuilder();
    var byRegion = active.Where(t => t.Pos is not null)
        .GroupBy(t => OblastGeo.Region(t.Pos!.Value.lat, t.Pos!.Value.lon))
        .OrderByDescending(g => g.Sum(t => t.Amount))
        .Take(12).ToList();
    int total = active.Where(t => t.Pos is not null).Sum(t => t.Amount);
    sb.Append("🛸 <b>У повітрі зараз: ~").Append(total).AppendLine("</b>");
    sb.AppendLine("(рух по областях)");
    sb.AppendLine();
    foreach (var g in byRegion)
    {
        sb.Append("📍 <b>").Append(H(g.Key)).AppendLine("</b>");
        var seen = new HashSet<string>();
        int lines = 0;
        foreach (var t in g.OrderByDescending(t => t.Amount))
        {
            var place = t.LastPlace.Length > 0 ? t.LastPlace : g.Key;
            if (!seen.Add($"{t.Kind}|{place}")) continue;
            if (lines++ >= 8) { sb.AppendLine("   …"); break; }
            sb.Append("   ").Append(ThreatParser.Icon(t.Kind)).Append(' ').Append(KindShort(t.Kind))
              .Append(t.Amount > 1 ? $" ×{t.Amount}" : "").Append(" → ").AppendLine(H(place));
        }
        sb.AppendLine();
    }
    sb.Append("🤖 Авто · звіряйтесь з офіційними джерелами");
    return sb.ToString();
}

// Режим налёту на велике місто: коротко — місто + райони/напрямки.
// [ppoCities] — міста, де щойно згадали роботу ППО (показуємо банер «🛡 Працює ППО»).
static string BuildCityRaidPost(List<Threat> active, List<string> cities, HashSet<string>? ppoCities = null)
{
    // Стиль локальних каналів («Реальний Київ»): короткі живі рядки по стадії —
    // «🛩 Троєщина», «💥 Гучно», «🇺🇦 Далі на Теремки». Швидкість читання під нальотом.
    var sb = new StringBuilder();
    foreach (var city in cities)
    {
        var near = active.Where(t => NearBigCity(t) == city).OrderBy(t => t.DistKm ?? 1e9).ToList();
        sb.Append("🚨 <b>").Append(H(city.ToUpper())).AppendLine("</b>");
        if (ppoCities != null && ppoCities.Contains(city)) sb.AppendLine("🛡 <b>Працює ППО</b>");
        var seen = new HashSet<string>();
        int n = 0;
        foreach (var t in near)
        {
            var line = RaidLine(t);
            if (!seen.Add(line)) continue;
            if (n++ >= 8) { sb.AppendLine("…"); break; }
            sb.AppendLine(line);
        }
        sb.AppendLine();
    }
    sb.Append("🤖 WarAlarm");
    return sb.ToString();
}

// Один короткий рядок про ціль у стилі локальних каналів.
static string RaidLine(Threat t)
{
    var p = t.LastPlace;
    return t.Stage switch
    {
        TrackStage.Impact   => p.Length > 0 ? $"💥 Гучно — {H(p)}" : "💥 Гучно",
        TrackStage.Passed   => p.Length > 0 ? $"🇺🇦 Далі на {H(p)}" : "🇺🇦 Пройшла далі",
        TrackStage.Overhead => p.Length > 0 ? $"⚠️ Над {H(p)}" : "⚠️ Над районом",
        TrackStage.Imminent => p.Length > 0 ? $"❗ На підльоті — {H(p)}" : "❗ На підльоті",
        _                   => p.Length > 0 ? $"{ThreatParser.Icon(t.Kind)} {H(p)}" : $"{ThreatParser.Icon(t.Kind)} ціль",
    };
}

// Статус по ТОЧЦІ (місто користувача): ймовірність + найближчі цілі.
string StatusAt(double lat, double lon)
{
    var pt = (lat, lon);
    var list = tracker.Active.Where(t => t.Pos is not null).ToList();
    var a = RiskModel.For(pt, list.Select(t => (t, t.Amount)).ToList());
    var sb = new StringBuilder();
    sb.AppendLine($"🎯 Ймовірність удару: {a.Percent}% — {a.Verdict}");
    var near = list.Select(t => (t, km: ThreatGeo.Km(t.Pos!.Value, pt)))
                   .OrderBy(x => x.km).Take(6).ToList();
    if (near.Count == 0 || near[0].km > 70)
        sb.AppendLine("🟢 Активних цілей поруч немає.");
    else
    {
        sb.AppendLine("\nНайближчі цілі:");
        foreach (var (t, km) in near.Where(x => x.km <= 250))
            sb.AppendLine($"• {ThreatTracker.Label(t)} — {km:0} км, {ThreatTracker.StageLabel(t.Stage)}"
                + (t.LastPlace.Length > 0 ? $", біля {t.LastPlace}" : ""));
    }
    return sb.ToString();
}

// Малює поточну карту загроз у PNG: області за ОФІЦІЙНОЮ тривогою (red/yellow),
// цілі — детальні силуети за курсом (як у застосунку), міста з підписами.
// channel=true → нейтральний заголовок для каналу (без дому й «ймовірності»).
async Task<byte[]?> BuildBotMapPng(bool channel = false)
{
    var live = tracker.Active.Where(t => t.Pos is not null).ToList();
    var snap = await OfficialAlerts.FetchFull();
    if (channel)
    {
        var subtitle = $"Тривога: {snap.RedUa.Count} обл · часткова: {snap.YellowUa.Count} · цілей: {live.Count}";
        return MapImage.Render((HomeLat, HomeLon), live, "WarAlarm · Карта тривог", subtitle, snap.ByPolygon,
            showHome: false, raionLevels: snap.ByRaion, oblastWhole: snap.ByOblastWhole);
    }
    var a = RiskModel.For((HomeLat, HomeLon), live.Select(t => (t, t.Amount)).ToList());
    return MapImage.Render((HomeLat, HomeLon), live, $"WarAlarm · {MyCity}",
        $"Ймовірність удару: {a.Percent}% — {a.Verdict}", snap.ByPolygon,
        showHome: true, raionLevels: snap.ByRaion, oblastWhole: snap.ByOblastWhole);
}

static string KindName(string cat) => cat switch
{
    "shahed" => "Шахед", "missile" => "Ракета", "balist" => "Балістика",
    "kab" => "КАБ", "avia" => "Авіація", _ => "Інше",
};

// Детальна ймовірність по ТОЧЦІ: відсоток + типи + головні чинники.
string RiskAt(double lat, double lon)
{
    var list = tracker.Active.Where(t => t.Pos is not null).Select(t => (t, t.Amount)).ToList();
    var a = RiskModel.For((lat, lon), list);
    var sb = new StringBuilder();
    sb.AppendLine($"🎯 Ймовірність удару: {a.Percent}% — {a.Verdict}");
    if (a.ByKind.Count > 0)
    {
        sb.AppendLine("\nЗа типами:");
        foreach (var kv in a.ByKind.OrderByDescending(k => k.Value))
            sb.AppendLine($"• {KindName(kv.Key)}: {kv.Value}%");
    }
    if (a.Factors.Count > 0)
    {
        sb.AppendLine("\nГоловні чинники:");
        foreach (var f in a.Factors.Take(4))
            sb.AppendLine($"• {f.label} — {f.distKm:0} км ({f.percent}%)");
    }
    if (a.Percent == 0) sb.AppendLine("Наразі прямих загроз не видно.");
    return sb.ToString();
}

// Чи тривога поруч із ТОЧКОЮ (для персональних сповіщень у боті).
(bool alarm, string text) AlarmAt(double lat, double lon)
{
    var pt = (lat, lon);
    var list = tracker.Active.Where(t => t.Pos is not null).ToList();
    if (list.Count == 0) return (false, "");
    Threat? top = null; double nearest = double.MaxValue;
    foreach (var t in list)
    {
        var d = ThreatGeo.Km(t.Pos!.Value, pt);
        if (d < nearest) { nearest = d; top = t; }
    }
    var a = RiskModel.For(pt, list.Select(t => (t, t.Amount)).ToList());
    bool alarm = nearest <= 25 || a.Percent >= 55;
    string text = top != null ? $"{ThreatTracker.Label(top)} ~{nearest:0} км" : "";
    return (alarm, text);
}

// Розумний аналіз обстановки від справжнього ІІ (Claude/Gemini), якщо ключ заданий.
async Task<string> BuildBotAi()
{
    if (advisor is null)
        return "🤖 ІІ не налаштовано. Додай  ai_api_key=...  у secrets.local.txt.";
    var reply = await advisor.Analyze(BuildSituation());
    return reply is { Length: > 0 } ? "🤖 " + reply : "ІІ не відповів — перевір ключ/мережу.";
}

// Аналітика з журналу alarms.csv: сьогодні / тиждень / усього / пік / за типами.
string BuildBotStats()
{
    if (!File.Exists("alarms.csv")) return "📈 Ще немає даних — журнал зʼявиться після перших тривог.";
    var rows = File.ReadAllLines("alarms.csv").Select(l => l.Split(',')).Where(c => c.Length >= 5).ToList();
    if (rows.Count == 0) return "📈 Журнал порожній.";
    var today = DateTime.Now.ToString("yyyy-MM-dd");
    var weekAgo = DateTime.Now.AddDays(-6).ToString("yyyy-MM-dd");
    bool IsAlarm(string[] c) => c[4] is "alarm" or "urgent";
    int todayA = rows.Count(c => IsAlarm(c) && c[0] == today);
    int week = rows.Count(c => IsAlarm(c) && string.CompareOrdinal(c[0], weekAgo) >= 0);
    int total = rows.Count(IsAlarm);
    int impacts = rows.Count(c => c[4] == "impact");
    var hours = new int[24];
    foreach (var c in rows.Where(IsAlarm))
        if (c[1].Length >= 2 && int.TryParse(c[1].AsSpan(0, 2), out var h)) hours[h % 24]++;
    int peak = 0; for (int i = 1; i < 24; i++) if (hours[i] > hours[peak]) peak = i;
    var sb = new StringBuilder();
    sb.AppendLine("📈 Аналітика тривог:");
    sb.AppendLine($"• Сьогодні: {todayA}");
    sb.AppendLine($"• За тиждень: {week}");
    sb.AppendLine($"• Усього: {total} (прильотів: {impacts})");
    if (total > 0) sb.AppendLine($"• Найнебезпечніша година: {peak:00}:00");
    var byType = rows.Where(IsAlarm).GroupBy(c => c[3]).OrderByDescending(g => g.Count()).Take(4).ToList();
    if (byType.Count > 0)
    {
        sb.AppendLine("• За типами:");
        foreach (var g in byType) sb.AppendLine($"   {g.Key} — {g.Count()}");
    }
    return sb.ToString();
}

// Офіційні тривоги по областях (free feed vadimklimenko) — чи є тривога у твоїй.
Task<string> BuildBotAlerts() => OfficialAlerts.Summary(MyCity, MyOblast);

// Рассылает тревогу во все подключённые каналы (падение одного не роняет остальные).
async Task Alert(AlarmInfo info)
{
    foreach (var n in notifiers)
        try { await n.Notify(info); } catch (Exception ex) { Console.Error.WriteLine("notify fail: " + ex.Message); }
}

// Необязательный секрет (без запроса в консоли, в отличие от Secret()).
string? SecretOpt(string key) => secrets != null && secrets.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

// ── Один сигнал на налёт + отбой (без повторных пушей) ────────────────────────
// Первая угроза городу = пуш+сирена. Дальше во время налёта — ТОЛЬКО консоль, телефон
// молчит (никаких «каждые 5 минут»). При «відбій» — один спокойный зелёный пуш,
// но лишь если тревога длилась дольше 90 сек (чтобы не дёргать на «угроза→сразу чисто»).
var alarmSince = new Dictionary<string, DateTime>();
var seenMsgs = new Dictionary<string, DateTime>(); // для дедупа одинаковых постов

async Task Notify(string zone, AlarmInfo info, bool clear)
{
    if (clear)
    {
        if (alarmSince.TryGetValue(zone, out var since))
        {
            alarmSince.Remove(zone);
            if (DateTime.Now - since >= TimeSpan.FromSeconds(OtboiMinSec))
            {
                LogAlarm(zone, info with { Clear = true });
                await Alert(info with { Clear = true });
            }
        }
        return;
    }
    if (!alarmSince.ContainsKey(zone))
    {
        alarmSince[zone] = DateTime.Now;
        LogAlarm(zone, info);
        await Alert(info); // первый сигнал налёта: консоль + сирена + пуш
    }
    else
        Ui.Line(ConsoleColor.DarkYellow, $"   ↳ [{info.Time}] {info.Source} · {info.Headline}  (триває тривога — телефон мовчить)");
}

// Пишет каждую тревогу/отбой в alarms.csv (для команды `stats`).
void LogAlarm(string zone, AlarmInfo info)
{
    try
    {
        var tag = info.Clear ? "clear" : info.Urgent ? "urgent" : "alarm";
        var head = info.Headline.Replace("\"", "'");
        File.AppendAllText("alarms.csv",
            $"{DateTime.Now:yyyy-MM-dd},{DateTime.Now:HH:mm:ss},{zone},{info.Kind},{tag},\"{head}\"\n");
    }
    catch { /* лог не критичен */ }
}

// ── Тест оповещения:  dotnet run -- push ────────────────────────────────────
if (args.Length >= 1 && args[0].Equals("push", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("📲 Отправляю тестовую тревогу во все каналы (консоль + сирена + push)...");
    await Alert(new AlarmInfo(DateTime.Now.ToString("HH:mm:ss"), "@test",
        "🔴 ТЕСТ · загроза у ТВОЄМУ районі 🛩 → Салтівський",
        "Це перевірка WarAlarm. Якщо телефон дзвонить — звʼязок працює. 🇺🇦", ThreatKind.Drone, true));
    await Task.Delay(2000); // дать пушу/сирене уйти
    Console.WriteLine("✅ Отправлено. Проверь телефон (и звук ПК).");
    return;
}

// ── Режим DUMP: вытянуть последние посты канала для анализа формата ───────────
//   dotnet run -- dump <канал> [сколько]
//   пример:  dotnet run -- dump napramok 40
if (args.Length >= 2 && args[0].Equals("dump", StringComparison.OrdinalIgnoreCase))
{
    var uname = args[1].TrimStart('@');
    var count = args.Length >= 3 && int.TryParse(args[2], out var n) ? n : 30;
    Console.WriteLine($"⬇ Тяну последние {count} постов из @{uname} ...\n");

    var resolved = await client.Contacts_ResolveUsername(uname);
    var channel = resolved.chats.Values.OfType<Channel>().FirstOrDefault();
    if (channel is null)
    {
        Console.WriteLine("❌ Публичный канал не найден по этому имени.");
        return;
    }

    var history = await client.Messages_GetHistory(channel, limit: count);
    foreach (var msg in history.Messages.OfType<Message>().Reverse())
    {
        if (string.IsNullOrWhiteSpace(msg.message)) continue;
        Console.WriteLine($"[{msg.date:dd.MM HH:mm}] ────────────────────────");
        Console.WriteLine(msg.message.Replace("\r", "").Trim());
        Console.WriteLine();
    }
    Console.WriteLine("✅ Готово. Скопируй посты сюда — соберём под них парсер.");
    return;
}

// ── Вступить в ЗАКРЫТЫЙ канал по инвайту + показать приватные каналы (ID + посты) ─
//   dotnet run -- joininvite <hash|ссылка t.me/+hash>
if (args.Length >= 2 && args[0].Equals("joininvite", StringComparison.OrdinalIgnoreCase))
{
    var hash = Regex.Match(args[1], @"([A-Za-z0-9_-]{12,})\s*$").Groups[1].Value;
    try { await client.Messages_ImportChatInvite(hash); Console.WriteLine($"✅ Вступив за інвайтом ({hash})."); }
    catch (Exception ex) { Console.WriteLine("ℹ import: " + ex.Message + " (можливо, вже учасник)"); }
    var dialogs = await client.Messages_GetAllDialogs();
    Console.WriteLine("\n📜 Приватні канали (без @username) — ID · назва · приклади постів:");
    foreach (var ch in dialogs.chats.Values.OfType<Channel>().Where(c => string.IsNullOrEmpty(c.username)))
    {
        Console.WriteLine($"\n  ID={ch.ID}  «{ch.title}»");
        try
        {
            var hist = await client.Messages_GetHistory(ch, limit: 5);
            foreach (var msg in hist.Messages.OfType<Message>().Reverse())
            {
                if (string.IsNullOrWhiteSpace(msg.message)) continue;
                var t = msg.message.Replace("\r", "").Replace("\n", " ⏎ ").Trim();
                Console.WriteLine("     · " + (t.Length > 160 ? t[..160] : t));
            }
        }
        catch (Exception ex) { Console.WriteLine("     (історія недоступна: " + ex.Message + ")"); }
    }
    return;
}

// ── Режим CHANNELS: список каналов, на которые ты подписан (с @username) ──────
//   dotnet run -- channels [фильтр]
if (args.Length >= 1 && args[0].Equals("channels", StringComparison.OrdinalIgnoreCase))
{
    var filter = args.Length >= 2 ? args[1] : null;
    Console.WriteLine("📜 Твои каналы (@username · название):\n");
    var dialogs = await client.Messages_GetAllDialogs();
    foreach (var ch in dialogs.chats.Values.OfType<Channel>()
                 .OrderBy(c => c.title, StringComparer.CurrentCultureIgnoreCase))
    {
        if (filter != null && ch.title?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) != true) continue;
        var u = ch.username is { Length: > 0 } name ? "@" + name : "(без username)";
        Console.WriteLine($"  {u,-26} {ch.title}");
    }
    Console.WriteLine("\n✅ Скопируй @username нужного и запусти:  dotnet run -- dump @username 50");
    return;
}

// ── Режим CHECK: свежие посты всех каналов через тревогу (не ждать налёта) ────
//   dotnet run -- check
if (args.Length >= 1 && args[0].Equals("check", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"🔎 Тягну свежие посты из каналов, прогоняю тревогу под район «{MyDistrict}»...\n");
    var sources = Enumerable.Empty<(string uname, string kind)>();
    if (WatchKharkiv) sources = sources.Concat(khChannels.Select(u => (uname: u, kind: "kh")));
    if (WatchKyiv) sources = sources.Concat(kyChannels.Select(u => (uname: u, kind: "ky")));
    sources = sources.Concat(uaChannels.Select(u => (uname: u, kind: "ua")));
    int total = 0, hits = 0;

    foreach (var (uname, kind) in sources)
    {
        Channel? channel = null;
        try
        {
            var resolved = await client.Contacts_ResolveUsername(uname);
            channel = resolved.chats.Values.OfType<Channel>().FirstOrDefault();
        }
        catch (Exception ex) { Console.WriteLine($"⚠ @{uname}: {ex.Message}\n"); continue; }
        if (channel is null) { Console.WriteLine($"⚠ @{uname}: не найден\n"); continue; }

        Console.WriteLine($"── @{uname} ──");
        foreach (var msg in (await client.Messages_GetHistory(channel, limit: 12)).Messages.OfType<Message>().Reverse())
        {
            if (string.IsNullOrWhiteSpace(msg.message)) continue;
            total++;
            var t = msg.message.Replace("\n", " ").Trim();
            if (t.Length > 66) t = t[..63] + "…";

            if (kind != "ua")
            {
                var e = kind == "kh" ? KharkivMonitor.Analyze(msg.message, MyDistrict) : KyivMonitor.Analyze(msg.message);
                if (e.Signal == KhSignal.News) continue;
                var (emoji, label) = KharkivMonitor.Verdict(e);
                var place = e.Places.Count > 0 ? " → " + string.Join(", ", e.Places) : "";
                Console.WriteLine($"  {emoji} {label}{place}   «{t}»");
                if (e.Alarm) hits++;
                // Рисуем карту по свежим новостям (тот же путь, что и live).
                var zone = kind == "kh" ? "Харків" : "Київ";
                tracker.Ingest(e, zone, DateTime.Now);
                if (e.Signal is KhSignal.AlarmOff or KhSignal.Clear) tracker.Sweep(zone);
            }
            else
            {
                var events = ThreatParser.Parse(msg.message);
                bool hasAllClear = events.Any(e => e.Kind == ThreatKind.AllClear);
                bool hasDrones = events.Any(e => e.Kind is ThreatKind.Drone or ThreatKind.Target);
                if (hasAllClear) tracker.Sweep("UA");
                else if (hasDrones) tracker.SweepKind("UA", ThreatKind.Drone, ThreatKind.Target);

                foreach (var ev in events)
                {
                    tracker.IngestUa(ev, DateTime.Now); // на карту — вся країна
                    var rel = ThreatParser.Relevance(ev, MyOblast, MyCity);
                    if (rel == RelLevel.None) continue;
                    Console.WriteLine($"  {(rel == RelLevel.City ? "🔴 твоє місто" : "🟠 твоя область")} {ev.City ?? ev.Oblast}   «{t}»");
                    if (rel == RelLevel.City) hits++;
                }
            }
        }
        Console.WriteLine();
    }

    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
    Console.WriteLine($"Σ свежих постов: {total} · сигналов для тебя: {hits}");
    Console.WriteLine(hits > 0
        ? "✅ Тревога работает — это уже прилетало по свежим постам."
        : "✅ Каналы читаются, парсер жив. Сейчас релевантных угроз нет — это хорошо.");
    Console.WriteLine($"🗺  Карта нарисована по свежим новостям: {Path.GetFullPath(MapPath)}");
    Console.WriteLine(tracker.Active.Count == 0
        ? "   (зараз активних цілей у новинах немає → карта показує «чисто»)"
        : $"   (активних цілей: {tracker.Active.Count} — відкрий карту в браузері)");
    return;
}

// ── Режим LISTEN (по умолчанию): слушаем каналы в реальном времени ───────────
// UpdateManager сам собирает users/chats и гарантирует порядок обновлений.
// Объявляем заранее: на manager ссылается локальная функция OnUpdate.
UpdateManager manager = null!;
manager = client.WithUpdateManager(OnUpdate, "updates.state");

// Проверка подписок: на какие нужные каналы аккаунт НЕ подписан (иначе live не придёт).
try
{
    var subscribed = (await client.Messages_GetAllDialogs()).chats.Values
        .OfType<Channel>().Where(c => c.username is { Length: > 0 })
        .Select(c => c.username).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var needed = new List<string>();
    if (WatchKharkiv) needed.AddRange(khChannels);
    if (WatchKyiv) needed.AddRange(kyChannels);
    needed.AddRange(uaChannels);
    needed.AddRange(powerChannels);
    var missing = needed.Where(u => !subscribed.Contains(u)).ToList();
    if (missing.Count > 0)
        Ui.Line(ConsoleColor.Yellow, "⚠ НЕ підписаний на: " + string.Join(", ", missing.Select(u => "@" + u)) + " — по ним live не прийде! Вступи в них.");
    else
        Ui.Line(ConsoleColor.Green, "✅ Підписки в порядку — усі потрібні канали на місці.");
}
catch { /* проверка не критична */ }

Console.WriteLine();
Ui.Line(ConsoleColor.Cyan, "═══════════ WarAlarm · живий режим ═══════════");
Console.WriteLine($"🏠 Дім: {MyDistrict} р-н, {MyCity}    🏙 Київ: {(WatchKyiv ? "стежу" : "вимк")}");
Ui.Line(ConsoleColor.Gray, "🔴/🟠 сирена+пуш · 🟡/🟢 сусід. район (тихіше) · ✅ відбій · Ctrl+C — вихід");
Console.WriteLine($"🗺  Карта загроз: {Path.GetFullPath(MapPath)}  (відкрий у браузері)");
Console.WriteLine();
// Тёплый старт: сразу восстановить актуальную картину из свежих постов, чтобы карта
// при открытии не была пустой до следующего поста. Сбой сети — просто пустая карта.
try { await WarmStart(); }
catch (Exception ex)
{
    Ui.Line(ConsoleColor.DarkYellow, "⚠ тёплий старт пропущено: " + ex.Message);
    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now); // фолбэк: стартова (пуста) карта
}

// Фоновый пульс трекера: раз в секунду пересчитывает ETA/стадии, «пикает» на подлёте
// с нужной частотой, гасит пролетевшие цели и обновляет карту.
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(1000);
        try
        {
            var ups = tracker.Tick(DateTime.Now);
            bool dirty = false;
            foreach (var u in ups)
            {
                dirty = true;
                if (OperatingSystem.IsWindows()) SirenSound.PlayStage(u.Sound, u.Threat.Kind);
                if (u.StageChanged)
                    Ui.Line(u.Threat.Stage == TrackStage.Passed ? ConsoleColor.Green : ConsoleColor.Red,
                            $"   ⏱ {u.Note}");
            }
            // ☢️ Вийшов час радіаційної небезпеки без нових згадок → знімаємо (відбій).
            if (nuclearUntil is { } nu && DateTime.Now >= nu && MapWriter.NuclearJson.Contains("\"active\":true"))
            {
                nuclearUntil = null;
                MapWriter.NuclearJson = "{\"active\":false}";
                Ui.Line(ConsoleColor.Green, "☢️ Радіаційна небезпека — знято (немає нових повідомлень).");
                dirty = true;
            }
            if (dirty || tracker.Active.Count > 0) MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
        }
        catch { /* пульс не должен ронять слушателя */ }
    }
});

// ── Сторож зв'язку (keepalive + автоперепідключення) ─────────────────────────
// Пасивний слухач сам НЕ переконектиться, якщо сокет тихо обірвався (як 11.09 о 20:20):
// процес живий, карту малює, але Telegram-повідомлень не отримує — «сліпе» зависання.
// Раз на 2 хв робимо легкий виклик Updates_GetState(): він (а) як пінг тримає з'єднання,
// (б) якщо канал мертвий — примушує WTelegram перепідключитися. Плюс попереджаємо в
// консоль/лог, якщо оновлень немає підозріло довго.
_ = Task.Run(async () =>
{
    int fails = 0;
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        try
        {
            await client.Updates_GetState();   // keepalive + форсує реконект, якщо зв'язок впав
            lastTgActivity = DateTime.Now;
            fails = 0;
        }
        catch (Exception ex)
        {
            fails++;
            Ui.Line(ConsoleColor.Yellow, $"⚠ keepalive ({fails}): " + ex.Message + " — пробую перепідключитися…");
            try { await client.LoginUserIfNeeded(); } catch (Exception e2) { Console.Error.WriteLine("⚠ reconnect fail: " + e2.Message); }
            // Клієнт WTelegram інколи «зависає» намертво (мережа падала — NetworkUnreachable),
            // і жоден in-process реконект не рятує. Після ~6 хв мертвого зв'язку виходимо з
            // кодом 2 — start.bat крутить у циклі й підніме СВІЖИЙ процес із чистим конектом.
            if (fails >= 3)
            {
                Console.Error.WriteLine("⛔ Зв'язок мертвий 6+ хв — перезапускаю процес (start.bat підніме).");
                Environment.Exit(2);
            }
        }
        var idle = DateTime.Now - lastTgActivity;
        if (idle > TimeSpan.FromMinutes(20))
            Ui.Line(ConsoleColor.Yellow, $"⚠ {idle.TotalMinutes:0} хв без даних від Telegram — зв'язок міг обірватися.");
    }
});

await Task.Delay(-1); // работаем, пока не закроют

// ── обработка одного обновления ──────────────────────────────────────────────
async Task OnUpdate(Update update)
{
    lastTgActivity = DateTime.Now; // будь-який апдейт = зв'язок живий (для сторожа)
    // UpdateNewChannelMessage наследуется от UpdateNewMessage — одна ветка ловит оба.
    if (update is not UpdateNewMessage { message: Message m } || string.IsNullOrEmpty(m.message))
        return;

    // username канала-источника
    string? uname = null, title = null;
    try { if (manager.UserOrChat(m.peer_id) is Channel ch) { uname = ch.username; title = ch.title; } }
    catch { /* неизвестный peer — пропустим */ }
    var src = uname is { Length: > 0 } ? "@" + uname : title ?? "?";
    var time = m.date.ToString("HH:mm:ss");
    var oneLine = m.message.Replace("\n", " ").Trim();

    // Дедуп: одинаковый текст из разных каналов за короткое окно — обрабатываем один раз
    // (частый случай: репост/копипаста одной и той же угрозы). Так не пищит дважды.
    if (SeenRecently(oneLine)) return;

    // ── ☢️ РАДІАЦІЙНА/ЯДЕРНА НЕБЕЗПЕКА — найвищий пріоритет над усім ──────────────
    // Тільки з наших моніторених каналів і лише за явним сигналом (NuclearMention).
    bool fromOurChannel = uname != null &&
        (khChannels.Contains(uname) || kyChannels.Contains(uname) || uaChannels.Contains(uname));
    if (fromOurChannel && NuclearMention(m.message))
    {
        bool wasActive = nuclearUntil is { } nu && DateTime.Now < nu;
        nuclearUntil = DateTime.Now.AddMinutes(30); // тримаємо активним; кожна нова згадка продовжує
        var msg = oneLine.Length > 160 ? oneLine[..160] : oneLine;
        var ms = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
        MapWriter.NuclearJson = $"{{\"active\":true,\"msg\":\"{msg.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",\"ts\":{ms}}}";
        MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now); // одразу у Firebase → застосунок
        Ui.Line(ConsoleColor.Red, $"☢️ [{time}] {src} · РАДІАЦІЙНА/ЯДЕРНА НЕБЕЗПЕКА · {oneLine}");
        if (!wasActive) // сигнал і пуш — один раз на активацію (не спамимо на кожен репост)
        {
            if (OperatingSystem.IsWindows()) SirenSound.PlayNuclear();
            await Notify("UA", new AlarmInfo(time, src,
                "☢️ РАДІАЦІЙНА НЕБЕЗПЕКА! Зайдіть у приміщення, зачиніть вікна й двері, вимкніть вентиляцію. Подалі від вікон. Йод — лише за вказівкою ДСНС. Слухайте офіційні джерела.",
                oneLine, ThreatKind.Unknown, true, Silent: false), clear: false);
        }
        return;
    }

    // ── 🚀 ПУСКИ (ракети/КАБ/ШАХЕДИ) / ракетна небезпека — попередження ──────────
    if (fromOurChannel && LaunchMention(m.message))
    {
        bool recent = lastLaunch is { } ll && (DateTime.Now - ll) < TimeSpan.FromMinutes(3);
        lastLaunch = DateTime.Now;
        var low = m.message.ToLowerInvariant();
        bool drones = Regex.IsMatch(low, @"шахед|шахід|бпла|дрон|геран|мопед");
        int? cnt = LaunchCount(m.message);
        // Заголовок за типом: шахеди / ракети-КАБ / загальна ракетна небезпека.
        var head = drones
            ? (cnt is { } c ? $"ПУСК ~{c} ШАХЕДІВ" : "ПУСК ШАХЕДІВ")
            : Regex.IsMatch(low, @"ракет|каб|крилат|міг|ту-?\d|су-?\d") ? "ПУСКИ РАКЕТ/КАБ"
            : "РАКЕТНА НЕБЕЗПЕКА";
        var txt = oneLine.Length > 200 ? oneLine[..200] : oneLine;
        Ui.Line(ConsoleColor.Red, $"🚀 [{time}] {src} · {head} · {oneLine}");
        if (!recent)
        {
            if (OperatingSystem.IsWindows()) SirenSound.PlayStage(TrackSound.Imminent, drones ? ThreatKind.Drone : ThreatKind.Missile);
            await Notify("UA", new AlarmInfo(time, src, $"🚀 {head} — прямують на Україну! {txt}", oneLine, drones ? ThreatKind.Drone : ThreatKind.Missile, true, Silent: false), clear: false);
            if (channelPoster is not null)
                await channelPoster.Post(
                    $"🚀 <b>УВАГА — {H(head)}</b>\n{H(txt)}\n\n⚠️ Прямують на Україну — очікуйте появу на карті. Будьте на зв'язку.\n🤖 WarAlarm", null);
        }
        // не return — у пості можуть бути й цілі; хай далі теж розпарситься
    }

    // ── 🔌 Світло (відключення/відновлення) → окреме оповіщення (без сирени) ─────
    if (uname != null && powerChannels.Contains(uname))
    {
        var pe = PowerMonitor.Analyze(m.message);
        if (pe.Kind is PowerKind.Off or PowerKind.On)
        {
            var icon = pe.Kind == PowerKind.Off ? "🔌" : "💡";
            var word = pe.Kind == PowerKind.Off ? "Відключення світла" : "Світло відновлюють";
            var head = $"{icon} {word} — {pe.Area}";
            var msg = oneLine.Length > 180 ? oneLine[..180] : oneLine;
            var ms = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds();
            static string J(string x) => x.Replace("\\", "\\\\").Replace("\"", "\\\"");
            MapWriter.PowerJson = $"{{\"status\":\"{(pe.Kind == PowerKind.Off ? "off" : "on")}\",\"area\":\"{J(pe.Area)}\",\"msg\":\"{J(msg)}\",\"ts\":{ms}}}";
            MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now); // одразу у Firebase → застосунок
            await Alert(new AlarmInfo(time, src, head, oneLine, ThreatKind.Unknown, Urgent: false, Power: true)); // пуш + консоль
            if (channelPoster is not null) // і в канал
                await channelPoster.Post($"{icon} <b>{H(word)} — {H(pe.Area)}</b>\n{H(msg)}\n🤖 WarAlarm", null);
        }
        else
            Ui.Line(ConsoleColor.DarkGray, $"💡 [{time}] {src} · {(pe.Kind == PowerKind.Schedule ? "графік відключень" : "новина про світло")} (тихо)");
        return;
    }

    // ── Харьковские каналы → монитор до района ────────────────────────────────
    if (WatchKharkiv && uname != null && khChannels.Contains(uname))
    {
        if (PpoMention(m.message)) ppoSeen["Харків"] = DateTime.Now;
        await HandleKh(KharkivMonitor.Analyze(m.message, MyDistrict), src, time, "Харків");
        return;
    }

    // ── Киевские каналы → монитор по городу (ты бываешь в Киеве) ───────────────
    if (WatchKyiv && uname != null && kyChannels.Contains(uname))
    {
        if (PpoMention(m.message)) ppoSeen["Київ"] = DateTime.Now;
        await HandleKh(KyivMonitor.Analyze(m.message), src, time, "Київ");
        return;
    }

    // ── Всеукраинский → город/область ─────────────────────────────────────────
    if (uname != null && uaChannels.Contains(uname))
    {
        var events = ThreatParser.Parse(m.message);
        if (events.Count == 0) return;

        bool hasAllClear = events.Any(e => e.Kind == ThreatKind.AllClear);
        bool hasDrones = events.Any(e => e.Kind is ThreatKind.Drone or ThreatKind.Target);

        // Если в посте отбой — сбрасываем цели по стране
        if (hasAllClear)
        {
            tracker.Sweep("UA");
        }
        // С каждым новым сообщением от всеукраинского радара — сбрасываем старые UA-цели
        // и наносим актуальный срез заново (чтобы не копились старые стрелки)
        else if (hasDrones)
        {
            tracker.SweepKind("UA", ThreatKind.Drone, ThreatKind.Target);
        }

        bool drew = false;
        foreach (var ev in events)
        {
            if (tracker.IngestUa(ev, DateTime.Now) is not null) drew = true; // на карту — вся країна
            var rel = ThreatParser.Relevance(ev, MyOblast, MyCity);
            if (rel == RelLevel.None) continue;
            var head = $"{(rel == RelLevel.City ? "🔴 твоє місто" : "🟠 твоя область")} {ThreatParser.Icon(ev.Kind)} {ev.City ?? ev.Oblast}";
            if (rel == RelLevel.City)
                await Notify(MyCity, new AlarmInfo(time, src, head, oneLine, ev.Kind, true), clear: false);
            else
                Ui.Line(ConsoleColor.Yellow, $"[{time}] {src} · {head}  (тихо)");
        }
        if (drew || hasAllClear || hasDrones) { MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now); await MaybeAi(false); }
        return;
    }

    // прочие каналы — не наши
}

// Дедуп одинаковых сообщений в коротком окне (репосты/копипаста между каналами).
bool SeenRecently(string text)
{
    var key = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
    if (key.Length < 4) return false;
    var now = DateTime.Now;
    if (seenMsgs.TryGetValue(key, out var t) && now - t < TimeSpan.FromMinutes(3)) return true;
    seenMsgs[key] = now;
    if (seenMsgs.Count > 500) // не растём бесконечно
        foreach (var k in seenMsgs.Where(p => now - p.Value > TimeSpan.FromMinutes(10)).Select(p => p.Key).ToList())
            seenMsgs.Remove(k);
    return false;
}

// Общая обработка события KharkivMonitor/KyivMonitor: пульс / тихо / тревога+push.
async Task HandleKh(KhEvent e, string src, string time, string zone)
{
    if (e.Signal == KhSignal.News)
    {
        Ui.Line(ConsoleColor.DarkGray, $"·  [{time}] {src} — новина/реклама, пропуск"); // пульс
        return;
    }
    var (emoji, label) = KharkivMonitor.Verdict(e);
    var place = e.Places.Count > 0 ? " → " + string.Join(", ", e.Places) : "";
    var kind = e.Kind != ThreatKind.Unknown && e.Signal is KhSignal.Threat or KhSignal.AlarmOn
        ? $" {ThreatParser.Icon(e.Kind)}{(e.Extra != null ? " " + e.Extra : "")}" : "";
    var oneLine = e.Raw.Replace("\n", " ").Trim();

    // Пустая «оголошена тривога» без конкретной цели — НЕ будим (по просьбе юзера),
    // только отметка в консоли. Ждём конкретную угрозу с местом/типом.
    if (e.Signal == KhSignal.AlarmOn)
    {
        Ui.Line(ConsoleColor.Cyan, $"🔔 [{time}] {src} · оголошено тривогу (чекаю конкретну ціль){kind}{place}");
        return;
    }

    // ── Живая угроза → трекер: стадии (наближ.→підльот→приліт), ETA, карта ──────
    if (e.Signal == KhSignal.Threat)
    {
        var up = tracker.Ingest(e, zone, DateTime.Now);
        if (up is { } u)
        {
            MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
            // Прилёт → отдельная запись в журнал (для аналитики «прильотів»).
            if (u.StageChanged && u.Threat.Stage == TrackStage.Impact)
                try { File.AppendAllText("alarms.csv", $"{DateTime.Now:yyyy-MM-dd},{DateTime.Now:HH:mm:ss},{zone},{u.Threat.Kind},impact,\"{u.Note.Replace('"', '\'')}\"\n"); } catch { }
            // ПРИЛІТ/НАД ДОМОМ (≤7 км) — окремий найсильніший сигнал і пуш «в укриття».
            bool impactHome = !u.Threat.Recon && u.Threat.DistKm is <= 7
                              && u.Threat.Stage is TrackStage.Impact or TrackStage.Overhead;
            if (impactHome)
            {
                if (OperatingSystem.IsWindows()) SirenSound.PlayImpactHome();
                Ui.Line(ConsoleColor.Red, $"💥 [{time}] {src} · ПРИЛІТ ПОРУЧ · {u.Note}");
                await Notify(zone, new AlarmInfo(time, src, $"💥 ПРИЛІТ ПОРУЧ! Може бути гучно — вибухи/робота ППО. В укриття, від вікон.", oneLine, e.Kind, true, Silent: false), clear: false);
                await MaybeAi(false);
                return;
            }

            // Звук — ТОЛЬКО на реальное событие стадии (новая цель/подлёт/приліт).
            // Раньше пустое None подменялось Ping → пикало на КАЖДЫЙ перепост той же цели
            // («орёт постоянно»). Разведка — молчит совсем.
            bool trackerPlayed = OperatingSystem.IsWindows() && u.Threat.DistKm is not null
                                 && !u.Threat.Recon && u.Sound != TrackSound.None;
            if (trackerPlayed)
                SirenSound.PlayStage(u.Sound, u.Threat.Kind);

            // Будим (пуш), если по твоему району/городу ИЛИ уже на подлёте/над нами/прилёт.
            // Разведку/дорозвідку — НЕ будим (телефон молчит), она только на карте.
            bool wake = !u.Threat.Recon
                     && (e.Tier is KhTier.MyArea or KhTier.CityWide
                        || u.Threat.Stage is TrackStage.Imminent or TrackStage.Overhead or TrackStage.Impact);
            if (u.Threat.Recon)
                Ui.Line(ConsoleColor.DarkGray, $"🛰 [{time}] {src} · {u.Note}  (розвідка — тихо)");
            else if (wake)
                await Notify(zone, new AlarmInfo(time, src, $"{emoji} {u.Note}", oneLine, e.Kind, true, Silent: OperatingSystem.IsWindows() && u.Threat.DistKm is not null), clear: false);
            else // сосед. район/пригород — слышно, поэтому звук есть, но телефон молчит
                Ui.Line(ConsoleColor.Yellow, $"{emoji} [{time}] {src} · {u.Note}  (сусід — тихіше, телефон мовчить)");
        }
        await MaybeAi(false); // настоящий ИИ — совет по обстановке (троттлинг внутри)
        return;
    }

    // ── Отбой/чисто — гасим цели зоны на карте + тихий зелёный пуш ──────────────
    Ui.Line(ConsoleColor.Green, $"{emoji} [{time}] {src} · {label}");
    if (tracker.Sweep(zone).Count > 0) MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
    await Notify(zone, new AlarmInfo(time, src, $"✅ Відбій — {zone} чисто", oneLine, ThreatKind.AllClear, false), clear: true);
}

// ── Тёплый старт: восстановить активную картину из свежих постов каналов ──────
// Тихо (без звука/пуша), с РЕАЛЬНЫМ временем постов — трекер сам погасит устаревшие
// (>4 хв) на первом Tick. Тот же разбор, что и в live/`check`, но при запуске.
async Task WarmStart()
{
    var sources = Enumerable.Empty<(string uname, string kind)>();
    if (WatchKharkiv) sources = sources.Concat(khChannels.Select(u => (uname: u, kind: "kh")));
    if (WatchKyiv) sources = sources.Concat(kyChannels.Select(u => (uname: u, kind: "ky")));
    sources = sources.Concat(uaChannels.Select(u => (uname: u, kind: "ua")));

    foreach (var (uname, kind) in sources)
    {
        Channel? channel = null;
        try
        {
            var resolved = await client.Contacts_ResolveUsername(uname);
            channel = resolved.chats.Values.OfType<Channel>().FirstOrDefault();
        }
        catch { continue; } // канал недоступен — пропускаем, старт не роняем
        if (channel is null) continue;

        Message[] msgs;
        try { msgs = (await client.Messages_GetHistory(channel, limit: 12)).Messages.OfType<Message>().Reverse().ToArray(); }
        catch { continue; }

        foreach (var msg in msgs) // хронологический порядок (старые → новые)
        {
            if (string.IsNullOrWhiteSpace(msg.message)) continue;
            var when = msg.date.ToLocalTime(); // реальное время поста → корректное старение

            if (kind != "ua")
            {
                var e = kind == "kh" ? KharkivMonitor.Analyze(msg.message, MyDistrict) : KyivMonitor.Analyze(msg.message);
                if (e.Signal == KhSignal.News) continue;
                var zone = kind == "kh" ? "Харків" : "Київ";
                if (e.Signal is KhSignal.AlarmOff or KhSignal.Clear) tracker.Sweep(zone);
                else tracker.Ingest(e, zone, when);
            }
            else
            {
                var events = ThreatParser.Parse(msg.message);
                if (events.Any(x => x.Kind == ThreatKind.AllClear)) tracker.Sweep("UA");
                else if (events.Any(x => x.Kind is ThreatKind.Drone or ThreatKind.Target)) tracker.SweepKind("UA", ThreatKind.Drone, ThreatKind.Target);
                foreach (var ev in events) tracker.IngestUa(ev, when);
            }
        }
    }

    tracker.Tick(DateTime.Now);                                  // погасить устаревшее по реальному времени
    MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now); // нарисовать актуальную картину
    var live = tracker.Active.Count;
    Ui.Line(live > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray,
        live > 0 ? $"🗺  Тёплий старт: відновлено активних цілей — {live}"
                 : "🗺  Тёплий старт: зараз чисто (свіжих загроз немає)");
    await MaybeAi(false); // ИИ-совет по восстановленной обстановке (троттлинг/только близкие — внутри)
}

// ── конфиг/секреты ───────────────────────────────────────────────────────────
string? Config(string what) => what switch
{
    "session_pathname"  => "WTelegram.session",             // файл сессии (в .gitignore!)
    "api_id"            => Secret("api_id"),
    "api_hash"          => Secret("api_hash"),
    "phone_number"      => Secret("phone_number"),
    "verification_code" => Ask("Код подтверждения из Telegram: "),
    "password"          => Ask("Пароль 2FA (Enter, если нет): "),
    _                   => null
};

// Загружает secrets.local.txt в словарь (без запроса в консоли). Идемпотентно.
void LoadSecrets()
{
    if (secrets is not null) return;
    secrets = new(StringComparer.OrdinalIgnoreCase);
    const string path = "secrets.local.txt";
    if (File.Exists(path))
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq > 0) secrets[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
}

// Читает secrets.local.txt (key=value). Если ключа нет — спросит в консоли.
string? Secret(string key)
{
    LoadSecrets();
    return secrets!.TryGetValue(key, out var v) && v.Length > 0 ? v : Ask($"Введите {key}: ");
}

static string? Ask(string prompt)
{
    Console.Write(prompt);
    return Console.ReadLine();
}

// Читает settings.local.txt (key=value) в словарь. Нет файла — пустой словарь (дефолты).
Dictionary<string, string> LoadConfig(string path)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (File.Exists(path))
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq > 0) d[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
    return d;
}

// ── Демонстрация парсера на реальных постах (оффлайн) ─────────────────────────
void RunParseDemo(string path)
{
    if (!File.Exists(path)) { Console.WriteLine($"❌ Не найден файл: {path}"); return; }

    var posts = Regex.Split(File.ReadAllText(path), @"(?m)^=====.*$")
        .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    const string myOblast = "Харківська", myCity = "Харків";
    Console.WriteLine($"👤 Тестовая локация: {myCity} ({myOblast})\n");

    int alarms = 0;
    for (int i = 0; i < posts.Count; i++)
    {
        var evs = ThreatParser.Parse(posts[i]);
        Console.WriteLine($"── пост #{i + 1} " + new string('─', 34));
        if (evs.Count == 0) { Console.WriteLine("   · шум/новость — угроз нет\n"); continue; }

        foreach (var e in evs)
        {
            var rel = ThreatParser.Relevance(e, myOblast, myCity);
            if (rel != RelLevel.None) alarms++;
            string tag = rel switch
            {
                RelLevel.City   => "   ← 🔴 ТРИВОГА (твоє місто)",
                RelLevel.Oblast => "   ← 🟠 твоя область",
                _               => ""
            };
            var s = $"   {ThreatParser.Icon(e.Kind)} {e.Kind,-9}";
            if (e.Count > 1)     s += $" ×{e.Count}";
            if (e.Oblast != null) s += $" | обл: {e.Oblast}";
            if (e.City != null)   s += $" | ціль: {e.City}";
            if (e.Extra != null)  s += $" | {e.Extra}";
            Console.WriteLine(s + tag);
        }
        Console.WriteLine();
    }
    Console.WriteLine($"Σ постов: {posts.Count} · сигналов релевантных тебе: {alarms}");
}

// ── Демонстрация харьковского монитора на реальных постах (оффлайн) ───────────
void RunKharkivDemo(string path, string myDistrict)
{
    if (!File.Exists(path)) { Console.WriteLine($"❌ Не найден файл: {path}"); return; }

    // Блоки постов разделены строкой из «-----», первая строка блока — @канал.
    var blocks = Regex.Split(File.ReadAllText(path).Replace("\r", ""), @"(?m)^-----\s*$")
        .Select(b => b.Trim()).Where(b => b.Length > 0).ToList();

    Console.WriteLine($"👤 Твій район: {myDistrict}");
    Console.WriteLine("   🔴 буди · 🟠 весь город · 🟡 сосед. район · 🟢 пригород · ▫️ область · · шум\n");

    int alarm = 0, pre = 0;
    foreach (var block in blocks)
    {
        string channel = "@?", body = block;
        var nl = block.IndexOf('\n');
        if (block.StartsWith("@")) { channel = nl > 0 ? block[..nl].Trim() : block; body = nl > 0 ? block[(nl + 1)..].Trim() : ""; }

        var e = KharkivMonitor.Analyze(body, myDistrict);
        var (emoji, label) = KharkivMonitor.Verdict(e);
        if (e.Alarm) alarm++; else if (e.PreAlert) pre++;

        var oneLine = body.Replace("\n", " ⏎ ");
        if (oneLine.Length > 64) oneLine = oneLine[..61] + "…";

        var extra = "";
        if (e.Kind != ThreatKind.Unknown && e.Signal is KhSignal.Threat or KhSignal.AlarmOn)
            extra += $" {ThreatParser.Icon(e.Kind)}{(e.Extra != null ? " " + e.Extra : "")}";
        if (e.Places.Count > 0) extra += $" → {string.Join(", ", e.Places)}";

        Console.WriteLine($"{emoji} {channel,-16} {oneLine}");
        Console.WriteLine($"     {label}{extra}");
    }
    Console.WriteLine($"\nΣ постов: {blocks.Count} · 🔴/🟠 разбудили: {alarm} · 🟡/🟢 предупредили: {pre}");
}

// ── Симуляция живой тревоги со звуком (тот же путь, что и в LISTEN) ───────────
void RunKharkivSim(string myDistrict, string path)
{
    if (!File.Exists(path)) { Console.WriteLine($"❌ Не найден файл: {path}"); return; }
    var blocks = Regex.Split(File.ReadAllText(path).Replace("\r", ""), @"(?m)^-----\s*$")
        .Select(b => b.Trim()).Where(b => b.Length > 0).ToList();

    Console.WriteLine($"🎧 Симуляція живої тривоги · район: {myDistrict}");
    Console.WriteLine("   (звук лунає на 🔴/🟠 — точно як у бойовому режимі)");
    Console.WriteLine($"🗺  Карта малюється з новин у реальному часі: {Path.GetFullPath(MapPath)}");
    Console.WriteLine("    (відкрий у браузері — вона оновлюється сама)\n");
    MapWriter.Write(MapPath, tracker, myDistrict, DateTime.Now);

    foreach (var block in blocks)
    {
        string channel = "@?", body = block;
        var nl = block.IndexOf('\n');
        if (block.StartsWith("@")) { channel = nl > 0 ? block[..nl].Trim() : block; body = nl > 0 ? block[(nl + 1)..].Trim() : ""; }

        var e = KharkivMonitor.Analyze(body, myDistrict);
        if (e.Signal == KhSignal.News) continue;
        var (emoji, label) = KharkivMonitor.Verdict(e);
        var place = e.Places.Count > 0 ? " → " + string.Join(", ", e.Places) : "";

        // Рисуем по новостям: каждый пост → в трекер → перерисовка карты (как в live).
        var up = tracker.Ingest(e, "Харків", DateTime.Now);
        if (e.Signal is KhSignal.AlarmOff or KhSignal.Clear) tracker.Sweep("Харків");
        MapWriter.Write(MapPath, tracker, myDistrict, DateTime.Now);

        if (up is { } u && u.Threat.DistKm is not null)
            Console.WriteLine($"{emoji}🔊 {channel,-15} {u.Note}");
        else
            Console.WriteLine($"{(e.Alarm ? emoji + "🔊" : emoji)} {channel,-15} {label}{place}");

        if (up is { } u2 && u2.Threat.DistKm is not null && OperatingSystem.IsWindows())
        { SirenSound.PlayStage(u2.Sound == TrackSound.None ? TrackSound.Ping : u2.Sound, u2.Threat.Kind); Thread.Sleep(3200); }
        else if (e.Alarm) { SirenNotifier.Play(e.Kind, e.Tier == KhTier.MyArea, false); Thread.Sleep(3200); }
        else Thread.Sleep(400);
    }
    Console.WriteLine($"\n✅ Симуляція завершена. Карта: {Path.GetFullPath(MapPath)}");
    Console.WriteLine("   Так само вона малюється наживо (dotnet run) і в `check` по свіжих постах.");
}

// ── Демонстрация киевского монитора (оффлайн, гость в Киеве) ──────────────────
void RunKyivDemo(string path)
{
    if (!File.Exists(path)) { Console.WriteLine($"❌ Не найден файл: {path}"); return; }
    var blocks = Regex.Split(File.ReadAllText(path).Replace("\r", ""), @"(?m)^-----\s*$")
        .Select(b => b.Trim()).Where(b => b.Length > 0).ToList();

    Console.WriteLine("🏙 Киев · гость (тревога по всему городу)");
    Console.WriteLine("   🟠 буди (весь Киев) · 🟢 пригород · ▫️ область/транзит · ✅ отбой · · шум\n");

    int alarm = 0, pre = 0;
    foreach (var block in blocks)
    {
        string channel = "@?", body = block;
        var nl = block.IndexOf('\n');
        if (block.StartsWith("@")) { channel = nl > 0 ? block[..nl].Trim() : block; body = nl > 0 ? block[(nl + 1)..].Trim() : ""; }

        var e = KyivMonitor.Analyze(body);
        var (emoji, label) = KharkivMonitor.Verdict(e);
        if (e.Alarm) alarm++; else if (e.PreAlert) pre++;
        var place = e.Places.Count > 0 ? " → " + string.Join(", ", e.Places) : "";
        var kind = e.Kind != ThreatKind.Unknown && e.Signal is KhSignal.Threat
            ? $" {ThreatParser.Icon(e.Kind)}{(e.Extra != null ? " " + e.Extra : "")}" : "";
        Console.WriteLine($"{(e.Alarm ? emoji + "🔊" : emoji)} {channel,-14} {label}{kind}{place}");
    }
    Console.WriteLine($"\nΣ постов: {blocks.Count} · 🟠 разбудили: {alarm} · 🟢 предупредили: {pre}");
}

// ── Статистика по журналу тревог alarms.csv ──────────────────────────────────
void RunStats(string path)
{
    if (!File.Exists(path)) { Console.WriteLine("📊 Ещё нет данных: alarms.csv появится после первых тревог."); return; }

    var rows = File.ReadAllLines(path)
        .Select(l => l.Split(',')).Where(c => c.Length >= 5).ToList();
    if (rows.Count == 0) { Console.WriteLine("📊 Журнал пуст."); return; }

    int alarms = rows.Count(c => c[4] != "clear");
    int clears = rows.Count(c => c[4] == "clear");
    Console.WriteLine($"📊 Статистика тревог  ·  всего записей: {rows.Count}");
    Console.WriteLine($"   🔴 тревог: {alarms} · ✅ отбоев: {clears}\n");

    Console.WriteLine("По городам:");
    foreach (var g in rows.Where(c => c[4] != "clear").GroupBy(c => c[2]).OrderByDescending(g => g.Count()))
        Console.WriteLine($"   {g.Key,-10} {g.Count()}");

    Console.WriteLine("\nПо типам угроз:");
    foreach (var g in rows.Where(c => c[4] != "clear").GroupBy(c => c[3]).OrderByDescending(g => g.Count()))
        Console.WriteLine($"   {g.Key,-12} {g.Count()}");

    Console.WriteLine("\nПо дням (последние 7):");
    foreach (var g in rows.Where(c => c[4] != "clear").GroupBy(c => c[0]).OrderByDescending(g => g.Key).Take(7))
        Console.WriteLine($"   {g.Key}  {new string('█', Math.Min(40, g.Count()))} {g.Count()}");

    var lastRow = rows[^1];
    Console.WriteLine($"\nПоследняя запись: {lastRow[0]} {lastRow[1]} · {lastRow[2]} · {(lastRow.Length >= 6 ? lastRow[5].Trim('"') : lastRow[4])}");
}
