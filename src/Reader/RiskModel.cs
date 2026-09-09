using System.Globalization;
using System.Text;

// ── Модель ймовірності удару ─────────────────────────────────────────────────
// Обчислює шанс (0–99%) удару по точці (дім / збережені місця) з живої картини
// загроз: стадія, відстань, траєкторія (курс на точку), тип, розмір кластера.
// Порт 1:1 з app/lib/domain/probability.dart — ті самі числа, що показує застосунок,
// публікуються у Firebase (state.risk) і віддаються публічним API.

public static class RiskModel
{
    static int Rank(TrackStage s) => s switch
    {
        TrackStage.Impact => 5,
        TrackStage.Overhead => 4,
        TrackStage.Imminent => 3,
        TrackStage.Approaching => 2,
        TrackStage.Passed => 1,
        _ => 0,
    };

    static string Cat(ThreatKind k) => k switch
    {
        ThreatKind.Ballistic => "balist",
        ThreatKind.Missile => "missile",
        ThreatKind.Kab => "kab",
        ThreatKind.Aviation => "avia",
        ThreatKind.Drone => "shahed",
        _ => "other",
    };

    // Мінімальна відстань курсу цілі до точки (км) + чи наближається. Порт closest().
    static (double minKm, bool approaching) Closest(Threat t, (double lat, double lon) p)
    {
        if (t.Pos is not { } pos || t.Heading is not { } hd) return (double.MaxValue, false);
        double la = p.lat * Math.PI / 180;
        double dN = (pos.lat - p.lat) * 111, dE = (pos.lon - p.lon) * 111 * Math.Cos(la);
        double th = hd * Math.PI / 180;
        double mE = Math.Sin(th), mN = Math.Cos(th);
        double rE = -dE, rN = -dN, proj = rE * mE + rN * mN;
        double min = proj <= 0
            ? Math.Sqrt(rE * rE + rN * rN)
            : Math.Sqrt(Math.Max(0, rE * rE + rN * rN - proj * proj));
        return (min, proj > 0);
    }

    // Шанс попадання однієї цілі по точці (0..0.985).
    static double PerTarget(Threat t, int amount, (double lat, double lon) p)
    {
        if (t.Pos is not { } pos) return 0;
        double d = ThreatGeo.Km(pos, p);
        int stage = Rank(t.Stage);
        double b;
        if (stage >= 5) b = 0.98;
        else if (stage >= 4) b = 0.9;
        else if (stage >= 3) b = 0.75;
        else if (t.Stage == TrackStage.Passed) b = 0.05;
        else if (d <= 8) b = 0.6;
        else if (d <= 15) b = 0.42;
        else if (d <= 25) b = 0.28;
        else if (d <= 40) b = 0.16;
        else if (d <= 80) b = 0.07;
        else b = 0.02;

        if (t.Heading is not null)
        {
            var (minKm, approaching) = Closest(t, p);
            if (approaching && minKm <= 12) b = b * 1.6 + 0.08;
            else if (approaching && minKm <= 30) b *= 1.25;
            else if (!approaching) b *= 0.5;
        }

        b *= Cat(t.Kind) switch
        {
            "balist" => 1.12,
            "missile" => 1.05,
            "kab" => 1.0,
            "avia" => 0.8,
            "shahed" => 0.9,
            _ => 0.9,
        };
        b *= 1 + Math.Min(amount - 1, 6) * 0.03;
        return Math.Clamp(b, 0.0, 0.985);
    }

    public sealed record Assessment(
        int Percent, string Verdict, string Cls,
        Dictionary<string, int> ByKind,
        List<(string label, double distKm, int percent)> Factors);

    public static Assessment For(
        (double lat, double lon) point,
        IReadOnlyList<(Threat t, int amount)> threats)
    {
        double none = 1;
        var kindNone = new Dictionary<string, double>();
        var factors = new List<(string, double, int)>();
        foreach (var (t, amount) in threats)
        {
            double p = PerTarget(t, amount, point);
            if (p < 0.02 || t.Pos is not { } pos) continue;
            none *= 1 - p;
            string cat = Cat(t.Kind);
            kindNone[cat] = (kindNone.TryGetValue(cat, out var kn) ? kn : 1) * (1 - p);
            factors.Add((ThreatTracker.Label(t), ThreatGeo.Km(pos, point), (int)Math.Round(p * 100)));
        }
        double overall = 1 - none;
        int percent = Math.Clamp((int)Math.Round(overall * 100), 0, 99);
        factors.Sort((a, b) => b.Item3.CompareTo(a.Item3));

        var byKind = new Dictionary<string, int>();
        foreach (var kv in kindNone) byKind[kv.Key] = (int)Math.Round((1 - kv.Value) * 100);

        var (verdict, cls) = Verdict(percent);
        return new Assessment(percent, verdict, cls, byKind,
            factors.Take(5).ToList());
    }

    static (string, string) Verdict(int p) => p switch
    {
        >= 75 => ("Дуже ймовірно", "alarm"),
        >= 50 => ("Ймовірно", "alarm"),
        >= 25 => ("Можливо", "warn"),
        >= 10 => ("Малоймовірно", "warn"),
        _ => ("Спокійно", "calm"),
    };

    static string N(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "").Replace("\n", " ");

    /// Serialises the risk block for the state JSON: home + per user place.
    public static void AppendJson(
        StringBuilder sb,
        (double lat, double lon) home,
        IReadOnlyList<(Threat t, int amount)> threats,
        IReadOnlyList<(string name, double lat, double lon)>? places)
    {
        var h = For(home, threats);
        sb.Append("{\"home\":");
        AppendOne(sb, h);
        sb.Append(",\"places\":[");
        if (places is { } pls)
            for (int i = 0; i < pls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var a = For((pls[i].lat, pls[i].lon), threats);
                sb.Append("{\"name\":\"").Append(Esc(pls[i].name)).Append("\",");
                AppendInner(sb, a);
                sb.Append('}');
            }
        sb.Append("]}");
    }

    static void AppendOne(StringBuilder sb, Assessment a)
    {
        sb.Append('{');
        AppendInner(sb, a);
        sb.Append('}');
    }

    static void AppendInner(StringBuilder sb, Assessment a)
    {
        sb.Append("\"percent\":").Append(a.Percent);
        sb.Append(",\"verdict\":\"").Append(Esc(a.Verdict)).Append('"');
        sb.Append(",\"cls\":\"").Append(a.Cls).Append('"');
        sb.Append(",\"byKind\":{");
        bool first = true;
        foreach (var kv in a.ByKind)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
        }
        sb.Append("},\"factors\":[");
        for (int i = 0; i < a.Factors.Count; i++)
        {
            var (label, dist, pc) = a.Factors[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"label\":\"").Append(Esc(label)).Append("\",\"distKm\":")
              .Append(N(dist)).Append(",\"percent\":").Append(pc).Append('}');
        }
        sb.Append(']');
    }
}
