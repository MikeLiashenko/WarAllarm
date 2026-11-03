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
    public int Amount => Math.Max(1, Count - Trim);
    public int Trim; // скільки з ×N — ті самі борти, що вже пораховані на сусідніх мітках (ReconcileCounts)
    public bool Covered; // мітка «центр області», усі борти якої вже стоять точними мітками — знімається

    // «БпЛА на Харків» — ціль ЛЕТИТЬ туди, а не вже над містом. Pos лишається містом (на ньому
    // тримаються тривога, дистанція й ETA — сирена не запізнюється), а Approach — звідки вона
    // летить: мітка малюється там і рухається до міста з реальною швидкістю.
    public (double lat, double lon)? Approach;
    // Approach — не «летить до міста», а плавний переліт мітки з того місця, де її видно, до
    // щойно названого: без телепортів між районами, далі — потоком.
    public bool Glide;

    // «Кружляє над Харковом»: ціль нікуди не летить — застосунок водить її колом над містом.
    public bool Orbit;
    public string? DownBy; // який канал написав останній «мінус» про цю ціль
    /// Кружляє над містом або вже долетіла до міста, куди летіла.
    public bool OrbitingAt(DateTime now) => Orbit || (Approach is { } a && !Glide && Pos is { } p
        && ThreatGeo.SpeedKmh(Kind, Ammo) * 0.85 / 60 * Math.Min(20, (now - LastUpdate).TotalMinutes) >= ThreatGeo.Km(a, p));

    /// Де малювати ціль зараз: на шляху Approach → місто, інакше просто Pos.
    public (double lat, double lon)? DrawPos(DateTime now)
    {
        if (Approach is not { } a || Pos is not { } p) return Pos;
        double dist = ThreatGeo.Km(a, p);
        if (dist < 0.1) return p;
        double mins = Math.Clamp((now - LastUpdate).TotalMinutes, 0, 20);
        double f = Math.Min(1, ThreatGeo.SpeedKmh(Kind, Ammo) * 0.85 / 60 * mins / dist);
        return (a.lat + (p.lat - a.lat) * f, a.lon + (p.lon - a.lon) * f);
    }

    /// Підпис для людей: «на Харків», якщо ще летить туди.
    public string PlaceLabel => Approach is null || Glide ? LastPlace : "на " + LastPlace;
}

public readonly record struct TrackUpdate(Threat Threat, bool StageChanged, TrackSound Sound, string Note);

public sealed class ThreatTracker
{
    readonly (double lat, double lon) _home;
    readonly Dictionary<string, Threat> _active = new();
    readonly Dictionary<string, string> _alias = new(); // ключ нового міста → Id цілі, що туди долетіла
    readonly object _gate = new(); // Ingest (поток updates) и Tick (фоновый Task) — разные потоки

    public ThreatTracker((double lat, double lon) home) => _home = home;

