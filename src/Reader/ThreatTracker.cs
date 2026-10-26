using System.Text.RegularExpressions;

// ── WarAlarm · Трекер жизненного цикла угрозы ────────────────────────────────
// Держит «живые» угрозы и ведёт каждую по стадиям, как её видит человек:
//   Approaching  — летит к тебе, далеко → пикает РЕДКО, ближе → ЧАЩЕ (обратный отсчёт);
//   Imminent     — на подлёте (ETA ≤ ~1.5 мин или ≤ 4 км) → непрерывный вой;
//   Overhead     — прямо над твоим районом → пик;
//   Impact       — «вибух/приліт/влучання» → звук взрыва;
//   Passed       — «пролетів/далі на <дальше>» или удаляется → отбой этой цели;
//   Gone         — давно нет обновлений → снимаем с карты.
// Трекер НЕ трогает звук/сеть сам — он возвращает «что произошло», а Program.cs решает,
// что сыграть и что показать. Так его легко гонять в демо (`dotnet run -- track`).

public enum TrackStage { Approaching, Imminent, Overhead, Impact, Passed, Gone }

// Что проиграть по этому обновлению (звук выбирает Program через SirenSound).
public enum TrackSound { None, Ping, Imminent, Overhead, Impact, Passed }

public sealed class Threat
{
    public required string Id;
    public required string Zone;          // "Харків" / "Київ"
    public ThreatKind Kind;
    public string? Ammo;
    public bool Recon;                    // «розвідка/дорозвідка» — низький пріоритет: не виє, не будить
    // Приблизна точка: місто не знайдено → центр ОБЛАСТІ (для Київщини це сам Київ!).
    // На карті показуємо, але тривогу/підліт/пікання від неї НЕ рахуємо.
    public bool Approx => LastPlace.EndsWith("(обл.)");
    public string? Oblast;                // область із заголовка поста радара (для заміни «кусками»)
    public (double lat, double lon)? Pos; // последняя геопривязка
    public string LastPlace = "";
    public double? DistKm;                // до дома
    public double? PrevDistKm;
    public string Dir = "";               // компас от дома («ПнСх»)
    public double? Heading;               // куда летит, градусы (0=Пн) — для стрелки на карте
    public double? EtaMin;                // подлёт до дома
    public DateTime FirstSeen;
    public DateTime LastUpdate;
    public DateTime LastPing;             // когда последний раз пикнули (для каденции)
    public TrackStage Stage = TrackStage.Approaching;
    public bool MyArea;                   // назван ТВОЙ район
    public HashSet<string> Hits = new();  // названные места по этой цели (для истории/траектории)
    public int Count;                     // явная к-сть из текста поста («2 БпЛА»)
    public HashSet<string> Srcs = new(StringComparer.OrdinalIgnoreCase); // канали, що бачили ціль (хаб джерел)
    public List<(string place, double? dist, DateTime t)> Trail = new();
    public List<(double lat, double lon)> Track = new(); // реальні точки шляху (для лінії на карті)
    // Курс із РЕАЛЬНОГО руху (дві точки / «з X на Y»), а не здогадка «вглиб країни».
    // Лише по такому курсу застосунок рухає маркер між постами.
    public bool HeadingKnown;
    public (string name, double lat, double lon)? CourseTo; // «курсом на Полтаву» — куди летить за текстом
    // ── Висота / падіння з тексту каналу (Altitude.Parse) ──
    public double? AltM;                    // «на висоті ~100 м»
    public string? AltNote;                 // «низько» / «знижується» / «набирає висоту» / «падає» / «збито»
    public DateTime AltAt;
    public bool Down;                       // падає / збито → знімаємо за кілька хвилин
    public DateTime DownAt;
    // ── Траєкторія (балістика / КАБ / «з X на Y»): звідки → куди, коли пуск ──
    public (double lat, double lon)? From;  // район пуску / точка скиду КАБ / звідки летить
    public string? FromName;
    public (double lat, double lon)? To;    // ціль польоту (для балістики/КАБ)
    public DateTime LaunchAt;               // коли почався політ (перше повідомлення)
    public bool Launch;                     // лише пуск (ціль невідома): коло досяжності з точки пуску
    public bool FromGuess;                  // звідки — наша оцінка, а не з тексту
    /// Тривалість польоту від From до To з швидкістю засобу, хв.
    public double? FlightMin => From is { } f && To is { } to
        ? ThreatGeo.Km(f, to) / ThreatGeo.SpeedKmh(Kind, Ammo) * 60 : null;
    // ×N = число целей строго из текста поста («2 БпЛА»), не раздувая по числу упомянутых мест
    public int Amount => Math.Max(1, Count);
}

