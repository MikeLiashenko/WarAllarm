// ── WarAlarm · типові маршрути шахедів ───────────────────────────────────────
// Між постами каналів ціль не стоїть: шахеди йдуть знайомими коридорами — з півночі
// (Курськ/Орел/Брянськ) через Сумщину й Чернігівщину на Київ, зі сходу через Харків і
// Полтаву, з Азовського моря й Криму — через Запоріжжя/Миколаїв на захід (Умань →
// Вінниця → Хмельницький → Львів). Застосунок рухає мітку цим маршрутом, а щойно канал
// напише, де ціль насправді, — переставляє її туди й веде далі вже звідти.
// Це ОЦІНКА: вона не вмикає тривогу і зупиняється на межі області без тривоги.

public static class TypicalRoutes
{
    public static readonly (double lat, double lon) Kyiv = (50.45, 30.52);
    static readonly (double lat, double lon)
        Sumy = (50.91, 34.80), Romny = (50.75, 33.47), Pryluky = (50.59, 32.39), Brovary = (50.51, 30.79),
        Shostka = (51.87, 33.48), Nizhyn = (51.05, 31.89), NSiverskyi = (52.00, 33.26), Chernihiv = (51.49, 31.29),
        Kupiansk = (49.71, 37.62), Kharkiv = (49.99, 36.23), Poltava = (49.59, 34.55), Lubny = (50.02, 33.00),
        Boryspil = (50.35, 30.95), Pavlohrad = (48.53, 35.87), Dnipro = (48.46, 35.04), Kremenchuk = (49.07, 33.42),
        Berdiansk = (46.76, 36.79), Zaporizhzhia = (47.84, 35.14), Kropyvnytskyi = (48.51, 32.26),
        Kherson = (46.64, 32.61), Mykolaiv = (46.97, 32.00), Voznesensk = (47.57, 31.33),
        Uman = (48.75, 30.22), Vinnytsia = (49.23, 28.47), Khmelnytskyi = (49.42, 26.99), Ternopil = (49.55, 25.59),
        Lviv = (49.84, 24.03), Zhytomyr = (50.25, 28.66), Rivne = (50.62, 26.25);

    // Захід: «хвіст» маршруту, куди йдуть цілі з півдня й сходу.
    static readonly (double lat, double lon)[] West = { Kropyvnytskyi, Uman, Vinnytsia, Khmelnytskyi, Ternopil, Lviv };

    static (double, double)[] R(params (double, double)[] p) => p;

    /// Типовий маршрут від місця пуску (перша точка — саме місце пуску).
    public static List<(double lat, double lon)>? FromOrigin(string origin)
    {
        if (LaunchFeed.SiteCoords(origin) is not { } o) return null;
        (double lat, double lon)[] tail = origin switch
        {
            "Курськ" or "Халіно" or "Бєлгород" => R(Sumy, Romny, Pryluky, Brovary, Kyiv),
            "Орел" or "Брянськ" or "Навля" or "Калуга" or "Шайковка" => R(Shostka, Nizhyn, Brovary, Kyiv),
            "Смоленськ" or "Шаталово" => R(NSiverskyi, Chernihiv, Kyiv),
            "Воронеж" or "Ліпецьк" or "Тамбов" or "Енгельс" or "Міллерово" or "Морозовськ" or "Луганськ" or "Ростов" or "Каспій"
                => R(Kupiansk, Kharkiv, Poltava, Lubny, Boryspil, Kyiv),
            "Донецьк" or "Макіївка" or "Горлівка" or "Таганрог"
                => R(Pavlohrad, Dnipro, Kremenchuk, Uman, Vinnytsia, Khmelnytskyi, Ternopil, Lviv),
            "Приморсько-Ахтарськ" or "Єйськ" or "Азовське море"
                => R(Berdiansk, Zaporizhzhia, Dnipro, Kropyvnytskyi, Uman, Vinnytsia, Khmelnytskyi, Ternopil, Lviv),
            "Крим" or "Чауда" or "Гвардійське" or "Саки"
                => R(Kherson, Mykolaiv, Voznesensk, Uman, Vinnytsia, Khmelnytskyi, Ternopil, Lviv),
            _ => R(Kyiv),
        };
        var route = new List<(double lat, double lon)> { o };
        route.AddRange(tail);
        // Коридори від місць пуску задані вручну й закінчуються в Україні — не обрізаємо
        // (з Криму маршрут перетинає затоку ~30 км і обривався біля Армянська).
        return route;
    }

    /// Маршрут, що ЗАЙШОВ в Україну, з неї не виходить: обрізаємо там, де він покидає
    /// країну (шахед з Одещини «на захід» летів через Молдову в Румунію, а «вздовж курсу»
    /// на 150 км — за Дунай). Початок поза Україною (пуск з РФ, з моря) — нормально.
    /// Короткі «вилазки» до 30 км не рахуються: полігони спрощені біля кордону й моря, а
    /// між Кримом і Херсонщиною — затока (маршрут з Криму обривався біля Армянська).
    public static List<(double lat, double lon)>? ClipToUkraine(List<(double lat, double lon)> route)
    {
        if (route.Count < 2) return null;
        double total = LengthKm(route);
        bool wasIn = false;
        double? leftAt = null;
        for (double d = 0; d <= total; d += 3)
        {
            var p = At(route, d);
            if (OblastGeo.InUkraine(p.lat, p.lon)) { wasIn = true; leftAt = null; continue; }
            if (!wasIn) continue;
            leftAt ??= d;
            if (d - leftAt.Value > 30) return Cut(route, leftAt.Value - 3); // остання точка ще в Україні
        }
        // Так і не зайшов в Україну: ціль біля кордону (спрощений контур лишив її «зовні» —
        // Городня, Любеч) і курс веде геть — у Білорусь / Росію. Маршруту нема, лишається, де бачили.
        if (!wasIn) return null;
        // Кінець маршруту — уже за кордоном (останній відрізок короткий).
        return leftAt is { } la ? Cut(route, la - 3) : route;
    }