    /// Для навчання потоків (Flows.Learn): ціль переїхала a → b за [хв], або канал назвав курс a → b.
    public Action<Threat, (double lat, double lon), (double lat, double lon), double, bool>? FlowSink;

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
        bool passed = Regex.IsMatch(low, @"пролет|пройш|вийш|мину[вл]|подал[іи]|далі на|дальше на|далее на|транзит");

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
        // Цю ціль уже веде всеукраїнський трекер: той самий дрон не малюємо вдруге.
        if (isNew && pos is { } mp0 && e.Kind is ThreatKind.Drone or ThreatKind.Target
            && _active.Values.Where(a => a.Zone == "UA" && a.Kind is ThreatKind.Drone or ThreatKind.Target && !a.Down && !a.Launch
                                         && a.Pos is { } ap0 && ThreatGeo.Km(ap0, mp0) <= NearSameKm && now - a.LastUpdate <= TimeSpan.FromMinutes(5))
                   .OrderBy(a => ThreatGeo.Km(a.Pos!.Value, mp0)).FirstOrDefault() is { } ua)
        {
            ua.MyArea |= e.Tier == KhTier.MyArea;
            return new TrackUpdate(ua, false, TrackSound.None, Note(ua));
        }
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
        UpdateOrbit(t, e.Raw, pos is { } np && t.Pos is { } op && ThreatGeo.Km(np, op) <= 3, now, false);
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
        // Область названо в пості — місто лише в ній (інакше далі піде центр області).
        var g = at
                ?? (ev.Oblast is { } eo0 && ev.City is { } cc0 ? PlaceFinder.FindIn(cc0, eo0) ?? PlaceFinder.Unique(cc0)
                    : ThreatGeo.Resolve(ev.City) ?? (ev.City is { } cc ? PlaceFinder.Find(cc, Array.Empty<string>()) : null));
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
            var placeKey = key;
            if (_alias.TryGetValue(key, out var aliased) && _active.ContainsKey(aliased)) key = aliased;
            bool isNew = !_active.TryGetValue(key, out var t);
            // Ціль з цього місця вже полетіла далі. Пост услід від іншого каналу (до 90 с) її не
            // повертає назад; пізніший чи «нова шаболда» — це вже інша ціль, а не та сама мітка.
            if (!isNew && !approx && t!.LastPlace is { Length: > 0 } was && was != place)
            {
                bool fresh = Slang.NewTarget(ev.Raw);
                if (!fresh && now - t.LastUpdate < TimeSpan.FromMinutes(4))
                {
                    // Група ракет уже далі, але «до 10 бандеролей на Одесу» — це про неї ж: ×N росте.
                    if (t.Kind == ThreatKind.Missile && ev.Count > t.Count) t.Count = ev.Count;
                    return new TrackUpdate(t, false, TrackSound.None, Note(t));
                }
                key = $"{placeKey}#{now:HHmmss}";
                _alias[placeKey] = key;
                isNew = true;
            }
            // Нове місто — це часто ТА САМА ціль, що пролетіла далі. Раніше кожне місто
            // ставало новою міткою, а стара висіла «примарою» 15 хв без оновлень (і курсу
            // не було — лише здогадка «вглиб країни»). Тепер шукаємо попередника.
            bool movedGroup = false;
            if (isNew && !approx && Predecessor(ev, pos, now) is { } pred)
            {
                _active.Remove(key);
                t = pred;
                _alias[placeKey] = pred.Id;
                isNew = false;
                movedGroup = ev.Kind == ThreatKind.Missile;
                if (pred.Kind == ThreatKind.Target && ev.Kind == ThreatKind.Drone) pred.Kind = ThreatKind.Drone;
            }
            // «8 реактивних на Київщину» — а всі вони вже стоять точними мітками в області: ще одна
            // мітка «Київщина (обл.)» лише двоїть їх (2.11: один шахед над Києвом — 2–3 мітки).
            if (isNew && approx && ev.Kind is ThreatKind.Drone or ThreatKind.Target && ExactDronesIn(pos, now) >= Math.Max(1, ev.Count))
            {
                _alias.Remove(placeKey);
                return null;
            }
            if (isNew) { t = new Threat { Id = key, Zone = "UA", Kind = ev.Kind, FirstSeen = now }; _active[key] = t; }
            // Місцевий монітор уже намалював цей дрон: лишається одна мітка.
            if (!approx && ev.Kind is ThreatKind.Drone or ThreatKind.Target)
                foreach (var z in _active.Where(a => a.Value.Zone != "UA" && a.Value.Kind is ThreatKind.Drone or ThreatKind.Target
                                                     && a.Value.Pos is { } zp && ThreatGeo.Km(zp, pos) <= NearSameKm).ToList())
                {
                    t!.MyArea |= z.Value.MyArea;
                    _active.Remove(z.Key);
                }
            // Балістика з названою ціллю — коло «куди могла долетіти» з місця пуску вже зайве.
            if (isNew && ev.Kind == ThreatKind.Ballistic && !approx)
                foreach (var k in _active.Where(a => a.Value.Launch && a.Value.Kind == ThreatKind.Ballistic).Select(a => a.Key).ToList())
                    _active.Remove(k);
            // Нова справжня ціль-дрон — це, ймовірно, частина групи пуску, що летіла сюди: забираємо з групи.
            if (isNew && !approx && ev.Kind is ThreatKind.Drone or ThreatKind.Target)
                LaunchGroups.Absorb(pos, Math.Max(1, ev.Count), now);
            var prevPos = t!.Pos;
            var drawnBefore = isNew ? null : t.DrawPos(now); // до оновлення LastUpdate: де мітка саме зараз
            var prevSeen = t.LastUpdate;
            // Пост про місце ПОЗАДУ цілі, проти потоку («Вишеньки» вже після «Мишоловки») — канал
            // запізнився, а не ціль розвернулась: мітка не відскакує назад і не стає другою ціллю.
            // Розворот, названий у тексті, чи названий новий курс — справжні.
            if (!isNew && !approx && t.Kind is ThreatKind.Drone or ThreatKind.Target && !t.Orbit
                && (drawnBefore ?? prevPos) is { } here && ThreatGeo.Km(here, pos) is >= 3 and <= 35
                && now - t.LastUpdate < TimeSpan.FromMinutes(6) && ev.Toward is null && !TurnBack.IsMatch(ev.Raw)
                && Flows.Against(here, pos, IsJet(t.Ammo) || IsJet(ev.Extra)))
            {
                if (t.Approach is null) t.LastUpdate = now;
                t.Hits.Add(ev.City ?? gp.name);
                return new TrackUpdate(t, false, TrackSound.None, Note(t));
            }
            // Прямо сказано «реактивний» — швидкість уточнюємо навіть для вже відомої цілі.
            t!.Ammo = IsJet(ev.Extra) ? ev.Extra : t.Ammo ?? ev.Extra;
            t.Oblast = ev.Oblast ?? t.Oblast;
            t.Recon = ((ev.Extra ?? "") + " " + ev.Raw).ToLowerInvariant().Contains("розвід");
            // Новий пост про ціль, яку вважали збитою, — отже, вона ще летить.
            var altNow = ev.Alt ?? Altitude.Parse(ev.Raw);
            if (t.Down && altNow is not { Down: true })
            {
                t.Down = false; t.AltNote = null;
                if (t.Stage == TrackStage.Passed) t.Stage = TrackStage.Approaching;
            }
            UpdateOrbit(t, ev.Raw, !isNew && prevPos is { } sp && ThreatGeo.Km(sp, pos) <= 3, now, approx);
            t.LastUpdate = now;
            t.Hits.Add(ev.City ?? gp.name);
            // Група ракет, що переїхала: «на Чорноморськ» без числа не зменшує «7х» групи.
            t.Count = movedGroup ? Math.Max(t.Count, Math.Max(1, ev.Count)) : ev.Count > 0 ? ev.Count : 1;
            t.PrevDistKm = t.DistKm;
            t.Pos = pos; t.LastPlace = place;
            t.DistKm = ThreatGeo.Km(pos, _home);
            t.Dir = ThreatGeo.Compass(ThreatGeo.Bearing(_home, pos));
            t.EtaMin = ThreatGeo.EtaMin(pos, _home, t.Kind, t.Ammo);
            if (t.Trail.Count == 0 || t.Trail[^1].place != gp.name) t.Trail.Add((gp.name, t.DistKm, now));
            // «на Харків» — ще летить: продовжуємо з того місця, де мітка зараз (повторний пост не
            // відкидає її назад); нова ціль — за ~10 хв польоту від міста з боку пуску / фронту.
            t.Approach = null; t.Glide = false;
            if (!approx && ev.Kind is ThreatKind.Drone or ThreatKind.Missile or ThreatKind.Target
                && ThreatParser.HeadingTo(ev.Raw, ev.City ?? gp.name))
            {
                var start = NotFarther(drawnBefore ?? ApproachStart(ev, t, pos), pos, MaxApproachKm(t.Kind));
                if (ThreatGeo.Km(start, pos) >= 3) t.Approach = start;
            }
            // Дрон уже на карті, а канал назвав нове місце: мітка не телепортується (у Києві канали
            // називають райони вперемішку — мітка стрибала туди-сюди на 5–10 км), а летить туди з
            // реальною швидкістю від того місця, де її видно.
            else if (!approx && !isNew && ev.Kind is ThreatKind.Drone or ThreatKind.Target && !t.Orbit
                     && drawnBefore is { } db && ThreatGeo.Km(db, pos) is >= 3 and <= 40)
            { t.Approach = db; t.Glide = true; }
            AddTrack(t, t.Approach ?? pos);
            if (prevPos is { } pp && ThreatGeo.Km(pp, pos) > 2)
            {
                t.Heading = ThreatGeo.Bearing(pp, pos); // реальний рух між двома точками
                t.HeadingKnown = true;
                if (!approx) FlowSink?.Invoke(t, pp, pos, (now - prevSeen).TotalMinutes, false);
            }
            // Названо, звідки летить: напрямок підльоту до міста (для навчання потоків).
            if (FlowSink is not null && !approx && OriginOf(ev, ev.City ?? gp.name, pos) is { } origin && ThreatGeo.Km(origin, pos) is >= 15 and <= 500)
                FlowSink(t, origin, pos, -1, true);
            else if (!t.HeadingKnown)
                t.Heading = ThreatGeo.Bearing(pos, ThreatGeo.UACenter); // лише для стрілки, не для руху
            ApplyCourse(t, ev, pos, isNew, now);
            if (t.Orbit) { t.Approach = null; t.CourseTo = null; }
            if (t.Approach is { } ap)
            {
                t.Heading = ThreatGeo.Bearing(ap, pos);
                t.HeadingKnown = true;
            }
            // Курс, названий у пості («курсом на Полтаву»), — справжній напрямок.
            if (ev.Toward is { } tw && (ThreatGeo.Resolve(tw) ?? PlaceFinder.Find(tw, Array.Empty<string>())) is { } dest
                && ThreatGeo.Km(pos, (dest.lat, dest.lon)) > 5)
            {
                t.Heading = ThreatGeo.Bearing(pos, (dest.lat, dest.lon));
                t.HeadingKnown = true;
                t.CourseTo = (dest.name, dest.lat, dest.lon);
                t.Orbit = false;
                if (!approx) FlowSink?.Invoke(t, pos, (dest.lat, dest.lon), 0, true);
            }
            ApplyAltitude(t, ev.Alt ?? Altitude.Parse(ev.Raw), now);
            var prev = t.Stage; t.Stage = Classify(t);
            return new TrackUpdate(t, t.Stage != prev || isNew, TrackSound.None, Note(t));
        }
    }

    // Кружляє: так написано («кружляє», «над містом»), або ціль уже в цьому місці й про неї знову
    // пишуть за ≥2 хв — отже, нікуди не полетіла. Нове місце без цих слів — знову в русі.
    static void UpdateOrbit(Threat t, string raw, bool samePlace, DateTime now, bool approx)
    {
        if (t.Kind is not (ThreatKind.Drone or ThreatKind.Target or ThreatKind.Fpv) || approx) { t.Orbit = false; return; }
        bool heading = Regex.IsMatch(raw, @"курс|далі|на\s+\p{Lu}|→", RegexOptions.None);
        t.Orbit = Slang.Circling(raw)
                  || (Slang.OverPlace(raw) && !heading)
                  || (samePlace && !heading && (t.Orbit || now - t.LastUpdate >= TimeSpan.FromMinutes(2)));
    }

    /// «Мінус» від каналу: нижчаємо ×N найближчої цілі (того ж типу, якщо є), або знімаємо її.
    public Threat? MarkShot((double lat, double lon) at, ThreatKind kind, int count, DateTime now, string? channel = null, bool lost = false)
    {
        lock (_gate)
        {
            // Ціль «на Ізюм» може бути ще далеко від міста: міряємо і до мітки, і до міста.
            static double Off(Threat t, (double lat, double lon) at, DateTime now) =>
                Math.Min(ThreatGeo.Km(t.DrawPos(now)!.Value, at), ThreatGeo.Km(t.Pos!.Value, at));
            // Той самий «мінус» пишуть кілька каналів: не знімаємо через нього ще й сусідню ціль.
            // Другий «мінус» того самого каналу — це вже друга збита ціль.
            var just = _active.Values.FirstOrDefault(t => t.DownAt != default && now - t.DownAt < TimeSpan.FromSeconds(90)
                                                          && t.DownBy != channel && t.Pos is not null && t.DrawPos(now) is not null && Off(t, at, now) <= 15);
            if (just is not null) return just;
            var near = _active.Values
                .Where(t => t.Kind is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Missile or ThreatKind.Fpv
                            && !t.Launch && !t.Down && t.Pos is not null && t.DrawPos(now) is not null && Off(t, at, now) <= 60)
                .OrderBy(t => t.Kind == kind || kind == ThreatKind.Target ? 0 : 1)
                .ThenBy(t => Off(t, at, now))
                .FirstOrDefault();
            if (near is null) return null;
            near.DownAt = now; near.DownBy = channel;
            if (near.Amount > count) { near.Count = near.Amount - count; near.Trim = 0; return near; }
            near.Down = true; near.AltNote = lost ? "зникла з радарів" : "збито"; near.AltAt = now;
            near.Orbit = false;
            near.Stage = TrackStage.Passed;
            near.LastUpdate = now; // ще 90 с видно, що збита, і знімаємо
            return near;
        }
    }

    /// «Кружляє» без місця: про найближчу до останнього місця каналу ціль.
    public Threat? MarkCircling((double lat, double lon) at, ThreatKind kind, DateTime now)
    {
        lock (_gate)
        {
            var near = _active.Values
                .Where(t => t.Kind is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Fpv
                            && !t.Launch && !t.Down && !t.Approx && t.Pos is { } p && ThreatGeo.Km(p, at) <= 40)
                .OrderBy(t => t.Kind == kind ? 0 : 1).ThenBy(t => ThreatGeo.Km(t.Pos!.Value, at)).FirstOrDefault();
            if (near is null) return null;
            near.Orbit = true; near.Approach = null; near.CourseTo = null; near.LastUpdate = now;
            return near;
        }
    }

    /// «Мінус» на міському каналі: ціль цієї зони, про яку писали останньою (місця в пості нема).
    public Threat? MarkShotZone(string zone, int count, DateTime now)
    {
        Threat? latest;
        lock (_gate)
            latest = _active.Values.Where(t => t.Zone == zone && !t.Down && !t.Launch && t.Pos is not null
                                               && t.Kind is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Missile or ThreatKind.Fpv)
                .OrderByDescending(t => t.LastUpdate).FirstOrDefault();
        return latest is null ? null : MarkShot(latest.Pos!.Value, latest.Kind, count, now);
    }

    // Звідки летить нова ціль «на X»: з названого місця пуску, інакше з боку найближчого фронту /
    // кордону. Канали називають найближче місце на шляху, тому мітка не далі ~30 км від нього —
    // і для реактивного теж: з 60 км він «летів» би до міста після того, як уже пролетів далі.
    static (double lat, double lon) ApproachStart(ThreatEvent ev, Threat t, (double lat, double lon) dest)
    {
        // Дрон приходить туди, звідки йде потік (не завжди з боку фронту: на Київ — з Чернігівщини,
        // на Білу Церкву — з-за Києва), і «на X» пишуть, коли він уже близько.
        bool jet = IsJet(t.Ammo) || IsJet(ev.Extra);
        if (LaunchSites.Resolve(ev.From) is null && t.Kind is ThreatKind.Drone or ThreatKind.Target && Flows.Dir(dest, jet) is { } d)
        {
            double r = (d + 180) * Math.PI / 180, km = jet ? 15 : 20;
            return (dest.lat + km * Math.Cos(r) / 111, dest.lon + km * Math.Sin(r) / (111 * Math.Cos(dest.lat * Math.PI / 180)));
        }
        var from = LaunchSites.Resolve(ev.From) is { } f ? (f.lat, f.lon) : LaunchSites.NearestFront(dest);
        return NotFarther(from, dest, MaxApproachKm(t.Kind));
    }

    static double MaxApproachKm(ThreatKind kind) => kind == ThreatKind.Missile ? 60 : 30;

    // Точка [from], але не далі [km] від [dest].
    static (double lat, double lon) NotFarther((double lat, double lon) from, (double lat, double lon) dest, double km)
    {
        double dist = ThreatGeo.Km(from, dest);
        if (dist <= km) return from;
        double k = km / dist;
        return (dest.lat + (from.lat - dest.lat) * k, dest.lon + (from.lon - dest.lon) * k);
    }

    // Два місця ближче цього — імовірно той самий дрон, про який різні канали пишуть по-різному.
    const double NearSameKm = 25;
    static readonly (double lat, double lon)[] BigCities = { (50.45, 30.52), (49.99, 36.23), (48.46, 35.04), (46.48, 30.73) };

    // Рядок згадує місце (за початком слова — назва відмінюється): «в бік Броварів» → Бровари.
    static bool Mentions(string raw, string place)
    {
        var stem = place.Split(' ')[0];
        stem = stem[..Math.Min(stem.Length, Math.Max(4, stem.Length - 2))];
        return stem.Length >= 3 && Regex.IsMatch(raw, @"(?<!\p{L})" + Regex.Escape(stem), RegexOptions.IgnoreCase);
    }

    static bool IsJet(string? ammo) => ammo?.Contains("реакт", StringComparison.OrdinalIgnoreCase) == true;

    // «Розвертається на Обухів», «повернув назад» — справжній розворот, а не запізнілий пост.
    // «5 на Кагарлик з Черкащини, 3 на Згурівку з Чернігівщини», «повз Козелець на Київщину»:
    // звідки летить саме ЦЕЙ дрон — лише з тієї частини рядка, де назване його місто.
    static readonly Regex OriginRe = new(@"(?:^|[\s(])(?:з|із|зі|від|повз|через)\s+((?-i:[А-ЯІЇЄҐ])[\w'ʼ’-]+(?:\s+(?-i:[А-ЯІЇЄҐ])[\w'ʼ’-]+)?)", RegexOptions.IgnoreCase);
    static (double lat, double lon)? OriginOf(ThreatEvent ev, string city, (double lat, double lon) pos)
    {
        var stem = city[..Math.Min(4, city.Length)];
        foreach (var clause in Regex.Split(ev.Raw, @"[,;\n]|\s[-–—]\s"))
        {
            if (clause.IndexOf(stem, StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (Match m in OriginRe.Matches(clause))
            {
                var name = m.Groups[1].Value;
                if (name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
                if ((LaunchSites.Resolve(name) ?? ThreatGeo.Resolve(name)) is not { } o) continue;
                // «з Одещини» про Ізмаїл — та сама область: це центр області, а не напрямок.
                if (Regex.IsMatch(name, @"щини|ччини|чини$", RegexOptions.IgnoreCase) && OblastGeo.Region(o.lat, o.lon) == OblastGeo.Region(pos.lat, pos.lon)) return null;
                return (o.lat, o.lon);
            }
            return null;
        }
        return null;
    }

    static readonly Regex TurnBack = new(@"розверт|розворот|разворот|разверн|поверну|поверта|поворачива|назад|поверн\w*\s+на", RegexOptions.IgnoreCase);

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
    Threat? Predecessor(ThreatEvent ev, (double lat, double lon) pos, DateTime now)
    {
        var kind = ev.Kind;
        if (kind is not (ThreatKind.Drone or ThreatKind.Missile or ThreatKind.Target)) return null;
        // «2 новые шаболды», «+2 шахеди» — канал прямо каже, що це інші цілі.
        if (Slang.NewTarget(ev.Raw)) return null;
        if (kind == ThreatKind.Missile) return MissileGroup(ev, pos, now);
        bool jet = IsJet(ev.Extra), plain = Slang.PlainDrone(ev.Raw);
        bool toBigCity = BigCities.Any(b => ThreatGeo.Km(b, pos) <= 5) && ThreatParser.HeadingTo(ev.Raw, ev.City);
        Threat? best = null;
        double bestKm = double.MaxValue;
        foreach (var c in _active.Values)
        {
            // «Ціль» — той самий дрон, тип якого канал не назвав («Бортничі» за хвилину після «шахед на ДВРЗ»).
            bool sameKind = c.Kind == kind || (c.Kind is ThreatKind.Drone or ThreatKind.Target && kind is ThreatKind.Drone or ThreatKind.Target);
            if (!sameKind || c.Zone != "UA" || c.Launch || c.Approx || c.Down || c.Pos is not { } cp) continue;
            if (c.Stage is TrackStage.Impact or TrackStage.Passed) continue;
            var age = now - c.LastUpdate;
            if (age > TimeSpan.FromMinutes(12)) continue;
            // Реактивний не стає звичайним шахедом, а одна ціль — трьома. Мітка без названого типу
            // («Бортничі») — може бути й реактивним.
            if (jet && c.Ammo is not null && !IsJet(c.Ammo)) continue;
            if (plain && IsJet(c.Ammo)) continue;
            if (ev.Count > Math.Max(1, c.Count)) continue;
            double km = ThreatGeo.Km(cp, pos);
            if (km < 3) continue; // те саме місце — це не рух
            double fromMark = ThreatGeo.Km(c.DrawPos(now) ?? cp, pos);
            // Той самий дрон, хоч канали назвали його по-різному й майже одночасно:
            // «Вишгород» і «Трою/Вишгород»; «Троя» і «Бровари» поруч; «на Київ» для цілі з Українки.
            bool samePost = age == TimeSpan.Zero;
            bool sameDrone = (!samePost && km <= 40 && Mentions(ev.Raw, c.LastPlace))
                             || (!samePost && km <= 15 && ev.Count <= 1)
                             || (toBigCity && Math.Min(km, fromMark) <= 50);
            if (!sameDrone)
            {
                if (age < TimeSpan.FromSeconds(40)) continue;
                // Відстань рахуємо від мітки: ціль «на X» може бути ще далеко від самого X.
                double reach = ThreatGeo.SpeedKmh(c.Kind, c.Ammo) / 60 * age.TotalMinutes * 1.5 + 35;
                if (fromMark > reach) continue;
            }
            if (!sameDrone && c.HeadingKnown && c.Heading is { } h)
            {
                double turn = Math.Abs(((ThreatGeo.Bearing(cp, pos) - h) % 360 + 540) % 360 - 180);
                if (turn > 110) continue;
            }
            // Найближча до мітки й найсвіжіша: стара мітка попереднього дрона, що стоїть поруч, не
            // має забирати новий пост, поки свіжа мітка цього дрона лишається «примарою»
            // (2.11 17:30: Вишеньки → Мишоловка дало дві мітки).
            double score = Math.Min(km, fromMark) + 5 * age.TotalMinutes;
            // Шлях від мітки сюди проти потоку — швидше інша ціль (чи запізнілий пост), ніж її рух.
            if (Flows.Against(c.DrawPos(now) ?? cp, pos, jet || IsJet(c.Ammo))) score += 25;
            if (score < bestKm) { bestKm = score; best = c; }
        }
        return best;
    }

    // Ракети йдуть групою, і канали називають її по-різному майже одночасно: «5 на Одесу»,
    // «7 на Чорноморськ», «на Затоку/Чорноморськ» (2.11 — десять бандеролей стали ~10 мітками
    // й ~50 «бортами»). Той самий засіб у межах 70 км за 8 хв — та сама група: мітка
    // переїжджає, ×N — найбільше назване число, а не сума.
    Threat? MissileGroup(ThreatEvent ev, (double lat, double lon) pos, DateTime now)
    {
        Threat? best = null;
        double bestKm = double.MaxValue;
        foreach (var c in _active.Values)
        {
            if (c.Kind != ThreatKind.Missile || c.Zone != "UA" || c.Launch || c.Approx || c.Down || c.Pos is not { } cp) continue;
            if (now - c.LastUpdate > TimeSpan.FromMinutes(8) || !SameAmmo(ev.Extra, c.Ammo)) continue;
            double km = Math.Min(ThreatGeo.Km(cp, pos), ThreatGeo.Km(c.DrawPos(now) ?? cp, pos));
            if (km < 3 || km > 70) continue;
            if (km < bestKm) { bestKm = km; best = c; }
        }
        return best;
    }

    // Реактивний летить 8 км щохвилини, а київські канали пишуть лише назви районів («Совки»,
    // «Чоколівка»): мітка, про яку 2 хв мовчать, а поруч (≤25 км) є свіжіша мітка такого ж
    // дрона, — це слід, що лишився позаду, а не ще одна ціль (2.11: 3 реактивні — 7 міток).
    bool Superseded(Threat t, DateTime now)
    {
        if (t.Zone != "UA" || t.Approx || t.Launch || t.Down || t.Orbit || t.Count > 1 || t.Pos is not { } tp) return false;
        if (t.Kind is not (ThreatKind.Drone or ThreatKind.Target) || !(IsJet(t.Ammo) || t.Kind == ThreatKind.Target)) return false;
        if (now - t.LastUpdate < TimeSpan.FromMinutes(2)) return false;
        return _active.Values.Any(o => o != t && o.Zone == "UA" && !o.Approx && !o.Launch && !o.Down
                                       && o.Kind is ThreatKind.Drone or ThreatKind.Target && (IsJet(o.Ammo) || o.Kind == ThreatKind.Target)
                                       && o.LastUpdate > t.LastUpdate && now - o.LastUpdate < TimeSpan.FromMinutes(2)
                                       && o.Pos is { } op && ThreatGeo.Km(op, tp) <= 25);
    }

    // Скільки бортів уже стоїть точними мітками (не «центр області») в області точки.
    int ExactDronesIn((double lat, double lon) pos, DateTime now)
    {
        var obl = OblastGeo.Region(pos.lat, pos.lon);
        return _active.Values.Where(t => t.Kind is ThreatKind.Drone or ThreatKind.Target && t.Zone == "UA" && !t.Approx && !t.Launch && !t.Down
                                         && t.Pos is { } p && now - t.LastUpdate < TimeSpan.FromMinutes(10) && OblastGeo.Region(p.lat, p.lon) == obl)
                             .Sum(t => Math.Max(1, t.Count));
    }

    static bool SameAmmo(string? a, string? b)
    {
        static string Norm(string? s) => Regex.Replace((s ?? "").ToLowerInvariant().Replace('x', 'х'), @"[^\p{L}\d]", "") is var n && n.StartsWith("бандерол") ? "бандерол" : n;
        return a is null || b is null || Norm(a) == Norm(b);
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
            // Ціль цієї балістики вже названо («на Харків») — повторні «загроза балістики з
            // Бєлгорода» не повертають велике коло досяжності поверх справжнього польоту.
            if (ev.Kind == ThreatKind.Ballistic && _active.Values.Any(a => a.Kind == ThreatKind.Ballistic && !a.Launch
                    && a.From is { } af && ThreatGeo.Km(af, pos) < 80 && now - a.LastUpdate < TimeSpan.FromMinutes(5)))
                return null;
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

    // Сума ×N по області не більша за те, що про неї кажуть зведення. Мітки лишаються всі, менше
    // стає лише ×N: «17 шахедів на Полтавщині» — це ті самі борти, що вже стоять над її містами,
    // а «8 на Лубни» в одного каналу й «5 на Пирятин, 5 на Оржицю» в іншого — та сама група.
    void ReconcileCounts(IReadOnlyDictionary<string, int>? listCaps)
    {
        foreach (var t in _active.Values) { t.Trim = 0; t.Covered = false; }
        // «Бандеролі на Одещину» — та сама група, що вже стоїть точною міткою в області.
        foreach (var o in _active.Values.Where(t => t.Kind == ThreatKind.Missile && t.Zone == "UA" && t.Approx && t.Pos is not null))
            o.Covered = _active.Values.Any(t => t.Kind == ThreatKind.Missile && t.Zone == "UA" && !t.Approx && !t.Launch && t.Pos is { } p
                                                && OblastGeo.Region(p.lat, p.lon) == OblastGeo.Region(o.Pos!.Value.lat, o.Pos!.Value.lon));
        var drones = _active.Values.Where(t => t.Kind is ThreatKind.Drone or ThreatKind.Target && !t.Launch && !t.Down && t.Pos is not null);
        foreach (var g in drones.GroupBy(t => Geo.Canonical(OblastGeo.Region(t.Pos!.Value.lat, t.Pos!.Value.lon)) ?? t.Oblast ?? ""))
        {
            int exact = g.Where(t => !t.LastPlace.EndsWith("(обл.)")).Sum(t => t.Amount);
            foreach (var o in g.Where(t => t.LastPlace.EndsWith("(обл.)")))
            {
                o.Trim = Math.Min(o.Amount - 1, exact);
                // Усі борти області вже на точних мітках — мітка «центр області» лише двоїть їх.
                o.Covered = exact >= Math.Max(1, o.Count);
            }
            if (listCaps is null || !listCaps.TryGetValue(g.Key, out var cap)) continue;
            for (int over = g.Sum(t => t.Amount) - cap; over > 0; over--)
            {
                var most = g.Where(t => t.Amount > 1).MaxBy(t => t.Amount);
                if (most is null) break;
                most.Trim++;
            }
        }
    }

    // ── тик по таймеру: пересчитать ETA и решить, пора ли пикнуть/сменить стадию ──
    public List<TrackUpdate> Tick(DateTime now, IReadOnlyDictionary<string, int>? listCaps = null, DateTime lastList = default)
    {
        var outp = new List<TrackUpdate>();
        lock (_gate)
        {
        foreach (var k in _alias.Where(a => !_active.ContainsKey(a.Value)).Select(a => a.Key).ToList())
            _alias.Remove(k);
        ReconcileCounts(listCaps);
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
                          : t.Orbit ? TimeSpan.FromMinutes(15)                  // кружляє годинами, а пишуть не щохвилини
                          : t.LastPlace.EndsWith("(обл.)") ? TimeSpan.FromMinutes(15)
                          : t.Zone == "UA" ? TimeSpan.FromMinutes(10)
                          : TimeSpan.FromMinutes(4);
            // Зведення виходять раз на 5–10 хв, але бувають паузи на 15+ (1.11: 17:13→17:29), і карта
            // пустіла посеред нальоту. Поки нового зведення немає, повільну ціль із нальоту тримаємо до
            // 20 хв; вийшло зведення, а її там нема — діє звичайний строк, тож сума не роздувається.
            if (t.Zone == "UA" && t.Kind is ThreatKind.Drone or ThreatKind.Target && ThreatGeo.SpeedKmh(t.Kind, t.Ammo) <= 250
                && now - lastList < TimeSpan.FromMinutes(20) && lastList <= t.LastUpdate.AddMinutes(1))
                goneAfter = TimeSpan.FromMinutes(20);
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
                || age > goneAfter || landed || launchOver || t.Covered || Superseded(t, now))
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
        @"(?:чисто|відбій|отбой)\W{0,3}(?:\w+\s+){0,2}?(?:по\s+)?(?:балісти|баллист|швидкісн|скоростн)|(?:балісти|баллист|швидкісн|скоростн)\w*\W{0,3}(?:\w+\s+){0,2}?(?:чисто|відбій|отбой)"
        + @"|без\s+(?:фіксації\s+)?(?:пусків|швидкісн|балісти)|виходів\s+не\s+було|(?:балісти|швидкісн)\w*\W{0,3}(?:\w+\s+){0,1}?скасован|скасован\w*\W{0,3}(?:\w+\s+){0,2}?балісти",
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

    /// Невідома «ціль» (Target / Unknown) на карті — це БпЛА: звичайні шахеди налітають роєм,
    /// реактивні — поодинці, до 3. Тож рахуємо БпЛА навколо (≤60 км, разом із самою ціллю):
    /// до 3 — реактивний, більше — звичайний шахед. Для конкретного типу — null.
    public static bool? JetGuess(Threat t, IEnumerable<Threat> all)
    {
        if (t.Kind is not (ThreatKind.Target or ThreatKind.Unknown) || t.Pos is not { } p) return null;
        if (t.Amount > 3) return false;
        int around = all.Where(o => o.Pos is { } q && !o.Launch && !o.Down
                && o.Kind is ThreatKind.Drone or ThreatKind.Target or ThreatKind.Unknown
                && ThreatGeo.Km(p, q) <= 60)
            .Sum(o => o.Amount);
        return Math.Max(around, t.Amount) <= 3;
    }

    /// Підпис невідомої цілі для карти: «Реактивний шахед (ймовірно)» / «Шахед (ймовірно)».
    public static string GuessLabel(bool jet) => jet ? "Реактивний шахед (ймовірно)" : "Шахед (ймовірно)";

    public static string StageLabel(TrackStage s) => s switch
    {
        TrackStage.Approaching => "наближається",
        TrackStage.Imminent    => "НА ПІДЛЬОТІ",
        TrackStage.Overhead    => "НАД РАЙОНОМ",
        TrackStage.Impact      => "ПРИЛІТ",
        TrackStage.Passed      => "пройшла далі",
        _                      => "знято",
    };

    /// Стадія для людей: ціль, що кружляє, не «наближається».
    public static string StageLabel(Threat t) =>
        t.Down ? "збито" : t.Orbit && t.Stage == TrackStage.Approaching ? "кружляє" : StageLabel(t.Stage);

    // Строка для консоли/пуша: «🛩 Шахед · наближається · 12 км · ~4 хв · ПнСх».
    static string Note(Threat t)
    {
        var eta = t.EtaMin is { } e ? (e < 1 ? "<1 хв" : $"~{e:0} хв") : "?";
        var dist = t.DistKm is { } d ? $"{d:0} км" : "?";
        var place = t.LastPlace is { Length: > 0 } ? $" [{t.LastPlace}]" : "";
        var amt = t.Amount > 1 ? $" ×{t.Amount}" : "";
        return $"{ThreatParser.Icon(t.Kind)} {Label(t)}{amt} · {StageLabel(t)} · {dist} · {eta} · {t.Dir}{place}";
    }

    public static string PublicNote(Threat t) => Note(t);
}
