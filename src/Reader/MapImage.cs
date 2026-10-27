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
                if (t.Pos is { } hpos && !t.Launch)
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
        using var ringText = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF, 0xB0), IsAntialias = true, TextSize = 12 };
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
        bool IsFlight(Threat t) => !t.Launch && t.Kind is ThreatKind.Ballistic or ThreatKind.Kab && t.From is not null && t.To is not null;
        var flights = threats.Where(t => t.Pos is not null && IsFlight(t)).ToList();
        var launchOnly = threats.Where(t => t.Launch && t.From is not null).ToList();
        var drawn = new List<(Threat t, int n)>();
        foreach (var t in threats)
        {
            if (t.Pos is not { } tp || IsFlight(t) || t.Launch) continue;
            int i = drawn.FindIndex(d => CatOf(d.t) == CatOf(t) && ThreatGeo.Km(d.t.Pos!.Value, tp) < 16);
            if (i >= 0) drawn[i] = (drawn[i].t, drawn[i].n + t.Amount); else drawn.Add((t, t.Amount));
        }
        var zones = AviationFeed.MapZones();
        var groups = LaunchGroups.Active;
        float Km2Px(double km) => (float)(km * pxPerKm);

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
        targetPts.AddRange(drawn.Select(d => P(d.t.Pos!.Value.lon, d.t.Pos!.Value.lat)));
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
            var c = CatColor(CatOf(t));
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
            var c = CatColor(CatOf(t));
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
            var c = CatColor(CatOf(t));
            for (int i = 1; i < track.Count; i++)
            {
                byte a = (byte)(0x30 + 0x70 * i / (track.Count - 1));
                using var tp = new SKPaint { Color = c.WithAlpha(a), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, StrokeCap = SKStrokeCap.Round };
                canvas.DrawLine(P(track[i - 1].lon, track[i - 1].lat), P(track[i].lon, track[i].lat), tp);
            }
        }

        // Курс (лише справжній — ціль бачили в русі): пунктир на ~10 хв польоту, ≤55 км.
        foreach (var (t, _) in drawn)
        {
            if (!t.HeadingKnown || t.Heading is not { } hd || t.Down) continue;
            double lead = Math.Min(55, ThreatGeo.SpeedKmh(t.Kind, t.Ammo) / 60 * 10);
            var from = t.Pos!.Value;
            var to = (lat: from.lat + lead * Math.Cos(hd * Math.PI / 180) / 111,
                      lon: from.lon + lead * Math.Sin(hd * Math.PI / 180) / (111 * Math.Cos(from.lat * Math.PI / 180)));
            if (!OblastGeo.InUkraine(to.lat, to.lon) && OblastGeo.InUkraine(from.lat, from.lon)) continue; // не «в Румунію»
            var c = CatColor(CatOf(t));
            var ps = P(to.lon, to.lat);
            using (var dash = Dashed(c.WithAlpha(0x99), 1.5f)) canvas.DrawLine(P(from.lon, from.lat), ps, dash);
            using (var dot = new SKPaint { Color = c.WithAlpha(0xCC), IsAntialias = true }) canvas.DrawCircle(ps, 3.2f, dot);
        }

        // ── Міста: крапки, потім цілі, а підписи — НАЙВИЩЕ (з обводкою) і з того боку
        // крапки, де немає цілі: раніше силует закривав «Харків» і «Запоріжжя».
        using var cityDot = new SKPaint { Color = Primary, IsAntialias = true };
        using var cityDotRim = new SKPaint { Color = MapLand, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        using var cityText = new SKPaint { Color = TextPrimary, IsAntialias = true, TextSize = 15 };
        using var cityHalo = new SKPaint { Color = MapLand, IsAntialias = true, TextSize = 15, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
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
            DrawFig(canvas, P(t.From!.Value.lon, t.From!.Value.lat), t.Heading ?? 0, CatOf(t), CatColor(CatOf(t)), Ppu);
        foreach (var t in flights)
        {
            var (at, hdg, _, landed) = FlightNow(t);
            if (!landed) DrawFig(canvas, at, hdg, CatOf(t), CatColor(CatOf(t)), Ppu, glow: new SKColor(0xFF, 0x45, 0x3A), glowStrength: 0.9f);
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
            var cat = CatOf(t);
            var pt = P(t.Pos!.Value.lon, t.Pos!.Value.lat);
            bool faded = t.Down || now - t.LastUpdate > TimeSpan.FromMinutes(6);
            bool near = t.Stage is TrackStage.Imminent or TrackStage.Overhead or TrackStage.Impact;
            if (faded) canvas.SaveLayer(fadeLayer);
            DrawFig(canvas, pt, t.Heading ?? 0, cat, CatColor(cat), Ppu,
                glow: near ? new SKColor(0xFF, 0x45, 0x3A) : null, glowStrength: near ? 0.9f : 0.55f);
            if (faded) canvas.Restore();
            if (t.Down) labels.Add((t.AltNote is { Length: > 0 } an ? an : "падає", new SKPoint(pt.X, pt.Y + 32), new SKColor(0xFF, 0x6B, 0x5E), 13, 'c'));
        }

        // ×N: ціль або група пуску з кількома бортами.
        var pillsToDraw = drawn.Where(d => d.n > 1).Select(d => (pt: P(d.t.Pos!.Value.lon, d.t.Pos!.Value.lat), n: d.n, c: CatColor(CatOf(d.t))))
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
            var at = new[] { right, left, below, below2 }.FirstOrDefault(r => !HitsTarget(r), right);
            canvas.DrawText(nm, at.Left, at.Bottom - 4, cityHalo); // темна обводка для читабельності
            canvas.DrawText(nm, at.Left, at.Bottom - 4, cityText);
        }

        // Підписи цілей («пуск: Бєлгород», «2 хв», «прогноз», «загроза КАБ») — поверх усього.
        foreach (var (text, at, color, size, align) in labels)
        {
            using var halo = new SKPaint { Color = MapLand, IsAntialias = true, TextSize = size, FakeBoldText = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round };
            using var fill = new SKPaint { Color = color, IsAntialias = true, TextSize = size, FakeBoldText = true };
            float x0 = align switch { 'c' => at.X - fill.MeasureText(text) / 2, 'r' => at.X - fill.MeasureText(text), _ => at.X };
            canvas.DrawText(text, x0, at.Y, halo);
            canvas.DrawText(text, x0, at.Y, fill);
        }

        // ×N — у темній «пігулці», щоб читалось на будь-якому фоні.
        using var xnFill = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 16, FakeBoldText = true };
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
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 30, FakeBoldText = true };
        using var sub = new SKPaint { Color = subColor, IsAntialias = true, TextSize = 21, FakeBoldText = true };
        using var muted = new SKPaint { Color = new SKColor(0x87, 0x92, 0xA6), IsAntialias = true, TextSize = 15 };
        using var timePaint = new SKPaint { Color = new SKColor(0xC7, 0xD2, 0xE3), IsAntialias = true, TextSize = 22, FakeBoldText = true };
        canvas.DrawText(title, pad + 2, 36, titlePaint);
        canvas.DrawText(subtitle, pad + 2, 64, sub);
        var hhmm = DateTime.Now.ToString("HH:mm");
        canvas.DrawText(hhmm, W - pad - 4 - timePaint.MeasureText(hhmm), 36, timePaint);
        var date = DateTime.Now.ToString("dd.MM.yyyy");
        canvas.DrawText(date, W - pad - 4 - muted.MeasureText(date), 58, muted);

        // ── Картка-легенда в лівому нижньому куті (Молдова/Румунія — там порожньо) ──
        // Лише ті типи, що зараз є на карті (з польотами, пусками, групами й авіацією), з кількістю.
        var kinds = drawn.Select(d => (cat: CatOf(d.t), n: d.n))
            .Concat(flights.Concat(launchOnly).Select(t => (cat: CatOf(t), n: t.Amount)))
            .Concat(groups.Select(g => (cat: g.Reactive ? "shahed_jet" : "shahed", n: g.Left)))
            .Concat(zones.Select(z => (cat: "avia", n: 1)))
            .GroupBy(k => k.cat).Select(g => (cat: g.Key, n: g.Sum(k => k.n)))
            .OrderByDescending(k => k.n).ToList();
        int total = kinds.Where(k => k.cat != "avia").Sum(k => k.n);
        using var cardTitle = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 17, FakeBoldText = true };
        using var cardText = new SKPaint { Color = new SKColor(0xC7, 0xD2, 0xE3), IsAntialias = true, TextSize = 15 };
        int kindRows = (kinds.Count + 1) / 2;
        float cw = 330, chh = 64 + kindRows * 30 + (showHome ? 24 : 0);
        var card = new SKRect(pad + 4, H - pad - 4 - chh, pad + 4 + cw, H - pad - 4);
        using (var cb = new SKPaint { Color = new SKColor(0x11, 0x16, 0x1D, 0xEE), IsAntialias = true })
            canvas.DrawRoundRect(card, 12, 12, cb);
        using (var cbs = new SKPaint { Color = Primary.WithAlpha(0x40), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
            canvas.DrawRoundRect(card, 12, 12, cbs);
        float x = card.Left + 14, y = card.Top + 26;
        if (total > 0) canvas.DrawText($"На карті цілей: {total}", x, y, cardTitle);
        else
        {
            using var calm = new SKPaint { Color = new SKColor(0x2F, 0xD1, 0x7C), IsAntialias = true, TextSize = 17, FakeBoldText = true };
            canvas.DrawText("Цілей на карті немає", x, y, calm);
        }
        y += 14;
        for (int i = 0; i < kinds.Count; i++)
        {
            float kx = x + (i % 2) * 158, ky = y + 22 + (i / 2) * 30;
            DrawFig(canvas, new SKPoint(kx + 10, ky - 5), 0, kinds[i].cat, CatColor(kinds[i].cat), 1.15f, glowStrength: 0.35f);
            canvas.DrawText(kinds[i].cat == "avia" ? KindLabel("avia") : $"{KindLabel(kinds[i].cat)} {kinds[i].n}", kx + 26, ky, cardText);
        }
        y += kindRows * 30 + 22;
        canvas.DrawRect(x, y - 12, 14, 14, landRed);
        canvas.DrawText($"тривога {reds}", x + 20, y, cardText);
        canvas.DrawRect(x + 140, y - 12, 14, 14, landYellow);
        canvas.DrawText($"часткова {yellows}", x + 160, y, cardText);
        if (showHome)
        {
            y += 24;
            using var hl = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawCircle(x + 7, y - 5, 6, hl);
            canvas.DrawText("твоє місто · кола 50 і 100 км", x + 20, y, cardText);
        }
        using var brand = new SKPaint { Color = new SKColor(0x5A, 0x66, 0x7A), IsAntialias = true, TextSize = 15, FakeBoldText = true };
        canvas.DrawText("WarAlarm", W - pad - 4 - brand.MeasureText("WarAlarm"), H - pad - 4, brand);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ── 🌙 Карта «Рух повітряних цілей» за ніч (як у ППО РАДАР / monitorwar) ─────
    // Ширша рамка: + захід Росії, щоб було видно місця пуску (Брянськ, Курськ, Орел…).
    // Шахеди (звичайні й реактивні) — жовті, крилаті — помаранчеві, балістика — червона,
    // вибухи — спалахи, місця пуску — шпильки з підписом.
    static readonly SKColor CDrone = new(0xF5, 0xC5, 0x18), CMissile = new(0xFF, 0x7A, 0x1A), CBallistic = new(0xFF, 0x2A, 0x2A);

    public static byte[] RenderNight(NightSummary.Data d)
    {
        const double LonA = 21.9, LonB = 42.3, LatTop = 55.2, LatBot = 44.1;
        const int W = 1400, pad = 10;
        double midLat = (LatTop + LatBot) / 2;
        double aspect = ((LonB - LonA) / (LatTop - LatBot)) * Math.Cos(midLat * Math.PI / 180);
        int pw = W - 2 * pad, ph = (int)(pw / aspect), H = ph + 2 * pad;

        using var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(0x15, 0x16, 0x19));
        SKPoint P(double lat, double lon) => new(
            (float)(pad + (lon - LonA) / (LonB - LonA) * pw),
            (float)(pad + (LatTop - lat) / (LatTop - LatBot) * ph));

        // Україна: області.
        using var land = new SKPaint { Color = new SKColor(0x2A, 0x2C, 0x31), IsAntialias = true };
        using var border = new SKPaint { Color = new SKColor(0x55, 0x58, 0x60), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        foreach (var (_, rings) in ParseOblasts())
        {
            using var path = new SKPath();
            foreach (var ring in rings)
            {
                if (ring.Count < 3) continue;
                path.MoveTo(P(ring[0][1], ring[0][0]));
                for (int i = 1; i < ring.Count; i++) path.LineTo(P(ring[i][1], ring[i][0]));
                path.Close();
            }
            canvas.DrawPath(path, land);
            canvas.DrawPath(path, border);
        }

        // Міста України.
        using var cityDot = new SKPaint { Color = new SKColor(0xB8, 0xBC, 0xC6), IsAntialias = true };
        using var cityText = new SKPaint { Color = new SKColor(0xE6, 0xE8, 0xEE), IsAntialias = true, TextSize = 17, FakeBoldText = true };
        using var cityHalo = new SKPaint { Color = new SKColor(0x15, 0x16, 0x19), IsAntialias = true, TextSize = 17, FakeBoldText = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
        foreach (var (nm, lon, lat) in Cities)
        {
            var cp = P(lat, lon);
            canvas.DrawCircle(cp, 3f, cityDot);
            canvas.DrawText(nm, cp.X + 6, cp.Y - 5, cityHalo);
            canvas.DrawText(nm, cp.X + 6, cp.Y - 5, cityText);
        }

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

        // Траєкторії шахедів і крилатих.
        var rnd = new Random(7);
        var tracks = NightSummary.BuildTracks(d);
        foreach (var t in tracks.OrderBy(t => t.Kind == ThreatKind.Missile)) // ракети поверх
        {
            var c = t.Kind == ThreatKind.Missile ? CMissile : CDrone;
            var pts = t.Pts.Select(p => P(p.lat, p.lon)).ToList();
            using var path = new SKPath();
            path.MoveTo(pts[0]);
            float bend = (float)(0.10 + rnd.NextDouble() * 0.12) * (rnd.Next(2) == 0 ? 1 : -1);
            for (int i = 1; i < pts.Count; i++) Arc(path, pts[i - 1], pts[i], i == 1 && t.Site != null ? bend : bend * 0.5f);
            using var stroke = new SKPaint { Color = c.WithAlpha(0xC8), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = t.Kind == ThreatKind.Missile ? 2.6f : 1.7f, StrokeCap = SKStrokeCap.Round };
            canvas.DrawPath(path, stroke);
            Arrow(pts[^1], pts.Count >= 2 ? pts[^2] : pts[0], c, t.Kind == ThreatKind.Missile ? 14 : 11);
        }

        // Балістика: червоні дуги від місця пуску до цілі.
        // Кожна ракета — своя лінія; кілька на одну ціль розходяться віялом (різний вигин).
        using var bStroke = new SKPaint { Color = CBallistic.WithAlpha(0xD0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f };
        foreach (var g in d.Ballistics.GroupBy(b => (b.Origin, b.Target)))
        {
            var b0 = g.First();
            var a = P(b0.OLat, b0.OLon); var z = P(b0.TLat, b0.TLon);
            int total = g.Sum(b => b.N);
            for (int i = 0; i < total; i++)
            {
                float bend = total == 1 ? 0.12f : -0.20f + 0.40f * i / (total - 1);
                using var path = new SKPath(); path.MoveTo(a); Arc(path, a, z, bend);
                canvas.DrawPath(path, bStroke);
            }
            Arrow(z, new SKPoint((a.X + z.X) / 2, (a.Y + z.Y) / 2), CBallistic, 15);
            if (total > 1)
            {
                using var nb = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 17, FakeBoldText = true };
                using var nh = new SKPaint { Color = new SKColor(0x15, 0x16, 0x19), IsAntialias = true, TextSize = 17, FakeBoldText = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
                canvas.DrawText($"×{total}", z.X + 10, z.Y + 20, nh);
                canvas.DrawText($"×{total}", z.X + 10, z.Y + 20, nb);
            }
        }

        // Вибухи — спалах (зірка) з червоним ядром.
        void Burst(SKPoint c, float r)
        {
            using var star = new SKPath();
            for (int i = 0; i < 16; i++)
            {
                double ang = i * Math.PI / 8, rr = i % 2 == 0 ? r : r * 0.45;
                var x = (float)(c.X + rr * Math.Cos(ang)); var y = (float)(c.Y + rr * Math.Sin(ang));
                if (i == 0) star.MoveTo(x, y); else star.LineTo(x, y);
            }
            star.Close();
            using var glow = new SKPaint { Color = new SKColor(0xFF, 0x6A, 0x00, 0x66), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, r * 0.6f) };
            canvas.DrawCircle(c, r, glow);
            using var f1 = new SKPaint { Color = new SKColor(0xFF, 0x8A, 0x1C), IsAntialias = true };
            canvas.DrawPath(star, f1);
            using var f2 = new SKPaint { Color = new SKColor(0xFF, 0x2D, 0x1A), IsAntialias = true };
            canvas.DrawCircle(c, r * 0.32f, f2);
        }
        foreach (var g in d.Impacts.GroupBy(i => (Math.Round(i.Lat, 2), Math.Round(i.Lon, 2))))
            Burst(P(g.First().Lat, g.First().Lon), 13 + Math.Min(9, g.Count() * 2));

        // Місця пуску (шпилька + підпис): шахедні хвилі + балістика.
        using var pin = new SKPaint { Color = new SKColor(0xF5, 0xC5, 0x18), IsAntialias = true };
        using var pinText = new SKPaint { Color = new SKColor(0xD8, 0xDA, 0xE0), IsAntialias = true, TextSize = 15 };
        using var pinHalo = new SKPaint { Color = new SKColor(0x15, 0x16, 0x19), IsAntialias = true, TextSize = 15, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
        var used = d.Waves.SelectMany(w => w.Items.Select(i => i.Origin))
                    .Concat(d.Ballistics.Select(b => b.Origin))
                    .Concat(tracks.Where(t => t.Site != null).Select(t => t.Site!)) // звідки реально йдуть лінії
                    .Distinct();
        foreach (var s in used)
        {
            if (LaunchFeed.SiteCoords(s) is not { } sc) continue;
            var sp = P(sc.lat, sc.lon);
            using var stem = new SKPaint { Color = new SKColor(0xD8, 0xDA, 0xE0), StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawLine(sp.X, sp.Y, sp.X, sp.Y - 14, stem);
            canvas.DrawCircle(sp.X, sp.Y - 15, 4, pin);
            canvas.DrawText(s, sp.X + 7, sp.Y - 10, pinHalo);
            canvas.DrawText(s, sp.X + 7, sp.Y - 10, pinText);
        }

        // Підпис (лівий низ, над Румунією/Чорним морем).
        float x0 = pad + 24, yb = H - 210;
        using var small = new SKPaint { Color = new SKColor(0xC8, 0xCB, 0xD2), IsAntialias = true, TextSize = 19 };
        using var big = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 38, FakeBoldText = true };
        using var stat = new SKPaint { Color = new SKColor(0xF5, 0xC5, 0x18), IsAntialias = true, TextSize = 19, FakeBoldText = true };
        canvas.DrawText($"За період {NightSummary.Period(d)}", x0, yb, small);
        canvas.DrawText("Рух повітряних цілей", x0, yb + 44, big);
        canvas.DrawText("над територією України", x0, yb + 86, big);
        var parts = new List<string>();
        if (d.Official is { } o)
        {   // офіційні цифри ПС ЗСУ
            if (o.Missiles > 0) parts.Add($"ракет {o.Missiles}");
            if (o.Drones > 0) parts.Add($"БпЛА {o.Drones}" + (o.Reactive > 0 ? $" ({o.Reactive} реакт.)" : ""));
            if (o.Down > 0) parts.Add($"збито {o.Down}");
            if (o.Hits > 0) parts.Add($"влучання {o.Hits}");
        }
        else
        {
            if (d.Launched > 0) parts.Add($"запущено ~{d.Launched}");
            if (d.MaxAir > 0) parts.Add($"макс. у повітрі ~{d.MaxAir}");
            if (d.Ballistics.Count > 0) parts.Add($"балістика {d.Ballistics.Sum(b => b.N)}");
            if (d.Impacts.Count > 0) parts.Add($"вибухи {d.Impacts.Count}");
        }
        canvas.DrawText(parts.Count > 0 ? string.Join(" · ", parts) : "Ніч була відносно спокійною", x0, yb + 120, stat);

        // Легенда.
        float ly = yb + 160;
        void LegLine(float x, SKColor c, string label)
        {
            using var lp = new SKPaint { Color = c, StrokeWidth = 4, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            canvas.DrawLine(x, ly - 6, x + 26, ly - 6, lp);
            canvas.DrawText(label, x + 34, ly, small);
        }
        LegLine(x0, CDrone, "шахеди");
        LegLine(x0 + 150, CMissile, "крилаті ракети");
        LegLine(x0 + 350, CBallistic, "балістика");
        Burst(new SKPoint(x0 + 520, ly - 7), 10); canvas.DrawText("вибухи", x0 + 536, ly, small);
        using var brand = new SKPaint { Color = new SKColor(0x9A, 0x9E, 0xA8), IsAntialias = true, TextSize = 22, FakeBoldText = true };
        canvas.DrawText("WarAlarm", W - 150, H - 22, brand);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
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
        "kab" => 0.98f, "shahed" => 0.9f, "shahed_jet" => 0.92f, _ => 0.8f,
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