public readonly record struct TrackUpdate(Threat Threat, bool StageChanged, TrackSound Sound, string Note);

public sealed class ThreatTracker
{
    readonly (double lat, double lon) _home;
    readonly Dictionary<string, Threat> _active = new();
    readonly Dictionary<string, string> _alias = new(); // ключ нового міста → Id цілі, що туди долетіла
    readonly object _gate = new(); // Ingest (поток updates) и Tick (фоновый Task) — разные потоки

    public ThreatTracker((double lat, double lon) home) => _home = home;

    // Снимок (копия списка) — безопасно перечислять из карты, пока Tick меняет словарь.
    public IReadOnlyCollection<Threat> Active { get { lock (_gate) return _active.Values.ToList(); } }
    public (double lat, double lon) Home => _home;

    // ── приход события от монитора ────────────────────────────────────────────
    // Возвращает обновление (что показать/сыграть) либо null, если событие не по теме.
    public TrackUpdate? Ingest(KhEvent e, string zone, DateTime now)
    {
        var low = e.Raw.ToLowerInvariant();

        // Отбой по городу — гасим все цели этой зоны (это делает Program через Sweep/clear).
        if (e.Signal is KhSignal.AlarmOff)
            return null;

        bool impact = Regex.IsMatch(low, @"вибух|влучанн|приліт|прил[её]т");
        bool passed = Regex.IsMatch(low, @"пролет|пройш|вийш|мину[вл]|подал[іи]|далі на|дальше на|транзит");

        // Не угроза (чисто/новость) без импакта — пропускаем.
        if (e.Signal != KhSignal.Threat && !impact) return null;

        // Ближайшее к дому из названных мест = «где сейчас цель».
        (double lat, double lon)? pos = null;
        string place = "";
        double best = double.MaxValue;
        foreach (var name in e.Places)
        {
            var c = ThreatGeo.Of(name);
            if (c is not { } cc) continue;
            double d = ThreatGeo.Km(cc, _home);
            if (d < best) { best = d; pos = cc; place = name; }
        }

        lock (_gate)
        {
        // Ключ цели: зона+место+тип — каждое НАЗВАННОЕ место = своя стрелка на карте.
        // Обобщённый пост БЕЗ места («вибух…», «тривога») цепляем к самой свежей цели этой зоны.
        var placeKey = place.Length > 0 ? place : zone;
        string key;
        // Лише ціль ТОГО Ж типу: «бандероль…» без місця не має оновлювати реактивний шахед.
        if (place.Length == 0
            && _active.Values.Where(a => a.Zone == zone && (e.Kind is ThreatKind.Target or ThreatKind.Unknown || a.Kind == e.Kind))
                   .OrderByDescending(a => a.LastUpdate).FirstOrDefault() is { } recent)
            key = recent.Id;
        else
            key = $"{zone}:{placeKey}:{e.Kind}";

        bool isNew = !_active.TryGetValue(key, out var t);
        if (isNew)
        {
            t = new Threat { Id = key, Zone = zone, Kind = e.Kind, FirstSeen = now };
            _active[key] = t;
        }
        t!.Ammo ??= e.Extra;
        // Розвідка/дорозвідка — низький пріоритет (не виє, не будить). Якщо в апдейті
        // з'явилось слово удару — знімаємо позначку (ціль ескалювала з розвідки в удар).
        t.Recon = Regex.IsMatch(low, @"розвід|дорозвід")
                  && !Regex.IsMatch(low, @"вибух|влучанн|приліт|удар|шахед на|каб|ракет");
        t.LastUpdate = now;
        foreach (var pn in e.Places) t.Hits.Add(pn);
        if (place.Length > 0) t.Hits.Add(place);
        var cm = Regex.Match(e.Raw, @"(\d+)\s*(шахед|шахід|дрон|бпла|ракет|ціл|каб)", RegexOptions.IgnoreCase);
        if (cm.Success && int.TryParse(cm.Groups[1].Value, out var cnt)) t.Count = cnt;
        else if (t.Count == 0) t.Count = 1;
        if (e.Tier == KhTier.MyArea) t.MyArea = true;

        if (pos is { } p)
        {
            t.PrevDistKm = t.DistKm;
            t.Pos = p;
            t.LastPlace = place;
            t.DistKm = ThreatGeo.Km(p, _home);
            t.Dir = ThreatGeo.Compass(ThreatGeo.Bearing(_home, p));
            t.EtaMin = ThreatGeo.EtaMin(p, _home, t.Kind, t.Ammo);
            if (t.Trail.Count == 0 || t.Trail[^1].place != place)
                t.Trail.Add((place, t.DistKm, now));
            AddTrack(t, p);
            // Направление стрелки: по движению (если есть 2 точки), иначе — вглубь страны.
            if (t.Trail.Count >= 2 && ThreatGeo.Of(t.Trail[^2].place) is { } prevPos)
            {
                t.Heading = ThreatGeo.Bearing(prevPos, p);
                t.HeadingKnown = true;
            }
            else if (!t.HeadingKnown)
                t.Heading = ThreatGeo.Bearing(p, ThreatGeo.UACenter);
        }
        ApplyAltitude(t, Altitude.Parse(e.Raw), now);

        var prev = t.Stage;
        if (impact) t.Stage = TrackStage.Impact;
        else if (passed && (t.PrevDistKm is null || (t.DistKm ?? 0) >= (t.PrevDistKm ?? 0))) t.Stage = TrackStage.Passed;
        else t.Stage = Classify(t);

        var sound = SoundFor(t.Stage, prev, isNew);
        if (t.Recon) sound = TrackSound.None; // разведка — тихо (только карта/консоль)
        var note = Note(t);
        if (t.Stage is TrackStage.Approaching && sound == TrackSound.Ping) t.LastPing = now;
        return new TrackUpdate(t, t.Stage != prev || isNew, sound, note);
        }
    }

