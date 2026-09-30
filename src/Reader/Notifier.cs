using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ── WarAlarm · Каналы оповещения ─────────────────────────────────────────────
// Развязка «обнаружение угрозы» ↔ «как оповестить». Ядро только строит AlarmInfo,
// а дальше её получают все подключённые каналы: консоль, сирена, push на телефон.
// Добавить новый способ (FCM, SMS, email) = ещё один класс INotifier.

public record AlarmInfo(
    string Time,        // "13:26:04"
    string Source,      // "@tlknewsua"
    string Headline,    // "🔴 загроза у ТВОЄМУ районі 🛩 → Салтівський"
    string Text,        // исходный пост (одной строкой)
    ThreatKind Kind,
    bool Urgent,        // 🔴 прямая угроза (район/город) — макс. приоритет
    bool Clear = false, // ✅ это отбой, а не тревога — тихий зелёный пуш, без сирены
    bool Silent = false,// звук уже играет трекер (стадии) — не дублировать одиночным воем
    bool Power = false);// 🔌 оповещение про свет (отключение/восстановление) — без сирены

public interface INotifier
{
    Task Notify(AlarmInfo a);
}

// Цветной вывод в консоль (не падает, если цвет недоступен).
public static class Ui
{
    public static void Line(ConsoleColor color, string text)
    {
        try { Console.ForegroundColor = color; Console.WriteLine(text); Console.ResetColor(); }
        catch { Console.WriteLine(text); }
    }
}

// Печать в консоль: тревога — жёсткий красный блок «В УКРИТТЯ», отбой — зелёный блок.
public sealed class ConsoleNotifier : INotifier
{
    const int W = 58;

    public Task Notify(AlarmInfo a)
    {
        if (a.Power) PowerBlock(a); else if (a.Clear) CalmBlock(a); else HardBlock(a);
        return Task.CompletedTask;
    }

    static void PowerBlock(AlarmInfo a)
    {
        Console.WriteLine();
        Row(ConsoleColor.DarkYellow, ConsoleColor.Black, "  " + Strip(a.Headline));
        Row(ConsoleColor.DarkYellow, ConsoleColor.Black, "  ДЖЕРЕЛО: " + a.Source + "   " + a.Time);
        Console.WriteLine();
    }

    // Убираем эмодзи (astral + BMP-символы), оставляем текст и стрелки — для строгого вида.
    static string Strip(string s) => Regex.Replace(Regex.Replace(s, @"[\p{Cs}\p{So}]", ""), @"\s{2,}", " ").Trim();

    static void Row(ConsoleColor bg, ConsoleColor fg, string text)
    {
        if (text.Length > W) text = text[..W];
        try
        {
            Console.BackgroundColor = bg; Console.ForegroundColor = fg;
            Console.Write(" " + text.PadRight(W) + " ");
            Console.ResetColor(); Console.WriteLine();
        }
        catch { Console.WriteLine(text); }
    }

    static void HardBlock(AlarmInfo a)
    {
        var action = a.Kind switch
        {
            ThreatKind.Ballistic => "БАЛІСТИКА. В УКРИТТЯ НЕГАЙНО. ЧАСУ — СЕКУНДИ.",
            ThreatKind.Missile   => "РАКЕТА. В УКРИТТЯ НЕГАЙНО.",
            ThreatKind.Kab       => "КАБ. В УКРИТТЯ. ПОДАЛІ ВІД ВІКОН.",
            ThreatKind.Drone     => "ШАХЕД. В УКРИТТЯ. ПРАВИЛО ДВОХ СТІН.",
            ThreatKind.Aviation  => "АВІАЦІЯ. МОЖЛИВІ ПУСКИ. В УКРИТТЯ.",
            _                    => "ЗАГРОЗА. В УКРИТТЯ. НЕГАЙНО.",
        };
        Console.WriteLine();
        Row(ConsoleColor.Red, ConsoleColor.White, "");
        Row(ConsoleColor.Red, ConsoleColor.White, "   >>>   П О В І Т Р Я Н А   Т Р И В О Г А   <<<");
        Row(ConsoleColor.Red, ConsoleColor.White, "");
        Row(ConsoleColor.DarkRed, ConsoleColor.White, "  ЦІЛЬ:    " + Strip(a.Headline));
        Row(ConsoleColor.DarkRed, ConsoleColor.White, "  ДІЯ:     " + action);
        Row(ConsoleColor.DarkRed, ConsoleColor.Gray,  "  ДЖЕРЕЛО: " + a.Source + "   " + a.Time);
        Row(ConsoleColor.Red, ConsoleColor.White, "");
        Console.WriteLine();
    }

