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
    /// /map → місто + ймовірність). [showHome] малює маркер дому лише в особистому.
    public static byte[] Render(
        (double lat, double lon) home,
        IReadOnlyList<Threat> threats,
        string title,
        string subtitle,
        Dictionary<string, string>? oblastLevels = null,
        bool showHome = true)
    {
        const int W = 1000, padTop = 96, pad = 16;
        double midLat = (Lat1 + (Lat1 - LatSpan)) / 2;
        double aspect = (LonSpan / LatSpan) * Math.Cos(midLat * Math.PI / 180);
        int pw = W - 2 * pad;
        int ph = (int)(pw / aspect);
        int H = ph + padTop + pad;

        using var bmp = new SKBitmap(W, H);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(0x0A, 0x0E, 0x17));

        SKPoint P(double lon, double lat) => new(
            (float)(pad + (lon - Lon0) / LonSpan * pw),
            (float)(padTop + (Lat1 - lat) / LatSpan * ph));

        // ── Області: колір за ОФІЦІЙНОЮ тривогою (red/yellow), інакше суша ──────
        var oblasts = ParseOblasts();
        int reds = 0, yellows = 0;
        using var land = new SKPaint { Color = new SKColor(0x14, 0x1E, 0x33), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var landRed = new SKPaint { Color = new SKColor(0xE2, 0x3A, 0x34, 0x99), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var landYellow = new SKPaint { Color = new SKColor(0xFF, 0xC4, 0x00, 0x7A), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var border = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF, 0x66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f };

        foreach (var (name, rings) in oblasts)
        {
            var level = oblastLevels != null && oblastLevels.TryGetValue(name, out var lv) ? lv : "";
            if (level == "red") reds++; else if (level == "yellow") yellows++;
            var fill = level == "red" ? landRed : level == "yellow" ? landYellow : land;
            using var path = new SKPath();
            foreach (var ring in rings)
            {
                if (ring.Count < 3) continue;
                path.MoveTo(P(ring[0][0], ring[0][1]));
                for (int i = 1; i < ring.Count; i++) path.LineTo(P(ring[i][0], ring[i][1]));
                path.Close();
            }
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, border);
        }

        // ── Міста (крапка + підпис, як у застосунку) ───────────────────────────
        using var cityDot = new SKPaint { Color = new SKColor(0xAF, 0xBD, 0xD4), IsAntialias = true };
        using var cityText = new SKPaint { Color = new SKColor(0xC7, 0xD2, 0xE3), IsAntialias = true, TextSize = 15 };
        using var cityHalo = new SKPaint { Color = new SKColor(0x0A, 0x0E, 0x17), IsAntialias = true, TextSize = 15, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
        foreach (var (nm, lon, lat) in Cities)
        {
            var cp = P(lon, lat);
            canvas.DrawCircle(cp, 2.6f, cityDot);
            canvas.DrawText(nm, cp.X + 5, cp.Y - 4, cityHalo); // темна обводка для читабельності
            canvas.DrawText(nm, cp.X + 5, cp.Y - 4, cityText);
        }

        // ── Цілі: детальні силуети (як у застосунку), повернуті за курсом ──────
        using var xnFill = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 20, FakeBoldText = true };
        using var xnHalo = new SKPaint { Color = new SKColor(0x0A, 0x0E, 0x17), IsAntialias = true, TextSize = 20, FakeBoldText = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
        foreach (var t in threats)
        {
            if (t.Pos is not { } p) continue;
            var pt = P(p.lon, p.lat);
            var c = KindColor(t.Kind);
            using (var glow = new SKPaint { Color = c.WithAlpha(0x44), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8) })
                canvas.DrawCircle(pt, 15, glow);
            DrawFig(canvas, pt, t.Heading ?? 0, CatOf(t.Kind), c, 2.4f);
            if (t.Amount > 1)
            {
                canvas.DrawText($"×{t.Amount}", pt.X + 15, pt.Y - 10, xnHalo);
                canvas.DrawText($"×{t.Amount}", pt.X + 15, pt.Y - 10, xnFill);
            }
        }

        // ── Дім (лише для особистого /map) ─────────────────────────────────────
        if (showHome)
        {
            var hp = P(home.lon, home.lat);
            using var ring2 = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
            using var homePaint = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF), IsAntialias = true };
            canvas.DrawCircle(hp, 12, ring2);
            canvas.DrawCircle(hp, 5.5f, homePaint);
        }

        // ── Заголовок ──────────────────────────────────────────────────────────
        var head = reds > 0 ? new SKColor(0xFF, 0x45, 0x3A)
                 : yellows > 0 ? new SKColor(0xF4, 0xC5, 0x50)
                 : new SKColor(0x34, 0xD3, 0x99);
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 30, FakeBoldText = true };
        using var sub = new SKPaint { Color = head, IsAntialias = true, TextSize = 22, FakeBoldText = true };
        using var muted = new SKPaint { Color = new SKColor(0x87, 0x92, 0xA6), IsAntialias = true, TextSize = 16 };
        canvas.DrawText(title, pad + 2, 34, titlePaint);
        canvas.DrawText(subtitle, pad + 2, 62, sub);
        canvas.DrawText(DateTime.Now.ToString("dd.MM HH:mm"), W - 150, 34, muted);
        // Легенда.
        canvas.DrawRect(pad + 2, 78, 14, 14, landRed);
        canvas.DrawText("офіційна тривога", pad + 22, 90, muted);
        canvas.DrawRect(pad + 190, 78, 14, 14, landYellow);
        canvas.DrawText("часткова", pad + 210, 90, muted);
        using var dotLegend = new SKPaint { Color = new SKColor(0xFF, 0x9F, 0x45), IsAntialias = true };
        canvas.DrawCircle(pad + 322, 85, 6, dotLegend);
        canvas.DrawText("цілі", pad + 334, 90, muted);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    static SKColor KindColor(ThreatKind k) => k switch
    {
        ThreatKind.Ballistic => new SKColor(0xFF, 0x3B, 0x30),
        ThreatKind.Missile => new SKColor(0xFF, 0x7A, 0x3C),
        ThreatKind.Kab => new SKColor(0xC7, 0x7D, 0xFF),
        ThreatKind.Aviation => new SKColor(0xF4, 0xC5, 0x50),
        ThreatKind.Drone => new SKColor(0xFF, 0x9F, 0x45),
        _ => new SKColor(0x9A, 0xA6, 0xB2),
    };

    static string CatOf(ThreatKind k) => k switch
    {
        ThreatKind.Ballistic => "balist",
        ThreatKind.Missile => "missile",
        ThreatKind.Kab => "kab",
        ThreatKind.Aviation => "avia",
        ThreatKind.Drone => "shahed",
        _ => "shahed",
    };

    static float FigScale(string cat) => cat switch
    {
        "balist" => 1.12f, "missile" => 1.0f, "avia" => 1.0f, "kab" => 0.98f, _ => 0.9f,
    };

    // Силуети цілей (ніс дивиться вгору, 0°=Пн) — ті самі, що на карті застосунку.
    // s: ' ' — колір типу, 'd' — темна деталь, 'l' — світлий акцент.
    static readonly Dictionary<string, (string d, char s)[]> Figs = new()
    {
        ["missile"] = new[]
        {
            ("M0,-11 C1.2,-8.8 1.2,-6 1.2,7.5 C1.2,8.6 -1.2,8.6 -1.2,7.5 C-1.2,-6 -1.2,-8.8 0,-11 Z", ' '),
            ("M-8,0.2 L8,0.2 L8,1.5 L-8,1.5 Z", ' '),
            ("M-3.4,5.4 L3.4,5.4 L3.4,6.8 L-3.4,6.8 Z", ' '),
            ("M0,-11 C0.7,-9.6 0.7,-8.3 0,-7.9 C-0.7,-8.3 -0.7,-9.6 0,-11 Z", 'd'),
            ("M-0.9,6.6 L0.9,6.6 L0.7,8.7 L-0.7,8.7 Z", 'd'),
        },
        ["balist"] = new[]
        {
            ("M0,-12.5 C0.9,-9 1,-4 1,7 L2.7,10.6 L0.9,9 L0.9,10.7 L-0.9,10.7 L-0.9,9 L-2.7,10.6 L-1,7 C-1,-4 -0.9,-9 0,-12.5 Z", ' '),
            ("M0,-12.5 C0.7,-10.6 0.7,-8.7 0,-8.1 C-0.7,-8.7 -0.7,-10.6 0,-12.5 Z", 'd'),
            ("M-0.28,-8 L0.28,-8 L0.28,7 L-0.28,7 Z", 'd'),
        },
        ["kab"] = new[]
        {
            ("M0,-6.6 C2.2,-6 2.2,-1 1.8,3.2 L1.8,5.2 L-1.8,5.2 L-1.8,3.2 C-2.2,-1 -2.2,-6 0,-6.6 Z", ' '),
            ("M-5.6,-0.4 L5.6,-0.4 L5.6,0.9 L-5.6,0.9 Z", ' '),
            ("M-2.8,4.6 L2.8,4.6 L2.3,7.1 L-2.3,7.1 Z", ' '),
            ("M0,-6.6 C1,-5.6 1,-4.4 0,-4 C-1,-4.4 -1,-5.6 0,-6.6 Z", 'd'),
        },
        ["avia"] = new[]
        {
            ("M0,-10.5 L1,-4.5 L1.3,6 L2.1,9.2 L0,8.2 L-2.1,9.2 L-1.3,6 L-1,-4.5 Z", ' '),
            ("M-1.1,-1 L-9,5 L-9,6.3 L-1.1,3.1 L1.1,3.1 L9,6.3 L9,5 L1.1,-1 Z", ' '),
            ("M-1,5.6 L-4.3,7.7 L-4.3,8.4 L-1,6.9 L1,6.9 L4.3,8.4 L4.3,7.7 L1,5.6 Z", ' '),
            ("M0,-6 C0.8,-6 0.9,-3.6 0,-3.2 C-0.9,-3.6 -0.8,-6 0,-6 Z", 'l'),
        },
        ["shahed"] = new[]
        {
            ("M0,-9.5 L1.6,-7.5 L9.5,5.2 L1.3,4.2 L1.3,5.4 L-1.3,5.4 L-1.3,4.2 L-9.5,5.2 L-1.6,-7.5 Z", ' '),
            ("M-0.7,-8.6 L0.7,-8.6 L0.9,6.6 L-0.9,6.6 Z", 'd'),
            ("M1.3,4.4 L3.2,6.7 L1.3,5.4 Z", ' '),
            ("M-1.3,4.4 L-3.2,6.7 L-1.3,5.4 Z", ' '),
            ("M-2.5,6.9 L2.5,6.9 L2.5,7.35 L-2.5,7.35 Z", 'l'),
        },
    };

    static SKColor Darken(SKColor c) =>
        new((byte)(c.Red * 0.35), (byte)(c.Green * 0.35), (byte)(c.Blue * 0.35));

    static void DrawFig(SKCanvas canvas, SKPoint at, double headingDeg, string cat, SKColor color, float ppu)
    {
        var parts = Figs.TryGetValue(cat, out var f) ? f : Figs["shahed"];
        canvas.Save();
        canvas.Translate(at.X, at.Y);
        canvas.RotateDegrees((float)headingDeg);
        var s = FigScale(cat) * ppu;
        canvas.Scale(s);
        foreach (var (d, kind) in parts)
        {
            using var path = SKPath.ParseSvgPathData(d);
            if (path == null) continue;
            var col = kind == 'd' ? Darken(color)
                    : kind == 'l' ? new SKColor(0xE8, 0xEC, 0xF4)
                    : color;
            using var paint = new SKPaint { Color = col, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawPath(path, paint);
        }
        canvas.Restore();
    }

    // Полігони областей з MapOblasts.Json: (назва «n», список кілець з точок [lon,lat]).
    static List<(string name, List<List<double[]>> rings)> ParseOblasts()
    {
        var outp = new List<(string, List<List<double[]>>)>();
        try
        {
            using var doc = JsonDocument.Parse(MapOblasts.Json);
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