    // ── приход всеукраинского события (@raketa_trevoga): «N ТИП на Місто» ──────
    // Геолокуем город по справочнику всей Украины и ставим стрелку на карте.
    public TrackUpdate? IngestUa(ThreatEvent ev, DateTime now, (string name, double lat, double lon)? at = null)
    {
        if (ev.Kind == ThreatKind.AllClear) return null;
        // Хаб уже знайшов точку з урахуванням області каналу — беремо її (пошук лише за назвою
        // плутає тезок: «Новоселівка» на Чернігівщині ≠ Новоселівка на Полтавщині).
        // Інакше: точне місто; якщо нема в довіднику — центр області (з заголовка поста).
        var g = at
                ?? ThreatGeo.Resolve(ev.City)
                ?? (ev.City is { } cc ? PlaceFinder.Find(cc, ev.Oblast is { } eo ? new[] { eo } : Array.Empty<string>()) : null);
        bool approx = false;
        // «на півночі Сумщини, курсом на Полтаву» — у тій частині області, а не над містом курсу.
        if (g is null && ev.Toward is not null && ev.Oblast is not null && OblastGeo.PartPoint(ev.Oblast, ev.Side) is { } part)
        { g = (OblastGeo.Region(part.lat, part.lon), part.lat, part.lon); approx = true; }
        if (g is null) { g = ThreatGeo.ResolveOblast(ev.Oblast); approx = true; }
        if (g is not { } gp) return null; // ни города, ни области — не ставим
        var pos = (gp.lat, gp.lon);
        // Приблизительные цели (по области) сливаем в одну метку области с ×N; точные — по городу.
        var place = approx ? gp.name + " (обл.)" : gp.name;
        lock (_gate)
        {
            // Приблизні (місто не знайдено → центр області): ключ із НАЗВОЮ міста, щоб
            // «Літин» і «Гнівань» не злипались в одну ціль із останнім ×N (губились дрони).
            // На карті вони все одно в одній точці — кластер МapWriter підсумує ×N.
            var key = approx ? $"UA:обл:{gp.name}:{(ev.City ?? "").ToLowerInvariant()}:{ev.Kind}" : $"UA:{gp.name}:{ev.Kind}";
            if (_alias.TryGetValue(key, out var aliased) && _active.ContainsKey(aliased)) key = aliased;
            bool isNew = !_active.TryGetValue(key, out var t);
            // Нове місто — це часто ТА САМА ціль, що пролетіла далі. Раніше кожне місто
            // ставало новою міткою, а стара висіла «примарою» 15 хв без оновлень (і курсу
            // не було — лише здогадка «вглиб країни»). Тепер шукаємо попередника.
            if (isNew && !approx && Predecessor(ev.Kind, pos, now) is { } pred)
            {
                t = pred;
                _alias[key] = pred.Id;
                isNew = false;
            }
            if (isNew) { t = new Threat { Id = key, Zone = "UA", Kind = ev.Kind, FirstSeen = now }; _active[key] = t; }
            // Балістика з названою ціллю — коло «куди могла долетіти» з місця пуску вже зайве.
            if (isNew && ev.Kind == ThreatKind.Ballistic && !approx)
                foreach (var k in _active.Where(a => a.Value.Launch && a.Value.Kind == ThreatKind.Ballistic).Select(a => a.Key).ToList())
                    _active.Remove(k);
            // Нова справжня ціль-дрон — це, ймовірно, частина групи пуску, що летіла сюди: забираємо з групи.
            if (isNew && !approx && ev.Kind is ThreatKind.Drone or ThreatKind.Target)
                LaunchGroups.Absorb(pos, Math.Max(1, ev.Count), now);
            var prevPos = t!.Pos;
            t!.Ammo ??= ev.Extra;
            t.Oblast = ev.Oblast ?? t.Oblast;
            t.Recon = ((ev.Extra ?? "") + " " + ev.Raw).ToLowerInvariant().Contains("розвід");
            t.LastUpdate = now;
            t.Hits.Add(ev.City ?? gp.name);
            t.Count = ev.Count > 0 ? ev.Count : 1;
            t.PrevDistKm = t.DistKm;
            t.Pos = pos; t.LastPlace = place;
            t.DistKm = ThreatGeo.Km(pos, _home);
            t.Dir = ThreatGeo.Compass(ThreatGeo.Bearing(_home, pos));
            t.EtaMin = ThreatGeo.EtaMin(pos, _home, t.Kind, t.Ammo);
            if (t.Trail.Count == 0 || t.Trail[^1].place != gp.name) t.Trail.Add((gp.name, t.DistKm, now));
            AddTrack(t, pos);
            if (prevPos is { } pp && ThreatGeo.Km(pp, pos) > 2)
            {
                t.Heading = ThreatGeo.Bearing(pp, pos); // реальний рух між двома точками
                t.HeadingKnown = true;
            }
            else if (!t.HeadingKnown)
                t.Heading = ThreatGeo.Bearing(pos, ThreatGeo.UACenter); // лише для стрілки, не для руху
            ApplyCourse(t, ev, pos, isNew, now);
            // Курс, названий у пості («курсом на Полтаву»), — справжній напрямок.
            if (ev.Toward is { } tw && (ThreatGeo.Resolve(tw) ?? PlaceFinder.Find(tw, Array.Empty<string>())) is { } dest
                && ThreatGeo.Km(pos, (dest.lat, dest.lon)) > 5)
            {
                t.Heading = ThreatGeo.Bearing(pos, (dest.lat, dest.lon));
                t.HeadingKnown = true;
                t.CourseTo = (dest.name, dest.lat, dest.lon);
            }
            ApplyAltitude(t, ev.Alt ?? Altitude.Parse(ev.Raw), now);
            var prev = t.Stage; t.Stage = Classify(t);
            return new TrackUpdate(t, t.Stage != prev || isNew, TrackSound.None, Note(t));
        }
    }