    static void CalmBlock(AlarmInfo a)
    {
        Console.WriteLine();
        Row(ConsoleColor.DarkGreen, ConsoleColor.White, "  ВІДБІЙ. Можна виходити з укриття.");
        Row(ConsoleColor.DarkGreen, ConsoleColor.Gray,  "  " + Strip(a.Headline) + "   " + a.Time);
        Console.WriteLine();
    }
}

// Сирена на ПК (реальный синтезированный звук через winmm — не пищалка).
public sealed class SirenNotifier : INotifier
{
    public Task Notify(AlarmInfo a) { if (!a.Silent && !a.Power) SirenSound.Play(a.Kind, a.Urgent, a.Clear); return Task.CompletedTask; }
    public static void Play(ThreatKind kind, bool urgent, bool clear) => SirenSound.Play(kind, urgent, clear);
}

// Генерирует настоящий WAV (низкий дрожащий вой с расстройкой = зловещий, «Stranger Things»-вайб)
// и проигрывает через звуковую карту. Свой синтез, не копия чужого звука.
public static class SirenSound
{
    const int SR = 22050;
    static readonly Dictionary<string, string> Cache = new();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool PlaySound(string? name, IntPtr hmod, uint flags);
    const uint SND_ASYNC = 0x0001, SND_FILENAME = 0x00020000;

