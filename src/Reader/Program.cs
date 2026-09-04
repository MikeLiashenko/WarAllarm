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
// Настраивается в settings.local.txt:  ua_channels=raketa_trevoga,chomu_trivoga,...
var uaChannels = new HashSet<string>(
    cfg.GetValueOrDefault("ua_channels", "raketa_trevoga")
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
var fbUrl = SecretOpt("firebase_db_url")
    ?? "https://waralarm-c8c1f-default-rtdb.europe-west1.firebasedatabase.app";
var fbSecret = SecretOpt("firebase_db_secret");
if (!string.IsNullOrWhiteSpace(fbSecret))
{
    MapWriter.Publisher = new FirebasePublisher(fbUrl, fbSecret);
    Console.WriteLine($"🔥 Firebase ВКЛ → публікую стан у {fbUrl}/state.json (застосунок бачитиме живі дані).\n");
}
else
{
    Console.WriteLine("🔥 Firebase ВИКЛ. Щоб застосунок бачив живі дані — додай  firebase_db_secret=...  у secrets.local.txt\n");
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

using var client = new Client(Config);
var me = await client.LoginUserIfNeeded();
Console.WriteLine($"✅ Вошли как: {me.first_name} {me.last_name} (@{me.username})\n");

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
            if (dirty || tracker.Active.Count > 0) MapWriter.Write(MapPath, tracker, MyDistrict, DateTime.Now);
        }
        catch { /* пульс не должен ронять слушателя */ }
    }
});

await Task.Delay(-1); // работаем, пока не закроют

// ── обработка одного обновления ──────────────────────────────────────────────
async Task OnUpdate(Update update)
{
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

    // ── Харьковские каналы → монитор до района ────────────────────────────────
    if (WatchKharkiv && uname != null && khChannels.Contains(uname))
    {
        await HandleKh(KharkivMonitor.Analyze(m.message, MyDistrict), src, time, "Харків");
        return;
    }

    // ── Киевские каналы → монитор по городу (ты бываешь в Киеве) ───────────────
    if (WatchKyiv && uname != null && kyChannels.Contains(uname))
    {
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
            bool trackerPlayed = OperatingSystem.IsWindows() && u.Threat.DistKm is not null;
            if (trackerPlayed)
                SirenSound.PlayStage(u.Sound == TrackSound.None ? TrackSound.Ping : u.Sound, u.Threat.Kind);

            // Будим (пуш), если по твоему району/городу ИЛИ уже на подлёте/над нами/прилёт.
            bool wake = e.Tier is KhTier.MyArea or KhTier.CityWide
                     || u.Threat.Stage is TrackStage.Imminent or TrackStage.Overhead or TrackStage.Impact;
            if (wake)
                await Notify(zone, new AlarmInfo(time, src, $"{emoji} {u.Note}", oneLine, e.Kind, true, Silent: trackerPlayed), clear: false);
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
