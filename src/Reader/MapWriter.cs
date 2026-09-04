using System.Globalization;
using System.Text;

// ── WarAlarm · Живая карта угроз ─────────────────────────────────────────────
// Тот же реальный контур Украины и стиль, что в design/index.html (концепт-приложение),
// но наполненный ЖИВЫМИ данными трекера: реальные позиции целей по геокоду названий,
// траектории на дом, обратный отсчёт ETA. Самодостаточно (offline, без CDN),
// перезаписывается на каждое изменение, страница сама обновляется (meta-refresh).

public static class MapWriter
{
    // Настройки для вкладок «Мої місця»/«Налаштування» — Program задаёт один раз при старте.
    public record Cfg(string District, string City, string Oblast,
                      bool WatchKharkiv, bool WatchKyiv, int OtboiSec, bool Ntfy,
                      IReadOnlyList<(string name, double lat, double lon)>? Places = null,
                      bool Ai = false);
    public static Cfg? Config;
    // data:-URI иконки приложения (щит+тризуб). Program задаёт при старте из WarAlarm.png.
    public static string IconUri = "";
    // Последний ответ настоящего ИИ (Claude) + время. Program обновляет по мере анализа.
    public static string AiText = "";
    public static DateTime? AiAt;

    static string N(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

    // «Сімейство» цілі для кластеризації на карті: шахед/ціль/невідоме — фактично одне й те саме
    // (різні джерела звуть по-різному), тож зливаємо їх разом; ракети/балістика/КАБ/авіація — окремо.
    static int Family(ThreatKind k) => k switch
    {
        ThreatKind.Drone or ThreatKind.Target or ThreatKind.Unknown => 0,
        ThreatKind.Missile => 1,
        ThreatKind.Ballistic => 2,
        ThreatKind.Kab => 3,
        ThreatKind.Aviation => 4,
        _ => 5,
    };

    /// Optional live publisher. When set, [Write] also pushes the same state
    /// JSON to Firebase RTDB (node "state"), so the Flutter app reads live data.
    public static FirebasePublisher? Publisher;

    public static void Write(string path, ThreatTracker tracker, string homeName, DateTime now)
    {
        var state = BuildState(tracker, homeName, now);
        var html = Template.Replace("/*__STATE__*/", state)
                           .Replace("/*__ICON__*/", IconUri)
                           .Replace("/*__OBLASTS__*/", MapOblasts.Json);
        try { File.WriteAllText(path, html, new UTF8Encoding(false)); } catch { /* карта не критична */ }
        if (Publisher is { } pub) { _ = pub.Publish("state", state); } // fire-and-forget
    }

    /// Builds the live state JSON (threats + stats + rows + cfg + aiText).
    /// Shared by the offline map.html and the Firebase publisher.
    public static string BuildState(ThreatTracker tracker, string homeName, DateTime now)
    {
        var home = tracker.Home;
        var rawThreats = tracker.Active.Where(t => t.Pos is not null).ToList();

        // Кластеризація: цілі одного «сімейства» (шахед/ціль/невідоме — це одне й те саме, просто
        // різні джерела: локальний монітор і всеукраїнський радар) у межах ~16 км зливаємо в ОДНУ
        // стрілку та підсумовуємо ×N. Так карта не «засмічується» накладеними стрілками над одним
        // містом (Київ, Харків…). Представник кластера — конкретніший тип, ближчий до дому.
        var merged = new List<(Threat t, int amount)>();
        foreach (var t in rawThreats
                     .OrderBy(t => t.Kind is ThreatKind.Target or ThreatKind.Unknown ? 1 : 0) // конкретний тип — представник
                     .ThenByDescending(t => t.Zone != "UA")                                    // локальний монітор точніший за місцем
                     .ThenBy(t => t.DistKm ?? double.MaxValue))                                 // ближча до дому — важливіша
        {
            int idx = merged.FindIndex(m => Family(m.t.Kind) == Family(t.Kind)
                && m.t.Pos is { } mp && ThreatGeo.Km(mp, t.Pos!.Value) < 16);
            if (idx >= 0) merged[idx] = (merged[idx].t, merged[idx].amount + t.Amount); // ×N кластера = сума
            else merged.Add((t, t.Amount));
        }

        var sb = new StringBuilder();
        sb.Append("{\"ts\":").Append(((DateTimeOffset)now.ToUniversalTime()).ToUnixTimeMilliseconds());
        sb.Append(",\"homeName\":\"").Append(Esc(homeName)).Append('"');
        sb.Append(",\"home\":{\"lat\":").Append(N(home.lat)).Append(",\"lon\":").Append(N(home.lon)).Append('}');
        sb.Append(",\"threats\":[");
        for (int i = 0; i < merged.Count; i++)
        {
            var (t, amount) = merged[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"label\":\"").Append(Esc(ThreatTracker.Label(t))).Append('"');
            sb.Append(",\"zone\":\"").Append(Esc(t.Zone)).Append('"');
            sb.Append(",\"cat\":\"").Append(Cat(t.Kind)).Append('"');
            sb.Append(",\"stage\":\"").Append(t.Stage).Append('"');
            sb.Append(",\"stageLabel\":\"").Append(Esc(ThreatTracker.StageLabel(t.Stage))).Append('"');
            sb.Append(",\"icon\":\"").Append(Esc(ThreatParser.Icon(t.Kind))).Append('"');
            sb.Append(",\"lat\":").Append(N(t.Pos!.Value.lat)).Append(",\"lon\":").Append(N(t.Pos!.Value.lon));
            sb.Append(",\"dist\":").Append(t.DistKm is { } d ? N(d) : "null");
            sb.Append(",\"eta\":").Append(t.EtaMin is { } e ? N(e) : "null");
            sb.Append(",\"dir\":\"").Append(Esc(t.Dir)).Append('"');
            sb.Append(",\"n\":").Append(amount);
            sb.Append(",\"heading\":").Append(t.Heading is { } h ? N(h) : "null");
            sb.Append(",\"seen\":").Append(((DateTimeOffset)t.LastUpdate).ToUnixTimeMilliseconds()); // час останнього спостереження позиції (для руху/екстраполяції)
            sb.Append(",\"place\":\"").Append(Esc(t.LastPlace)).Append('"');
            sb.Append(",\"trail\":[");
            bool first = true;
            foreach (var seg in t.Trail)
            {
                var c = ThreatGeo.Of(seg.place);
                if (c is not { } cc) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('[').Append(N(cc.lat)).Append(',').Append(N(cc.lon)).Append(']');
            }
            sb.Append("]}");
        }
        sb.Append(']');
        sb.Append(",\"stats\":"); AppendStats(sb, now);
        sb.Append(",\"rows\":"); AppendRows(sb, now);
        if (Config is { } cf)
        {
            sb.Append(",\"cfg\":{");
            sb.Append("\"district\":\"").Append(Esc(cf.District)).Append('"');
            sb.Append(",\"city\":\"").Append(Esc(cf.City)).Append('"');
            sb.Append(",\"oblast\":\"").Append(Esc(cf.Oblast)).Append('"');
            sb.Append(",\"watchKharkiv\":").Append(cf.WatchKharkiv ? "true" : "false");
            sb.Append(",\"watchKyiv\":").Append(cf.WatchKyiv ? "true" : "false");
            sb.Append(",\"otboiSec\":").Append(cf.OtboiSec);
            sb.Append(",\"ntfy\":").Append(cf.Ntfy ? "true" : "false");
            sb.Append(",\"places\":[");
            if (cf.Places is { } pls)
                for (int j = 0; j < pls.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"name\":\"").Append(Esc(pls[j].name)).Append("\",\"lat\":").Append(N(pls[j].lat))
                      .Append(",\"lon\":").Append(N(pls[j].lon)).Append('}');
                }
            sb.Append("]");
            sb.Append(",\"ai\":").Append(cf.Ai ? "true" : "false");
            sb.Append('}');
        }
        // Ответ настоящего ИИ (Claude) для вкладки ШІ.
        sb.Append(",\"aiText\":\"").Append(Esc(AiText)).Append('"');
        sb.Append(",\"aiAt\":").Append(AiAt is { } aa ? ((DateTimeOffset)aa.ToUniversalTime()).ToUnixTimeMilliseconds().ToString() : "null");
        sb.Append('}');
        return sb.ToString();
    }

