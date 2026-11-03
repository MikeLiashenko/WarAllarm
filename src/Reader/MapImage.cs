using System.Text.Json;
using SkiaSharp;

// ── WarAlarm · рендер карти загроз у PNG ─────────────────────────────────────
// Малює контур областей України (ті самі полігони, що й у застосунку), підсвічує
// області з цілями червоним, ставить цілі та дім — і віддає PNG для Telegram-бота
// (/map). Проекція — рівнопроміжна з поправкою на широту (як у застосунку).
public static class MapImage
{
    // Ті самі межі, що у Flutter-карті.
    const double Lon0 = 22.14, LonSpan = 18.09, Lat1 = 52.38, LatSpan = 7.99;

    // Великі міста з підписами (як у застосунку): назва, lon, lat.
    static readonly (string name, double lon, double lat)[] Cities =
    {
        ("Київ", 30.52, 50.45), ("Харків", 36.23, 49.99), ("Одеса", 30.73, 46.48),
        ("Дніпро", 35.05, 48.47), ("Львів", 24.03, 49.84), ("Запоріжжя", 35.14, 47.84),
        ("Миколаїв", 31.99, 46.98), ("Полтава", 34.55, 49.59), ("Суми", 34.80, 50.91),
        ("Чернігів", 31.29, 51.50), ("Вінниця", 28.47, 49.23), ("Херсон", 32.61, 46.64),
        ("Луцьк", 25.34, 50.75), ("Житомир", 28.66, 50.25), ("Черкаси", 32.06, 49.44),
        ("Ів.-Франківськ", 24.71, 48.92), ("Тернопіль", 25.59, 49.55), ("Рівне", 26.25, 50.62),
        ("Ужгород", 22.29, 48.62), ("Чернівці", 25.94, 48.29), ("Кропивницький", 32.26, 48.51),
        ("Кривий Ріг", 33.39, 47.91), ("Маріуполь", 37.54, 47.10), ("Донецьк", 37.80, 48.00),
        ("Луганськ", 39.30, 48.57), ("Сімферополь", 34.10, 44.95),
    };