    // Траєкторія: «з X на Y» дає курс; балістика й КАБ летять від точки пуску/скиду до
    // цілі з реальною швидкістю засобу (застосунок рухає маркер уздовж From→To).
    static void ApplyCourse(Threat t, ThreatEvent ev, (double lat, double lon) pos, bool isNew, DateTime now)
    {
        if (LaunchSites.Resolve(ev.From) is { } f && ThreatGeo.Km((f.lat, f.lon), pos) > 3)
        {
            t.From = (f.lat, f.lon); t.FromName = f.name; t.FromGuess = false;
        }
        if (t.Kind is ThreatKind.Ballistic or ThreatKind.Kab)
        {
            t.To = pos;
            if (t.From is null)
            {
                if (t.Kind == ThreatKind.Ballistic)
                {
                    var o = LaunchSites.BallisticOrigin(pos);
                    t.From = (o.lat, o.lon); t.FromName = o.name;
                }
                else { t.From = LaunchSites.KabRelease(pos); t.FromName = "скид КАБ"; }
                t.FromGuess = true;
            }
            // Коли канал пише «балістика на Київ», ракета вже на спуску: моніторів чути за
            // ~1 хв до удару. Тож політ «стартував» раніше — на ~80 % шляху (КАБ — ~50 %).
            // Інакше мітка вилітала з Брянська в момент поста й «летіла» ще після вибухів.
            if (isNew)
                t.LaunchAt = t.FlightMin is { } fm0
                    ? now - TimeSpan.FromMinutes(fm0 * (t.Kind == ThreatKind.Ballistic ? 0.8 : 0.5))
                    : now;
        }
        // Відомо, звідки летить, а траєкторії з місць ще нема — курс від From до цілі.
        if (t.From is { } fr && t.Trail.Count < 2)
        {
            t.Heading = ThreatGeo.Bearing(fr, pos);
            t.HeadingKnown |= !t.FromGuess; // «з X на Y» у тексті — справжній курс
        }
    }