    // Перші [km] маршруту; null — якщо від нього майже нічого не лишилось.
    static List<(double lat, double lon)>? Cut(List<(double lat, double lon)> route, double km)
    {
        if (km < 5) return null;
        var outp = new List<(double lat, double lon)> { route[0] };
        double run = 0;
        for (int i = 1; i < route.Count; i++)
        {
            double seg = ThreatGeo.Km(route[i - 1], route[i]);
            if (run + seg >= km) { outp.Add(At(route, km)); return outp; }
            run += seg;
            outp.Add(route[i]);
        }
        return outp;
    }

    /// Куди ймовірно полетить ціль, що зараз у точці [p] (перша точка — сама p).
    /// Відомий курс (дві точки) — продовжуємо його; інакше — до Києва з північного
    /// сходу, на захід — з півдня/сходу/вже західніше Києва. null — уже біля Києва.
    public static List<(double lat, double lon)>? FromPosition((double lat, double lon) p, double? heading, bool headingKnown)
    {
        if (ThreatGeo.Km(p, Kyiv) < 25) return null; // над Києвом — ціль уже тут
        var route = new List<(double lat, double lon)> { p };
        if (headingKnown && heading is { } h)
        {
            route.Add(Move(p, h, 150));
            return ClipToUkraine(route);
        }
        bool northEast = p.lat > 49.3 && p.lon > 30.9 && p.lon < 38.5;
        if (northEast)
        {
            route.Add(Kyiv);
            return route;
        }
        if (p.lon < 30.2 && p.lat > 50.0) // Житомирщина, Рівненщина — далі на захід
        {
            foreach (var w in new[] { Zhytomyr, Rivne, Lviv }) if (w.lon < p.lon - 0.3) route.Add(w);
            return route.Count > 1 ? ClipToUkraine(route) : null;
        }
        // Бессарабія (Ізмаїл, Рені, Болград): «на захід» — це Молдова й Румунія. Там цілі
        // летять на порти Дунаю, тобто вже біля цілі — маршруту далі нема.
        if (p.lat < 46.4 && p.lon < 30.3) return null;
        foreach (var w in West) if (w.lon < p.lon - 0.3) route.Add(w);
        return route.Count > 1 ? ClipToUkraine(route) : null;
    }

    /// Маршрут цілі: якщо в пості названо, куди вона летить («курсом на Полтаву»), — спершу
    /// туди, далі типовим шляхом від того міста; інакше — від поточної точки.
    public static List<(double lat, double lon)>? ForTarget(Threat t)
    {
        if (t.Pos is not { } p) return null;
        // «на Харків»: летить до міста й там зупиняється, поки канал не напише, куди далі.
        if (t.Approach is { } a) return new List<(double lat, double lon)> { a, p };
        if (t.CourseTo is { } c)
        {
            var route = new List<(double lat, double lon)> { p, (c.lat, c.lon) };
            if (FromPosition((c.lat, c.lon), null, false) is { } further) route.AddRange(further.Skip(1));
            return ClipToUkraine(route);
        }
        return FromPosition(p, t.Heading, t.HeadingKnown);
    }

    static (double lat, double lon) Move((double lat, double lon) p, double brg, double km)
    {
        double r = brg * Math.PI / 180;
        return (p.lat + km * Math.Cos(r) / 111, p.lon + km * Math.Sin(r) / (111 * Math.Cos(p.lat * Math.PI / 180)));
    }

    public static double LengthKm(IReadOnlyList<(double lat, double lon)> route)
    {
        double s = 0;
        for (int i = 1; i < route.Count; i++) s += ThreatGeo.Km(route[i - 1], route[i]);
        return s;
    }

    /// Точка на маршруті через [km] від початку.
    public static (double lat, double lon) At(IReadOnlyList<(double lat, double lon)> route, double km)
    {
        for (int i = 1; i < route.Count; i++)
        {
            double seg = ThreatGeo.Km(route[i - 1], route[i]);
            if (km <= seg && seg > 0)
            {
                double f = km / seg;
                return (route[i - 1].lat + (route[i].lat - route[i - 1].lat) * f, route[i - 1].lon + (route[i].lon - route[i - 1].lon) * f);
            }
            km -= seg;
        }
        return route[^1];
    }

    /// Скільки км маршрутом до найближчої до [home] точки і наскільки близько він проходить.
    public static (double alongKm, double passKm) Closest(IReadOnlyList<(double lat, double lon)> route, (double lat, double lon) home)
    {
        double total = LengthKm(route), best = double.MaxValue, bestAlong = 0;
        for (double d = 0; d <= total; d += 5)
        {
            double k = ThreatGeo.Km(At(route, d), home);
            if (k < best) { best = k; bestAlong = d; }
        }
        return (bestAlong, best);
    }
}