    /// [title]/[subtitle] задає викликач (канал → нейтральний заголовок; особистий
    /// /map → місто + ймовірність). [showHome] малює маркер дому (і кола 50/100 км) лише
    /// в особистому. [tone] — колір підзаголовка за ризиком: "calm" / "warn" / "alarm",
    /// "" — нейтральний (канал: червоний через вічно-червоний Крим лише лякав).
    public static byte[] Render(
        (double lat, double lon) home,
        IReadOnlyList<Threat> threats,
        string title,
        string subtitle,
        Dictionary<string, string>? oblastLevels = null,
        bool showHome = true,
        Dictionary<string, string>? raionLevels = null,
        Dictionary<string, string>? oblastWhole = null,
        string tone = "")
    {
        const int W = 1000, padTop = 80, pad = 16;
        double midLat = (Lat1 + (Lat1 - LatSpan)) / 2;
        double aspect = (LonSpan / LatSpan) * Math.Cos(midLat * Math.PI / 180);
        int pw = W - 2 * pad;
        int ph = (int)(pw / aspect);
        int H = ph + padTop + pad;
        float pxPerKm = (float)(ph / LatSpan / 111.0);

        using var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        var bg = Bg;
        canvas.Clear(bg);

        SKPoint P(double lon, double lat) => new(
            (float)(pad + (lon - Lon0) / LonSpan * pw),
            (float)(padTop + (Lat1 - lat) / LatSpan * ph));

        // ── Країна — з полігонів областей (як у застосунку): м'яке світіння по краю,
        // суша з легким градієнтом, тривоги — бордо / бурштин, межі — блакитні ──────
        var oblasts = ParseOblasts();
        int reds = 0, yellows = 0;
        using var landRed = new SKPaint { Color = AlarmRed.WithAlpha(0x80), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var landYellow = new SKPaint { Color = AlarmYellow.WithAlpha(0x99), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var border = new SKPaint { Color = Primary.WithAlpha(0x80), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f, StrokeJoin = SKStrokeJoin.Round };
        var obPaths = new List<(string name, SKPath path, string whole)>();
        using var landPath = new SKPath();
        foreach (var (name, rings) in oblasts)
        {
            var level = oblastLevels != null && oblastLevels.TryGetValue(name, out var lv) ? lv : "";
            if (level == "red") reds++; else if (level == "yellow") yellows++;
            // Заливаємо ЦІЛУ область лише коли тривога на всю область (oblastWhole).
            // Якщо oblastWhole не передано — стара поведінка (заливка за level).
            var wlv = oblastWhole != null
                ? (oblastWhole.TryGetValue(name, out var w) ? w : "")
                : level;
            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var ring in rings)
            {
                if (ring.Count < 3) continue;
                path.MoveTo(P(ring[0][0], ring[0][1]));
                for (int i = 1; i < ring.Count; i++) path.LineTo(P(ring[i][0], ring[i][1]));
                path.Close();
            }
            obPaths.Add((name, path, wlv));
            landPath.AddPath(path);
        }
        using (var glowLand = new SKPaint { Color = Primary.WithAlpha(0x1A), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18) })
            canvas.DrawPath(landPath, glowLand);
        using (var land = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, padTop), new SKPoint(0, padTop + ph),
                new[] { Lerp(MapLand, SKColors.White, 0.05f), MapLand }, null, SKShaderTileMode.Clamp),
        })
            canvas.DrawPath(landPath, land);

        // Гарячі зони під скупченнями цілей (червоне світіння, як у застосунку).
        using (var hot = new SKPaint { Color = new SKColor(0xFF, 0x3B, 0x30, 0x24), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20) })
            foreach (var t in threats)
                if (t.DrawPos(DateTime.Now) is { } hpos && !t.Launch)
                    canvas.DrawCircle(P(hpos.lon, hpos.lat), 22 + 5f * Math.Min(t.Amount, 8), hot);

        foreach (var (_, path, wlv) in obPaths)
            if (wlv is "red" or "yellow") canvas.DrawPath(path, wlv == "red" ? landRed : landYellow);

        // ── Райони: розділення тривог (як на офіційній) — заливаємо ті, де тривога ─
        if (raionLevels is { Count: > 0 })
        {
            var rl = new Dictionary<string, string>();
            foreach (var kv in raionLevels) rl[Napo(kv.Key)] = kv.Value;
            using var raiBorder = new SKPaint { Color = new SKColor(0x82, 0x96, 0xB4, 0x48), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f };
            foreach (var (rname, rings) in ParseRaions())
            {
                using var path = new SKPath();
                foreach (var ring in rings)
                {
                    if (ring.Count < 3) continue;
                    path.MoveTo(P(ring[0][0], ring[0][1]));
                    for (int i = 1; i < ring.Count; i++) path.LineTo(P(ring[i][0], ring[i][1]));
                    path.Close();
                }
                if (rl.TryGetValue(Napo(rname), out var rlv))
                    canvas.DrawPath(path, rlv == "red" ? landRed : landYellow);
                canvas.DrawPath(path, raiBorder);
            }
        }
        // Межі областей (вони ж контур країни) і кольоровий край у повністю тривожних.
        foreach (var (_, path, _) in obPaths) canvas.DrawPath(path, border);
        foreach (var (_, path, wlv) in obPaths)
        {
            if (wlv is not ("red" or "yellow")) continue;
            using var edge = new SKPaint { Color = (wlv == "red" ? AlarmRed : AlarmYellow).WithAlpha(0xE6), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, edge);
        }
        foreach (var (_, path, _) in obPaths) path.Dispose();

        // ── Кола 50 / 100 км навколо дому: одразу видно, наскільки ціль близько ──
        var hp = P(home.lon, home.lat);
        using var ringText = new SKPaint { Typeface = Ui, Color = new SKColor(0x5A, 0xA9, 0xFF, 0xB0), IsAntialias = true, TextSize = 12 };
        if (showHome)
            foreach (var km in new[] { 50, 100 })
            {
                using var rp = new SKPaint
                {
                    Color = new SKColor(0x5A, 0xA9, 0xFF, 0x70), IsAntialias = true, Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.3f, PathEffect = SKPathEffect.CreateDash(new[] { 6f, 5f }, 0),
                };
                float r = km * pxPerKm;
                canvas.DrawCircle(hp, r, rp);
                canvas.DrawText($"{km} км", hp.X + r * 0.71f + 3, hp.Y + r * 0.71f + 12, ringText);
            }

        // ── Цілі — як на живій карті застосунку: ті самі силуети зі світінням, політ
        // балістики/КАБ від місця пуску, коло досяжності «лише пуску», зони авіації над
        // РФ / окупованою, прогноз груп пуску, курс і слід. Однакові цілі ≤16 км — одна ×N.
        var now = DateTime.Now;
        // Невідома «ціль» — БпЛА: поодинці (до 3) — реактивний, у рої — звичайний шахед.
        string Cat(Threat t) => ThreatTracker.JetGuess(t, threats) is { } jet ? (jet ? "shahed_jet" : "shahed") : CatOf(t);
        bool IsFlight(Threat t) => !t.Launch && t.Kind is ThreatKind.Ballistic or ThreatKind.Kab && t.From is not null && t.To is not null;
        var flights = threats.Where(t => t.Pos is not null && IsFlight(t)).ToList();
        var launchOnly = threats.Where(t => t.Launch && t.From is not null).ToList();
        var drawn = new List<(Threat t, int n)>();
        foreach (var t in threats)
        {
            if (t.Pos is not { } tp || IsFlight(t) || t.Launch) continue;
            int i = drawn.FindIndex(d => Cat(d.t) == Cat(t) && ThreatGeo.Km(d.t.Pos!.Value, tp) < 16);
            if (i >= 0) drawn[i] = (drawn[i].t, drawn[i].n + t.Amount); else drawn.Add((t, t.Amount));
        }
        var zones = AviationFeed.MapZones();
        var groups = LaunchGroups.Active;
        float Km2Px(double km) => (float)(km * pxPerKm);
        // Де мітка зараз: «на Харків» — ще на шляху до міста, а не над ним.
        SKPoint DP(Threat t) { var q = t.DrawPos(now)!.Value; return P(q.lon, q.lat); }

        // Де зараз мітка польоту і скільки лишилось (хв) — та сама формула, що в застосунку.
        (SKPoint at, float heading, double left, bool landed) FlightNow(Threat t)
        {
            var (f, to) = (t.From!.Value, t.To!.Value);
            double dist = Math.Max(0.1, ThreatGeo.Km(f, to));
            double spd = ThreatGeo.SpeedKmh(t.Kind, t.Ammo) / 60;
            double p = Math.Clamp(spd * (now - t.LaunchAt).TotalMinutes / dist, 0, 1);
            var at = P(f.lon + (to.lon - f.lon) * p, f.lat + (to.lat - f.lat) * p);
            return (at, (float)ThreatGeo.Bearing(f, to), dist / spd * (1 - p), p >= 1);
        }

        var targetPts = new List<SKPoint>();
        targetPts.AddRange(drawn.Select(d => DP(d.t)));
        targetPts.AddRange(flights.Select(t => FlightNow(t).at));
        targetPts.AddRange(launchOnly.Select(t => P(t.From!.Value.lon, t.From!.Value.lat)));
        targetPts.AddRange(zones.Select(z => P(z.lon, z.lat)));
        targetPts.AddRange(groups.Select(g => g.PosAt(now)).Select(p => P(p.lon, p.lat)));
        var labels = new List<(string text, SKPoint at, SKColor color, float size, char align)>(); // align: c / l / r

        // ✈ Зони авіації: коло досяжності КАБ (фіолетове) / авіації (блакитне).
        foreach (var z in zones)
        {
            var zc = z.kab ? CatColor("kab") : CatColor("avia");
            var ctr = P(z.lon, z.lat);
            float r = Km2Px(z.areaKm);
            using (var fill = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(ctr, r, new[] { zc.WithAlpha(0x2E), zc.WithAlpha(0x0A) }, null, SKShaderTileMode.Clamp) })
                canvas.DrawCircle(ctr, r, fill);
            using (var ring = Dashed(zc.WithAlpha(0xCC), 1.6f)) canvas.DrawCircle(ctr, r, ring);
            labels.Add((z.kab ? "загроза КАБ" : "авіація", new SKPoint(ctr.X, ctr.Y - r - 6), zc, 14, 'c')); // над колом: нижче — міста
        }

        // «Лише пуск» (ціль невідома): коло, куди могла долетіти.
        foreach (var t in launchOnly)
        {
            var c = CatColor(Cat(t));
            var at = P(t.From!.Value.lon, t.From!.Value.lat);
            double reach = Math.Min(t.Kind == ThreatKind.Ballistic ? 450 : 1200,
                ThreatGeo.SpeedKmh(t.Kind, t.Ammo) / 60 * Math.Max(0, (now - t.LaunchAt).TotalMinutes));
            float r = Km2Px(reach);
            if (r > 4)
            {
                using (var fill = new SKPaint { Color = c.WithAlpha(0x10), IsAntialias = true }) canvas.DrawCircle(at, r, fill);
                using (var ring = Dashed(c.WithAlpha(0xB3), 1.6f)) canvas.DrawCircle(at, r, ring);
            }
            labels.Add(($"пуск: {t.FromName}", new SKPoint(at.X, at.Y + 30), c, 14, 'c'));
        }

        // Політ балістики / КАБ: увесь шлях пунктиром, пройдене — суцільно, приціл на цілі.
        foreach (var t in flights)
        {
            var c = CatColor(Cat(t));
            var a = P(t.From!.Value.lon, t.From!.Value.lat);
            var b = P(t.To!.Value.lon, t.To!.Value.lat);
            var (at, _, left, landed) = FlightNow(t);
            using (var dash = Dashed(c.WithAlpha(0x73), 1.5f)) canvas.DrawLine(a, b, dash);
            using (var flown = new SKPaint { Color = c.WithAlpha(0xD9), IsAntialias = true, StrokeWidth = 2.6f, StrokeCap = SKStrokeCap.Round })
                canvas.DrawLine(a, at, flown);
            using (var dot = new SKPaint { Color = c.WithAlpha(0xB3), IsAntialias = true }) canvas.DrawCircle(a, 4, dot);
            using (var aim = new SKPaint { Color = (landed ? new SKColor(0xFF, 0x45, 0x3A) : c).WithAlpha(0xE6), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            {
                canvas.DrawCircle(b, landed ? 16 : 10, aim);
                canvas.DrawLine(b.X - 7, b.Y, b.X + 7, b.Y, aim);
                canvas.DrawLine(b.X, b.Y - 7, b.X, b.Y + 7, aim);
            }
            if (t.FromName is { Length: > 0 } fn && !t.FromGuess)
                labels.Add(($"пуск: {fn}", new SKPoint(a.X + 14, a.Y + 5), c, 13, 'l'));
            if (!landed) labels.Add((left < 1 ? "<1 хв" : $"{left:0} хв", new SKPoint(at.X - 18, at.Y + 5), c, 15, 'r'));
        }

        // 🚀 Групи пуску (прогноз): куди летять типовим маршрутом.
        foreach (var g in groups)
        {
            var p = g.PosAt(now);
            var c = CatColor(g.Reactive ? "shahed_jet" : "shahed");
            var gone = ThreatGeo.Km(g.Route[0], p);
            var ahead = new List<(double lat, double lon)> { p };
            ahead.AddRange(g.Route.Skip(1).Where(w => ThreatGeo.Km(g.Route[0], w) > gone + 1));
            using var dash = Dashed(c.WithAlpha(0x5A), 1.4f);
            for (int i = 1; i < ahead.Count; i++)
                canvas.DrawLine(P(ahead[i - 1].lon, ahead[i - 1].lat), P(ahead[i].lon, ahead[i].lat), dash);
        }

        // Слід — звідки ціль прилетіла (реальні точки шляху), тонка лінія, що згасає.
        foreach (var (t, _) in drawn)
        {
            List<(double lat, double lon)> track; lock (t.Track) track = t.Track.TakeLast(6).ToList();
            if (track.Count < 2) continue;
            var c = CatColor(Cat(t));
            for (int i = 1; i < track.Count; i++)
            {
                byte a = (byte)(0x30 + 0x70 * i / (track.Count - 1));
                using var tp = new SKPaint { Color = c.WithAlpha(a), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, StrokeCap = SKStrokeCap.Round };
                canvas.DrawLine(P(track[i - 1].lon, track[i - 1].lat), P(track[i].lon, track[i].lat), tp);
            }
        }

        // Кружляє над містом: пунктирне коло замість курсу.
        foreach (var (t, _) in drawn)
        {
            if (!t.OrbitingAt(now) || t.Down) continue;
            var c = CatColor(Cat(t));
            var at = P(t.Pos!.Value.lon, t.Pos!.Value.lat);
            float r = Math.Max(Km2Px(t.Kind == ThreatKind.Fpv ? 3 : 8), 22);
            using (var ring = Dashed(c.WithAlpha(0xB3), 1.6f)) canvas.DrawCircle(at, r, ring);
            labels.Add(("кружляє", new SKPoint(at.X, at.Y + r + 15), c, 13, 'c'));
        }

        // Курс (лише справжній — ціль бачили в русі): пунктир на ~10 хв польоту, ≤55 км.
        foreach (var (t, _) in drawn)
        {
            if (t.OrbitingAt(now)) continue;
            // Летить до названого міста: пунктир до нього і приціл, далі — нічого не вигадуємо.
            if (t.Approach is not null)
            {
                var c0 = CatColor(Cat(t));
                var dst = P(t.Pos!.Value.lon, t.Pos!.Value.lat);
                using (var dash = Dashed(c0.WithAlpha(0x99), 1.5f)) canvas.DrawLine(DP(t), dst, dash);
                using (var aim = new SKPaint { Color = c0.WithAlpha(0xCC), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f }) canvas.DrawCircle(dst, 6, aim);
                continue;
            }
            if (!t.HeadingKnown || t.Heading is not { } hd || t.Down) continue;
            double lead = Math.Min(55, ThreatGeo.SpeedKmh(t.Kind, t.Ammo) / 60 * 10);
            var from = t.Pos!.Value;
            var to = (lat: from.lat + lead * Math.Cos(hd * Math.PI / 180) / 111,
                      lon: from.lon + lead * Math.Sin(hd * Math.PI / 180) / (111 * Math.Cos(from.lat * Math.PI / 180)));
            if (!OblastGeo.InUkraine(to.lat, to.lon) && OblastGeo.InUkraine(from.lat, from.lon)) continue; // не «в Румунію»
            var c = CatColor(Cat(t));
            var ps = P(to.lon, to.lat);
            using (var dash = Dashed(c.WithAlpha(0x99), 1.5f)) canvas.DrawLine(P(from.lon, from.lat), ps, dash);
            using (var dot = new SKPaint { Color = c.WithAlpha(0xCC), IsAntialias = true }) canvas.DrawCircle(ps, 3.2f, dot);
        }

        // ── Міста: крапки, потім цілі, а підписи — НАЙВИЩЕ (з обводкою) і з того боку
        // крапки, де немає цілі: раніше силует закривав «Харків» і «Запоріжжя».
        using var cityDot = new SKPaint { Color = Primary, IsAntialias = true };
        using var cityDotRim = new SKPaint { Color = MapLand, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        using var cityText = new SKPaint { Typeface = Ui, Color = TextPrimary, IsAntialias = true, TextSize = 15 };
        using var cityHalo = new SKPaint { Typeface = Ui, Color = MapLand, IsAntialias = true, TextSize = 15, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
        foreach (var (_, lon, lat) in Cities) { canvas.DrawCircle(P(lon, lat), 2.8f, cityDot); canvas.DrawCircle(P(lon, lat), 2.8f, cityDotRim); }

        const float Ppu = 2.3f; // розмір силуету: шахед ≈ 40 px на картинці 1000 px
        using var fadeLayer = new SKPaint { Color = SKColors.White.WithAlpha(0x80) };
        foreach (var z in zones)
        {
            // Літак кружляє над РФ / окупованою — ніс у бік області, яку б'є.
            var hdg = OblastGeo.PartPoint(z.canon, null) is { } tc ? ThreatGeo.Bearing((z.lat, z.lon), tc) : 270;
            DrawFig(canvas, P(z.lon, z.lat), hdg, "avia", CatColor("avia"), Ppu);
        }
        foreach (var t in launchOnly)
            DrawFig(canvas, P(t.From!.Value.lon, t.From!.Value.lat), t.Heading ?? 0, Cat(t), CatColor(Cat(t)), Ppu);
        foreach (var t in flights)
        {
            var (at, hdg, _, landed) = FlightNow(t);
            if (!landed) DrawFig(canvas, at, hdg, Cat(t), CatColor(Cat(t)), Ppu, glow: new SKColor(0xFF, 0x45, 0x3A), glowStrength: 0.9f);
        }
        foreach (var g in groups)
        {
            var p = g.PosAt(now);
            var at = P(p.lon, p.lat);
            var cat = g.Reactive ? "shahed_jet" : "shahed";
            var gone = ThreatGeo.Km(g.Route[0], p);
            var next = g.Route.Skip(1).FirstOrDefault(w => ThreatGeo.Km(g.Route[0], w) > gone + 1, g.Route[^1]);
            canvas.SaveLayer(fadeLayer);
            DrawFig(canvas, at, ThreatGeo.Bearing(p, next), cat, CatColor(cat), Ppu);
            canvas.Restore();
            labels.Add(("прогноз", new SKPoint(at.X, at.Y + 30), CatColor(cat), 13, 'c'));
        }
        foreach (var (t, _) in drawn)
        {
            var cat = Cat(t);
            var pt = DP(t);
            bool faded = t.Down || now - t.LastUpdate > TimeSpan.FromMinutes(6);
            bool near = t.Stage is TrackStage.Imminent or TrackStage.Overhead or TrackStage.Impact;
            if (faded) canvas.SaveLayer(fadeLayer);
            DrawFig(canvas, pt, t.Heading ?? 0, cat, CatColor(cat), Ppu,
                glow: near ? new SKColor(0xFF, 0x45, 0x3A) : null, glowStrength: near ? 0.9f : 0.55f);
            if (faded) canvas.Restore();
            if (t.Down) labels.Add((t.AltNote is { Length: > 0 } an ? an : "падає", new SKPoint(pt.X, pt.Y + 32), new SKColor(0xFF, 0x6B, 0x5E), 13, 'c'));
        }

        // ×N: ціль або група пуску з кількома бортами.
        var pillsToDraw = drawn.Where(d => d.n > 1).Select(d => (pt: DP(d.t), n: d.n, c: CatColor(Cat(d.t))))
            .Concat(groups.Where(g => g.Left > 1).Select(g => (pt: P(g.PosAt(now).lon, g.PosAt(now).lat), n: g.Left, c: CatColor(g.Reactive ? "shahed_jet" : "shahed"))))
            .ToList();

        // Зайняте: силуети (±14 px) і пігулки «×N» (праворуч-вгорі від силуету).
        var pills = pillsToDraw.Select(p => new SKRect(p.pt.X + 10, p.pt.Y - 28, p.pt.X + 48, p.pt.Y - 5)).ToList();
        bool HitsTarget(SKRect r)
        {
            var big = r; big.Inflate(14, 14);
            return targetPts.Any(big.Contains) || pills.Any(r.IntersectsWith);
        }
        foreach (var (nm, lon, lat) in Cities)
        {
            var cp = P(lon, lat);
            float tw = cityText.MeasureText(nm);
            var right = new SKRect(cp.X + 5, cp.Y - 17, cp.X + 5 + tw, cp.Y);
            var left = new SKRect(cp.X - 5 - tw, cp.Y - 17, cp.X - 5, cp.Y);
            var below = new SKRect(cp.X - tw / 2, cp.Y + 4, cp.X + tw / 2, cp.Y + 20);
            var below2 = new SKRect(cp.X - tw / 2, cp.Y + 14, cp.X + tw / 2, cp.Y + 30); // ціль прямо на місті
            // Біля краю картинки (Луганськ) — не справа, бо обріжеться до «Луганс».
            var fits = new[] { right, left, below, below2 }.Where(r => r.Left >= 2 && r.Right <= W - 2).ToArray();
            var at = fits.FirstOrDefault(r => !HitsTarget(r), fits.Length > 0 ? fits[0] : left);
            canvas.DrawText(nm, at.Left, at.Bottom - 4, cityHalo); // темна обводка для читабельності
            canvas.DrawText(nm, at.Left, at.Bottom - 4, cityText);
        }

        // Підписи цілей («пуск: Бєлгород», «2 хв», «прогноз», «загроза КАБ») — поверх усього.
        foreach (var (text, at, color, size, align) in labels)
        {
            using var halo = new SKPaint { Typeface = UiBold, Color = MapLand, IsAntialias = true, TextSize = size, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
            using var fill = new SKPaint { Typeface = UiBold, Color = color, IsAntialias = true, TextSize = size };
            float x0 = align switch { 'c' => at.X - fill.MeasureText(text) / 2, 'r' => at.X - fill.MeasureText(text), _ => at.X };
            canvas.DrawText(text, x0, at.Y, halo);
            canvas.DrawText(text, x0, at.Y, fill);
        }

        // ×N — у темній «пігулці», щоб читалось на будь-якому фоні.
        using var xnFill = new SKPaint { Typeface = UiBold, Color = SKColors.White, IsAntialias = true, TextSize = 16 };
        foreach (var (pt, n, pc) in pillsToDraw)
        {
            var label = $"×{n}";
            float w = xnFill.MeasureText(label) + 10;
            var pill = new SKRect(pt.X + 12, pt.Y - 26, pt.X + 12 + w, pt.Y - 7);
            using var pb = new SKPaint { Color = Bg.WithAlpha(0xE0), IsAntialias = true };
            using var ps = new SKPaint { Color = pc, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
            canvas.DrawRoundRect(pill, 9, 9, pb);
            canvas.DrawRoundRect(pill, 9, 9, ps);
            canvas.DrawText(label, pill.Left + 5, pill.Bottom - 5, xnFill);
        }

        // ── Дім (лише для особистого /map) ─────────────────────────────────────
        if (showHome)
        {
            // Як у застосунку: блакитна крапка з кільцем і м'яким світінням.
            using var homeGlow = new SKPaint { Color = HomeBlue.WithAlpha(0x55), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) };
            using var ring2 = new SKPaint { Color = HomeBlue, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
            using var homePaint = new SKPaint { Color = HomeBlue, IsAntialias = true };
            canvas.DrawCircle(hp, 14, homeGlow);
            canvas.DrawCircle(hp, 12, ring2);
            canvas.DrawCircle(hp, 5.5f, homePaint);
        }

        // ── Заголовок: назва, підзаголовок кольором РИЗИКУ (не кількості тривог) ──
        var subColor = tone switch
        {
            "alarm" => new SKColor(0xFF, 0x45, 0x3A),
            "warn"  => new SKColor(0xF4, 0xC5, 0x50),
            "calm"  => new SKColor(0x34, 0xD3, 0x99),
            _       => new SKColor(0xC7, 0xD2, 0xE3),
        };
        using var titlePaint = new SKPaint { Typeface = UiBold, Color = SKColors.White, IsAntialias = true, TextSize = 30 };
        using var sub = new SKPaint { Typeface = UiBold, Color = subColor, IsAntialias = true, TextSize = 21 };
        using var muted = new SKPaint { Typeface = Ui, Color = new SKColor(0x87, 0x92, 0xA6), IsAntialias = true, TextSize = 15 };
        using var timePaint = new SKPaint { Typeface = UiBold, Color = new SKColor(0xC7, 0xD2, 0xE3), IsAntialias = true, TextSize = 22 };
        DrawLogo(canvas, pad + 2, 13, 54);
        float tx0 = LogoBmp is null ? pad + 2 : pad + 70;
        canvas.DrawText(title, tx0, 37, titlePaint);
        canvas.DrawText(subtitle, tx0, 65, sub);
        var hhmm = DateTime.Now.ToString("HH:mm");
        canvas.DrawText(hhmm, W - pad - 4 - timePaint.MeasureText(hhmm), 36, timePaint);
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        canvas.DrawText(date, W - pad - 4 - muted.MeasureText(date), 58, muted);

        // ── Картка-легенда в лівому нижньому куті (Молдова/Румунія — там порожньо) ──
        // Лише ті типи, що зараз є на карті (з польотами, пусками, групами й авіацією), з кількістю.
        var kinds = drawn.Select(d => (cat: Cat(d.t), n: d.n))
            .Concat(flights.Concat(launchOnly).Select(t => (cat: Cat(t), n: t.Amount)))
            .Concat(groups.Select(g => (cat: g.Reactive ? "shahed_jet" : "shahed", n: g.Left)))
            .Concat(zones.Select(z => (cat: "avia", n: 1)))
            .GroupBy(k => k.cat).Select(g => (cat: g.Key, n: g.Sum(k => k.n)))
            .OrderByDescending(k => k.n).ToList();
        int total = kinds.Where(k => k.cat != "avia").Sum(k => k.n);
        using var cardTitle = new SKPaint { Typeface = UiBold, Color = SKColors.White, IsAntialias = true, TextSize = 17.5f };
        using var cardText = new SKPaint { Typeface = Ui, Color = new SKColor(0xC7, 0xD2, 0xE3), IsAntialias = true, TextSize = 15 };
        int kindRows = (kinds.Count + 1) / 2;
        float cw = 330, chh = 64 + kindRows * 30 + (showHome ? 24 : 0);
        var card = new SKRect(pad + 4, H - pad - 4 - chh, pad + 4 + cw, H - pad - 4);
        using (var sh = new SKPaint { Color = SKColors.Black.WithAlpha(0x80), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10) })
            canvas.DrawRoundRect(new SKRect(card.Left, card.Top + 3, card.Right, card.Bottom + 3), 14, 14, sh);
        using (var cb = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, card.Top), new SKPoint(0, card.Bottom),
                new[] { new SKColor(0x15, 0x1C, 0x28, 0xF0), new SKColor(0x0D, 0x12, 0x1A, 0xF4) }, null, SKShaderTileMode.Clamp),
        })
            canvas.DrawRoundRect(card, 14, 14, cb);
        using (var cbs = new SKPaint { Color = Primary.WithAlpha(0x55), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f })
            canvas.DrawRoundRect(card, 14, 14, cbs);
        using (var acc = new SKPaint
        {
            IsAntialias = true, StrokeWidth = 3, StrokeCap = SKStrokeCap.Round,
            Shader = SKShader.CreateLinearGradient(new SKPoint(card.Left + 14, 0), new SKPoint(card.Left + 110, 0),
                new[] { new SKColor(0x4C, 0x93, 0xFF), new SKColor(0xFF, 0xD5, 0x00) }, null, SKShaderTileMode.Clamp),
        })
            canvas.DrawLine(card.Left + 14, card.Top + 1.5f, card.Left + 110, card.Top + 1.5f, acc);
        float x = card.Left + 14, y = card.Top + 26;
        if (total > 0) canvas.DrawText($"На карті цілей: {total}", x, y, cardTitle);
        else
        {
            using var calm = new SKPaint { Typeface = UiBold, Color = new SKColor(0x2F, 0xD1, 0x7C), IsAntialias = true, TextSize = 17 };
            canvas.DrawText("Цілей на карті немає", x, y, calm);
        }
        y += 14;
        for (int i = 0; i < kinds.Count; i++)
        {
            float kx = x + (i % 2) * 158, ky = y + 22 + (i / 2) * 30;
            var kc = CatColor(kinds[i].cat);
            DrawFig(canvas, new SKPoint(kx + 10, ky - 5), 0, kinds[i].cat, kc, 1.15f, glowStrength: 0.35f);
            var kl = KindLabel(kinds[i].cat);
            canvas.DrawText(kl, kx + 26, ky, cardText);
            if (kinds[i].cat != "avia")
            {
                using var kn = new SKPaint { Typeface = UiBold, Color = Lerp(kc, SKColors.White, 0.12f), IsAntialias = true, TextSize = 15 };
                canvas.DrawText($"{kinds[i].n}", kx + 26 + cardText.MeasureText(kl) + 5, ky, kn);
            }
        }
        y += kindRows * 30 + 22;
        using (var sw = new SKPaint { Color = AlarmRed.WithAlpha(0xD0), IsAntialias = true }) canvas.DrawRoundRect(new SKRect(x, y - 12, x + 15, y + 2), 4, 4, sw);
        using (var sw = new SKPaint { Color = AlarmYellow.WithAlpha(0xD0), IsAntialias = true }) canvas.DrawRoundRect(new SKRect(x + 140, y - 12, x + 155, y + 2), 4, 4, sw);
        canvas.DrawText($"тривога {reds}", x + 21, y, cardText);
        canvas.DrawText($"жовтий рівень {yellows}", x + 161, y, cardText);
        if (showHome)
        {
            y += 24;
            using var hl = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawCircle(x + 7, y - 5, 6, hl);
            canvas.DrawText("твоє місто · кола 50 і 100 км", x + 20, y, cardText);
        }
        DrawBrand(canvas, W - pad - 4, H - pad - 2, 16);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ── 🌙 Карта «Рух повітряних цілей» за ніч (як у ППО РАДАР / monitorwar) ─────
    // Ширша рамка: + захід Росії, щоб було видно місця пуску (Брянськ, Курськ, Орел…).
    // Шари — кольорами й силуетами живої карти застосунку (kThreatColors): шахед жовтий, реактивний
    // помаранчевий, крилата («бандероль») рожева, Циркон блакитний, балістика червона, КАБ
    // фіолетовий, ФПВ бірюзовий. «Гарячі» місця — значки з силуетом, пуски — шпильки з кількістю.
    static string NightFig(string cat) => cat switch
    {
        "jet" => "shahed_jet", "cruise" => "banderol", "hyper" => "hyper", "kab" => "kab", "fpv" => "fpv", "ballistic" => "balist", _ => "shahed",
    };
    static SKColor NightColor(string cat) => CatColor(NightFig(cat));

    public static byte[] RenderNight(NightSummary.Data d)
    {
        const double LonA = 21.9, LonB = 42.3, LatTop = 55.2, LatBot = 44.1;
        const int W = 1400, pad = 10;
        double midLat = (LatTop + LatBot) / 2;
        double aspect = ((LonB - LonA) / (LatTop - LatBot)) * Math.Cos(midLat * Math.PI / 180);
        int pw = W - 2 * pad, ph = (int)(pw / aspect);
        var tracks = NightSummary.BuildTracks(d);
        var cBal = CatColor("balist");

        // Легенда — пігулки з силуетом і кількістю. Рідкісні шари — лише коли були вночі.
        int drones = d.Official is { Drones: > 0 } od ? od.Drones : d.Launched;
        int jets = d.Official is { Reactive: > 0 } oj ? oj.Reactive : d.Waves.Sum(w => w.Items.Where(i => i.Reactive).Sum(i => i.N));
        int hyper = d.Count("hyper"), kab = d.Count("kab"), fpv = d.Count("fpv"), bal = d.Ballistics.Sum(b => b.N);
        var legend = new List<(string fig, SKColor c, string label, string? value)> { ("shahed", CatColor("shahed"), "шахеди", drones > 0 ? $"~{drones}" : null) };
        if (jets > 0 || tracks.Any(t => t.Cat == "jet")) legend.Add(("shahed_jet", CatColor("shahed_jet"), "реактивні", jets > 0 ? $"~{jets}" : null));
        if (d.HadCruise || d.Official is { Missiles: > 0 })
            legend.Add(("banderol", CatColor("banderol"), d.Official is { Missiles: > 0 } ? "ракети" : "крилаті ракети", d.Official is { Missiles: > 0 } om ? $"{om.Missiles}" : null));
        if (hyper > 0) legend.Add(("hyper", CatColor("hyper"), "Циркони", $"~{hyper}"));
        if (bal > 0) legend.Add(("balist", cBal, "балістика", $"{bal}"));
        if (kab > 0) legend.Add(("kab", CatColor("kab"), "КАБ", $"~{kab}"));
        if (fpv > 0) legend.Add(("fpv", CatColor("fpv"), "ФПВ", $"~{fpv}"));
        if (d.Impacts.Count > 0) legend.Add(("burst", new SKColor(0xFF, 0x8A, 0x1C), "вибухи", $"{d.Impacts.Count}"));
        const float inner = 28, chipH = 32, chipGap = 9;
        var chipPos = new List<(float x, int row)>();
        {
            float fx = 0; int row = 0;
            foreach (var (fig, c, label, value) in legend)
            {
                float w = Chip(null!, 0, 0, fig, c, label, value, measure: true);
                if (fx > 0 && fx + w > W - 2 * inner) { fx = 0; row++; }
                chipPos.Add((fx, row));
                fx += w + chipGap;
            }
        }
        int chipRows = chipPos.Count == 0 ? 1 : chipPos.Max(p => p.row) + 1;
        int footH = (int)(40 + 46 + 24 + chipRows * (chipH + chipGap) - chipGap + 36 + 18);
        int H = ph + 2 * pad + footH;

        using var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        // Тло — як у застосунку: темно-синє з легкою віньєткою до країв.
        using (var bgp = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(new SKPoint(W * 0.55f, H * 0.45f), W * 0.75f,
                new[] { new SKColor(0x10, 0x16, 0x20), Bg }, null, SKShaderTileMode.Clamp),
        })
            canvas.DrawRect(0, 0, W, H, bgp);
        SKPoint P(double lat, double lon) => new(
            (float)(pad + (lon - LonA) / (LonB - LonA) * pw),
            (float)(pad + (LatTop - lat) / (LatTop - LatBot) * ph));

        // Україна: суша з градієнтом і світінням по краю, межі областей — блакитні (як у застосунку).
        using var landPath = new SKPath();
        var obPaths = new List<SKPath>();
        foreach (var (_, rings) in ParseOblasts())
        {
            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var ring in rings)
            {
                if (ring.Count < 3) continue;
                path.MoveTo(P(ring[0][1], ring[0][0]));
                for (int i = 1; i < ring.Count; i++) path.LineTo(P(ring[i][1], ring[i][0]));
                path.Close();
            }
            obPaths.Add(path); landPath.AddPath(path);
        }
        using (var glowLand = new SKPaint { Color = Primary.WithAlpha(0x22), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 22) })
            canvas.DrawPath(landPath, glowLand);
        using (var land = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, H * 0.25f), new SKPoint(0, H * 0.95f),
                new[] { Lerp(MapLand, SKColors.White, 0.06f), MapLand }, null, SKShaderTileMode.Clamp),
        })
            canvas.DrawPath(landPath, land);
        using (var border = new SKPaint { Color = Primary.WithAlpha(0x66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, StrokeJoin = SKStrokeJoin.Round })
            foreach (var path in obPaths) canvas.DrawPath(path, border);
        using (var rim = new SKPaint { Color = Primary.WithAlpha(0xA0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, StrokeJoin = SKStrokeJoin.Round })
        using (var outline = landPath.Simplify() ?? new SKPath(landPath))
            canvas.DrawPath(outline, rim);
        foreach (var path in obPaths) path.Dispose();

        // Міста України — підписи малюємо ПІСЛЯ ліній (інакше жмуток шахедів їх закриває: «Чер аси»).
        using var cityDot = new SKPaint { Color = Primary, IsAntialias = true };
        using var cityRim = new SKPaint { Color = Bg, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
        using var cityText = new SKPaint { Typeface = UiBold, Color = TextPrimary, IsAntialias = true, TextSize = 17 };
        using var cityHalo = new SKPaint { Typeface = UiBold, Color = Bg, IsAntialias = true, TextSize = 17, Style = SKPaintStyle.Stroke, StrokeWidth = 5, StrokeJoin = SKStrokeJoin.Round };

        // Дуга a→b (контрольна точка зсунута вбік — «вигнуті» лінії як на радарах).
        void Arc(SKPath path, SKPoint a, SKPoint b, float bend)
        {
            float mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, dx = b.X - a.X, dy = b.Y - a.Y;
            path.QuadTo(mx - dy * bend, my + dx * bend, b.X, b.Y);
        }
        void Arrow(SKPoint tip, SKPoint from, SKColor c, float size)
        {
            double ang = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
            using var tri = new SKPath();
            tri.MoveTo(tip.X, tip.Y);
            tri.LineTo((float)(tip.X - size * Math.Cos(ang - 0.45)), (float)(tip.Y - size * Math.Sin(ang - 0.45)));
            tri.LineTo((float)(tip.X - size * Math.Cos(ang + 0.45)), (float)(tip.Y - size * Math.Sin(ang + 0.45)));
            tri.Close();
            using var p = new SKPaint { Color = c, IsAntialias = true };
            canvas.DrawPath(tri, p);
        }
        // Курс у градусах (0° = вгору) — для силуету на кінці лінії.
        static float Heading(SKPoint from, SKPoint to) => (float)(Math.Atan2(to.X - from.X, from.Y - to.Y) * 180 / Math.PI);

        // Траєкторії: м'яке світіння під лінією, на кінці — стрілка (шахеди) або силует (ракети).
        var rnd = new Random(7);
        string[] order = { "shahed", "jet", "cruise", "hyper" }; // ракети поверх
        var heads = new List<(SKPoint at, float hdg, string cat)>();
        foreach (var t in tracks.OrderBy(t => Array.IndexOf(order, t.Cat)))
        {
            var c = NightColor(t.Cat);
            bool fast = t.Cat is "cruise" or "hyper";
            var pts = t.Pts.Select(p => P(p.lat, p.lon)).ToList();
            using var path = new SKPath();
            path.MoveTo(pts[0]);
            float bend = (float)(0.10 + rnd.NextDouble() * 0.12) * (rnd.Next(2) == 0 ? 1 : -1);
            for (int i = 1; i < pts.Count; i++) Arc(path, pts[i - 1], pts[i], i == 1 && t.Site != null ? bend : bend * 0.5f);
            float wdt = fast ? 2.6f : t.Cat == "jet" ? 2.0f : 1.6f;
            using (var glow = new SKPaint { Color = c.WithAlpha(0x30), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = wdt * 3.2f, StrokeCap = SKStrokeCap.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) })
                canvas.DrawPath(path, glow);
            using (var stroke = new SKPaint { Color = c.WithAlpha(0xD0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = wdt, StrokeCap = SKStrokeCap.Round })
                canvas.DrawPath(path, stroke);
            var prev = pts.Count >= 2 ? pts[^2] : pts[0];
            if (fast) heads.Add((pts[^1], Heading(prev, pts[^1]), t.Cat));
            else Arrow(pts[^1], prev, c, t.Cat == "jet" ? 12 : 11);
        }
        foreach (var (at, hdg, cat) in heads) DrawFig(canvas, at, hdg, NightFig(cat), NightColor(cat), 1.15f, glowStrength: 0.6f);

        // Балістика: червоні дуги від місця пуску до цілі. Не більше 3 дуг на напрямок (вузький
        // жмуток) і справжня кількість «×N»: раніше кожна ракета — своя дуга, і 22 дуги
        // Брянськ→Київ малювали страшне червоне віяло на півкарти.
        using var bGlow = new SKPaint { Color = cBal.WithAlpha(0x38), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 7f, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) };
        using var bStroke = new SKPaint { Color = cBal.WithAlpha(0xD8), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        var balHits = new List<(SKPoint at, float hdg, int n)>();
        foreach (var g in d.Ballistics.GroupBy(b => (b.Origin, b.Target)))
        {
            var b0 = g.First();
            var a = P(b0.OLat, b0.OLon); var z = P(b0.TLat, b0.TLon);
            int total = g.Sum(b => b.N);
            int lines = Math.Min(3, total);
            for (int i = 0; i < lines; i++)
            {
                float bend = lines == 1 ? 0.12f : 0.06f + 0.10f * i / (lines - 1);
                using var path = new SKPath(); path.MoveTo(a); Arc(path, a, z, bend);
                canvas.DrawPath(path, bGlow);
                canvas.DrawPath(path, bStroke);
            }
            balHits.Add((z, Heading(new SKPoint((a.X + z.X) / 2, (a.Y + z.Y) / 2), z), total));
        }

        // Вибухи — спалахи.
        foreach (var g in d.Impacts.GroupBy(i => (Math.Round(i.Lat, 2), Math.Round(i.Lon, 2))))
            DrawBurst(canvas, P(g.First().Lat, g.First().Lon), 13 + Math.Min(9, g.Count() * 2));

        // Підписи без накладань: праворуч, ліворуч, під, над точкою.
        var taken = new List<SKRect>();
        SKRect? Spot(SKPoint p, float tw, float th, bool force)
        {
            var opts = new[]
            {
                new SKRect(p.X + 7, p.Y - th - 3, p.X + 7 + tw, p.Y - 3), new SKRect(p.X - 7 - tw, p.Y - th - 3, p.X - 7, p.Y - 3),
                new SKRect(p.X - tw / 2, p.Y + 6, p.X + tw / 2, p.Y + 6 + th), new SKRect(p.X - tw / 2, p.Y - th - 9, p.X + tw / 2, p.Y - 9),
            };
            foreach (var r in opts)
                if (r.Left >= 2 && r.Right <= W - 2 && r.Top >= 2 && !taken.Any(t => t.IntersectsWith(r))) { taken.Add(r); return r; }
            if (!force) return null;
            var f = opts[0].Right <= W - 2 ? opts[0] : opts[1];
            taken.Add(f); return f;
        }
        // Куди летіло найбільше: значок із силуетом шару (більший — частіше). КАБ і ФПВ — лише значками.
        // Значки — до підписів, і підписи їх оминають.
        var cityNames = Cities.Select(c => c.name).ToHashSet();
        var takes = new (string cat, int n)[] { ("shahed", 12), ("jet", 7), ("cruise", 10), ("hyper", 5), ("kab", 5), ("fpv", 4) };
        var hot = takes.SelectMany(x => d.HotSpots(x.cat, x.n).Select(h => (h, cat: x.cat))).ToList();
        var maxBy = hot.GroupBy(x => x.cat).ToDictionary(g => g.Key, g => Math.Max(1, g.Max(x => x.h.score)));
        float BadgeR(string cat, int score) => 9 + 6 * (float)Math.Sqrt((double)score / maxBy[cat]);
        // Менші — зверху, щоб великий значок не ховав сусіда.
        foreach (var (h, cat) in hot.OrderByDescending(x => BadgeR(x.cat, x.h.score)))
        {
            var hp = P(h.lat, h.lon);
            float rr = BadgeR(cat, h.score);
            DrawBadge(canvas, hp, NightFig(cat), NightColor(cat), rr);
            taken.Add(new SKRect(hp.X - rr, hp.Y - rr, hp.X + rr, hp.Y + rr));
        }
        // Балістика: силует ракети на цілі і «×N» у пігулці.
        using var xnFill = new SKPaint { Typeface = UiBold, Color = SKColors.White, IsAntialias = true, TextSize = 16 };
        foreach (var (z, hdg, n) in balHits)
        {
            DrawFig(canvas, z, hdg, "balist", cBal, 1.25f, glow: cBal, glowStrength: 0.8f);
            taken.Add(new SKRect(z.X - 13, z.Y - 13, z.X + 13, z.Y + 13));
            if (n <= 1) continue;
            var label = $"×{n}";
            float w = xnFill.MeasureText(label) + 12;
            var pill = new SKRect(z.X + 11, z.Y + 2, z.X + 11 + w, z.Y + 23);
            using (var pb = new SKPaint { Color = Bg.WithAlpha(0xE8), IsAntialias = true }) canvas.DrawRoundRect(pill, 10, 10, pb);
            using (var ps = new SKPaint { Color = cBal, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f }) canvas.DrawRoundRect(pill, 10, 10, ps);
            canvas.DrawText(label, pill.Left + 6, pill.Bottom - 5.5f, xnFill);
            taken.Add(pill);
        }
        foreach (var (nm, lon, lat) in Cities)
        {
            var cp = P(lat, lon);
            canvas.DrawCircle(cp, 3.4f, cityDot);
            canvas.DrawCircle(cp, 3.4f, cityRim);
            var r = Spot(cp, cityText.MeasureText(nm), 17, force: true)!.Value;
            canvas.DrawText(nm, r.Left, r.Bottom - 3, cityHalo);
            canvas.DrawText(nm, r.Left, r.Bottom - 3, cityText);
        }
        var labelled = new HashSet<string>(cityNames); // обласні центри вже підписані
        foreach (var (h, cat) in hot)
        {
            if (!labelled.Add(h.place)) continue; // кілька шарів в одному місці — одна назва
            var hp = P(h.lat, h.lon);
            float rr = BadgeR(cat, h.score);
            var col = Lerp(NightColor(cat), SKColors.White, 0.1f);
            using var ht = new SKPaint { Typeface = UiBold, Color = col, IsAntialias = true, TextSize = 15 };
            using var hh = new SKPaint { Typeface = UiBold, Color = Bg, IsAntialias = true, TextSize = 15, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
            // У щільному жмутку (Черкащина) — дрібнішим шрифтом, аби значок не лишився без назви.
            foreach (var size in new[] { 15f, 12.5f })
            {
                ht.TextSize = hh.TextSize = size;
                if (Spot(new SKPoint(hp.X + rr - 3, hp.Y + 4), ht.MeasureText(h.place), size, force: false) is not { } lr) continue;
                canvas.DrawText(h.place, lr.Left, lr.Bottom - 2, hh);
                canvas.DrawText(h.place, lr.Left, lr.Bottom - 2, ht);
                break;
            }
        }

        // Місця пуску: шпилька кольору того, що звідти летіло (балістика / реактивні / шахеди),
        // і скільки — «Смоленськ 36».
        var siteN = new Dictionary<string, (int drones, int jets, int bal)>();
        void AddSite(string s, int dr, int jt, int bl)
        {
            var v = siteN.GetValueOrDefault(s);
            siteN[s] = (v.drones + dr, v.jets + jt, v.bal + bl);
        }
        foreach (var i in d.Waves.SelectMany(w => w.Items)) AddSite(i.Origin, i.Reactive ? 0 : i.N, i.Reactive ? i.N : 0, 0);
        foreach (var b in d.Ballistics) AddSite(b.Origin, 0, 0, b.N);
        foreach (var t in tracks.Where(t => t.Site != null)) AddSite(t.Site!, 0, 0, 0); // звідки реально йдуть лінії
        using var pinText = new SKPaint { Typeface = UiMed, Color = new SKColor(0xDC, 0xE2, 0xEC), IsAntialias = true, TextSize = 15 };
        using var pinNum = new SKPaint { Typeface = UiBold, IsAntialias = true, TextSize = 15 };
        using var pinHalo = new SKPaint { Typeface = UiBold, Color = Bg, IsAntialias = true, TextSize = 15, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
        foreach (var (s, v) in siteN)
        {
            if (LaunchFeed.SiteCoords(s) is not { } sc) continue;
            var sp = P(sc.lat, sc.lon);
            var pc = v.bal > v.drones + v.jets ? cBal : v.jets > v.drones ? CatColor("shahed_jet") : CatColor("shahed");
            DrawPin(canvas, sp, pc, 22);
            int n = v.drones + v.jets + v.bal;
            float tx = sp.X + 10, ty = sp.Y - 8;
            canvas.DrawText(s, tx, ty, pinHalo); canvas.DrawText(s, tx, ty, pinText);
            if (n > 0)
            {
                var ns = n.ToString(); float px = tx + pinText.MeasureText(s) + 5;
                pinNum.Color = Lerp(pc, SKColors.White, 0.1f);
                canvas.DrawText(ns, px, ty, pinHalo); canvas.DrawText(ns, px, ty, pinNum);
            }
        }

        // ── Смуга підсумку під картою ──────────────────────────────────────────
        float fy = ph + 2 * pad;
        using (var fb = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, fy), new SKPoint(0, H),
                new[] { new SKColor(0x13, 0x1A, 0x25), new SKColor(0x0B, 0x0F, 0x16) }, null, SKShaderTileMode.Clamp),
        })
            canvas.DrawRect(0, fy, W, H - fy, fb);
        // Синьо-жовта лінія-розділювач, як логотип.
        using (var acc = new SKPaint
        {
            StrokeWidth = 3,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, 0),
                new[] { new SKColor(0x4C, 0x93, 0xFF), new SKColor(0x4C, 0x93, 0xFF), new SKColor(0xFF, 0xD5, 0x00), new SKColor(0xFF, 0xD5, 0x00) },
                new[] { 0f, 0.42f, 0.58f, 1f }, SKShaderTileMode.Clamp),
        })
            canvas.DrawLine(0, fy + 1.5f, W, fy + 1.5f, acc);

        using var periodP = new SKPaint { Typeface = UiMed, Color = new SKColor(0x9F, 0xB0, 0xC8), IsAntialias = true, TextSize = 19 };
        using var big = new SKPaint { Typeface = UiBlack, Color = SKColors.White, IsAntialias = true, TextSize = 37 };
        using var noteP = new SKPaint { Typeface = Ui, Color = new SKColor(0x9F, 0xB0, 0xC8), IsAntialias = true, TextSize = 16 };
        using var extraP = new SKPaint { Typeface = UiBold, Color = new SKColor(0xDC, 0xE2, 0xEC), IsAntialias = true, TextSize = 16 };
        float x0 = inner, y = fy + 40;
        // Місяць + період.
        using (var moon = new SKPath())
        {
            var mc = new SKPoint(x0 + 9, y - 7);
            moon.AddCircle(mc.X, mc.Y, 9);
            using var cut = new SKPath(); cut.AddCircle(mc.X + 5, mc.Y - 4, 8);
            using var crescent = moon.Op(cut, SKPathOp.Difference);
            using var mp = new SKPaint { Color = new SKColor(0xFF, 0xD5, 0x00), IsAntialias = true };
            if (crescent != null) canvas.DrawPath(crescent, mp);
        }
        canvas.DrawText($"Підсумок ночі  ·  {NightSummary.Period(d).Replace("→", "–")}", x0 + 28, y, periodP);
        DrawBrand(canvas, W - inner, y + 4, 24);
        y += 46; canvas.DrawText("Рух повітряних цілей над територією України", x0, y, big);
        y += 24 + chipH / 2;
        for (int i = 0; i < legend.Count; i++)
        {
            var (fig, c, label, value) = legend[i];
            var (fx, row) = chipPos[i];
            Chip(canvas, x0 + fx, y + row * (chipH + chipGap), fig, c, label, value);
        }
        if (legend.Count == 0)
        {
            using var calm = new SKPaint { Typeface = UiBold, Color = new SKColor(0x34, 0xD3, 0x99), IsAntialias = true, TextSize = 19 };
            canvas.DrawText("Ніч була відносно спокійною", x0, y + 7, calm);
        }
        y += (chipRows - 1) * (chipH + chipGap) + chipH / 2 + 30;
        // Додаткові цифри (максимум у повітрі; офіційно — збито / влучання) і пояснення значків.
        var extra = new List<string>();
        if (d.MaxAir > 0) extra.Add($"макс. у повітрі одночасно ~{d.MaxAir}");
        if (d.Official is { } o)
        {
            if (o.Down > 0) extra.Add($"збито / подавлено {o.Down}");
            if (o.Hits > 0) extra.Add($"влучання {o.Hits}");
        }
        float nx = x0;
        if (extra.Count > 0)
        {
            var ex = string.Join("  ·  ", extra);
            canvas.DrawText(ex, nx, y, extraP);
            nx += extraP.MeasureText(ex) + 26;
        }
        DrawBadge(canvas, new SKPoint(nx + 10, y - 6), "shahed", CatColor("shahed"), 10);
        canvas.DrawText("куди летіло найбільше (більший — частіше)", nx + 26, y, noteP);
        nx += 26 + noteP.MeasureText("куди летіло найбільше (більший — частіше)") + 24;
        DrawPin(canvas, new SKPoint(nx + 8, y + 3), CatColor("shahed"), 20);
        canvas.DrawText("місце пуску і скільки звідти", nx + 22, y, noteP);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ── Шрифт Roboto (як у застосунку) і логотип — з assets/ поруч із програмою, тож картинка
    // однакова на ПК і на сервері; немає файлів — системний шрифт, без логотипа.
    static readonly string AssetDir = Path.Combine(AppContext.BaseDirectory, "assets");
    static SKTypeface Face(string style, SKFontStyleWeight w)
    {
        try { var f = Path.Combine(AssetDir, $"Roboto-{style}.ttf"); if (File.Exists(f) && SKTypeface.FromFile(f) is { } t) return t; } catch { }
        return SKTypeface.FromFamilyName(null, w, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
    }
    static readonly SKTypeface Ui = Face("Regular", SKFontStyleWeight.Normal), UiMed = Face("Medium", SKFontStyleWeight.Medium),
        UiBold = Face("Bold", SKFontStyleWeight.Bold), UiBlack = Face("Black", SKFontStyleWeight.Black);
    static readonly SKBitmap? LogoBmp = LoadLogo();
    static SKBitmap? LoadLogo()
    {
        try { var f = Path.Combine(AssetDir, "logo.png"); return File.Exists(f) ? SKBitmap.Decode(f) : null; } catch { return null; }
    }

    /// Іконка застосунку (прапор, щит, тризуб) із заокругленими кутами.
    static void DrawLogo(SKCanvas c, float x, float y, float size)
    {
        if (LogoBmp is null) return;
        var r = new SKRect(x, y, x + size, y + size);
        using (var sh = new SKPaint { Color = SKColors.Black.WithAlpha(0x70), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * 0.08f) })
            c.DrawRoundRect(new SKRect(r.Left, r.Top + 2, r.Right, r.Bottom + 2), size * 0.22f, size * 0.22f, sh);
        c.Save();
        using (var clip = new SKPath()) { clip.AddRoundRect(r, size * 0.22f, size * 0.22f); c.ClipPath(clip, antialias: true); }
        using (var bp = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true }) c.DrawBitmap(LogoBmp, r, bp);
        c.Restore();
    }

    /// Підпис «WarAlarm» з логотипом у правому нижньому куті.
    static void DrawBrand(SKCanvas c, float right, float bottom, float size)
    {
        using var t = new SKPaint { Typeface = UiBold, Color = new SKColor(0xB4, 0xBE, 0xCC), IsAntialias = true, TextSize = size };
        float tw = t.MeasureText("WarAlarm"), ls = size * 1.35f;
        c.DrawText("WarAlarm", right - tw, bottom - size * 0.12f, t);
        DrawLogo(c, right - tw - 8 - ls, bottom - ls + size * 0.18f, ls);
    }

    /// Силует цілі як іконка: вписаний у квадрат box (ніс угору); «burst» — спалах, «pin» — шпилька.
    static void DrawIcon(SKCanvas c, SKPoint at, string fig, SKColor col, float box)
    {
        switch (fig)
        {
            case "burst": DrawBurst(c, at, box * 0.5f); return;
            case "pin": DrawPin(c, new SKPoint(at.X, at.Y + box * 0.45f), col, box * 0.95f); return;
            default: DrawFig(c, at, 0, fig, col, box / 25f / FigScale(fig), glowStrength: 0.35f); return;
        }
    }

    /// Значок «гарячого» місця: темне коло з кільцем і силуетом шару всередині.
    static void DrawBadge(SKCanvas c, SKPoint at, string fig, SKColor col, float r)
    {
        using (var glow = new SKPaint { Color = col.WithAlpha(0x50), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.45f) })
            c.DrawCircle(at, r + 1.5f, glow);
        using (var fill = new SKPaint { Color = new SKColor(0x0B, 0x10, 0x18, 0xEE), IsAntialias = true }) c.DrawCircle(at, r, fill);
        using (var ring = new SKPaint { Color = col, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.8f, r * 0.15f) })
            c.DrawCircle(at, r, ring);
        DrawFig(c, at, 0, fig, col, r * 1.3f / 25f / FigScale(fig), glowStrength: 0.2f);
    }

    /// Шпилька-крапля місця пуску: вістря на точці, біле ядро, тінь.
    static void DrawPin(SKCanvas c, SKPoint tip, SKColor col, float h)
    {
        float r = h * 0.37f;
        var ctr = new SKPoint(tip.X, tip.Y - h + r);
        using var path = new SKPath();
        path.MoveTo(tip);
        path.CubicTo(tip.X - r * 0.25f, tip.Y - h * 0.3f, ctr.X - r, ctr.Y + r * 0.75f, ctr.X - r, ctr.Y);
        path.ArcTo(new SKRect(ctr.X - r, ctr.Y - r, ctr.X + r, ctr.Y + r), 180, 180, false);
        path.CubicTo(ctr.X + r, ctr.Y + r * 0.75f, tip.X + r * 0.25f, tip.Y - h * 0.3f, tip.X, tip.Y);
        path.Close();
        using (var sh = new SKPaint { Color = SKColors.Black.WithAlpha(0x90), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) })
        { c.Save(); c.Translate(1.5f, 2); c.DrawPath(path, sh); c.Restore(); }
        using (var fill = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, tip.Y - h), tip, new[] { Lerp(col, SKColors.White, 0.35f), Lerp(col, SKColors.Black, 0.2f) }, null, SKShaderTileMode.Clamp),
        })
            c.DrawPath(path, fill);
        using (var o = new SKPaint { Color = new SKColor(0x0A, 0x0F, 0x19, 0xCC), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f }) c.DrawPath(path, o);
        using (var core = new SKPaint { Color = SKColors.White, IsAntialias = true }) c.DrawCircle(ctr, r * 0.42f, core);
    }

    /// Вибух — спалах (зірка) з помаранчевим світінням і червоним ядром.
    static void DrawBurst(SKCanvas c, SKPoint at, float r)
    {
        using var star = new SKPath();
        for (int i = 0; i < 16; i++)
        {
            double ang = i * Math.PI / 8, rr = i % 2 == 0 ? r : r * 0.45;
            var x = (float)(at.X + rr * Math.Cos(ang)); var y = (float)(at.Y + rr * Math.Sin(ang));
            if (i == 0) star.MoveTo(x, y); else star.LineTo(x, y);
        }
        star.Close();
        using var glow = new SKPaint { Color = new SKColor(0xFF, 0x6A, 0x00, 0x66), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.6f) };
        c.DrawCircle(at, r, glow);
        using var f1 = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(at, r, new[] { new SKColor(0xFF, 0xE0, 0x6A), new SKColor(0xFF, 0x8A, 0x1C) }, null, SKShaderTileMode.Clamp) };
        c.DrawPath(star, f1);
        using var f2 = new SKPaint { Color = new SKColor(0xFF, 0x2D, 0x1A), IsAntialias = true };
        c.DrawCircle(at, r * 0.3f, f2);
    }

    /// Пігулка легенди: іконка у кольорі шару, підпис і число тим самим кольором. Повертає ширину.
    static float Chip(SKCanvas c, float x, float yMid, string? fig, SKColor col, string label, string? value, float size = 17, bool measure = false)
    {
        using var lt = new SKPaint { Typeface = UiMed, TextSize = size, IsAntialias = true, Color = new SKColor(0xDC, 0xE2, 0xEC) };
        using var vt = new SKPaint { Typeface = UiBold, TextSize = size, IsAntialias = true, Color = Lerp(col, SKColors.White, 0.12f) };
        float h = size + 15, icon = fig is null ? 0 : h - 6;
        float w = 9 + (fig is null ? 3 : icon + 7) + lt.MeasureText(label) + (value is null ? 0 : 6 + vt.MeasureText(value)) + 13;
        if (measure) return w;
        var r = new SKRect(x, yMid - h / 2, x + w, yMid + h / 2);
        using (var bg = new SKPaint { Color = Lerp(new SKColor(0x10, 0x15, 0x1D), col, 0.14f).WithAlpha(0xF0), IsAntialias = true }) c.DrawRoundRect(r, h / 2, h / 2, bg);
        using (var bd = new SKPaint { Color = col.WithAlpha(0x99), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f }) c.DrawRoundRect(r, h / 2, h / 2, bd);
        float tx = x + 9;
        if (fig is not null) { DrawIcon(c, new SKPoint(tx + icon / 2, yMid), fig, col, icon); tx += icon + 7; } else tx += 3;
        float bl = yMid + size * 0.36f;
        c.DrawText(label, tx, bl, lt);
        if (value is not null) c.DrawText(value, tx + lt.MeasureText(label) + 6, bl, vt);
        return w;
    }

    // ── Палітра живої карти застосунку (app_colors.dart, темна тема; ukraine_geo.dart) ──
    static readonly SKColor Bg = new(0x0A, 0x0D, 0x12), MapLand = new(0x16, 0x1D, 0x27),
        Primary = new(0x4C, 0x93, 0xFF), TextPrimary = new(0xEC, 0xF0, 0xF5), HomeBlue = new(0x5A, 0xA9, 0xFF),
        AlarmRed = new(0xC0, 0x30, 0x3F), AlarmYellow = new(0xF2, 0xB7, 0x05);

    // kThreatColors застосунку: колір цілі за її силуетом.
    static SKColor CatColor(string cat) => cat switch
    {
        "balist" => new SKColor(0xFF, 0x3B, 0x30),
        "missile" => new SKColor(0xFF, 0x7A, 0x3C),
        "banderol" => new SKColor(0xFF, 0x5F, 0xA2),
        "shahed" => new SKColor(0xFF, 0xE1, 0x4D),
        "shahed_jet" => new SKColor(0xFF, 0x91, 0x24),
        "kab" => new SKColor(0xC7, 0x7D, 0xFF),
        "avia" => new SKColor(0x7D, 0xD3, 0xFC),
        "fpv" => new SKColor(0x2D, 0xD4, 0xBF),
        "hyper" => new SKColor(0x3F, 0xD0, 0xFF),
        _ => new SKColor(0x9A, 0xA6, 0xB2),
    };

    // Силует цілі — як iconKey() застосунку: реактивний шахед і «бандероль» мають свої.
    static string CatOf(Threat t)
    {
        var label = ThreatTracker.Label(t);
        return t.Kind switch
        {
            ThreatKind.Ballistic => "balist",
            ThreatKind.Missile => label == "Бандероль" ? "banderol" : "missile",
            ThreatKind.Kab => "kab",
            ThreatKind.Aviation => "avia",
            ThreatKind.Drone => (label + " " + t.Ammo).Contains("реакт", StringComparison.OrdinalIgnoreCase) ? "shahed_jet" : "shahed",
            ThreatKind.Fpv => "fpv",
            _ => "other",
        };
    }

    static string KindLabel(string cat) => cat switch
    {
        "balist" => "балістика", "missile" => "ракети", "banderol" => "бандеролі", "kab" => "КАБ",
        "avia" => "авіація / КАБ-зона", "fpv" => "ФПВ", "shahed" => "шахеди", "shahed_jet" => "реакт. шахеди",
        _ => "цілі",
    };

    static float FigScale(string cat) => cat switch
    {
        "balist" => 1.12f, "missile" => 1.0f, "banderol" => 1.0f, "avia" => 1.0f, "fpv" => 0.85f,
        "kab" => 0.98f, "shahed" => 0.9f, "shahed_jet" => 0.92f, "hyper" => 1.08f, _ => 0.8f,
    };

    // Силуети цілей (ніс угору, 0° = Пн) — ТІ САМІ, що figParts() у застосунку (ukraine_geo.dart).
    // s: ' ' — корпус (колір типу з градієнтом), 'd' — темна деталь, 'l' — світлий акцент.
    static readonly Dictionary<string, (string d, char s)[]> Figs = new()
    {
        ["shahed"] = new[] // Shahed-136 / Герань-2: дельта з зламом, кілі на кінцях крил, гвинт
        {
            ("M0,-10 L1.2,-8.6 L1.4,-4 L9.6,4.6 L9.6,6.2 L1.4,5.2 L1.2,7.2 L-1.2,7.2 L-1.4,5.2 L-9.6,6.2 L-9.6,4.6 L-1.4,-4 L-1.2,-8.6 Z", ' '),
            ("M-0.45,-8.8 L0.45,-8.8 L0.45,6.8 L-0.45,6.8 Z", 'd'),
            ("M8.8,3.4 L10.3,3.4 L10.3,6.9 L8.8,6.9 Z", 'd'),
            ("M-10.3,3.4 L-8.8,3.4 L-8.8,6.9 L-10.3,6.9 Z", 'd'),
            ("M1.4,-3.4 L6.2,1.4 L5.6,1.9 L1.4,-2.2 Z", 'l'),
            ("M-1.4,-3.4 L-6.2,1.4 L-5.6,1.9 L-1.4,-2.2 Z", 'l'),
            ("M-3.2,7.6 L3.2,7.6 L3.2,8.3 L-3.2,8.3 Z", 'l'),
        },
        ["shahed_jet"] = new[] // Герань-3 (реактивний): стрілоподібніший, двигун зверху, полум'я
        {
            ("M0,-10.8 L1.3,-8.4 L1.5,-3.2 L8.6,5.2 L8.6,6.6 L1.5,5.4 L1.3,7.4 L-1.3,7.4 L-1.5,5.4 L-8.6,6.6 L-8.6,5.2 L-1.5,-3.2 L-1.3,-8.4 Z", ' '),
            ("M7.9,4 L9.3,4 L9.3,7.2 L7.9,7.2 Z", 'd'),
            ("M-9.3,4 L-7.9,4 L-7.9,7.2 L-9.3,7.2 Z", 'd'),
            ("M-1.7,1.6 C-1.7,0.6 1.7,0.6 1.7,1.6 L1.7,8.4 L-1.7,8.4 Z", 'd'),
            ("M-1.1,8.4 L1.1,8.4 L0,11.4 Z", 'l'),
            ("M1.5,-2.6 L5.8,2.4 L5.2,2.9 L1.5,-1.4 Z", 'l'),
            ("M-1.5,-2.6 L-5.8,2.4 L-5.2,2.9 L-1.5,-1.4 Z", 'l'),
        },
        ["missile"] = new[] // крилата (Х-101 / Калібр): довгий корпус, крила, хрестоподібний хвіст
        {
            ("M0,-11.5 C1.1,-9.8 1.2,-7 1.2,8 L0.9,9.4 L-0.9,9.4 L-1.2,8 C-1.2,-7 -1.1,-9.8 0,-11.5 Z", ' '),
            ("M-1.2,0.4 L-8,2.4 L-8,3.5 L-1.2,2.5 L1.2,2.5 L8,3.5 L8,2.4 L1.2,0.4 Z", ' '),
            ("M-1.1,6.6 L-3.9,8.4 L-3.9,9.2 L-1.1,8.2 L1.1,8.2 L3.9,9.2 L3.9,8.4 L1.1,6.6 Z", ' '),
            ("M0,-11.5 C0.7,-10.2 0.75,-9 0,-8.5 C-0.75,-9 -0.7,-10.2 0,-11.5 Z", 'd'),
            ("M-0.75,4.4 L0.75,4.4 L0.75,7.4 L-0.75,7.4 Z", 'd'),
            ("M-0.22,-8 L0.22,-8 L0.22,3.6 L-0.22,3.6 Z", 'l'),
        },
        ["banderol"] = new[] // «бандероль»: тонка крилата, стрілоподібні крила й хвіст
        {
            ("M0,-12 C1,-10 1,-7 1,8 C1,9 -1,9 -1,8 C-1,-7 -1,-10 0,-12 Z", ' '),
            ("M-1,-1 L-7.5,4.2 L-7.5,5.2 L-1,2.4 L1,2.4 L7.5,5.2 L7.5,4.2 L1,-1 Z", ' '),
            ("M-1,6.2 L-3.6,8.8 L-3.6,9.6 L-1,8.3 L1,8.3 L3.6,9.6 L3.6,8.8 L1,6.2 Z", ' '),
            ("M0,-12 C0.6,-10.8 0.6,-9.8 0,-9.3 C-0.6,-9.8 -0.6,-10.8 0,-12 Z", 'd'),
            ("M-0.2,-8.6 L0.2,-8.6 L0.2,2 L-0.2,2 Z", 'l'),
            ("M-0.6,8.4 L0.6,8.4 L0.5,9.6 L-0.5,9.6 Z", 'l'),
        },
        ["balist"] = new[] // балістика (Іскандер): тонка, гострий темний ніс, стабілізатори, полум'я
        {
            ("M0,-12.5 C0.9,-10 1.15,-6 1.15,7.5 L1.15,9.6 L-1.15,9.6 L-1.15,7.5 C-1.15,-6 -0.9,-10 0,-12.5 Z", ' '),
            ("M1.15,6.2 L3.1,9.9 L1.15,9.6 Z", ' '),
            ("M-1.15,6.2 L-3.1,9.9 L-1.15,9.6 Z", ' '),
            ("M0,-12.5 C0.7,-10.6 0.75,-9.2 0,-8.6 C-0.75,-9.2 -0.7,-10.6 0,-12.5 Z", 'd'),
            ("M-1.15,-3 L1.15,-3 L1.15,-2.3 L-1.15,-2.3 Z", 'd'),
            ("M-0.9,9.6 L0.9,9.6 L0,12.8 Z", 'l'),
        },
        ["hyper"] = new[] // Циркон: гіперзвукова, клиноподібний ніс, повітрозабірник, короткі стабілізатори
        {
            ("M0,-12.5 L1.6,-6 L1.9,8.4 L1.4,10 L-1.4,10 L-1.9,8.4 L-1.6,-6 Z", ' '),
            ("M1.9,5.2 L4.6,9.4 L4.6,10.3 L1.9,9.3 Z", ' '),
            ("M-1.9,5.2 L-4.6,9.4 L-4.6,10.3 L-1.9,9.3 Z", ' '),
            ("M0,-12.5 L0.8,-9.3 L-0.8,-9.3 Z", 'd'),
            ("M-1.2,-3.2 L1.2,-3.2 L1.2,-1.4 L-1.2,-1.4 Z", 'd'),
            ("M-0.25,-8.4 L0.25,-8.4 L0.25,4 L-0.25,4 Z", 'l'),
            ("M-1.1,10 L1.1,10 L0,13.4 Z", 'l'),
        },
        ["kab"] = new[] // КАБ (ФАБ-500 + УМПК): товста бомба, розкриті крила, Х-хвіст
        {
            ("M0,-7.2 C2.3,-6.6 2.4,-2 2,3 L1.4,5.6 L-1.4,5.6 L-2,3 C-2.4,-2 -2.3,-6.6 0,-7.2 Z", ' '),
            ("M-0.6,-2.2 L-9.5,-0.6 L-9.5,0.4 L-0.6,-0.6 L0.6,-0.6 L9.5,0.4 L9.5,-0.6 L0.6,-2.2 Z", ' '),
            ("M-1.3,5 L-3.8,7.6 L-2.8,8.2 L0,6.3 L2.8,8.2 L3.8,7.6 L1.3,5 Z", ' '),
            ("M0,-7.2 C1.1,-6.4 1.1,-5.2 0,-4.8 C-1.1,-5.2 -1.1,-6.4 0,-7.2 Z", 'd'),
            ("M-2.05,0.8 L2.05,0.8 L2,1.5 L-2,1.5 Z", 'l'),
        },
        ["avia"] = new[] // винищувач-бомбардувальник (Су-34/35): довгий ніс, два кілі
        {
            ("M0,-11 L0.9,-8 L1.3,-4.5 L2.6,-2.5 L9.5,2.8 L9.5,4 L3,3.4 L2.6,6 L5.4,8.6 L5.4,9.4 L1.6,8.6 L1.2,9.6 L-1.2,9.6 L-1.6,8.6 L-5.4,9.4 L-5.4,8.6 L-2.6,6 L-3,3.4 L-9.5,4 L-9.5,2.8 L-2.6,-2.5 L-1.3,-4.5 L-0.9,-8 Z", ' '),
            ("M1.6,4.4 L2.3,4.4 L2.6,7.6 L1.9,7.6 Z", 'd'),
            ("M-2.3,4.4 L-1.6,4.4 L-1.9,7.6 L-2.6,7.6 Z", 'd'),
            ("M-1.2,7.8 L1.2,7.8 L1.2,9.6 L-1.2,9.6 Z", 'd'),
            ("M0,-7.4 C0.7,-7.2 0.75,-5.4 0,-4.8 C-0.75,-5.4 -0.7,-7.2 0,-7.4 Z", 'l'),
        },
        ["fpv"] = new[] // ФПВ / квадрокоптер згори: рама Х, чотири гвинти
        {
            ("M-7,-6 L-6,-7 L7,6 L6,7 Z", ' '),
            ("M6,-7 L7,-6 L-6,7 L-7,6 Z", ' '),
            ("M-2,-2.6 L2,-2.6 L2,2.6 L-2,2.6 Z", ' '),
            ("M8.9,-5.5 L7.5,-4.1 L5.5,-4.1 L4.1,-5.5 L4.1,-7.5 L5.5,-8.9 L7.5,-8.9 L8.9,-7.5 Z", 'l'),
            ("M-4.1,-5.5 L-5.5,-4.1 L-7.5,-4.1 L-8.9,-5.5 L-8.9,-7.5 L-7.5,-8.9 L-5.5,-8.9 L-4.1,-7.5 Z", 'l'),
            ("M8.9,7.5 L7.5,8.9 L5.5,8.9 L4.1,7.5 L4.1,5.5 L5.5,4.1 L7.5,4.1 L8.9,5.5 Z", 'l'),
            ("M-4.1,7.5 L-5.5,8.9 L-7.5,8.9 L-8.9,7.5 L-8.9,5.5 L-7.5,4.1 L-5.5,4.1 L-4.1,5.5 Z", 'l'),
            ("M0,-4.6 L1.4,-2.6 L-1.4,-2.6 Z", 'd'),
        },
        ["other"] = new[] // невідома «ціль»: ромб-маркер з темним ядром
        {
            ("M0,-8.5 L7,0 L0,8.5 L-7,0 Z", ' '),
            ("M0,-4.2 L3.5,0 L0,4.2 L-3.5,0 Z", 'd'),
            ("M0,-8.5 L1.4,-6.8 L-1.4,-6.8 Z", 'l'),
        },
    };

    static readonly Dictionary<string, (SKPath path, char s)[]> FigPaths =
        Figs.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => (SKPath.ParseSvgPathData(p.d), p.s)).ToArray());

    static SKColor Lerp(SKColor a, SKColor b, float t) => new(
        (byte)(a.Red + (b.Red - a.Red) * t), (byte)(a.Green + (b.Green - a.Green) * t),
        (byte)(a.Blue + (b.Blue - a.Blue) * t), a.Alpha);

    static SKPaint Dashed(SKColor c, float width) => new()
    {
        Color = c, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round,
        PathEffect = SKPathEffect.CreateDash(new[] { 7f, 5f }, 0),
    };

    /// Ціль — як _drawIcon у застосунку: м'яке світіння, темний контур (видно на будь-якій
    /// області), корпус з градієнтом «світлий ніс → колір → темніше», деталі зверху.
    static void DrawFig(SKCanvas canvas, SKPoint at, double headingDeg, string cat, SKColor color, float ppu,
        SKColor? glow = null, float glowStrength = 0.55f)
    {
        var parts = FigPaths.TryGetValue(cat, out var f) ? f : FigPaths["other"];
        var k = FigScale(cat) * ppu;
        canvas.Save();
        canvas.Translate(at.X, at.Y);
        canvas.RotateDegrees((float)headingDeg);
        canvas.Scale(k);
        using var halo = new SKPaint { Color = (glow ?? color).WithAlpha((byte)(255 * 0.75f * glowStrength)), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f / k) };
        using var outline = new SKPaint { Color = new SKColor(0x0A, 0x0F, 0x19, 0xE6), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f / k, StrokeJoin = SKStrokeJoin.Round };
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, -12), new SKPoint(0, 11),
                new[] { Lerp(color, SKColors.White, 0.45f), color, Lerp(color, SKColors.Black, 0.15f) },
                new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp),
        };
        using var dark = new SKPaint { Color = new SKColor(0x0C, 0x10, 0x1A, 0x80), IsAntialias = true };
        using var light = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xB8), IsAntialias = true };
        foreach (var (p, s) in parts) if (s == ' ' && p != null) canvas.DrawPath(p, halo);
        foreach (var (p, s) in parts) if (s == ' ' && p != null) canvas.DrawPath(p, outline);
        foreach (var (p, s) in parts)
            if (p != null) canvas.DrawPath(p, s == 'd' ? dark : s == 'l' ? light : fill);
        canvas.Restore();
    }

    // Нормалізація апострофа (фід дає U+0027, геометрія — U+2019) для збігу назв районів.
    static string Napo(string s) => s.Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'');

    static List<(string name, List<List<double[]>> rings)> ParseOblasts() => ParsePolys(MapOblasts.Json);
    static List<(string name, List<List<double[]>> rings)> ParseRaions() => ParsePolys(MapRaions.Json);

    // Полігони з JSON у форматі [{"n":назва,"r":[[[lon,lat]...]]}]: (назва, список кілець).
    static List<(string name, List<List<double[]>> rings)> ParsePolys(string json)
    {
        var outp = new List<(string, List<List<double[]>>)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                var name = o.TryGetProperty("n", out var n) ? n.GetString() ?? "" : "";
                var rings = new List<List<double[]>>();
                foreach (var r in o.GetProperty("r").EnumerateArray())
                {
                    var ring = new List<double[]>();
                    foreach (var p in r.EnumerateArray())
                        ring.Add(new[] { p[0].GetDouble(), p[1].GetDouble() });
                    if (ring.Count >= 3) rings.Add(ring);
                }
                if (rings.Count > 0) outp.Add((name, rings));
            }
        }
        catch { /* немає полігонів — карта без областей */ }
        return outp;
    }
}