    // Попередник для нового міста: та сама ціль, що пролетіла далі. Того ж типу, точна
    // (не «центр області»), востаннє бачена 40 с … 12 хв тому (не з цього ж поста —
    // інакше «Мерефа, Зміїв» в одному пості злиплись би), і реально могла долетіти:
    // відстань ≤ швидкість × час × 1.5 + 35 км (точки — центри міст, пости запізнюються). Якщо курс уже відомий — не розвертаємо
    // ціль назад (> 110°). Обираємо найближчу.
    Threat? Predecessor(ThreatKind kind, (double lat, double lon) pos, DateTime now)
    {
        if (kind is not (ThreatKind.Drone or ThreatKind.Missile or ThreatKind.Target)) return null;
        Threat? best = null;
        double bestKm = double.MaxValue;
        foreach (var c in _active.Values)
        {
            if (c.Kind != kind || c.Zone != "UA" || c.Launch || c.Approx || c.Down || c.Pos is not { } cp) continue;
            if (c.Stage is TrackStage.Impact or TrackStage.Passed) continue;
            var age = now - c.LastUpdate;
            if (age < TimeSpan.FromSeconds(40) || age > TimeSpan.FromMinutes(12)) continue;
            double km = ThreatGeo.Km(cp, pos);
            if (km < 3) continue; // те саме місце — це не рух
            double reach = ThreatGeo.SpeedKmh(c.Kind, c.Ammo) / 60 * age.TotalMinutes * 1.5 + 35;
            if (km > reach) continue;
            if (c.HeadingKnown && c.Heading is { } h)
            {
                double turn = Math.Abs(((ThreatGeo.Bearing(cp, pos) - h) % 360 + 540) % 360 - 180);
                if (turn > 110) continue;
            }
            if (km < bestKm) { bestKm = km; best = c; }
        }
        return best;
    }

    static void AddTrack(Threat t, (double lat, double lon) p)
    {
        if (t.Track.Count > 0 && ThreatGeo.Km(t.Track[^1], p) < 0.5) return;
        t.Track.Add(p);
        if (t.Track.Count > 24) t.Track.RemoveAt(0);
    }

    // Висота / «падає» / «збито» з тексту поста.
    static void ApplyAltitude(Threat t, Altitude.Info? info, DateTime now)
    {
        if (info is not { } a) return;
        if (a.Meters is { } m) t.AltM = m;
        if (a.Note is { } n) t.AltNote = n;
        t.AltAt = now;
        if (a.Down && !t.Down) { t.Down = true; t.DownAt = now; }
        if (a.Note == "збито") t.Stage = TrackStage.Passed; // збита — не загроза, знімемо за 90 с
    }

    // ── лише пуск (ціль ще невідома): «балістика з Курська» → коло досяжності ──────
    public TrackUpdate? IngestLaunch(ThreatEvent ev, DateTime now)
    {
        if (LaunchSites.Resolve(ev.From) is not { } f) return null;
        var pos = (f.lat, f.lon);
        lock (_gate)
        {
            var key = $"LAUNCH:{f.name}:{ev.Kind}";
            bool isNew = !_active.TryGetValue(key, out var t);
            if (isNew) { t = new Threat { Id = key, Zone = "UA", Kind = ev.Kind, FirstSeen = now, LaunchAt = now }; _active[key] = t; }
            t!.Ammo ??= ev.Extra;
            t.Launch = true;
            t.LastUpdate = now;
            t.Count = Math.Max(t.Count, Math.Max(1, ev.Count));
            t.Pos = pos; t.From = pos; t.FromName = f.name;
            t.LastPlace = $"пуск: {f.name}";
            t.DistKm = ThreatGeo.Km(pos, _home);
            t.Dir = ThreatGeo.Compass(ThreatGeo.Bearing(_home, pos));
            t.Heading = ThreatGeo.Bearing(pos, _home);
            t.EtaMin = null; // ціль невідома
            return new TrackUpdate(t, isNew, TrackSound.None, $"{Label(t)} — пуск з {f.name}");
        }
    }