    // Категория для цвета/формы блипа (как в концепте).
    static string Cat(ThreatKind k) => k switch
    {
        ThreatKind.Ballistic => "balist",
        ThreatKind.Missile   => "missile",
        ThreatKind.Kab       => "kab",
        ThreatKind.Aviation  => "avia",
        ThreatKind.Drone     => "shahed",
        _                    => "other",
    };

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "").Replace("\n", "\\n").Replace("\t", " ");

    // Статистика из журнала alarms.csv: тревог/прилётов сегодня, всего, время последней.
    // Формат строки: date,time,zone,kind,tag,"head"  (tag: alarm|urgent|clear|impact)
    static void AppendStats(StringBuilder sb, DateTime now)
    {
        int todayA = 0, todayI = 0, total = 0, week = 0; string last = "";
        var today = now.ToString("yyyy-MM-dd");
        var weekAgo = now.AddDays(-6).ToString("yyyy-MM-dd");
        try
        {
            if (File.Exists("alarms.csv"))
                foreach (var line in File.ReadLines("alarms.csv"))
                {
                    var c = line.Split(',');
                    if (c.Length < 5) continue;
                    var tag = c[4];
                    if (tag is "alarm" or "urgent")
                    {
                        total++; last = c[1];
                        if (c[0] == today) todayA++;
                        if (string.CompareOrdinal(c[0], weekAgo) >= 0) week++;
                    }
                    else if (tag == "impact" && c[0] == today) todayI++;
                }
        }
        catch { /* нет журнала — нули */ }
        sb.Append("{\"todayAlarms\":").Append(todayA)
          .Append(",\"todayImpacts\":").Append(todayI)
          .Append(",\"week\":").Append(week)
          .Append(",\"total\":").Append(total)
          .Append(",\"last\":\"").Append(Esc(last)).Append("\"}");
    }

    // Компактные записи журнала для графиков аналитики (последние 120 дней, максимум 3000).
    // {d:дата, h:час(0-23), z:зона, k:тип, g:тег(alarm|urgent|impact)}
    static void AppendRows(StringBuilder sb, DateTime now)
    {
        sb.Append('[');
        try
        {
            if (File.Exists("alarms.csv"))
            {
                var cut = now.AddDays(-120).ToString("yyyy-MM-dd");
                var rows = new List<string[]>();
                foreach (var line in File.ReadLines("alarms.csv"))
                {
                    var c = line.Split(',');
                    if (c.Length < 5) continue;
                    if (c[4] is not ("alarm" or "urgent" or "impact")) continue;
                    if (string.CompareOrdinal(c[0], cut) < 0) continue;
                    rows.Add(c);
                }
                int start = Math.Max(0, rows.Count - 3000);
                bool first = true;
                for (int i = start; i < rows.Count; i++)
                {
                    var c = rows[i];
                    int hh = c[1].Length >= 2 && int.TryParse(c[1].AsSpan(0, 2), out var h) ? h : 0;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"d\":\"").Append(c[0]).Append("\",\"h\":").Append(hh)
                      .Append(",\"z\":\"").Append(Esc(c[2])).Append("\",\"k\":\"").Append(Esc(c[3]))
                      .Append("\",\"g\":\"").Append(c[4]).Append("\"}");
                }
            }
        }
        catch { /* нет журнала — пусто */ }
        sb.Append(']');
    }

    // Страница: реальный контур Украины (тот же path, что в design/index.html) + живой overlay.
    const string Template = """
<!doctype html><html lang="uk"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="icon" id="fav" href="/*__ICON__*/">
<title>WarAlarm · карта загроз</title>
<style>
:root{
  --bg:#0A0E17;--bg2:#0E1424;--panel:#121A2C;--elev:#172034;
  --line:rgba(255,255,255,.07);--line2:rgba(255,255,255,.12);
  --tx:#E8ECF4;--mut:#8792A6;--dim:#5C6780;--sky:#5AA9FF;--wheat:#F4C550;
  --calm:#34D399;--calm-glow:rgba(52,211,153,.30);--alarm:#FF3B30;--alarm-glow:rgba(255,59,48,.34);
  --balist:#FF3B30;--missile:#FF7A3C;--shahed:#FF9F45;--kab:#C77DFF;--avia:#F4C550;--other:#9AA6B2;
  --sans:"Segoe UI Variable","Segoe UI",system-ui,-apple-system,Roboto,sans-serif;
  --mono:ui-monospace,"Cascadia Code","Consolas",monospace;color-scheme:dark;
}
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:var(--sans);color:var(--tx);min-height:100vh;
  background:radial-gradient(1100px 600px at 30% -10%,#12203c 0,transparent 60%),
    radial-gradient(900px 700px at 90% 110%,#1a1030 0,transparent 55%),var(--bg)}
header{display:flex;align-items:center;gap:12px;padding:14px 22px;border-bottom:1px solid var(--line)}
header .mark{width:30px;height:30px;border-radius:9px;background:linear-gradient(150deg,var(--wheat),#E0932F);
  display:grid;place-items:center;color:#3a2a00}
header b{font-size:19px;letter-spacing:-.3px}
header .home{color:var(--sky);font-weight:600;font-size:14px;display:flex;align-items:center;gap:6px}
header .clock{margin-left:auto;font-family:var(--mono);color:var(--mut);font-size:13px}
nav.tabs{display:flex;gap:4px;padding:8px 14px;border-bottom:1px solid var(--line);flex-wrap:wrap;overflow-x:auto;position:sticky;top:0;background:var(--bg);z-index:5}
nav.tabs button{border:0;background:transparent;color:var(--mut);font-family:var(--sans);font-size:14px;font-weight:600;padding:9px 13px;border-radius:10px;cursor:pointer;white-space:nowrap;display:flex;align-items:center;gap:7px;transition:.2s}
nav.tabs button:hover{color:var(--tx)}
nav.tabs button.on{background:var(--elev);color:var(--tx)}
nav.tabs .b{min-width:19px;height:19px;padding:0 5px;border-radius:10px;background:var(--alarm);color:#fff;font-size:11px;font-weight:700;display:none;align-items:center;justify-content:center}
nav.tabs .b.show{display:inline-flex}
.view{display:none;padding:20px 18px;max-width:1120px;margin:0 auto}
.view.on{display:block}
.view#v-map{max-width:1280px}
.soon{color:var(--mut);text-align:center;padding:56px 20px}
.soon .big{font-size:46px;margin-bottom:14px}
.soon b{color:var(--tx);font-size:18px}
.soon p{margin:12px auto 0;max-width:440px;line-height:1.6}
.soon ul{list-style:none;margin:16px auto 0;max-width:420px;text-align:left}
.soon li{padding:8px 0;border-bottom:1px solid var(--line);color:var(--mut)}
.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:10px;margin-bottom:6px}
.stat{border:1px solid var(--line);border-radius:12px;padding:13px 14px;background:var(--panel)}
.stat .num{font-size:26px;font-weight:700;font-variant-numeric:tabular-nums;line-height:1}
.stat .lbl{font-size:12px;color:var(--mut);margin-top:6px}
.stat.hot .num{color:var(--alarm)} .stat.warm .num{color:var(--wheat)} .stat.sky .num{color:var(--sky)}
.chips{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:14px;align-items:center}
.chips .plab{color:var(--mut);font-size:13px;margin-right:2px}
.chips button{border:1px solid var(--line);background:var(--panel);color:var(--mut);padding:6px 12px;border-radius:20px;cursor:pointer;font-size:13px;font-weight:600;font-family:var(--sans);display:flex;align-items:center;gap:5px}
.chips button:hover{color:var(--tx)} .chips button.on{background:var(--elev);color:var(--tx);border-color:var(--line2)}
.periods{display:flex;gap:6px;align-items:center;margin-bottom:16px;flex-wrap:wrap}
.periods .plab{color:var(--mut);font-size:13px;margin-right:4px}
.periods button{border:1px solid var(--line);background:var(--panel);color:var(--mut);padding:7px 13px;border-radius:9px;cursor:pointer;font-size:13px;font-weight:600;font-family:var(--sans)}
.periods button:hover{color:var(--tx)} .periods button.on{background:var(--elev);color:var(--tx);border-color:var(--line2)}
.dash{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:16px;margin-top:16px}
.chart{border:1px solid var(--line);border-radius:14px;padding:16px 18px;background:var(--panel)}
.chart h4{font-size:14px;font-weight:650} .chart .sub{font-size:12px;color:var(--dim);margin:2px 0 16px}
.bars{display:flex;align-items:flex-end;gap:2px;height:150px}
.bars .bar{flex:1;background:var(--alarm);border-radius:2px 2px 0 0;min-height:2px;opacity:.9;transition:height .3s}
.bars.hour .bar{background:var(--wheat)}
.blabels{display:flex;margin-top:7px;font-size:10px;color:var(--dim)}
.hbars .row{display:flex;align-items:center;gap:10px;margin:8px 0;font-size:13px}
.hbars .lab{width:92px;color:var(--mut);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.hbars .track{flex:1;background:rgba(255,255,255,.05);border-radius:6px;height:18px;overflow:hidden}
.hbars .fill{height:100%;border-radius:6px;transition:width .3s}
.hbars .val{width:38px;text-align:right;font-variant-numeric:tabular-nums;font-weight:600}
.kv{display:flex;justify-content:space-between;align-items:center;padding:12px 4px;border-bottom:1px solid var(--line)}
.kv .k{color:var(--mut)} .kv .v{font-weight:600}
.pill{font-size:12px;padding:3px 10px;border-radius:20px;font-weight:700}
.pill.on{background:rgba(52,211,153,.14);color:var(--calm)} .pill.off{background:rgba(255,255,255,.06);color:var(--dim)}
.pill.warn{background:rgba(244,197,80,.16);color:var(--wheat)} .pill.alarm{background:rgba(255,59,48,.16);color:#FF9A93}
#nowPlaces .card{border-left:3px solid var(--line2)} #nowPlaces .card.calm{border-left-color:var(--calm)}
#nowPlaces .card.warn{border-left-color:var(--wheat)} #nowPlaces .card.alarm{border-left-color:var(--alarm)}
.note{color:var(--dim);font-size:12.5px;margin-top:16px;line-height:1.6;padding:12px 14px;border:1px dashed var(--line);border-radius:12px}
.mini{margin-left:8px;border:1px solid var(--line);background:transparent;color:var(--mut);width:24px;height:24px;border-radius:7px;cursor:pointer;font-size:13px;line-height:1}
.mini:hover{color:var(--alarm);border-color:var(--alarm)}
.frm{display:flex;flex-direction:column;gap:9px;margin-top:12px}
.frm input,.frm select{background:var(--bg2);border:1px solid var(--line);color:var(--tx);border-radius:9px;padding:10px 12px;font:14px var(--sans);width:100%}
.frm input:focus,.frm select:focus{outline:none;border-color:var(--sky)}
.frm .row2{display:flex;gap:9px}.frm .row2>*{flex:1}
.gpsbtn{border:1px solid rgba(90,169,255,.4);background:rgba(90,169,255,.10);color:var(--sky);border-radius:9px;padding:10px;cursor:pointer;font:600 14px var(--sans)}
.gpsbtn:hover{background:rgba(90,169,255,.18)}
.savebtn{border:0;background:linear-gradient(150deg,var(--calm),#1FA97C);color:#04140e;border-radius:9px;padding:10px;cursor:pointer;font:700 14px var(--sans)}
.wrap{display:flex;flex-wrap:wrap;gap:20px}
.mapcard{position:relative;flex:1;min-width:320px;border-radius:18px;overflow:hidden;
  background:linear-gradient(180deg,#0b1424,#0a1020);border:1px solid var(--line)}
.mapcard svg{display:block;width:100%;height:auto;cursor:grab;touch-action:none}
.mapcard svg:active{cursor:grabbing}
.ua-fill{fill:none;stroke:rgba(120,150,190,.35);stroke-width:.9;stroke-linejoin:round;vector-effect:non-scaling-stroke}
.obl{stroke:rgba(130,150,180,.22);stroke-width:.4px;stroke-linejoin:round;vector-effect:non-scaling-stroke}
.clabel2{fill:rgba(230,237,243,.6);font-size:6px;font-weight:600;font-family:var(--sans);paint-order:stroke;stroke:#0a1020;stroke-width:1.2px;text-anchor:middle}
.city{fill:var(--mut)}.clabel{fill:var(--dim);font-size:6px;font-family:var(--mono)}
.you{fill:var(--sky)}.you-ring{fill:none;stroke:var(--sky);stroke-width:1.1;animation:pip 2s ease-in-out infinite}
.traj{stroke-width:1.4;fill:none;stroke-dasharray:4 5;stroke-linecap:round;opacity:.9;animation:flow 1.3s linear infinite}
.blip{transform-box:fill-box;transform-origin:center;animation:pip 1.6s ease-in-out infinite}
.arrow{animation:pip 1.5s ease-in-out infinite;stroke-linecap:round}
.fig{animation:figp 1.7s ease-in-out infinite;filter:drop-shadow(0 .4px .9px rgba(0,0,0,.55))}
.fig path{stroke-linejoin:round}
.tlabel{font-size:8px;font-weight:700;font-family:var(--mono);fill:var(--tx);paint-order:stroke;stroke:#0a1020;stroke-width:1px}
.plabel{font-size:3.4px;font-weight:600;font-family:var(--sans);paint-order:stroke;stroke:#0a1020;stroke-width:.8px}
.pred{opacity:.55;stroke-linecap:round;fill:none}
.ghost{opacity:.3}
.predlab{font-size:3.2px;font-weight:600;font-family:var(--sans);paint-order:stroke;stroke:#0a1020;stroke-width:.7px;opacity:.85}
.zoomc{position:absolute;top:10px;right:10px;display:flex;flex-direction:column;gap:6px}
.zoomc button{width:30px;height:30px;border-radius:8px;border:1px solid var(--line2);background:rgba(11,20,36,.85);
  color:var(--tx);font-size:17px;line-height:1;cursor:pointer;display:grid;place-items:center;font-family:var(--sans)}
.zoomc button:hover{background:var(--elev)}
.legend{position:absolute;left:12px;bottom:10px;display:flex;gap:12px;flex-wrap:wrap;font-size:11px;color:var(--mut)}
.legend span{display:flex;align-items:center;gap:5px}.legend i{width:9px;height:9px;border-radius:2px;display:inline-block}
.side{flex:1;min-width:270px}
.status{border-radius:16px;border:1px solid var(--line);padding:16px;margin-bottom:16px;text-align:center;position:relative;overflow:hidden}
.status .ring{width:120px;height:120px;border-radius:50%;margin:6px auto;padding:3px;transition:.5s;
  background:conic-gradient(from 220deg,var(--calm),#0f2f28 60%,var(--calm))}
.status.alarm .ring{background:conic-gradient(from 220deg,var(--alarm),#3a0d0a 55%,#FF7A45)}
.status .inner{width:100%;height:100%;border-radius:50%;background:#0b1220;display:grid;place-items:center}
.status .inner b{font-size:19px}.status.calm .inner b{color:var(--calm)}.status.alarm .inner b{color:var(--alarm)}
.status .t{font-size:10px;color:var(--dim);text-transform:uppercase;letter-spacing:2px;margin-top:4px}
.status .msg{margin-top:8px;font-size:14px;font-weight:600}
h3{font-size:12px;text-transform:uppercase;letter-spacing:.5px;color:var(--dim);margin:0 2px 10px}
.card{border:1px solid var(--line);border-radius:12px;padding:11px 13px;margin-bottom:10px;background:var(--panel)}
.card .top{display:flex;align-items:center;gap:8px;font-weight:700}
.badge{margin-left:auto;font-size:11px;padding:3px 9px;border-radius:20px;font-weight:700}
.meta{color:var(--mut);font-size:13px;margin-top:5px}.eta{font-variant-numeric:tabular-nums;font-weight:700;color:var(--tx)}
.empty{color:var(--calm);padding:22px;text-align:center;border:1px dashed var(--line);border-radius:12px}
@keyframes flow{to{stroke-dashoffset:-18}}
@keyframes pip{0%,100%{opacity:.5}50%{opacity:1}}
@keyframes figp{0%,100%{opacity:1}50%{opacity:.62}}
</style></head><body>
<header>
  <span class="mark" id="mark"><svg width="16" height="16" viewBox="0 0 24 24"><path d="M12 2l7 3v6c0 4.5-3 8-7 11-4-3-7-6.5-7-11V5l7-3z" fill="currentColor"/></svg></span>
  <b>WarAlarm</b>
  <span class="home">📍 <span id="homeName"></span></span>
  <span class="clock" id="clock"></span>
</header>
<nav class="tabs" id="tabs">
  <button data-v="now" class="on">🛡 Зараз <span class="b" id="badgeNow"></span></button>
  <button data-v="map">🗺 Карта</button>
  <button data-v="analytics">📊 Аналітика</button>
  <button data-v="places">📍 Мої місця</button>
  <button data-v="settings">⚙ Налаштування</button>
  <button data-v="ai">🤖 ШІ</button>
</nav>

<section class="view on" id="v-now">
  <div class="status" id="status">
    <div class="ring"><div class="inner"><span><b id="stTitle">Спокійно</b><div class="t" id="stSub">моніторинг</div></span></div></div>
    <div class="msg" id="stMsg"></div>
  </div>
  <div class="chips" id="selNow"><span class="plab">Місце:</span></div>
  <h3>Мої місця</h3>
  <div id="nowPlaces"></div>
  <h3 style="margin-top:20px">Активні цілі · вся Україна</h3>
  <div id="list"></div>
</section>

<section class="view" id="v-map">
  <div class="wrap">
  <div class="mapcard">
    <svg id="uamap" viewBox="0 0 300 200" preserveAspectRatio="xMidYMid meet">
      <g id="zoom"><g id="ua-fit"><g transform="translate(0 1024) scale(0.1 -0.1)">
        <path id="ua-path" class="ua-fill" d="M6381 8544 c-47 -10 -74 -11 -100 -4 -52 14 -101 12 -107 -4 -3 -8 -35 -34 -70 -57 -53 -34 -67 -39 -79 -29 -13 10 -19 10 -34 -4 -13 -12 -27 -14 -52 -9 -44 8 -96 30 -104 43 -3 5 -17 10 -30 10 -13 0 -27 5 -30 10 -3 6 -13 10 -21 10 -12 0 -14 -9 -9 -43 5 -33 3 -46 -9 -56 -14 -11 -14 -16 -1 -33 12 -18 12 -24 -3 -46 -9 -14 -34 -38 -56 -53 -55 -40 -109 -40 -151 1 -27 27 -35 29 -58 21 -16 -6 -37 -7 -59 0 -18 5 -51 9 -72 9 -22 0 -47 7 -56 15 -15 14 -20 13 -43 -6 -15 -11 -27 -29 -27 -39 0 -18 -40 -36 -50 -21 -14 21 -65 28 -97 13 -18 -9 -33 -20 -33 -25 0 -9 -32 -52 -84 -110 -15 -18 -37 -49 -47 -70 -11 -21 -35 -55 -53 -75 -19 -20 -37 -52 -41 -70 -10 -47 -9 -103 2 -111 15 -10 24 -59 12 -66 -7 -5 -6 -12 4 -23 24 -28 30 -63 17 -103 -11 -33 -17 -39 -42 -39 -16 0 -31 6 -34 15 -4 8 -14 15 -23 15 -10 0 -27 10 -40 21 -19 17 -22 28 -16 56 5 28 1 41 -19 68 -35 45 -88 55 -145 26 -34 -18 -44 -19 -62 -9 -17 11 -22 10 -34 -5 -19 -27 -49 -18 -53 16 -3 21 -9 27 -27 27 -13 0 -33 -9 -44 -21 -11 -12 -24 -18 -30 -15 -6 4 -13 -8 -17 -29 -7 -39 -7 -39 -35 -24 -16 8 -26 7 -45 -6 -21 -14 -24 -14 -24 0 0 8 -8 20 -19 27 -14 9 -18 20 -14 48 5 28 1 41 -19 68 -35 45 -88 55 -111 85 -8 -8 -27 -14 -43 -13 -65 4 -80 -1 -107 -42 -14 -22 -32 -40 -39 -40 -7 0 -17 19 -23 43 -9 42 -9 42 -67 51 -49 8 -61 7 -74 -7 -16 -15 -19 -14 -45 26 -15 23 -28 48 -28 55 0 19 -40 14 -47 -5 -20 -59 -85 -81 -123 -43 -11 11 -32 20 -46 20 -24 0 -26 -2 -20 -34 6 -30 3 -37 -24 -56 -49 -35 -68 -3 -30 50 l21 29 -52 16 c-62 18 -83 19 -110 5 -13 -7 -25 -7 -38 0 -14 7 -28 7 -47 0 -25 -10 -26 -9 -22 19 3 20 0 32 -10 36 -11 4 -13 16 -8 50 l7 45 -53 0 c-30 0 -59 -5 -65 -11 -8 -8 -17 -8 -31 0 -11 6 -30 11 -42 11 -11 0 -32 12 -45 26 -33 35 -72 44 -112 26 -38 -18 -63 -5 -63 35 0 19 -5 24 -22 23 -81 -7 -119 0 -144 27 -21 22 -35 28 -72 29 -26 0 -60 8 -77 16 -25 12 -49 13 -134 6 -94 -8 -106 -7 -125 10 -43 38 -66 41 -136 18 -67 -21 -130 -24 -284 -13 -66 5 -71 4 -113 -26 -33 -25 -42 -38 -41 -57 3 -33 -6 -46 -53 -76 -22 -14 -52 -38 -67 -55 -25 -27 -30 -29 -61 -20 -19 6 -38 8 -43 4 -6 -3 -7 3 -3 16 5 16 1 23 -18 30 -14 6 -28 14 -31 19 -3 6 -24 3 -51 -7 -33 -12 -44 -20 -39 -31 4 -8 5 -22 4 -30 -1 -8 -1 -47 -1 -87 0 -40 -4 -75 -9 -78 -13 -8 -13 -85 0 -85 5 0 18 -19 28 -42 10 -24 26 -49 35 -57 10 -7 17 -28 17 -47 0 -18 5 -36 10 -39 6 -3 10 -14 10 -24 0 -10 6 -24 14 -30 7 -6 16 -25 20 -42 3 -17 19 -43 36 -59 16 -15 30 -37 30 -48 0 -18 -4 -20 -30 -12 -28 8 -32 6 -41 -17 -7 -20 -6 -35 6 -60 9 -18 20 -33 25 -33 18 0 9 -114 -13 -175 l-22 -60 -83 -5 -83 -5 -47 -52 c-26 -29 -83 -78 -126 -109 -93 -65 -284 -255 -340 -337 -24 -34 -47 -57 -58 -57 -11 0 -18 -7 -18 -18 0 -10 -15 -25 -33 -35 -41 -23 -66 -79 -45 -104 9 -12 13 -48 11 -132 0 -64 -3 -127 -5 -141 -2 -18 4 -28 24 -38 35 -16 50 -44 46 -82 l-3 -29 -73 30 c-93 37 -107 37 -120 -1 -7 -19 -24 -36 -46 -47 -32 -15 -36 -21 -36 -54 0 -23 -6 -41 -15 -46 -8 -5 -21 -28 -30 -53 -16 -45 -50 -95 -96 -142 -47 -47 -54 -88 -30 -161 22 -66 65 -137 82 -137 6 0 35 -23 65 -51 38 -37 54 -59 54 -78 0 -42 13 -52 72 -55 76 -4 82 -10 74 -64 -4 -28 -2 -50 5 -58 17 -21 29 -17 29 11 0 24 3 25 38 19 31 -5 42 -2 62 18 14 13 30 33 36 43 14 25 60 13 69 -18 4 -12 21 -37 38 -55 29 -30 36 -32 95 -32 83 0 150 -17 163 -42 9 -17 17 -19 68 -13 53 6 61 4 118 -29 63 -37 85 -38 147 -5 18 11 102 4 123 -9 5 -4 19 -28 30 -53 13 -28 33 -54 52 -65 17 -10 37 -34 46 -56 12 -32 20 -38 44 -38 67 0 93 13 159 80 63 63 71 68 122 74 31 4 94 4 140 1 51 -4 105 -2 135 5 28 7 78 12 112 12 60 0 63 1 83 34 12 18 32 42 44 51 12 10 26 29 29 43 10 38 18 44 72 58 60 15 141 68 150 97 5 19 6 19 16 3 14 -25 69 -24 69 0 0 9 -9 23 -20 30 -26 16 -26 42 -1 42 11 0 41 -11 66 -24 52 -28 75 -25 75 10 0 29 11 30 35 4 14 -16 26 -19 48 -14 21 4 32 1 41 -12 15 -20 36 -11 36 15 0 9 6 26 14 37 14 18 15 18 21 -9 9 -34 37 -36 59 -3 23 36 54 43 73 16 13 -18 21 -20 58 -15 41 7 43 6 65 -32 39 -64 61 -83 96 -83 35 0 46 -10 48 -44 1 -15 9 -22 29 -24 15 -2 27 -10 27 -17 0 -23 18 -27 42 -11 31 22 58 20 58 -3 0 -10 -6 -24 -14 -30 -8 -6 -16 -21 -19 -31 -7 -27 18 -26 37 2 l15 21 9 -24 c5 -13 11 -33 13 -43 6 -31 26 -29 32 2 7 37 59 62 88 44 10 -7 32 -9 50 -5 38 7 59 -14 59 -60 0 -16 9 -40 20 -54 11 -14 20 -34 20 -45 0 -15 4 -17 16 -10 11 6 27 4 46 -5 27 -14 31 -14 54 8 29 27 41 19 46 -31 2 -26 7 -35 21 -34 21 2 20 5 2 -40 -14 -34 -13 -41 8 -37 5 1 1 -9 -7 -21 -9 -12 -16 -42 -16 -66 0 -31 -7 -53 -26 -77 -16 -22 -23 -39 -18 -47 5 -7 7 -37 5 -66 -2 -44 -1 -51 13 -46 9 3 22 2 29 -4 6 -5 24 -12 38 -15 25 -6 33 -20 53 -80 10 -32 36 -31 72 4 l34 33 0 -36 c0 -20 -5 -40 -11 -43 -7 -5 -8 -25 -4 -56 6 -40 4 -49 -8 -49 -28 0 -29 -19 -3 -44 27 -24 37 -88 16 -101 -18 -11 -1 -23 41 -30 30 -5 43 -13 50 -32 8 -22 14 -25 49 -21 32 3 40 0 40 -13 0 -9 9 -24 20 -34 17 -16 20 -31 20 -117 0 -91 -1 -98 -20 -98 -28 0 -25 -16 7 -46 16 -14 39 -36 53 -49 14 -12 36 -28 49 -35 21 -11 22 -13 8 -27 -13 -13 -23 -14 -58 -5 -46 12 -59 9 -59 -14 0 -34 -46 0 -75 55 -17 33 -35 41 -35 15 0 -11 -6 -13 -19 -9 -15 5 -24 -2 -38 -27 -16 -29 -18 -31 -21 -11 -2 12 -15 26 -30 32 -15 6 -32 21 -37 33 -14 30 -32 33 -40 9 -8 -27 -22 -27 -30 -1 -3 11 -10 20 -15 20 -12 0 -24 -46 -15 -59 3 -6 -1 -11 -9 -11 -8 0 -19 -8 -25 -17 -5 -10 -13 -15 -16 -11 -4 4 -3 12 1 20 4 7 8 38 9 70 1 53 -1 58 -21 58 -12 0 -27 -7 -34 -15 -7 -8 -25 -15 -41 -15 -19 0 -34 -8 -44 -24 -9 -13 -24 -26 -34 -29 -18 -4 -18 -9 -7 -64 9 -42 10 -66 2 -82 -9 -20 -6 -27 22 -52 18 -16 32 -31 32 -34 0 -2 -14 -25 -32 -50 -28 -40 -31 -48 -19 -66 7 -12 11 -22 9 -24 -2 -1 -28 -10 -58 -20 -79 -25 -88 -36 -82 -96 4 -45 2 -51 -17 -56 -12 -3 -21 -13 -21 -24 0 -15 -7 -19 -35 -19 -25 0 -35 -4 -35 -16 0 -10 -6 -14 -15 -10 -19 7 -38 -59 -29 -103 4 -20 3 -31 -5 -31 -6 0 -11 -13 -11 -30 0 -33 -19 -40 -40 -15 -19 22 -86 19 -110 -5 -24 -24 -26 -40 -5 -40 9 0 15 -9 15 -25 0 -36 23 -76 57 -101 53 -39 130 -73 181 -80 55 -7 96 1 74 15 -10 6 -10 15 -3 34 12 32 25 33 69 7 40 -24 39 -24 45 0 5 20 21 29 94 51 24 6 50 20 59 31 13 13 31 18 76 18 66 0 107 -15 158 -58 29 -25 35 -38 40 -81 11 -108 52 -41 56 91 l3 87 -45 -6 c-31 -4 -44 -2 -44 6 0 25 118 135 227 213 68 48 140 109 181 155 84 93 97 115 87 139 -5 11 -16 39 -24 63 -14 36 -24 47 -57 60 -22 9 -52 25 -65 36 -48 38 -44 105 7 105 15 0 23 -4 19 -11 -9 -14 22 -47 67 -70 44 -22 53 -35 50 -70 -1 -18 5 -29 23 -39 20 -11 22 -16 14 -32 -6 -10 -9 -27 -7 -36 3 -14 6 -12 17 8 35 65 126 217 141 235 10 11 22 34 28 51 8 27 7 35 -9 51 -33 32 -15 50 54 58 34 4 88 16 120 27 47 17 62 19 83 9 45 -20 74 -13 110 26 19 20 37 33 41 30 4 -4 1 -13 -5 -19 -7 -7 -12 -15 -12 -19 0 -13 52 -20 66 -9 9 8 37 10 86 5 107 -10 124 11 103 126 -8 41 16 99 40 99 18 0 20 -16 4 -26 -6 -3 -17 -21 -25 -40 -12 -28 -12 -37 0 -61 8 -15 21 -45 30 -67 14 -33 20 -38 42 -33 19 3 30 -2 46 -23 17 -23 31 -30 79 -35 63 -7 77 -25 37 -46 -27 -14 -204 -3 -261 17 -22 8 -72 14 -111 14 -42 0 -77 5 -84 12 -24 24 -22 0 2 -29 34 -41 77 -58 105 -43 14 7 27 7 39 1 9 -5 43 -12 75 -16 42 -5 60 -13 68 -27 6 -11 9 -21 7 -22 -35 -26 -82 -46 -105 -46 -39 0 -61 -23 -43 -45 18 -22 50 -19 50 5 0 29 15 25 40 -10 15 -21 30 -30 50 -30 15 0 30 -7 34 -15 3 -8 17 -15 30 -15 26 0 56 -32 56 -60 0 -11 25 -22 86 -39 l87 -24 51 19 c36 13 79 19 146 20 126 1 125 0 110 19 -19 23 -3 31 28 13 32 -19 62 -8 62 22 0 11 5 20 10 20 6 0 10 -17 10 -37 0 -33 1 -35 13 -18 15 20 42 12 33 -10 -3 -8 1 -19 9 -25 10 -8 14 -8 18 2 3 9 12 4 27 -15 21 -26 23 -27 46 -12 13 8 31 15 40 15 13 0 14 3 5 14 -10 12 -8 19 7 34 16 16 22 17 36 7 13 -10 15 -22 10 -53 -3 -23 -3 -57 0 -77 6 -32 10 -35 41 -35 19 0 35 -4 35 -10 0 -5 -7 -7 -15 -4 -11 5 -17 -3 -21 -25 -7 -33 -11 -36 -33 -22 -10 7 -23 3 -39 -10 -18 -14 -28 -16 -38 -8 -9 8 -22 2 -51 -24 -66 -59 -105 -71 -129 -40 -12 16 -14 15 -28 -18 -10 -24 -26 -41 -48 -50 -55 -22 -128 -79 -134 -105 -3 -13 -13 -24 -20 -24 -8 0 -27 -8 -42 -18 -36 -24 -60 -38 -105 -62 -39 -22 -59 -51 -39 -58 7 -2 12 -14 12 -26 0 -17 9 -24 42 -33 36 -10 45 -9 66 7 13 11 43 22 67 25 49 8 63 0 170 -91 33 -28 75 -59 93 -68 30 -16 35 -16 67 0 19 9 35 20 35 24 0 15 46 26 78 18 35 -9 41 -28 16 -49 -18 -15 -12 -49 16 -88 11 -14 25 -52 31 -84 11 -54 10 -60 -11 -89 -19 -26 -21 -38 -16 -82 4 -29 2 -64 -3 -79 -7 -18 -7 -28 1 -33 12 -8 -44 -34 -73 -34 -24 0 -24 -5 0 -39 13 -17 42 -35 84 -50 55 -19 67 -28 76 -53 l11 -30 86 7 c100 8 147 25 180 65 14 16 40 34 59 40 19 6 35 17 35 23 0 7 9 18 19 24 10 7 26 34 35 61 15 46 24 55 97 101 88 57 118 71 153 71 27 0 131 44 141 60 3 6 28 21 55 35 26 13 50 29 52 36 4 12 64 15 97 4 16 -5 20 -3 15 9 -3 9 -3 24 1 34 4 9 9 24 10 34 1 9 9 28 18 42 15 23 24 26 73 26 63 0 127 -27 145 -63 16 -30 30 -37 51 -26 10 6 27 8 37 4 12 -4 21 2 29 20 13 27 13 27 113 9 17 -3 31 2 44 16 10 11 27 20 37 20 23 0 78 27 78 39 0 5 -7 15 -16 22 -21 17 -14 122 10 148 9 10 16 22 16 28 0 6 8 9 18 7 10 -3 28 6 44 23 l27 28 -37 30 c-34 27 -45 30 -126 34 -75 3 -95 0 -131 -18 -27 -14 -45 -30 -49 -45 -9 -37 -56 -44 -96 -14 -20 15 -30 29 -27 40 8 29 -32 22 -43 -7 -5 -14 -14 -25 -20 -25 -5 0 -10 -8 -10 -17 0 -10 -6 -26 -13 -35 -17 -22 -105 -68 -130 -68 -11 0 -25 -6 -31 -14 -16 -19 -66 -30 -66 -14 0 14 -106 68 -132 68 -18 0 -58 41 -58 60 0 8 9 23 20 35 17 18 18 26 9 60 -15 52 -88 126 -118 118 -15 -4 -21 -1 -21 11 0 9 -6 16 -14 16 -8 0 -16 5 -18 12 -3 9 -12 9 -36 -1 -17 -7 -40 -10 -49 -6 -16 6 -14 9 10 21 35 19 34 57 0 62 -31 5 -29 30 2 38 29 7 34 24 7 24 -19 0 -63 -30 -100 -67 -12 -13 -29 -23 -38 -23 -13 0 -12 4 4 27 11 16 32 39 46 52 26 25 34 59 16 77 -5 5 -7 45 -3 97 l6 87 41 0 c24 0 52 8 66 17 13 10 51 32 85 48 43 21 64 39 73 61 8 16 24 34 35 40 28 12 34 50 12 74 -9 10 -15 25 -13 32 3 8 9 5 17 -10 6 -12 18 -22 26 -22 9 0 15 -13 17 -37 4 -47 32 -57 50 -18 10 23 9 33 -9 68 -30 60 -35 76 -39 137 -4 60 9 96 25 71 5 -8 12 -39 16 -69 4 -30 15 -68 26 -86 10 -17 19 -42 19 -55 0 -22 1 -23 18 -9 9 9 28 19 41 23 13 4 63 46 110 94 73 73 93 88 133 96 25 6 56 8 67 3 11 -4 34 -2 51 3 42 15 60 6 60 -31 0 -21 -11 -43 -37 -71 -35 -39 -36 -41 -13 -37 20 2 35 22 73 93 26 50 52 91 57 93 5 2 25 14 45 28 20 13 44 24 55 24 10 0 22 5 25 10 9 15 77 11 99 -5 15 -12 17 -22 12 -57 -6 -39 -5 -41 7 -23 8 11 24 50 36 87 17 50 32 75 57 96 88 72 182 93 194 42 6 -24 7 -23 35 20 16 25 43 68 61 96 l33 51 72 6 c42 4 78 12 88 21 9 8 32 14 51 13 19 -1 47 2 63 6 22 6 27 4 27 -10 1 -44 16 -41 73 12 36 33 57 61 57 75 0 12 8 34 19 48 10 15 20 39 23 54 4 23 1 27 -21 30 -29 3 -28 -2 -15 121 6 63 10 70 46 102 22 18 51 36 65 40 14 3 40 17 57 30 17 13 43 28 59 34 15 6 27 16 27 23 0 7 11 36 24 64 l24 51 178 1 c204 2 280 9 310 29 18 11 25 34 39 126 16 106 56 214 85 232 13 8 13 66 0 100 -6 15 -33 52 -60 83 -41 46 -50 63 -50 93 0 34 -3 38 -51 60 -50 24 -50 24 -45 64 11 93 21 120 43 123 12 2 31 -2 43 -8 38 -21 107 53 80 85 -14 17 -50 20 -50 4 0 -5 -4 -7 -10 -4 -5 3 -35 10 -65 15 -41 7 -63 17 -85 39 -17 16 -30 32 -30 34 0 5 58 16 101 20 38 3 55 22 68 74 13 50 57 99 104 115 15 6 27 13 27 17 0 4 -11 34 -24 67 -22 55 -27 60 -70 74 -42 14 -65 32 -66 52 0 3 16 12 35 18 19 6 37 15 40 20 11 18 -8 61 -34 74 -23 12 -36 12 -92 -1 -61 -15 -69 -15 -111 1 -42 16 -57 33 -72 79 -5 15 -19 17 -105 17 l-99 0 -32 40 c-35 44 -68 52 -87 20 -7 -11 -27 -27 -44 -36 -30 -15 -32 -15 -58 9 -14 14 -39 29 -54 33 -22 5 -27 11 -24 26 4 14 -5 26 -36 46 -28 19 -42 23 -45 14 -2 -7 -22 -12 -49 -12 -37 0 -50 5 -74 31 -16 17 -29 35 -29 40 0 5 -31 9 -70 9 -73 0 -76 -2 -60 -46 6 -14 8 -41 4 -59 -5 -29 -9 -32 -30 -26 -14 3 -33 20 -44 36 -11 17 -54 49 -102 76 l-83 46 -3 46 c-2 37 -9 51 -33 72 -17 14 -36 25 -43 25 -7 0 -21 9 -31 20 -16 18 -16 20 -1 26 9 4 16 17 16 34 0 27 -1 28 -32 19 -18 -5 -47 -9 -64 -9 -19 0 -54 -15 -93 -40 -48 -31 -71 -40 -106 -40 -35 0 -58 -9 -107 -40 -77 -50 -98 -56 -136 -36 -17 9 -49 16 -71 16 -22 0 -43 5 -46 10 -3 6 -33 28 -65 50 -74 50 -116 52 -232 9 -121 -44 -118 -43 -118 -26 0 9 -14 32 -31 51 -43 50 -50 82 -32 162 16 77 9 120 -30 183 -11 19 -24 59 -28 89 -8 58 -27 81 -67 82 -37 0 -60 17 -72 55 -22 67 -65 77 -178 39 -59 -20 -82 -18 -82 5 0 11 -13 29 -29 40 -24 17 -44 21 -110 21 -87 0 -95 6 -65 49 17 23 12 32 -20 44 -6 2 -5 15 1 33 16 44 4 91 -31 129 l-32 33 36 11 c19 6 50 20 67 31 31 19 32 22 27 67 -7 54 -38 94 -95 124 -31 15 -42 31 -68 93 -17 41 -31 81 -31 90 0 8 -10 27 -23 43 -13 15 -38 50 -56 77 -35 50 -94 86 -141 85 -14 -1 -54 -7 -89 -15z"/>
      </g></g><g id="ua-overlay"></g></g>
    </svg>
    <div class="zoomc"><button id="zin">+</button><button id="zout">−</button><button id="zreset">⤢</button></div>
    <div class="legend">
      <span><i style="background:var(--shahed)"></i>Шахед</span>
      <span><i style="background:var(--missile)"></i>Крилата ракета</span>
      <span><i style="background:var(--balist)"></i>Балістика</span>
      <span><i style="background:var(--kab)"></i>КАБ</span>
      <span><i style="background:var(--avia)"></i>Авіація</span>
    </div>
  </div>
  </div>
</section>

<section class="view" id="v-analytics">
  <div class="chips" id="selAn"><span class="plab">Місце:</span></div>
  <div class="periods" id="periods">
    <span class="plab">Період:</span>
    <button data-d="7">7 днів</button><button data-d="30" class="on">30 днів</button>
    <button data-d="90">90 днів</button><button data-d="all">Усе</button>
  </div>
  <div class="stats" id="aKpi"></div>
  <div class="dash" id="aDash"></div>
</section>

<section class="view" id="v-places"><h3>Мої місця (стежу)</h3><div id="places"></div>
  <div class="note">🏠 Дім і 🏙 Київ — з <b>settings.local.txt</b>. Місця 📍, додані тут, зберігаються у цьому браузері (кнопка «За GPS» працює на телефоні). Перемикай активне місце зверху на вкладках «Зараз» / «Аналітика».</div></section>

<section class="view" id="v-settings"><h3>Налаштування</h3><div id="settings"></div>
  <div class="note">Значення читаються з <b>settings.local.txt</b> і <b>secrets.local.txt</b>, застосовуються без перезбірки.</div></section>

<section class="view" id="v-ai">
  <h3>🤖 Аналіз від ІІ (Claude)</h3>
  <div id="aiLLM"></div>
  <h3 style="margin-top:22px">Розумний висновок · зараз</h3>
  <div id="aiNow"></div>
  <h3 style="margin-top:22px">Оцінка ризику по місцях</h3>
  <div id="aiRisk"></div>
  <h3 style="margin-top:22px">Прогноз траєкторії</h3>
  <div id="aiForecast"></div>
  <h3 style="margin-top:22px">Патерни з історії</h3>
  <div class="dash" id="aiPatterns"></div>
  <div class="note">Це евристичний аналіз на основі твоїх даних (канали моніторингу + журнал тривог): оцінка ризику, геометричний прогноз курсу і патерни. Повноцінний ІІ-прогноз навченою моделлю — наступний крок.</div>
</section>
<script>
const S = /*__STATE__*/;
const OBLASTS = /*__OBLASTS__*/;
const ICON="/*__ICON__*/";
if(ICON){var mk=document.getElementById('mark');if(mk){mk.style.background='none';mk.innerHTML='<img src="'+ICON+'" alt="WarAlarm" style="width:100%;height:100%;object-fit:cover;border-radius:9px;display:block">';}}
const NS='http://www.w3.org/2000/svg';
const C={balist:'#FF3B30',missile:'#FF7A3C',shahed:'#FF9F45',kab:'#C77DFF',avia:'#F4C550',other:'#9AA6B2'};
function el(n,at,txt){var e=document.createElementNS(NS,n);for(var k in at)e.setAttribute(k,at[k]);if(txt!=null)e.textContent=txt;document.getElementById('ua-overlay').appendChild(e);return e;}
// Детальні силуети цілей (ніс дивиться вгору, 0°=Пн; поворот по heading). Кілька частин на фігуру:
// s:'dark' — темніша деталь (ніс/фюзеляж), s:'light' — світлий акцент (гвинт/кабіна), інакше — колір типу.
function figParts(cat){
  switch(cat){
    case 'missile': return [ // Крилата ракета: довгий корпус, прямі серединні крила, хрестове оперення
      {d:'M0,-11 C1.2,-8.8 1.2,-6 1.2,7.5 C1.2,8.6 -1.2,8.6 -1.2,7.5 C-1.2,-6 -1.2,-8.8 0,-11 Z'},
      {d:'M-8,0.2 L8,0.2 L8,1.5 L-8,1.5 Z'},
      {d:'M-3.4,5.4 L3.4,5.4 L3.4,6.8 L-3.4,6.8 Z'},
      {d:'M0,-11 C0.7,-9.6 0.7,-8.3 0,-7.9 C-0.7,-8.3 -0.7,-9.6 0,-11 Z',s:'dark'},
      {d:'M-0.9,6.6 L0.9,6.6 L0.7,8.7 L-0.7,8.7 Z',s:'dark'}
    ];
    case 'balist': return [ // Балістика: дуже довга, гострий ніс, БЕЗ крил, малі хвостові стабілізатори
      {d:'M0,-12.5 C0.9,-9 1,-4 1,7 L2.7,10.6 L0.9,9 L0.9,10.7 L-0.9,10.7 L-0.9,9 L-2.7,10.6 L-1,7 C-1,-4 -0.9,-9 0,-12.5 Z'},
      {d:'M0,-12.5 C0.7,-10.6 0.7,-8.7 0,-8.1 C-0.7,-8.7 -0.7,-10.6 0,-12.5 Z',s:'dark'},
      {d:'M-0.28,-8 L0.28,-8 L0.28,7 L-0.28,7 Z',s:'dark'}
    ];
    case 'kab': return [ // КАБ (УМПК): коротке товсте тіло, серединні крила, хвостовий короб-оперення
      {d:'M0,-6.6 C2.2,-6 2.2,-1 1.8,3.2 L1.8,5.2 L-1.8,5.2 L-1.8,3.2 C-2.2,-1 -2.2,-6 0,-6.6 Z'},
      {d:'M-5.6,-0.4 L5.6,-0.4 L5.6,0.9 L-5.6,0.9 Z'},
      {d:'M-2.8,4.6 L2.8,4.6 L2.3,7.1 L-2.3,7.1 Z'},
      {d:'M0,-6.6 C1,-5.6 1,-4.4 0,-4 C-1,-4.4 -1,-5.6 0,-6.6 Z',s:'dark'}
    ];
    case 'avia': return [ // Авіація: фюзеляж із гострим носом, стрілоподібні крила, хвостове оперення, ліхтар
      {d:'M0,-10.5 L1,-4.5 L1.3,6 L2.1,9.2 L0,8.2 L-2.1,9.2 L-1.3,6 L-1,-4.5 Z'},
      {d:'M-1.1,-1 L-9,5 L-9,6.3 L-1.1,3.1 L1.1,3.1 L9,6.3 L9,5 L1.1,-1 Z'},
      {d:'M-1,5.6 L-4.3,7.7 L-4.3,8.4 L-1,6.9 L1,6.9 L4.3,8.4 L4.3,7.7 L1,5.6 Z'},
      {d:'M0,-6 C0.8,-6 0.9,-3.6 0,-3.2 C-0.9,-3.6 -0.8,-6 0,-6 Z',s:'light'}
    ];
    default: return [ // Шахед-136 / ціль: дельта-крило, фюзеляж, два кільові стабілізатори, штовхальний гвинт
      {d:'M0,-9.5 L1.6,-7.5 L9.5,5.2 L1.3,4.2 L1.3,5.4 L-1.3,5.4 L-1.3,4.2 L-9.5,5.2 L-1.6,-7.5 Z'},
      {d:'M-0.7,-8.6 L0.7,-8.6 L0.9,6.6 L-0.9,6.6 Z',s:'dark'},
      {d:'M1.3,4.4 L3.2,6.7 L1.3,5.4 Z'},
      {d:'M-1.3,4.4 L-3.2,6.7 L-1.3,5.4 Z'},
      {d:'M-2.5,6.9 L2.5,6.9 L2.5,7.35 L-2.5,7.35 Z',s:'light'}
    ];
  }
}
function figScale(cat){return cat==='balist'?1.12:cat==='missile'?1.0:cat==='avia'?1.0:cat==='kab'?0.98:cat==='shahed'?0.9:0.8;}
// Міста за рівнями деталізації [назва,lon,lat,tier]: 0 — великі (завжди), 1 — обл. центри (зум≥1.7),
// 2 — містечка Харківщини та Київщини (зум≥2.8). З'являються при наближенні.
var CITY=[
 ['Київ',30.52,50.45,0],['Харків',36.23,49.99,0],['Дніпро',35.05,48.47,0],['Одеса',30.73,46.48,0],
 ['Львів',24.03,49.84,0],['Запоріжжя',35.14,47.84,0],['Донецьк',37.80,48.00,0],['Миколаїв',31.99,46.98,0],
 ['Полтава',34.55,49.59,1],['Суми',34.80,50.91,1],['Чернігів',31.29,51.50,1],['Черкаси',32.06,49.44,1],
 ['Житомир',28.66,50.25,1],['Вінниця',28.47,49.23,1],['Хмельницький',26.99,49.42,1],['Рівне',26.25,50.62,1],
 ['Луцьк',25.34,50.75,1],['Тернопіль',25.59,49.55,1],['Ів.-Франківськ',24.71,48.92,1],['Ужгород',22.29,48.62,1],
 ['Чернівці',25.94,48.29,1],['Кропивницький',32.26,48.51,1],['Кривий Ріг',33.39,47.91,1],['Херсон',32.61,46.64,1],
 ['Маріуполь',37.54,47.10,1],['Кременчук',33.42,49.07,1],['Мелітополь',35.37,46.85,1],['Бердянськ',36.79,46.76,1],
 ['Чугуїв',36.68,49.84,2],['Ізюм',37.25,49.21,2],['Лозова',36.32,48.89,2],["Куп'янськ",37.62,49.71,2],
 ['Балаклія',36.86,49.46,2],['Богодухів',35.53,50.16,2],['Первомайський',36.22,49.39,2],['Люботин',35.94,49.94,2],
 ['Дергачі',36.12,50.10,2],['Мерефа',36.05,49.82,2],['Пісочин',36.03,49.98,2],['Вовчанськ',36.94,50.29,2],
 ['Золочів',35.98,50.28,2],['Валки',35.62,49.83,2],['Красноград',35.44,49.38,2],
 ['Бровари',30.79,50.51,2],['Бориспіль',30.95,50.35,2],['Біла Церква',30.11,49.80,2],['Фастів',29.92,50.08,2],
 ['Ірпінь',30.25,50.52,2],['Буча',30.22,50.54,2],['Вишневе',30.37,50.39,2],['Васильків',30.31,50.18,2],
 ['Обухів',30.62,50.11,2],['Вишгород',30.49,50.58,2],['Славутич',30.75,51.52,2],['Переяслав',31.45,50.07,2]
];
var figNodes=[],cityNodes=[],homeNode=null; // для контр-масштабу при зумі (розмір цілей/підписів фіксований)

// Проекция контура (те же границы Украины, что в концепте) — совмещаем overlay с path.
let P=null;
function fit(){
  const path=document.getElementById('ua-path'), bb=path.getBBox();
  if(!bb.width) return false;
  const fx0=bb.x*0.1, fx1=(bb.x+bb.width)*0.1, fy0=1024-(bb.y+bb.height)*0.1, fy1=1024-bb.y*0.1;
  const FB={x:fx0,y:fy0,w:fx1-fx0,h:fy1-fy0}, pad=10, aW=300-2*pad, aH=200-2*pad;
  const s=Math.min(aW/FB.w,aH/FB.h), pw=FB.w*s, ph=FB.h*s, oX=(300-pw)/2, oY=(200-ph)/2;
  document.getElementById('ua-fit').setAttribute('transform','translate('+(oX-FB.x*s)+' '+(oY-FB.y*s)+') scale('+s+')');
  P=function(lon,lat){return [oX+((lon-22.14)/18.09)*pw, oY+((52.38-lat)/7.99)*ph];};
  return true;
}
// точка в полигоне (луч), кольцо — массив [lon,lat]
function pip(lon,lat,ring){var inside=false;for(var i=0,j=ring.length-1;i<ring.length;j=i++){var xi=ring[i][0],yi=ring[i][1],xj=ring[j][0],yj=ring[j][1];if(((yi>lat)!=(yj>lat))&&(lon<(xj-xi)*(lat-yi)/(yj-yi)+xi))inside=!inside;}return inside;}
let built=false;
function build(){
  if(built||!fit()) return;
  built=true;
  // ОБЛАСТИ: регион заливается красным, если в нём есть цели (интенсивность ∝ кол-ву) — как на реальной карте.
  OBLASTS.forEach(function(o){
    var outer=o.r[0]||[];
    var cnt=0; S.threats.forEach(function(t){ if(t.lat!=null && pip(t.lon,t.lat,outer)) cnt+=(t.n||1); });
    var fill = cnt>0 ? 'rgba(255,59,48,'+Math.min(0.60,0.20+cnt*0.07)+')' : 'rgba(70,110,90,0.10)';
    var d=o.r.map(function(ring){ if(ring.length<3)return ''; return 'M'+ring.map(function(p){var q=P(p[0],p[1]);return q[0].toFixed(1)+' '+q[1].toFixed(1);}).join(' L')+'Z'; }).join(' ');
    el('path',{'class':'obl',d:d,fill:fill});
  });
  // Города (3 рівні деталізації) — точки+підписи; видимість/розмір керує applyZoom за зумом.
  cityNodes=[];
  CITY.forEach(function(c){
    var p=P(c[1],c[2]);
    var dot=el('circle',{'class':'city',cx:p[0],cy:p[1],r:1});
    var lab=el('text',{'class':'clabel2',x:p[0],y:p[1]},c[0]);
    cityNodes.push({dot:dot,lab:lab,x:p[0],y:p[1],tier:c[3],
      bf:(c[3]===0?5.4:c[3]===1?4.4:3.6), br:(c[3]===0?1.15:c[3]===1?0.95:0.72)});
  });
  // цели: ДЕТАЛЬНИЙ СИЛУЕТ типу (шахед/крилата/балістика/КАБ/авіація) у точці цілі, ніс — куди летить.
  // Розмір ФІКСОВАНИЙ на екрані (не роздувається при зумі) — applyZoom робить контр-масштаб.
  figNodes=[];
  S.threats.forEach(function(t){
    var col=C[t.cat]||C.other, rot=(t.heading==null?0:t.heading), known=t.heading!=null;
    // прогноз курсу (під фігурою): пунктир уперед + «привид» силуета у передбаченій точці
    var pred=el('line',{'class':'pred',stroke:col});
    var ghost=el('g',{'class':'ghost'});
    figParts(t.cat).forEach(function(p){ if(p.s)return; var pa=document.createElementNS(NS,'path'); pa.setAttribute('d',p.d); pa.setAttribute('fill',col); ghost.appendChild(pa); });
    // основна фігура
    var g=el('g',{'class':'fig'});
    figParts(t.cat).forEach(function(p){
      var pa=document.createElementNS(NS,'path');
      pa.setAttribute('d',p.d);
      pa.setAttribute('fill', p.s==='dark'?'rgba(12,16,26,.5)':p.s==='light'?'rgba(255,255,255,.72)':col);
      if(!p.s){ pa.setAttribute('stroke','rgba(10,15,25,.6)'); pa.setAttribute('stroke-width','0.35'); }
      g.appendChild(pa);
    });
    var predlab = known ? el('text',{'class':'predlab',fill:col}) : null; // передбачене місто (ІІ)
    var nlab = t.n>1 ? el('text',{'class':'tlabel',fill:col},'×'+t.n) : null;   // ×N
    var plab = t.place ? el('text',{'class':'plabel',fill:col},esc(t.place)) : null; // назва міста цілі (за зумом)
    figNodes.push({g:g,pred:pred,ghost:ghost,predlab:predlab,nlab:nlab,plab:plab,
      lon0:t.lon,lat0:t.lat,mlon:t.lon,mlat:t.lat,rot:rot,known:known,base:figScale(t.cat),spd:speedKmMin(t),seen:(t.seen||S.ts)});
  });
  // дом
  var hp=P(S.home.lon,S.home.lat);
  var hr=el('circle',{'class':'you-ring',cx:hp[0],cy:hp[1],r:4});
  var hd=el('circle',{'class':'you',cx:hp[0],cy:hp[1],r:1.7});
  homeNode={ring:hr,dot:hd,x:hp[0],y:hp[1]};
  applyZoom(); // застосувати рівні деталізації та контр-масштаб під поточний зум
}
// Автозум на дом+цели (в SVG-координатах группы #zoom).
let scale=1,tx=0,ty=0;
// зсув точки (lon,lat) на dkm за курсом brg (0°=Пн, за годинниковою)
function moveKm(lon,lat,brg,dkm){var rad=Math.PI/180,la=lat*rad;
  return [lon+(dkm*Math.sin(brg*rad))/(111*Math.cos(la)), lat+(dkm*Math.cos(brg*rad))/111];}
// найближче місто до точки (для передбачення цілі ІІ), у межах ~28 км
function nearestCity(lon,lat){var best=null,bd=1e9;CITY.forEach(function(c){var dx=(c[1]-lon)*71,dy=(c[2]-lat)*111,d=Math.sqrt(dx*dx+dy*dy);if(d<bd){bd=d;best=c;}});return bd<=28?best:null;}
// Розмістити одну ціль: силует у поточній (екстрапольованій) точці + прогноз курсу вперед.
function layoutFig(f){
  var inv=1/scale, cs=P(f.mlon,f.mlat);
  f.g.setAttribute('transform','translate('+cs[0].toFixed(2)+' '+cs[1].toFixed(2)+') rotate('+f.rot.toFixed(1)+') scale('+(f.base*inv).toFixed(3)+')');
  // ІІ-прогноз: куди летить далі (пунктир + привид + передбачене місто)
  if(f.known && f.spd>0){
    var lead=Math.min(55,f.spd*10), pd=moveKm(f.mlon,f.mlat,f.rot,lead), ps=P(pd[0],pd[1]);
    f.pred.setAttribute('display',''); f.pred.setAttribute('x1',cs[0].toFixed(2)); f.pred.setAttribute('y1',cs[1].toFixed(2)); f.pred.setAttribute('x2',ps[0].toFixed(2)); f.pred.setAttribute('y2',ps[1].toFixed(2));
    f.pred.style.strokeWidth=(0.9*inv).toFixed(2)+'px'; f.pred.setAttribute('stroke-dasharray',(1.7*inv).toFixed(2)+' '+(1.5*inv).toFixed(2));
    f.ghost.setAttribute('display',''); f.ghost.setAttribute('transform','translate('+ps[0].toFixed(2)+' '+ps[1].toFixed(2)+') rotate('+f.rot.toFixed(1)+') scale('+(f.base*inv*0.92).toFixed(3)+')');
    if(f.predlab){ var nc=nearestCity(pd[0],pd[1]); if(nc){ f.predlab.setAttribute('display',''); f.predlab.textContent='→ '+nc[0]+'?'; f.predlab.setAttribute('x',(ps[0]+2*inv).toFixed(2)); f.predlab.setAttribute('y',(ps[1]-1.5*inv).toFixed(2)); f.predlab.style.fontSize=(3.2*inv).toFixed(2)+'px'; f.predlab.style.strokeWidth=(0.7*inv).toFixed(2)+'px'; } else f.predlab.setAttribute('display','none'); }
  } else { f.pred.setAttribute('display','none'); f.ghost.setAttribute('display','none'); if(f.predlab)f.predlab.setAttribute('display','none'); }
  if(f.nlab){ f.nlab.setAttribute('x',(cs[0]+5*inv).toFixed(2)); f.nlab.setAttribute('y',(cs[1]-5*inv).toFixed(2)); f.nlab.style.fontSize=(7*inv).toFixed(2)+'px'; f.nlab.style.strokeWidth=(1.0*inv).toFixed(2)+'px'; }
  if(f.plab){ var sp=scale>=2.4; f.plab.setAttribute('display',sp?'':'none'); if(sp){ f.plab.setAttribute('x',(cs[0]+5*inv).toFixed(2)); f.plab.setAttribute('y',(cs[1]+6.5*inv).toFixed(2)); f.plab.style.fontSize=(3.6*inv).toFixed(2)+'px'; f.plab.style.strokeWidth=(0.7*inv).toFixed(2)+'px'; } }
}
// Рух цілей (dead-reckoning): екстраполюємо позицію за курсом і швидкістю від часу даних.
// Коли з каналів приходить реальне місце — S оновлюється і ціль «телепортується» туди.
function animateFigs(){
  if(!P||!figNodes.length) return;
  figNodes.forEach(function(f){
    if(f.known){ var em=Math.max(0,(Date.now()-(f.seen||S.ts))/60000); // хв від останнього спостереження позиції
      var d=Math.min(60,f.spd*em), m=moveKm(f.lon0,f.lat0,f.rot,d); f.mlon=m[0]; f.mlat=m[1]; }
    layoutFig(f);
  });
}
function applyZoom(){
  document.getElementById('zoom').setAttribute('transform','translate('+tx+' '+ty+') scale('+scale+')');
  try{localStorage.setItem('wa_zoom',JSON.stringify({s:scale,tx:tx,ty:ty}));}catch(e){}
  var inv=1/scale; // контр-масштаб: екранний розмір лишається сталим попри зум (розмір/обводку задаємо через style)
  // Цілі: розмір/позиція/прогноз — через layoutFig (спільно з анімацією руху).
  figNodes.forEach(layoutFig);
  // Міста: рівні деталізації за зумом; точки/підписи не роздуваються (розмір через style).
  cityNodes.forEach(function(c){
    var show = c.tier===0 || (c.tier===1 && scale>=1.7) || (c.tier===2 && scale>=2.8);
    c.dot.setAttribute('display',show?'':'none'); c.lab.setAttribute('display',show?'':'none');
    if(show){
      c.dot.setAttribute('r',(c.br*inv).toFixed(3));
      c.lab.setAttribute('x',c.x.toFixed(2)); c.lab.setAttribute('y',(c.y-(c.br+1.6)*inv).toFixed(2));
      c.lab.style.fontSize=(c.bf*inv).toFixed(2)+'px'; c.lab.style.strokeWidth=(1.0*inv).toFixed(2)+'px';
    }
  });
  if(homeNode){ homeNode.ring.setAttribute('r',(3.4*inv).toFixed(3)); homeNode.dot.setAttribute('r',(1.5*inv).toFixed(3)); }
}
function autoFocus(){
  if(!P) return;
  var lons=[S.home.lon],lats=[S.home.lat];
  S.threats.forEach(function(t){lons.push(t.lon);lats.push(t.lat);(t.trail||[]).forEach(function(p){lons.push(p[1]);lats.push(p[0]);});});
  // Региональный охват вокруг дома: показываем восток Украины + кластер целей, не улетая
  // в уличный зум (иначе метки/подписи раздуваются вместе с группой).
  var spanLon=6.0, spanLat=4.2, cx=(Math.min(...lons)+Math.max(...lons))/2, cy=(Math.min(...lats)+Math.max(...lats))/2;
  var lo=Math.max(spanLon,(Math.max(...lons)-Math.min(...lons))*1.6), la=Math.max(spanLat,(Math.max(...lats)-Math.min(...lats))*1.6);
  var a=P(cx-lo/2,cy+la/2), b=P(cx+lo/2,cy-la/2);
  var bw=Math.abs(b[0]-a[0]), bh=Math.abs(b[1]-a[1]);
  scale=Math.max(1,Math.min(2.6, Math.min(300/bw,200/bh)));
  var mid=P(cx,cy); tx=150-mid[0]*scale; ty=100-mid[1]*scale; applyZoom();
}
function etaNow(t){if(t.eta==null)return null;return Math.max(0,t.eta-(Date.now()-S.ts)/60000);}
function fmt(m){if(m==null)return '?';return m<1?'<1 хв':'~'+Math.round(m)+' хв';}
function esc(s){return (s==null?'':''+s).replace(/[&<>]/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;'}[c];});}
var RANK={Impact:5,Overhead:4,Imminent:3,Approaching:2,Passed:1};
var SEL='all';                 // выбранное место ('all' | id)
// Все места. Дом из настроек + Киев-гость + збережені (cfg) + пользовательские.
function allPlaces(){
  var cfg=S.cfg||{}, ps=[];
  ps.push({id:'home',icon:'🏠',name:cfg.district||S.homeName||'Дім',sub:cfg.city||'',lat:S.home.lat,lon:S.home.lon,zone:'Харків'});
  if(cfg.watchKyiv) ps.push({id:'kyiv',icon:'🏙',name:'Київ',sub:'гість',lat:50.45,lon:30.52,zone:'Київ'});
  (cfg.places||[]).forEach(function(p,i){ps.push({id:'p'+i,icon:'📍',name:p.name,sub:'',lat:p.lat,lon:p.lon});});
  userPlaces().forEach(function(p,i){ps.push({id:'u'+i,icon:'📍',name:p.name,sub:'моє',lat:p.lat,lon:p.lon});});
  return ps;
}
// видимі місця (без видалених/прихованих), з позначкою paused
function myPlaces(){ return allPlaces().filter(function(p){return !isHidden(p.id);}).map(function(p){p.paused=isPaused(p.id);return p;}); }
// місця, що беруть участь у моніторингу (без тих, що на паузі)
function activePlaces(){ return myPlaces().filter(function(p){return !p.paused;}); }
var _up=null; // кэш/фолбэк, если localStorage недоступен
function userPlaces(){ if(_up)return _up; try{_up=JSON.parse(localStorage.getItem('wa_places')||'[]');}catch(e){_up=[];} return _up; }
function saveUserPlaces(a){ _up=a; try{localStorage.setItem('wa_places',JSON.stringify(a));}catch(e){} }
// ── Пауза місця: сповіщення/аналіз тимчасово вимкнені, місце лишається у списку ──
var _paused=null;
function pausedList(){ if(_paused)return _paused; try{_paused=JSON.parse(localStorage.getItem('wa_paused')||'[]');}catch(e){_paused=[];} return _paused; }
function savePausedList(a){ _paused=a; try{localStorage.setItem('wa_paused',JSON.stringify(a));}catch(e){} }
function isPaused(id){ return pausedList().indexOf(id)>=0; }
// ── Видалення місця: похідні (дім/Київ/збережені) ховаємо (можна відновити) ──
var _hidden=null;
function hiddenList(){ if(_hidden)return _hidden; try{_hidden=JSON.parse(localStorage.getItem('wa_hidden')||'[]');}catch(e){_hidden=[];} return _hidden; }
function saveHiddenList(a){ _hidden=a; try{localStorage.setItem('wa_hidden',JSON.stringify(a));}catch(e){} }
function isHidden(id){ return hiddenList().indexOf(id)>=0; }
// расстояние цель→место (км) в браузере из координат цели — работает для любого места
function kmTo(t,pl){ if(t.lat==null||pl.lat==null)return null; var R=6371,rad=Math.PI/180;
  var dLa=(pl.lat-t.lat)*rad,dLo=(pl.lon-t.lon)*rad,la1=t.lat*rad,la2=pl.lat*rad;
  var h=Math.sin(dLa/2)*Math.sin(dLa/2)+Math.cos(la1)*Math.cos(la2)*Math.sin(dLo/2)*Math.sin(dLo/2);
  return R*2*Math.atan2(Math.sqrt(h),Math.sqrt(1-h)); }
// цели, релевантные месту: ≤40 км ИЛИ та же зона; pdist = дистанция до ЭТОГО места
function placeThreats(pl){
  return S.threats.map(function(t){return {t:t,d:kmTo(t,pl)};})
    .filter(function(o){return (o.d!=null&&o.d<=40)||(pl.zone&&o.t.zone===pl.zone);})
    .map(function(o){var c=Object.assign({},o.t);c.pdist=(o.d!=null?o.d:o.t.dist);return c;})
    .sort(function(a,b){return (a.pdist==null?1e9:a.pdist)-(b.pdist==null?1e9:b.pdist);});
}
function selectedPlaces(){var ps=activePlaces();return SEL==='all'?ps:ps.filter(function(p){return p.id===SEL;});}
function selThreats(){var seen={},out=[];selectedPlaces().forEach(function(pl){placeThreats(pl).forEach(function(t){var k=t.zone+'|'+t.place+'|'+t.label;if(!seen[k]){seen[k]=1;out.push(t);}});});return out;}
function selZone(){if(SEL==='all')return null;var p=myPlaces().filter(function(x){return x.id===SEL;})[0];return p?p.zone:null;}
function worstOf(arr){var w=null;arr.forEach(function(t){var r=RANK[t.stage]||0;if(!w||r>w.r)w={r:r,t:t};});return w;}
function statusOf(w){if(w&&w.r>=3)return{cls:'alarm',title:'ТРИВОГА'};if(w)return{cls:'warn',title:'Загроза поруч'};return{cls:'calm',title:'Спокійно'};}
function placeCard(pl){
  var arr=placeThreats(pl), s=statusOf(worstOf(arr));
  var pill='<span class="pill '+(s.cls==='calm'?'on':s.cls)+'">'+s.title+'</span>';
  var detail;
  if(arr.length){var n0=arr[0], d=n0.pdist==null?'':Math.round(n0.pdist)+' км · ';
    detail='найближча: '+n0.icon+' '+esc(n0.label)+(n0.n>1?' ×'+n0.n:'')+' · '+d+'<span class="eta">'+fmt(etaNow(n0))+'</span> · '+esc(n0.stageLabel)
      +(n0.dir?' · '+esc(n0.dir):'')+(arr.length>1?' &nbsp;·&nbsp; ще '+(arr.length-1)+' поруч':'');
  } else detail='Загроз поруч немає — спокійно ✅';
  return '<div class="card '+s.cls+'"><div class="top">'+pl.icon+' '+esc(pl.name)+pill+'</div>'+
    '<div class="meta">'+(pl.sub?esc(pl.sub)+' — ':'')+detail+'</div></div>';
}
// Селектор мест — пересобираем каждый render (места могут добавляться в интерфейсе).
function renderSelectors(){
  var ps=activePlaces();
  var html='<span class="plab">Місце:</span><button data-p="all">Всі місця</button>'
    + ps.map(function(p){return '<button data-p="'+p.id+'">'+p.icon+' '+esc(p.name)+'</button>';}).join('');
  ['selNow','selAn'].forEach(function(id){var box=document.getElementById(id);if(!box)return;
    box.innerHTML=html;
    box.querySelectorAll('button').forEach(function(b){b.classList.toggle('on',b.dataset.p===SEL);b.onclick=function(){setSel(b.dataset.p);};});
  });
}
function setSel(v){SEL=v;try{localStorage.setItem('wa_sel',v);}catch(e){}render();renderAnalytics();}
// ── Аналітика: дашборд з графіками з журналу alarms.csv (S.rows) ──────────────
var PERIOD='30';
var KIND={Drone:{n:'Шахед/БпЛА',c:'#FF9F45'},Ballistic:{n:'Балістика',c:'#FF3B30'},
  Missile:{n:'Ракета',c:'#FF5A3C'},Kab:{n:'КАБ',c:'#FF6B4A'},Aviation:{n:'Авіація',c:'#F4C550'},
  Target:{n:'Ціль',c:'#8792A6'},Unknown:{n:'Інше',c:'#8792A6'}};
function dayAgo(n){return new Date(Date.now()-n*86400000).toISOString().slice(0,10);}
function rowsIn(p){var r=S.rows||[]; if(p==='all')return r; var cut=dayAgo(+p); return r.filter(function(x){return x.d>=cut;});}
function renderAnalytics(){
  var kpi=document.getElementById('aKpi'); if(!kpi) return;
  var rows=rowsIn(PERIOD), z=selZone(); if(z)rows=rows.filter(function(r){return r.z===z;});
  var al=rows.filter(function(r){return r.g!=='impact';}), im=rows.filter(function(r){return r.g==='impact';});
  var days={}; al.forEach(function(r){days[r.d]=1;}); var dc=Object.keys(days).length;
  var pd=PERIOD==='all'?Math.max(1,dc):+PERIOD;
  function tile(cls,num,lbl){return '<div class="stat '+cls+'"><div class="num">'+num+'</div><div class="lbl">'+lbl+'</div></div>';}
  kpi.innerHTML=
    tile('hot',al.length,'🔔 Тривог')+
    tile('hot',im.length,'💥 Прильотів')+
    tile('sky',S.threats.length,'🎯 Цілей зараз')+
    tile('',dc,'📅 Днів з тривогами')+
    tile('warm',(al.length/pd).toFixed(1),'📈 Середньо/день');
  document.getElementById('aDash').innerHTML =
    chartByDay(al)+chartByCat(al,'k',KIND,'За типами загроз')+chartByHour(al)+chartByCat(al,'z',null,'По містах / зонах');
}
function chartByDay(al){
  var counts={}; al.forEach(function(r){counts[r.d]=(counts[r.d]||0)+1;});
  var days=[];
  if(PERIOD!=='all'){for(var i=+PERIOD-1;i>=0;i--){var d=dayAgo(i);days.push([d,counts[d]||0]);}}
  else{Object.keys(counts).sort().forEach(function(d){days.push([d,counts[d]]);});}
  if(!days.length)days=[[dayAgo(0),0]];
  var max=Math.max.apply(null,[1].concat(days.map(function(x){return x[1];})));
  var bars=days.map(function(x){return '<div class="bar" title="'+x[0]+': '+x[1]+'" style="height:'+Math.round(x[1]/max*100)+'%"></div>';}).join('');
  var lab='<span>'+days[0][0].slice(5)+'</span><span style="margin-left:auto">'+days[days.length-1][0].slice(5)+'</span>';
  return '<div class="chart"><h4>Тривоги по днях</h4><div class="sub">пік '+max+'/день · днів: '+days.length+'</div>'+
    '<div class="bars">'+bars+'</div><div class="blabels">'+lab+'</div></div>';
}
function chartByHour(al){
  var h=[]; for(var i=0;i<24;i++)h.push(0); al.forEach(function(r){h[(r.h||0)%24]++;});
  var max=Math.max.apply(null,[1].concat(h));
  var bars=h.map(function(v,i){return '<div class="bar" title="'+i+':00 — '+v+'" style="height:'+Math.round(v/max*100)+'%"></div>';}).join('');
  return '<div class="chart"><h4>За годиною доби</h4><div class="sub">коли найчастіше тривоги</div>'+
    '<div class="bars hour">'+bars+'</div>'+
    '<div class="blabels"><span>00</span><span style="margin:0 auto">12</span><span>23</span></div></div>';
}
function chartByCat(al,field,meta,title){
  var g={}; al.forEach(function(r){var k=r[field]||'—';g[k]=(g[k]||0)+1;});
  var arr=Object.keys(g).map(function(k){return [k,g[k]];}).sort(function(a,b){return b[1]-a[1];});
  var max=Math.max.apply(null,[1].concat(arr.map(function(x){return x[1];})));
  var body=arr.length?arr.map(function(x){var m=(meta&&meta[x[0]])||{n:x[0],c:'#5AA9FF'};
    return '<div class="row"><span class="lab">'+esc(m.n)+'</span><span class="track"><span class="fill" style="width:'+Math.round(x[1]/max*100)+'%;background:'+m.c+'"></span></span><span class="val">'+x[1]+'</span></div>';
  }).join(''):'<div class="sub">немає даних за період</div>';
  return '<div class="chart"><h4>'+title+'</h4><div class="sub">за період</div><div class="hbars">'+body+'</div></div>';
}
function renderNowPlaces(){
  var box=document.getElementById('nowPlaces'); if(!box) return;
  box.innerHTML=selectedPlaces().map(placeCard).join('');
}
function render(){
  document.getElementById('homeName').textContent=S.homeName;
  document.getElementById('clock').textContent=new Date().toLocaleTimeString('uk-UA');
  var n=S.threats.length, badge=document.getElementById('badgeNow');
  badge.textContent=n; badge.classList.toggle('show',n>0);
  renderSelectors();
  // Кольцо = ситуация по ВЫБРАННОМУ месту (или по всем моим местам).
  var sel=selThreats(), w=worstOf(sel), st=document.getElementById('status');
  var pdist=function(t){return t.pdist!=null?Math.round(t.pdist):(t.dist!=null?Math.round(t.dist):'?');};
  if(w&&w.r>=3){st.className='status alarm';document.getElementById('stTitle').textContent='ТРИВОГА';
    document.getElementById('stSub').textContent=w.t.stageLabel;
    document.getElementById('stMsg').textContent='В УКРИТТЯ — '+w.t.label+' поруч';}
  else if(w){st.className='status alarm';document.getElementById('stTitle').textContent='ЗАГРОЗА';
    document.getElementById('stSub').textContent='поруч';
    document.getElementById('stMsg').textContent=w.t.label+' · '+pdist(w.t)+' км · '+fmt(etaNow(w.t));}
  else{st.className='status calm';document.getElementById('stTitle').textContent='Спокійно';
    document.getElementById('stSub').textContent='моніторинг';
    document.getElementById('stMsg').textContent = n?('по країні цілей: '+n+' (далеко)'):'Загроз для ваших місць немає';}
  renderNowPlaces(); renderAnalytics();
  // список: при выбранном месте — его цели, при «Всі» — все по стране
  var listArr = SEL==='all' ? S.threats.slice().map(function(t){return t;}) : selThreats();
  var list=document.getElementById('list');
  list.innerHTML = !listArr.length ? '<div class="empty">✅ Наразі чисто — активних цілей немає</div>'
    : listArr.map(function(t){
      var col=C[t.cat]||C.other, dv=(t.pdist!=null?t.pdist:t.dist), dist=dv==null?'?':Math.round(dv)+' км';
      return '<div class="card"><div class="top">'+t.icon+' '+esc(t.label)+(t.n>1?' <b style="color:'+col+'">×'+t.n+'</b>':'')+
        '<span class="badge" style="background:'+col+'22;color:'+col+'">'+esc(t.stageLabel)+'</span></div>'+
        '<div class="meta">'+dist+' · <span class="eta">'+fmt(etaNow(t))+'</span> · напрям '+esc(t.dir)+' · '+esc(t.place)+'</div></div>';
    }).join('');
  renderPlaces(); renderSettings(); renderAI();
}
// Крупные города для быстрого выбора координат.
var CITIES=[['Харків',49.99,36.23],['Київ',50.45,30.52],['Одеса',46.48,30.72],['Дніпро',48.47,35.05],
['Львів',49.84,24.03],['Запоріжжя',47.84,35.14],['Кривий Ріг',47.91,33.39],['Миколаїв',46.98,31.99],
['Вінниця',49.23,28.47],['Херсон',46.64,32.61],['Полтава',49.59,34.55],['Чернігів',51.50,31.29],
['Черкаси',49.44,32.06],['Житомир',50.25,28.66],['Суми',50.91,34.80],['Хмельницький',49.42,26.99],
['Чернівці',48.29,25.94],['Рівне',50.62,26.25],['Кропивницький',48.51,32.26],['Івано-Франківськ',48.92,24.71],
['Тернопіль',49.55,25.59],['Луцьк',50.75,25.34],['Ужгород',48.62,22.29],['Кременчук',49.07,33.42],
['Біла Церква',49.80,30.11],['Краматорськ',48.74,37.58],['Ізмаїл',45.35,28.84],['Умань',48.75,30.22]];
// Мои места: карточки со статусом + редактор (додати/видалити место, GPS, вибір міста).
function renderPlaces(){
  var box=document.getElementById('places'); if(!box) return;
  // Зберегти ввід форми + фокус/каретку, щоб щосекундний render() не скидав заповнення.
  var focId=(document.activeElement||{}).id||'', ss=null,se=null, fe=focId&&document.getElementById(focId);
  if(fe&&fe.setSelectionRange){try{ss=fe.selectionStart;se=fe.selectionEnd;}catch(e){}}
  var keep={}; ['wpName','wpLat','wpLon','wpCity'].forEach(function(id){var el=document.getElementById(id); if(el)keep[id]=el.value;});
  var html=myPlaces().map(function(pl){
    var arr=pl.paused?[]:placeThreats(pl), s=statusOf(worstOf(arr));
    var pill=pl.paused?'<span class="pill off">⏸ на паузі</span>':'<span class="pill '+(s.cls==='calm'?'on':s.cls)+'">'+s.title+'</span>';
    var pauseB='<button class="mini" title="'+(pl.paused?'відновити':'пауза')+'" onclick="waTogglePause(\''+pl.id+'\')">'+(pl.paused?'▶':'⏸')+'</button>';
    var delB='<button class="mini" title="видалити" onclick="waDelPlace(\''+pl.id+'\')">✕</button>';
    var meta=pl.paused?'на паузі — сповіщення та аналіз вимкнені':(pl.sub?esc(pl.sub)+' · ':'')+(arr.length?arr.length+' ціль(ей) поруч':'чисто');
    return '<div class="card '+(pl.paused?'calm':s.cls)+'"'+(pl.paused?' style="opacity:.5"':'')+'><div class="top">'+pl.icon+' '+esc(pl.name)+pill+pauseB+delB+'</div>'+
      '<div class="meta">'+meta+'</div></div>';
  }).join('');
  var hid=allPlaces().filter(function(p){return isHidden(p.id);});
  if(hid.length){
    html+='<div class="card" style="margin-top:14px"><div class="top">🗑 Видалені місця ('+hid.length+')</div><div class="meta">'
      + hid.map(function(p){return '<span style="white-space:nowrap;margin-right:12px;display:inline-block">'+p.icon+' '+esc(p.name)+' <button class="mini" title="відновити" onclick="waRestore(\''+p.id+'\')">↩</button></span>';}).join('')
      + '</div></div>';
  }
  html+='<div class="card" style="margin-top:14px"><div class="top">➕ Додати місце</div><div class="frm">'
    + '<input id="wpName" placeholder="Назва (напр. Дача, Бабуся, Робота)">'
    + '<select id="wpCity"><option value="">— обрати місто зі списку —</option>'
    + CITIES.map(function(c){return '<option value="'+c[1]+','+c[2]+'">'+c[0]+'</option>';}).join('')+'</select>'
    + '<div class="row2"><input id="wpLat" placeholder="широта" inputmode="decimal"><input id="wpLon" placeholder="довгота" inputmode="decimal"></div>'
    + '<div class="row2"><button class="gpsbtn" onclick="waGps()">📍 За GPS</button><button class="savebtn" onclick="waAddPlace()">Зберегти місце</button></div>'
    + '<div class="meta" id="wpMsg">Обери місто, натисни «За GPS» або впиши координати вручну.</div></div></div>';
  box.innerHTML=html;
  var sel=document.getElementById('wpCity');
  if(sel) sel.onchange=function(){ if(!sel.value)return; var v=sel.value.split(',');
    document.getElementById('wpLat').value=v[0]; document.getElementById('wpLon').value=v[1];
    var nm=document.getElementById('wpName'); if(!nm.value) nm.value=sel.options[sel.selectedIndex].text; };
  // Відновити збережений ввід і фокус/каретку.
  ['wpName','wpLat','wpLon','wpCity'].forEach(function(id){ if(keep[id]!=null){var el=document.getElementById(id); if(el)el.value=keep[id];}});
  if(focId){var el=document.getElementById(focId); if(el){try{el.focus(); if(ss!=null&&el.setSelectionRange)el.setSelectionRange(ss,se);}catch(e){}}}
}
window.waGps=function(){ var m=document.getElementById('wpMsg');
  if(!navigator.geolocation){ m.textContent='GPS недоступний на цьому пристрої'; return; }
  m.textContent='Визначаю місцезнаходження…';
  navigator.geolocation.getCurrentPosition(function(pos){
    document.getElementById('wpLat').value=pos.coords.latitude.toFixed(4);
    document.getElementById('wpLon').value=pos.coords.longitude.toFixed(4);
    m.textContent='✓ Координати з GPS ('+Math.round(pos.coords.accuracy)+' м)';
  },function(){ m.textContent='Не вдалося визначити GPS — дозволь доступ або впиши вручну'; },{enableHighAccuracy:true,timeout:10000});
};
window.waAddPlace=function(){ var m=document.getElementById('wpMsg');
  var nm=document.getElementById('wpName').value.trim();
  var la=parseFloat(document.getElementById('wpLat').value), lo=parseFloat(document.getElementById('wpLon').value);
  if(!nm){ m.textContent='Впиши назву місця'; return; }
  if(isNaN(la)||isNaN(lo)||la<44||la>53||lo<22||lo>41){ m.textContent='Потрібні коректні координати (обери місто, GPS або вручну)'; return; }
  var a=userPlaces(); a.push({name:nm,lat:la,lon:lo}); saveUserPlaces(a); render();
};
window.waDelPlace=function(id){ id=''+id;
  if(id.charAt(0)==='u'){ var a=userPlaces(); a.splice(+id.slice(1),1); saveUserPlaces(a); }
  else { var h=hiddenList(); if(h.indexOf(id)<0){ h.push(id); saveHiddenList(h); } } // похідне місце — ховаємо (відновлюється)
  var p=pausedList(), pi=p.indexOf(id); if(pi>=0){ p.splice(pi,1); savePausedList(p); }
  if(SEL===id) setSel('all'); else render();
};
window.waTogglePause=function(id){ id=''+id; var p=pausedList(), i=p.indexOf(id);
  if(i>=0) p.splice(i,1); else p.push(id);
  savePausedList(p);
  if(SEL===id) setSel('all'); else render();
};
window.waRestore=function(id){ id=''+id; var h=hiddenList(), i=h.indexOf(id);
  if(i>=0){ h.splice(i,1); saveHiddenList(h); } render();
};
function renderSettings(){
  var box=document.getElementById('settings'); if(!box) return;
  var c=S.cfg; if(!c){box.innerHTML='<div class="note">Налаштування недоступні.</div>';return;}
  function kv(k,v){return '<div class="kv"><span class="k">'+k+'</span><span class="v">'+v+'</span></div>';}
  function yn(b){return b?'<span class="pill on">увімк</span>':'<span class="pill off">вимк</span>';}
  box.innerHTML=
    kv('Мій район',esc(c.district))+
    kv('Місто',esc(c.city))+
    kv('Область',esc(c.oblast))+
    kv('Стежити за Харковом',yn(c.watchKharkiv))+
    kv('Стежити за Києвом',yn(c.watchKyiv))+
    kv('Поріг відбою',c.otboiSec+' с')+
    kv('Push на телефон (ntfy)',yn(c.ntfy));
}
// ── ШІ: евристичний помічник (ризик, прогноз курсу, патерни) ─────────────────
function speedKmMin(t){var a=(t.label||'').toLowerCase();
  if(t.cat==='balist')return 67; if(t.cat==='missile')return a.indexOf('циркон')>=0?100:13;
  if(t.cat==='kab')return 15; return a.indexOf('реакт')>=0?8:3;}          // шахед ~3 км/хв
function riskOf(pl){
  var arr=placeThreats(pl);
  if(!arr.length)return {score:0,lvl:'Спокій',cls:'calm',adv:'Загроз поруч немає. Можна займатися справами.',place:pl};
  var t=arr[0], st=RANK[t.stage]||0, d=(t.pdist==null?999:t.pdist), eta=etaNow(t), score;
  if(st>=5)score=100;else if(st>=4)score=95;else if(st>=3)score=88;
  else if(d<=8)score=75;else if(d<=15)score=58;else if(d<=25)score=42;else if(d<=40)score=30;else score=15;
  score=Math.min(100,Math.round(score+Math.min(15,(arr.length-1)*4)));
  var lvl,cls,adv;
  if(score>=85){lvl='Критичний';cls='alarm';adv='В укриття НЕГАЙНО. Подалі від вікон, правило двох стін.';}
  else if(score>=55){lvl='Високий';cls='alarm';adv='Готуйся до укриття: одягнись, візьми телефон і повербанк, будь ближче до укриття.';}
  else if(score>=35){lvl='Середній';cls='warn';adv='Стеж за ситуацією — ціль у сусідньому районі/передмісті.';}
  else{lvl='Низький';cls='warn';adv='Загроза активна, але далеко. Тримай звук увімкненим.';}
  return {score:score,lvl:lvl,cls:cls,adv:adv,t:t,eta:eta,d:d,place:pl,cnt:arr.length};
}
// геометрия: ближайшее сближение цели (курс+позиция) с местом
function closest(t,pl){
  var la=pl.lat*Math.PI/180;
  var dN=(t.lat-pl.lat)*111, dE=(t.lon-pl.lon)*111*Math.cos(la);
  var th=(t.heading||0)*Math.PI/180, mE=Math.sin(th), mN=Math.cos(th);
  var rE=-dE, rN=-dN, proj=rE*mE+rN*mN;
  var min=proj<=0?Math.sqrt(rE*rE+rN*rN):Math.sqrt(Math.max(0,rE*rE+rN*rN-proj*proj));
  return {min:min, along:proj, approaching:proj>0};
}
function renderAI(){
  var nowBox=document.getElementById('aiNow'); if(!nowBox) return;
  // Блок настоящего ИИ (Claude)
  var llm=document.getElementById('aiLLM');
  if(llm){
    if(S.aiText){
      var when=S.aiAt?new Date(S.aiAt).toLocaleTimeString('uk-UA'):'';
      llm.innerHTML='<div class="card" style="border-color:rgba(90,169,255,.4)"><div class="top">🤖 Claude'+
        (when?'<span class="pill off" style="margin-left:auto">'+when+'</span>':'')+'</div>'+
        '<div class="meta" style="font-size:14px;color:var(--tx);line-height:1.55;white-space:pre-line">'+esc(S.aiText)+'</div></div>';
    } else if(S.cfg&&S.cfg.ai){
      llm.innerHTML='<div class="note">🤖 ІІ підключено. Аналіз зʼявиться під час активних загроз (або запусти <b>dotnet run -- ai</b>).</div>';
    } else {
      llm.innerHTML='<div class="note">🤖 Щоб увімкнути справжній ІІ (Claude): додай у <b>secrets.local.txt</b> рядок <b>ai_api_key=…</b> (ключ Anthropic). Модель — <b>ai_model=</b> (за замовч. claude-opus-5; дешевша — claude-haiku-4-5). Нижче — евристичний аналіз, що працює завжди.</div>';
    }
  }
  var risks=activePlaces().map(riskOf);
  var worst=risks.slice().sort(function(a,b){return b.score-a.score;})[0];
  if(worst&&worst.score>=55){
    nowBox.innerHTML='<div class="card alarm"><div class="top" style="font-size:16px">🔴 Зараз небезпечно — '+esc(worst.place.name)+'</div>'+
      '<div class="meta">'+worst.t.icon+' '+esc(worst.t.label)+' · '+Math.round(worst.d)+' км · '+fmt(worst.eta)+' · напрям '+esc(worst.t.dir)+
      '<br><b style="color:#FF9A93">➤ '+esc(worst.adv)+'</b></div></div>';
  } else if(worst&&worst.score>=35){
    nowBox.innerHTML='<div class="card warn"><div class="top" style="font-size:16px">🟡 Підвищена увага — '+esc(worst.place.name)+'</div>'+
      '<div class="meta">'+worst.t.icon+' '+esc(worst.t.label)+' · '+Math.round(worst.d)+' км · '+fmt(worst.eta)+'<br>'+esc(worst.adv)+'</div></div>';
  } else {
    var near=S.threats.slice().filter(function(t){return t.dist!=null;}).sort(function(a,b){return a.dist-b.dist;})[0];
    nowBox.innerHTML='<div class="card calm"><div class="top" style="font-size:16px">🟢 Зараз безпечно</div>'+
      '<div class="meta">Активних загроз поруч немає.'+(S.threats.length?' По країні цілей: '+S.threats.length+' (далеко). Найближча — '+(near?esc(near.label)+' '+Math.round(near.dist)+' км':'—')+'.':'')+'</div></div>';
  }
  document.getElementById('aiRisk').innerHTML=risks.map(function(r){
    var col=r.cls==='alarm'?'#FF3B30':r.cls==='warn'?'#F4C550':'#34D399';
    return '<div class="card '+r.cls+'"><div class="top">'+r.place.icon+' '+esc(r.place.name)+
      '<span class="pill '+(r.cls==='calm'?'on':r.cls)+'" style="margin-left:auto">'+r.lvl+' · '+r.score+'/100</span></div>'+
      '<div class="track" style="height:10px;margin:9px 0 7px;background:rgba(255,255,255,.06);border-radius:6px;overflow:hidden"><div style="height:100%;width:'+r.score+'%;background:'+col+';border-radius:6px"></div></div>'+
      '<div class="meta">'+esc(r.adv)+'</div></div>';
  }).join('');
  var fc=[];
  activePlaces().forEach(function(pl){
    var best=null;
    S.threats.forEach(function(t){ if(t.lat==null||t.heading==null)return; var ca=closest(t,pl); if(!best||ca.min<best.ca.min)best={t:t,ca:ca,d:kmTo(t,pl)}; });
    if(!best)return;
    var v = best.ca.approaching
      ? 'при збереженні курсу пройде за <b>~'+Math.round(best.ca.min)+' км</b> від «'+esc(pl.name)+'»'+(best.ca.min<=12?' — БЛИЗЬКО ⚠':'')
      : 'віддаляється від «'+esc(pl.name)+'»';
    fc.push('<div class="card"><div class="top">'+best.t.icon+' '+esc(best.t.label)+' → '+esc(pl.name)+'</div>'+
      '<div class="meta">зараз '+(best.d==null?'?':Math.round(best.d)+' км')+', курс '+esc(best.t.dir)+'. '+v+'.</div></div>');
  });
  document.getElementById('aiForecast').innerHTML=fc.join('')||'<div class="note">Немає активних цілей з відомим курсом.</div>';
  document.getElementById('aiPatterns').innerHTML=patternCards();
}
function patternCards(){
  var r=S.rows||[]; if(r.length<3)return '<div class="note">Ще мало даних для патернів — накопичиться з тривогами.</div>';
  var al=r.filter(function(x){return x.g!=='impact';});
  var hh=[]; for(var i=0;i<24;i++)hh.push(0); al.forEach(function(x){hh[x.h||0]++;});
  var ph=hh.indexOf(Math.max.apply(null,hh));
  var kc={}; al.forEach(function(x){kc[x.k]=(kc[x.k]||0)+1;});
  var tk=Object.keys(kc).sort(function(a,b){return kc[b]-kc[a];})[0];
  var w1=al.filter(function(x){return x.d>=dayAgo(7);}).length, w0=al.filter(function(x){return x.d>=dayAgo(14)&&x.d<dayAgo(7);}).length;
  var tr=w0===0?(w1>0?'+100%':'—'):((w1-w0>=0?'+':'')+Math.round((w1-w0)/w0*100)+'%');
  var im=r.filter(function(x){return x.g==='impact';}), zc={}; im.forEach(function(x){zc[x.z]=(zc[x.z]||0)+1;});
  var tz=Object.keys(zc).sort(function(a,b){return zc[b]-zc[a];})[0]||'—';
  function ins(icon,title,val,sub){return '<div class="chart"><h4>'+icon+' '+title+'</h4><div style="font-size:24px;font-weight:700;margin:8px 0">'+val+'</div><div class="sub">'+sub+'</div></div>';}
  return ins('🕐','Найнебезпечніша година',(ph<10?'0':'')+ph+':00','найбільше тривог у цей час')+
    ins('🎯','Найчастіша загроза',esc((KIND[tk]||{n:tk||'—'}).n),'за весь період')+
    ins('📈','Тренд за тиждень',tr,'тривог vs попередній тиждень')+
    ins('💥','Найбільше прильотів',esc(tz),'за твоїми зонами');
}
// ── вкладки ────────────────────────────────────────────────────────────────
function showView(v){
  document.querySelectorAll('.view').forEach(function(s){s.classList.toggle('on',s.id==='v-'+v);});
  document.querySelectorAll('#tabs button').forEach(function(b){b.classList.toggle('on',b.dataset.v===v);});
  try{localStorage.setItem('wa_tab',v);}catch(e){}
  if(v==='map'){ init(0); requestAnimationFrame(applyZoom); }
  if(v==='analytics'){ renderAnalytics(); }
  if(v==='ai'){ renderAI(); }
}
document.querySelectorAll('#tabs button').forEach(function(b){b.addEventListener('click',function(){showView(b.dataset.v);});});
document.querySelectorAll('#periods button').forEach(function(b){b.addEventListener('click',function(){
  PERIOD=b.dataset.d;
  document.querySelectorAll('#periods button').forEach(function(x){x.classList.toggle('on',x===b);});
  renderAnalytics();
});});
// zoom/pan (как в концепте)
(function(){
  var svg=document.getElementById('uamap');
  function toSvg(cx,cy){var r=svg.getBoundingClientRect();return [(cx-r.left)/r.width*300,(cy-r.top)/r.height*200];}
  function zoomAt(px,py,f){var ns=Math.max(1,Math.min(8,scale*f)),k=ns/scale;tx=px-(px-tx)*k;ty=py-(py-ty)*k;scale=ns;applyZoom();}
  svg.addEventListener('wheel',function(e){e.preventDefault();var p=toSvg(e.clientX,e.clientY);zoomAt(p[0],p[1],e.deltaY<0?1.15:1/1.15);},{passive:false});
  var drag=false,lx=0,ly=0;
  svg.addEventListener('pointerdown',function(e){drag=true;lx=e.clientX;ly=e.clientY;try{svg.setPointerCapture(e.pointerId);}catch(_){} });
  svg.addEventListener('pointermove',function(e){if(!drag)return;var r=svg.getBoundingClientRect();tx+=(e.clientX-lx)/r.width*300;ty+=(e.clientY-ly)/r.height*200;lx=e.clientX;ly=e.clientY;applyZoom();});
  svg.addEventListener('pointerup',function(){drag=false;});
  document.getElementById('zin').addEventListener('click',function(){zoomAt(150,100,1.3);});
  document.getElementById('zout').addEventListener('click',function(){zoomAt(150,100,1/1.3);});
  document.getElementById('zreset').addEventListener('click',function(){scale=1;tx=0;ty=0;applyZoom();});
})();
function init(n){ build(); render(); if(!built && n<40) requestAnimationFrame(function(){init(n+1);}); }
// восстановить последнюю вкладку и зум карты (переживают авто-обновление страницы)
try{var z=JSON.parse(localStorage.getItem('wa_zoom')||'null');if(z){scale=z.s;tx=z.tx;ty=z.ty;}}catch(e){}
try{SEL=localStorage.getItem('wa_sel')||'all';}catch(e){}
var savedTab='now'; try{savedTab=localStorage.getItem('wa_tab')||'now';}catch(e){}
render();
requestAnimationFrame(function(){ showView(savedTab); });
window.addEventListener('load',function(){ showView(savedTab); });
setInterval(render,1000);
setInterval(animateFigs,400); // рух цілей + прогноз курсу (dead-reckoning між оновленнями даних)
// Авто-оновлення даних (заміна meta-refresh), але НЕ перезавантажуємо, поки заповнюють поле/форму.
setInterval(function(){
  var a=document.activeElement, tag=a&&a.tagName;
  if(tag==='INPUT'||tag==='SELECT'||tag==='TEXTAREA') return; // користувач вводить — не заважаємо
  location.reload();
},4000);
</script></body></html>
""";
}
