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
    public List<(string place, double? dist, DateTime t)> Trail = new();
    // ×N = число целей строго из текста поста («2 БпЛА»), не раздувая по числу упомянутых мест
    public int Amount => Math.Max(1, Count);
}

public readonly record struct TrackUpdate(Threat Threat, bool StageChanged, TrackSound Sound, string Note);

public sealed class ThreatTracker
{
    readonly (double lat, double lon) _home;
    readonly Dictionary<string, Threat> _active = new();
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
        if (place.Length == 0
            && _active.Values.Where(a => a.Zone == zone).OrderByDescending(a => a.LastUpdate).FirstOrDefault() is { } recent)
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
            // Направление стрелки: по движению (если есть 2 точки), иначе — вглубь страны.
            if (t.Trail.Count >= 2 && ThreatGeo.Of(t.Trail[^2].place) is { } prevPos)
                t.Heading = ThreatGeo.Bearing(prevPos, p);
            else
                t.Heading = ThreatGeo.Bearing(p, ThreatGeo.UACenter);
        }

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
    public TrackUpdate? IngestUa(ThreatEvent ev, DateTime now)
    {
        if (ev.Kind == ThreatKind.AllClear) return null;
        // Точный город; если нет в справочнике — фолбэк в центр области (по заголовку поста).
        var g = ThreatGeo.Resolve(ev.City);
        bool approx = false;
        if (g is null) { g = ThreatGeo.ResolveOblast(ev.Oblast); approx = true; }
        if (g is not { } gp) return null; // ни города, ни области — не ставим
        var pos = (gp.lat, gp.lon);
        // Приблизительные цели (по области) сливаем в одну метку области с ×N; точные — по городу.
        var place = approx ? gp.name + " (обл.)" : gp.name;
        lock (_gate)
        {
            var key = approx ? $"UA:обл:{gp.name}:{ev.Kind}" : $"UA:{gp.name}:{ev.Kind}";
            bool isNew = !_active.TryGetValue(key, out var t);
            if (isNew) { t = new Threat { Id = key, Zone = "UA", Kind = ev.Kind, FirstSeen = now }; _active[key] = t; }
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
            t.Heading = t.Trail.Count >= 2 && ThreatGeo.Of(t.Trail[^2].place) is { } pp
                ? ThreatGeo.Bearing(pp, pos) : ThreatGeo.Bearing(pos, ThreatGeo.UACenter);
            var prev = t.Stage; t.Stage = Classify(t);
            return new TrackUpdate(t, t.Stage != prev || isNew, TrackSound.None, Note(t));
        }
    }

    // ── тик по таймеру: пересчитать ETA и решить, пора ли пикнуть/сменить стадию ──
    public List<TrackUpdate> Tick(DateTime now)
    {
        var outp = new List<TrackUpdate>();
        lock (_gate)
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
            var areaWide = t.Zone == "UA" || t.LastPlace.EndsWith("(обл.)");
            var goneAfter = areaWide ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(4);
            var age = now - t.LastUpdate;
            if ((t.Stage is TrackStage.Impact or TrackStage.Passed && age > TimeSpan.FromSeconds(90))
                || age > goneAfter)
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
                if (!t.Recon && t.DistKm is { } dd && dd <= 55 && (now - t.LastPing).TotalSeconds >= every)
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
            ThreatKind.Aviation  => "Авіація",
            _                    => "Ціль",
        };
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