    // ── тик по таймеру: пересчитать ETA и решить, пора ли пикнуть/сменить стадию ──
    public List<TrackUpdate> Tick(DateTime now)
    {
        var outp = new List<TrackUpdate>();
        lock (_gate)
        {
        foreach (var k in _alias.Where(a => !_active.ContainsKey(a.Value)).Select(a => a.Key).ToList())
            _alias.Remove(k);
        foreach (var t in _active.Values.ToList())
        {
            // Уменьшаем ETA на прошедшее время (цель летит и между постами).
            if (t.EtaMin is { } eta && t.Stage is TrackStage.Approaching or TrackStage.Imminent)
            {
                double mins = (now - t.LastUpdate).TotalMinutes;
                t.EtaMin = Math.Max(0, eta - 0); // базу не трогаем, показываем от LastUpdate
                _ = mins;
            }

            // Снятие с карты (точність!): ціль НЕ повинна «зависати», але й не має блимати.
            // Приліт/пройшла — 90 сек. Точна локальна ціль — 4 хв (свіжі оновлення від
            // локального монітора тримають її живою). Обласна ціль (всеукр. канал згадує
            // область рідше, ніж раз на 4 хв) — тримаємо 15 хв, інакше карта пустіє посеред
            // нальоту (баг «з'явилось і за хвилину зникло», 12.09).
            // Точне місто з всеукр. каналу — 10 хв (рухома ціль тепер оновлюється сама,
            // а не лишає «примару»); лише «центр області» — 15 хв.
            var goneAfter = t.Kind == ThreatKind.Fpv ? TimeSpan.FromMinutes(12) // ФПВ-загроза громаді — коротка, канали повторюють
                          : t.LastPlace.EndsWith("(обл.)") ? TimeSpan.FromMinutes(15)
                          : t.Zone == "UA" ? TimeSpan.FromMinutes(10)
                          : TimeSpan.FromMinutes(4);
            // «Падає» — 3 хв без нових згадок, і ціль знімаємо.
            if (t.Down && now - t.DownAt > TimeSpan.FromMinutes(3) && t.LastUpdate <= t.DownAt)
                goneAfter = TimeSpan.Zero;
            var age = now - t.LastUpdate;
            // Балістика / КАБ: політ триває лічені хвилини — знімаємо через ~3 хв після
            // розрахункового прильоту; «лише пуск» без цілі — через 8 хв.
            var flown = now - t.LaunchAt;
            bool landed = t.Kind is ThreatKind.Ballistic or ThreatKind.Kab && !t.Launch
                          && t.FlightMin is { } fm && flown > TimeSpan.FromMinutes(fm + 1); // долетіла — за хвилину знімаємо
            bool launchOver = t.Launch && flown > TimeSpan.FromMinutes(t.Kind == ThreatKind.Ballistic ? 4 : 8); // балістика — лічені хвилини
            if ((t.Stage is TrackStage.Impact or TrackStage.Passed && age > TimeSpan.FromSeconds(90))
                || age > goneAfter || landed || launchOver)
            {
                t.Stage = TrackStage.Gone;
                _active.Remove(t.Id);
                outp.Add(new TrackUpdate(t, true, TrackSound.None, $"{Label(t)} — знято (тиша {age.TotalMinutes:0} хв)"));
                continue;
            }

            var prev = t.Stage;
            var st = Classify(t);
            if (st != prev && st is TrackStage.Imminent)
            {
                t.Stage = st;
                outp.Add(new TrackUpdate(t, true, TrackSound.Imminent, Note(t)));
                continue;
            }
            t.Stage = st;

            // Каденция «пикания» на подлёте: чем ближе — тем чаще.
            if (t.Stage == TrackStage.Approaching)
            {
                // Пикаем только по близким целям (≤55 км). Далёкие по стране — только на карте.
                // Разведку НЕ пикаем совсем — иначе «орёт постоянно» без реальной угрозы удара.
                double every = PingEverySec(EtaNow(t, now), t.DistKm);
                if (!t.Recon && !t.Approx && t.DistKm is { } dd && dd <= 55 && (now - t.LastPing).TotalSeconds >= every)
                {
                    t.LastPing = now;
                    outp.Add(new TrackUpdate(t, false, TrackSound.Ping, Note(t)));
                }
            }
            else if (t.Stage == TrackStage.Imminent)
            {
                if ((now - t.LastPing).TotalSeconds >= 6) // непрерывный вой каждые ~6 с
                {
                    t.LastPing = now;
                    outp.Add(new TrackUpdate(t, false, TrackSound.Imminent, Note(t)));
                }
            }
        }
        }
        return outp;
    }