    public static void Play(ThreatKind kind, bool urgent, bool clear)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { PlaySound(FileFor(kind, urgent, clear), IntPtr.Zero, SND_ASYNC | SND_FILENAME); }
        catch { /* звук не критичен */ }
    }

    // Звук по СТАДИИ жизненного цикла цели (трекер):
    //   Ping — короткий «сонар» подлёта (чем ближе, тем чаще его зовут);
    //   Imminent/Overhead — вой на подлёте / над районом (тип слышно по мотиву);
    //   Impact — низкий взрыв; Passed — нисходящий «отбой этой цели».
    public static void PlayStage(TrackSound sound, ThreatKind kind)
    {
        if (!OperatingSystem.IsWindows() || sound == TrackSound.None) return;
        var key = $"stage_{sound}_{kind}";
        try
        {
            if (!Cache.TryGetValue(key, out var path) || !File.Exists(path))
            {
                path = Path.Combine(Path.GetTempPath(), $"waralarm_{key}.wav");
                byte[] data = sound switch
                {
                    TrackSound.Ping     => BuildPing(kind),
                    TrackSound.Impact   => BuildImpact(),
                    TrackSound.Passed   => BuildPassed(),
                    TrackSound.Overhead => BuildWail(kind, true),
                    _                   => BuildWail(kind, true), // Imminent
                };
                File.WriteAllBytes(path, data);
                Cache[key] = path;
            }
            PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
        }
        catch { /* звук не критичен */ }
    }

    // ── «ПРИЛІТ НАД ДОМОМ» — окремий, найсильніший сигнал ────────────────────────
    // Швидкий двотоновий аварійний вій (як EU-emergency) — гучний, безперервний,
    // ні з чим не сплутати. Зациклюваний (тони згасають на межах — чисте повторення).
    public static byte[] BuildImpactHome()
    {
        var buf = new List<short>();
        for (int r = 0; r < 4; r++)
        {
            AppendTone(buf, 1040, 150); // высокий
            AppendTone(buf, 720, 150);  // низкий — резкий контраст, «паника»
        }
        return Wav(buf.ToArray());
    }

    // Проиграть сильный сигнал прилёта над домом (Windows).
    public static void PlayImpactHome()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            const string key = "impact_home";
            if (!Cache.TryGetValue(key, out var path) || !File.Exists(path))
            {
                path = Path.Combine(Path.GetTempPath(), "waralarm_impact_home.wav");
                File.WriteAllBytes(path, BuildImpactHome());
                Cache[key] = path;
            }
            PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
        }
        catch { /* звук не критичен */ }
    }

    // ── ☢️ РАДІАЦІЙНА/ЯДЕРНА НЕБЕЗПЕКА — окремий зловісний сигнал ЦО ──────────────
    // Повільний вій «attack signal»: підйом і спад, низько й гучно. Ні з чим не сплутати,
    // навмисно відрізняється від усіх інших звуків. Зациклюваний.
    public static byte[] BuildNuclear()
    {
        var buf = new List<short>();
        AppendSweep(buf, 300, 620, 1800); // повільний підйом
        AppendSweep(buf, 620, 300, 1800); // повільний спад
        return Wav(buf.ToArray());
    }

    public static void PlayNuclear()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            const string key = "nuclear";
            if (!Cache.TryGetValue(key, out var path) || !File.Exists(path))
            {
                path = Path.Combine(Path.GetTempPath(), "waralarm_nuclear.wav");
                File.WriteAllBytes(path, BuildNuclear());
                Cache[key] = path;
            }
            PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
        }
        catch { /* звук не критичен */ }
    }

    static string FileFor(ThreatKind kind, bool urgent, bool clear)
    {
        var key = $"{kind}_{urgent}_{clear}";
        if (Cache.TryGetValue(key, out var p) && File.Exists(p)) return p;
        var path = Path.Combine(Path.GetTempPath(), $"waralarm_{key}.wav");
        File.WriteAllBytes(path, clear ? BuildClear() : BuildWail(kind, urgent));
        Cache[key] = path;
        return path;
    }

    static byte[] BuildWail(ThreatKind kind, bool urgent)
    {
        // У каждого типа — свой РИТМИЧНЫЙ мотив пиков (не монотонно): тройные, восходящие,
        // тяжёлые двойные. Мотив = список (частота Гц, длина мс), повторяется несколько раз.
        (double f, int ms)[] motif = kind switch
        {
            ThreatKind.Ballistic => new (double, int)[] { (1250, 85), (1250, 85), (760, 150) },           // паническое «пи-пи-БО»
            ThreatKind.Missile   => new (double, int)[] { (600, 120), (850, 120), (1180, 190) },          // восходящее
            ThreatKind.Kab       => new (double, int)[] { (180, 300), (135, 380) },                       // тяжёлое «ДУН… ДУНН»
            ThreatKind.Drone     => new (double, int)[] { (720, 130), (560, 130), (720, 130), (930, 210) },// волнистое
            ThreatKind.Aviation  => new (double, int)[] { (360, 180), (300, 240) },                       // низкое двойное
            _                    => new (double, int)[] { (700, 120), (930, 120), (700, 150) },
        };
        int innerGapMs = 45;                              // пауза между пиками мотива
        int patternGapMs = kind == ThreatKind.Kab ? 360 : 210; // пауза между повторами мотива
        int reps = urgent ? 5 : 3;

        var buf = new List<short>();
        // Драматичный «взлёт» настоящей сирены перед мотивом (только для срочных).
        if (urgent)
        {
            double lo = kind == ThreatKind.Kab ? 160 : 380;
            AppendSweep(buf, lo, lo * 2.6, 420);          // разгон вверх
            AppendSweep(buf, lo * 2.6, lo * 1.6, 200);    // спад — «вой»
            AppendSilence(buf, 120);
        }
        for (int r = 0; r < reps; r++)
        {
            foreach (var (f, ms) in motif) { AppendTone(buf, f, ms); AppendSilence(buf, innerGapMs); }
            AppendSilence(buf, patternGapMs);
        }
        return Wav(buf.ToArray());
    }

    // Плавный «вой» сирены: частота едет от f0 к f1. Богатый тембр (гармоники + суб).
    static void AppendSweep(List<short> buf, double f0, double f1, int ms)
    {
        int n = SR * ms / 1000; double sec = ms / 1000.0; double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double f = f0 + (f1 - f0) * (t / sec);
            phase += 2 * Math.PI * f / SR;
            double wave = 0.50 * Math.Sign(Math.Sin(phase))
                        + 0.28 * Math.Sin(phase * 2)
                        + 0.30 * Math.Sin(phase * 0.5);
            double env = Math.Min(1.0, Math.Min(t / 0.02, (sec - t) / 0.02));
            buf.Add((short)(Math.Tanh(Math.Clamp(wave, -1, 1) * 1.3) * env * 32000));
        }
    }

    static void AppendTone(List<short> buf, double f, int ms)
    {
        int n = SR * ms / 1000; double sec = ms / 1000.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double trem = 0.86 + 0.14 * Math.Sin(2 * Math.PI * 7.0 * t);       // тремоло — «живая» сирена
            double wave = 0.46 * Math.Sign(Math.Sin(2 * Math.PI * f * t))       // жёсткий квадрат — страшно
                        + 0.28 * Math.Sin(2 * Math.PI * f * 1.012 * t)          // расстройка → «неправильный» тон
                        + 0.20 * Math.Sin(2 * Math.PI * f * 2.0 * t)            // октава вверх — метал/яркость
                        + 0.30 * Math.Sin(2 * Math.PI * f * 0.5 * t);          // суб-октава — мощь/бас
            double punch = Math.Exp(-t * 60) * 0.5;                             // атака-щелчок для панча
            double env = Math.Min(1.0, Math.Min(t / 0.005, (sec - t) / 0.010));
            double v = Math.Clamp((wave * trem + punch), -1, 1) * env;
            v = Math.Tanh(v * 1.4);                                             // мягкое насыщение — плотнее и громче
            buf.Add((short)(v * 32000));
        }
    }

    static void AppendSilence(List<short> buf, int ms)
    {
        int n = SR * ms / 1000;
        for (int i = 0; i < n; i++) buf.Add(0);
    }

    // Одиночный «сонар»: короткий тон с быстрым спадом + лёгкое эхо. Тон зависит от типа
    // (баллистика/ракета выше и тревожнее, шахед — характерный). Это «пикание» подлёта.
    static byte[] BuildPing(ThreatKind kind)
    {
        double f = kind switch
        {
            ThreatKind.Ballistic => 1150,
            ThreatKind.Missile   => 980,
            ThreatKind.Kab       => 300,
            ThreatKind.Drone     => 760,
            ThreatKind.Aviation  => 360,
            _                    => 700,
        };
        var buf = new List<short>();
        AppendPing(buf, f, 150, 0.9);   // основной
        AppendSilence(buf, 90);
        AppendPing(buf, f, 90, 0.35);   // эхо
        return Wav(buf.ToArray());
    }

    static void AppendPing(List<short> buf, double f, int ms, double amp)
    {
        int n = SR * ms / 1000; double sec = ms / 1000.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double wave = 0.55 * Math.Sign(Math.Sin(2 * Math.PI * f * t))
                        + 0.45 * Math.Sin(2 * Math.PI * f * 1.01 * t);
            double env = Math.Exp(-t * 9) * Math.Min(1.0, t / 0.004); // резкий старт, быстрый спад
            _ = sec;
            buf.Add((short)(Math.Clamp(wave, -1, 1) * env * amp * 32000));
        }
    }

    // Взрыв: мощный удар — резкий «кряк» (шум) + глубокий суб-бас 120→28 Гц с длинным хвостом.
    static byte[] BuildImpact()
    {
        double seconds = 1.4;
        int n = (int)(SR * seconds);
        var s = new short[n];
        var rng = new Random(20260829); // детерминизм → стабильный кэш-файл
        double lp = 0; // однополюсный НЧ-фильтр для «бумкающего» шума
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double f = 120 - 92 * (t / seconds);                       // тон падает вниз
            double body = Math.Sin(2 * Math.PI * f * t);
            double sub  = Math.Sin(2 * Math.PI * (48 - 22 * (t / seconds)) * t); // суб-бас — чувствуется телом
            double noise = rng.NextDouble() * 2 - 1;
            lp += (noise - lp) * 0.12;                                 // приглушённый рокот
            double crack = noise * Math.Exp(-t * 30);                  // острый треск на старте
            double rumble = lp * Math.Exp(-t * 2.0);                   // рокот-хвост
            double env = Math.Min(1.0, t / 0.002) * Math.Exp(-t * 2.4);
            double v = (0.60 * body + 0.45 * sub + 0.40 * crack + 0.35 * rumble) * env;
            s[i] = (short)(Math.Tanh(v * 1.5) * 32000);               // насыщение — плотный «бум»
        }
        return Wav(s);
    }

    // «Пройшла далі»: нисходящее трио — тревога по этой цели снимается, но не общий отбой.
    static byte[] BuildPassed()
    {
        var buf = new List<short>();
        foreach (var f in new[] { 720.0, 560.0, 420.0 })
        {
            AppendTone(buf, f, 160);
            AppendSilence(buf, 60);
        }
        return Wav(buf.ToArray());
    }

    static byte[] BuildClear()
    {
        double seconds = 1.7;
        int n = (int)(SR * seconds);
        var s = new short[n];
        double[] notes = { 523.25, 659.25, 783.99 }; // C-E-G, спокойно
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SR;
            double f = notes[Math.Min(2, (int)(t / (seconds / 3)))];
            double env = Math.Min(1.0, Math.Min(t / 0.05, (seconds - t) / 0.3));
            s[i] = (short)(Math.Sin(2 * Math.PI * f * t) * env * 0.4 * 30000);
        }
        return Wav(s);
    }

    static byte[] Wav(short[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var v in samples) w.Write(v);
        return ms.ToArray();
    }
}

