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

    public static byte[] Render(
        (double lat, double lon) home,
        string homeName,
        IReadOnlyList<Threat> threats,
        int percent,
        string verdict)
    {
        const int W = 1000, padTop = 74, pad = 16;
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

        // ── Області ──────────────────────────────────────────────────────────
        var oblasts = ParseOblasts();
        using var land = new SKPaint { Color = new SKColor(0x14, 0x1E, 0x33), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var landHot = new SKPaint { Color = new SKColor(0xE2, 0x3A, 0x34, 0x8C), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var border = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF, 0x66), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.1f };

        foreach (var poly in oblasts)
        {
            bool hot = threats.Any(t => t.Pos is { } p && PointIn(poly, p.lon, p.lat));
            using var path = new SKPath();
            foreach (var ring in poly)
            {
                if (ring.Count < 3) continue;
                path.MoveTo(P(ring[0][0], ring[0][1]));
                for (int i = 1; i < ring.Count; i++) path.LineTo(P(ring[i][0], ring[i][1]));
                path.Close();
            }
            canvas.DrawPath(path, hot ? landHot : land);
            canvas.DrawPath(path, border);
        }

        // ── Цілі ─────────────────────────────────────────────────────────────
        foreach (var t in threats)
        {
            if (t.Pos is not { } p) continue;
            var pt = P(p.lon, p.lat);
            var c = KindColor(t.Kind);
            using var glow = new SKPaint { Color = c.WithAlpha(0x55), IsAntialias = true };
            using var dot = new SKPaint { Color = c, IsAntialias = true };
            canvas.DrawCircle(pt, 9, glow);
            canvas.DrawCircle(pt, 5, dot);
        }

        // ── Дім ──────────────────────────────────────────────────────────────
        var hp = P(home.lon, home.lat);
        using var ring2 = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
        using var homePaint = new SKPaint { Color = new SKColor(0x5A, 0xA9, 0xFF), IsAntialias = true };
        canvas.DrawCircle(hp, 12, ring2);
        canvas.DrawCircle(hp, 5.5f, homePaint);

        // ── Заголовок ──────────────────────────────────────────────────────────
        var head = percent >= 50 ? new SKColor(0xFF, 0x45, 0x3A)
                 : percent >= 25 ? new SKColor(0xF4, 0xC5, 0x50)
                 : new SKColor(0x34, 0xD3, 0x99);
        using var title = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 30, FakeBoldText = true };
        using var sub = new SKPaint { Color = head, IsAntialias = true, TextSize = 22, FakeBoldText = true };
        using var muted = new SKPaint { Color = new SKColor(0x87, 0x92, 0xA6), IsAntialias = true, TextSize = 16 };
        canvas.DrawText($"WarAlarm · {homeName}", pad + 2, 34, title);
        canvas.DrawText($"Ймовірність удару: {percent}% — {verdict}", pad + 2, 62, sub);
        var now = DateTime.Now.ToString("dd.MM HH:mm");
        canvas.DrawText($"цілей: {threats.Count}   ·   {now}", W - 250, 34, muted);

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

    // Полігони областей з MapOblasts.Json: список областей, кожна — список кілець
    // з точок [lon,lat].
    static List<List<List<double[]>>> ParseOblasts()
    {
        var outp = new List<List<List<double[]>>>();
        try
        {
            using var doc = JsonDocument.Parse(MapOblasts.Json);
            foreach (var o in doc.RootElement.EnumerateArray())
            {
                var rings = new List<List<double[]>>();
                foreach (var r in o.GetProperty("r").EnumerateArray())
                {
                    var ring = new List<double[]>();
                    foreach (var p in r.EnumerateArray())
                        ring.Add(new[] { p[0].GetDouble(), p[1].GetDouble() });
                    if (ring.Count >= 3) rings.Add(ring);
                }
                if (rings.Count > 0) outp.Add(rings);
            }
        }
        catch { /* немає полігонів — карта без областей */ }
        return outp;
    }

    // Точка в межах області (парність перетинів по всіх кільцях — з дірками).
    static bool PointIn(List<List<double[]>> poly, double lon, double lat)
    {
        bool inside = false;
        foreach (var ring in poly)
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
                if (((yi > lat) != (yj > lat)) &&
                    (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
        return inside;
    }
}