    // Погасить все цели зоны (пришёл общий «відбій по місту»).
    public List<Threat> Sweep(string zone)
    {
        lock (_gate)
        {
            var gone = _active.Values.Where(t => t.Zone == zone).ToList();
            foreach (var t in gone) _active.Remove(t.Id);
            return gone;
        }
    }

    // Радар постит «кусками» (1–3 області за пост): новий пост — актуальна картина ЛИШЕ
    // для своїх областей. Тож гасимо старі дрони тільки цих областей, решту не чіпаємо
    // (раніше гасили все по країні → на карті лишався останній шматок, 2 цілі замість 150).
    public int SweepUaOblasts(IEnumerable<ThreatEvent> events)
    {
        var obl = events.Where(e => e.Oblast != null && e.Kind is ThreatKind.Drone or ThreatKind.Target)
                        .Select(e => e.Oblast!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (obl.Count == 0) return 0;
        return SweepWhere(t => t.Zone == "UA" && t.Kind is ThreatKind.Drone or ThreatKind.Target
                               && t.Oblast != null && obl.Contains(t.Oblast)).Count;
    }

    // Відбій із поста радара: з областю — гасимо лише цю область; без області — усю країну.
    public int SweepUaClear(IEnumerable<ThreatEvent> events)
    {
        var clears = events.Where(e => e.Kind == ThreatKind.AllClear).ToList();
        if (clears.Count == 0) return 0;
        if (clears.Any(e => e.Oblast == null)) return Sweep("UA").Count;
        var obl = clears.Select(e => e.Oblast!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return SweepWhere(t => t.Zone == "UA" && t.Oblast != null && obl.Contains(t.Oblast)).Count;
    }

    // «Чисто по балістиці», «По балістичних ракетах чисто», «по швидкісним чисто»,
    // «Наразі чисто по швидкісним цілям», «Отбой по баллистике».
    public static readonly Regex BallisticClear = new(
        @"(?:чисто|відбій|отбой)\W{0,3}(?:\w+\s+){0,2}?(?:по\s+)?(?:балісти|баллист|швидкісн|скоростн)|(?:балісти|баллист|швидкісн|скоростн)\w*\W{0,3}(?:\w+\s+){0,2}?(?:чисто|відбій|отбой)",
        RegexOptions.IgnoreCase);

    // «Чисто по балістиці / по швидкісних» — уся балістика (і кола пусків) знімається одразу.
    public int ClearBallistic() => SweepWhere(t => t.Kind == ThreatKind.Ballistic).Count;

    // «Вибухи у Києві» — балістика, що летіла туди (ціль ≤ 40 км), уже долетіла.
    public int LandBallistic((double lat, double lon) at, DateTime now) =>
        SweepWhere(t => t.Kind == ThreatKind.Ballistic && !t.Launch && t.To is { } to && ThreatGeo.Km(to, at) <= 40
                        && now - t.FirstSeen > TimeSpan.FromSeconds(20)).Count;

    // Погасить цели по произвольному условию (например, все цели в области, где офиц. отбой).
    public List<Threat> SweepWhere(Func<Threat, bool> pred)
    {
        lock (_gate)
        {
            var gone = _active.Values.Where(pred).ToList();
            foreach (var t in gone) _active.Remove(t.Id);
            return gone;
        }
    }

    // Погасить цели зоны по типам (например, сбросить старые дроны при выходе новой всеукраинской сводки).
    public List<Threat> SweepKind(string zone, params ThreatKind[] kinds)
    {
        lock (_gate)
        {
            var gone = _active.Values
                .Where(t => t.Zone == zone && (kinds.Length == 0 || kinds.Contains(t.Kind)))
                .ToList();
            foreach (var t in gone) _active.Remove(t.Id);
            return gone;
        }
    }

    // ── классификация стадии по расстоянию/ETA ────────────────────────────────
    static TrackStage Classify(Threat t)
    {
        if (t.Stage is TrackStage.Impact or TrackStage.Passed) return t.Stage; // терминальные держим
        if (t.Recon) return TrackStage.Approaching;                            // разведка НЕ воет и не «над районом»
        if (t.Approx) return TrackStage.Approaching;                           // центр області ≠ реальна позиція
        if (t.Kind == ThreatKind.Ballistic) return TrackStage.Imminent;        // всегда секунды
        double? eta = t.EtaMin;
        double dist = t.DistKm ?? 999;
        if (t.MyArea && dist <= 3) return TrackStage.Overhead;
        if (dist <= 4 || (eta is { } e && e <= 1.5)) return TrackStage.Imminent;
        return TrackStage.Approaching;
    }

    // Текущая ETA с поправкой на время, прошедшее после последнего поста.
    static double? EtaNow(Threat t, DateTime now)
    {
        if (t.EtaMin is not { } e) return null;
        return Math.Max(0, e - (now - t.LastUpdate).TotalMinutes);
    }

    // Интервал между «пиками» в секундах: далеко → редко, близко → часто.
    static double PingEverySec(double? etaMin, double? distKm)
    {
        double eta = etaMin ?? (distKm is { } d ? d / 3.0 : 10); // нет ETA → грубо по 3 км/мин
        if (eta > 8) return 45;
        if (eta > 4) return 20;
        if (eta > 2) return 10;
        return 6;
    }

    static TrackSound SoundFor(TrackStage stage, TrackStage prev, bool isNew) => stage switch
    {
        TrackStage.Impact   => TrackSound.Impact,
        TrackStage.Passed   => TrackSound.Passed,
        TrackStage.Overhead => TrackSound.Overhead,
        TrackStage.Imminent => TrackSound.Imminent,
        TrackStage.Approaching => isNew ? TrackSound.Ping : TrackSound.None,
        _ => TrackSound.None,
    };

    public static string Label(Threat t)
    {
        var name = t.Kind switch
        {
            ThreatKind.Ballistic => "Балістика",
            ThreatKind.Missile   => "Ракета",
            ThreatKind.Kab       => "КАБ",
            ThreatKind.Drone     => "Шахед",
            ThreatKind.Fpv       => "ФПВ",
            ThreatKind.Aviation  => "Авіація",
            _                    => "Ціль",
        };
        if (t.Ammo is { } b0 && b0.Contains("бандерол", StringComparison.OrdinalIgnoreCase)) return "Бандероль";
        // Реактивний шахед — окремий засіб (500 км/год), а не «Шахед (реакт. шахед)».
        if (t.Kind == ThreatKind.Drone && (t.Ammo ?? "").Contains("реакт", StringComparison.OrdinalIgnoreCase)) return "Реактивний шахед";
        return t.Ammo is { Length: > 0 } a ? $"{name} ({a})" : name;
    }

    public static string StageLabel(TrackStage s) => s switch
    {
        TrackStage.Approaching => "наближається",
        TrackStage.Imminent    => "НА ПІДЛЬОТІ",
        TrackStage.Overhead    => "НАД РАЙОНОМ",
        TrackStage.Impact      => "ПРИЛІТ",
        TrackStage.Passed      => "пройшла далі",
        _                      => "знято",
    };

    // Строка для консоли/пуша: «🛩 Шахед · наближається · 12 км · ~4 хв · ПнСх».
    static string Note(Threat t)
    {
        var eta = t.EtaMin is { } e ? (e < 1 ? "<1 хв" : $"~{e:0} хв") : "?";
        var dist = t.DistKm is { } d ? $"{d:0} км" : "?";
        var place = t.LastPlace is { Length: > 0 } ? $" [{t.LastPlace}]" : "";
        var amt = t.Amount > 1 ? $" ×{t.Amount}" : "";
        return $"{ThreatParser.Icon(t.Kind)} {Label(t)}{amt} · {StageLabel(t.Stage)} · {dist} · {eta} · {t.Dir}{place}";
    }

    public static string PublicNote(Threat t) => Note(t);
}
