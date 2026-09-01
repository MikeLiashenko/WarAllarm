// ── WarAlarm · GPS → район Харькова ──────────────────────────────────────────
// Переводит координаты телефона (широта/долгота) в название района.
// Сейчас — по ближайшему центру района (просто и без данных). Позже заменим на
// точные полигоны районов из OpenStreetMap (point-in-polygon) — интерфейс тот же.

public static class GeoLocate
{
    // Приблизительные центры 9 районов Харькова.
    static readonly (string name, double lat, double lon)[] Districts =
    {
        ("Салтівський",     50.052, 36.300),
        ("Немишлянський",   49.983, 36.330),
        ("Індустріальний",  49.958, 36.350),
        ("Слобідський",     49.945, 36.290),
        ("Основ'янський",   49.960, 36.255),
        ("Шевченківський",  50.002, 36.230),
        ("Київський",       50.010, 36.283),
        ("Холодногірський", 49.990, 36.190),
        ("Новобаварський",  49.958, 36.205),
    };

    // Ближайший район + расстояние в км до его центра.
    public static (string name, double km) Nearest(double lat, double lon)
    {
        string best = "";
        double bestKm = double.MaxValue;
        foreach (var (name, dLat, dLon) in Districts)
        {
            double dx = (dLat - lat) * 111.0;                              // 1° широты ≈ 111 км
            double dy = (dLon - lon) * 111.0 * Math.Cos(lat * Math.PI / 180); // долгота сжимается к северу
            double km = Math.Sqrt(dx * dx + dy * dy);
            if (km < bestKm) { bestKm = km; best = name; }
        }
        return (best, bestKm);
    }
}