// Push на телефон через ntfy.sh — телефон звонит, даже когда ты не у ПК.
// Ставишь приложение «ntfy» из Google Play, подписываешься на свой топик — и всё.
// JSON-API поддерживает UTF-8 (кириллицу в заголовке/тексте).
public sealed class NtfyNotifier : INotifier
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly string _topic;
    readonly string _server;

    public NtfyNotifier(string topic, string server = "https://ntfy.sh") { _topic = topic; _server = server.TrimEnd('/'); }

    public async Task Notify(AlarmInfo a)
    {
        var title = a.Power  ? "🔌 Світло"
                  : a.Clear  ? "✅ ВІДБІЙ — можна виходити"
                  : a.Urgent ? "🔴 В УКРИТТЯ — НЕГАЙНО"
                             : "🔴 ПОВІТРЯНА ТРИВОГА";
        var body = a.Power || a.Clear ? $"{a.Headline}\n{a.Text}"
                                      : $"{a.Headline}\n➤ В УКРИТТЯ. Подалі від вікон.\n{a.Text}";
        var payload = JsonSerializer.Serialize(new
        {
            topic = _topic,
            title,
            message = body,
            priority = a.Power ? 4 : a.Clear ? 3 : 5, // свет — высокий, но не «сирена сквозь тишину»
            tags = new[] { a.Power ? "electric_plug" : a.Clear ? "white_check_mark" : "rotating_light" },
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var resp = await Http.PostAsync(_server, content);
        if (!resp.IsSuccessStatusCode)
            Console.Error.WriteLine($"⚠ push ntfy: {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }
}
