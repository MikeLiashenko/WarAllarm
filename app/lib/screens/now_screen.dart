import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../controllers/settings_controller.dart';
import '../data/ukraine_geo.dart';
import '../domain/probability.dart';
import '../domain/situation.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import '../widgets/analytics_dashboard.dart';
import '../widgets/floating_nav.dart';
import '../widgets/glass.dart';
import '../widgets/place_selector.dart';
import '../widgets/probability_gauge.dart';
import '../widgets/radar_scope.dart';
import '../widgets/shelters_sheet.dart';
import '../widgets/ui_kit.dart';
import 'home_shell.dart';

/// Everything «Зараз» and the map card show, derived once per build.
class Situation {
  Situation(this.data, this.alarm, AlertsController alerts) {
    stale = data.stale;
    scope = NowScreen.scopeOf(data);
    // Place-level danger uses only real positions — a target pinned to an
    // oblast centre ("… (обл.)") isn't 0 km from that city. Same rule as the
    // server's RiskModel (state.risk) and the siren.
    final real = data.alarmTargets;
    final seen = <String>{};
    for (final pl in scope) {
      for (final t in placeThreats(pl, real)) {
        if (seen.add('${t.zone}|${t.place}|${t.label}')) sel.add(t);
      }
    }
    probs = scope
        .map((p) => probabilityFor(p, real,
            officialLevel: officialLevelFor(p, alerts.levels)))
        .toList()
      ..sort((a, b) => b.percent.compareTo(a.percent));
    prob = probs.isEmpty ? null : probs.first;
    // The card / radar follow the most endangered place.
    place = prob?.place ?? scope.first;
    focus = placeThreats(place, real);
    nearest = focus.isEmpty ? null : focus.first;

    final worst = worstOf(sel);
    final rank = worst == null ? 0 : (kRank[worst.stage] ?? 0);
    active = !stale && (alarm.level == 'alarm' || rank >= 3);
    threat = !stale && worst != null;

    if (stale) {
      tone = AppColors.warn;
      status = 'Дані застаріли';
      message = 'Монітор не оновлює дані ${fmtAge(data.dataAge)} '
          '(останні — о ${fmtClock(data.stateTs).substring(0, 5)}). '
          'Тривога за цими даними не вмикається.';
    } else if (!data.live && !data.isDemo) {
      tone = AppColors.textMuted;
      status = 'Немає даних';
      message = 'Чекаю монітор… Це не означає «чисто».';
    } else if (active) {
      tone = AppColors.threat;
      status = 'Тривога — в укриття';
      message = '${worst?.label ?? 'Загроза'} поруч · ${worst?.stageLabel ?? ''}';
    } else if (threat) {
      tone = AppColors.warn;
      status = 'Загроза поруч';
      message = '${worst!.label} · ${worst.pdist?.round() ?? '?'} км · '
          '${fmtEta(etaNow(worst, data.stateTs))}';
    } else {
      tone = AppColors.safe;
      status = 'Спокійно';
      message = data.targets.isEmpty
          ? 'Загроз для ваших місць немає'
          : 'По країні цілей: ${data.targets.length} — далеко';
    }
  }

  final MapDataController data;
  final AlarmController alarm;
  late final bool stale;
  late final List<MonitoredPlace> scope;
  final List<MapTarget> sel = [];
  late final List<ThreatProbability> probs;
  late final ThreatProbability? prob;
  late final MonitoredPlace place;
  late final List<MapTarget> focus;
  late final MapTarget? nearest;
  late final bool active;
  late final bool threat;
  late final Color tone;
  late final String status;
  late final String message;
}

class NowScreen extends StatefulWidget {
  const NowScreen({super.key});

  /// The places the screen reports on: the selection, or home when the user
  /// hasn't saved any places yet (otherwise everything would read "calm").
  static List<MonitoredPlace> scopeOf(MapDataController d) {
    final sel = d.selectedPlaces;
    if (sel.isNotEmpty) return sel;
    return [
      MonitoredPlace(
          id: 'home',
          icon: '🏠',
          name: d.homeName,
          lat: d.home.lat,
          lon: d.home.lon),
    ];
  }

  @override
  State<NowScreen> createState() => _NowScreenState();
}

class _NowScreenState extends State<NowScreen> {
  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final alerts = context.watch<AlertsController>();
    final settings = context.watch<SettingsController>();
    final s = Situation(data, alarm, alerts);
    const gap = SizedBox(height: 22);

    final events = _eventNotices(s);
    final radar = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionLabel('Радар'),
        _Panel(
          child: Center(
            child: RadarScope(
              lat: s.place.lat,
              lon: s.place.lon,
              targets: s.stale
                  ? const []
                  : (data.isDemo ? data.targets : data.alarmTargets),
              tone: s.tone,
              active: s.active,
              alarmRadiusKm: settings.alarmRadiusKm,
              size: 260,
            ),
          ),
        ),
      ],
    );
    final prob = s.prob != null && !s.stale
        ? Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SectionLabel('Ймовірність удару',
                  trailing: Tag('через API')),
              ProbabilityGauge(prob: s.prob!),
            ],
          )
        : null;
    final places = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionLabel('Мої місця', count: '${s.scope.length}'),
        for (final p in s.scope)
          _PlaceRow(
            place: p,
            data: data,
            stale: s.stale,
            prob: s.probs.where((x) => x.place.id == p.id).firstOrNull,
          ),
      ],
    );
    final targets = data.selectedId == 'all' ? data.targets : s.sel;
    final targetList = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionLabel(
          data.selectedId == 'all' ? 'Цілі · вся Україна' : 'Цілі поруч',
          // «Вся Україна» → скільки бортів у повітрі всього (сума ×N).
          count:
              data.selectedId == 'all' ? '~${data.airborne}' : '${s.sel.length}',
          trailing: s.stale ? Tag('застарілі', color: AppColors.warn) : null,
        ),
        if (targets.isEmpty)
          _EmptyTargets(live: data.fresh)
        else
          _Panel(
            padding: EdgeInsets.zero,
            child: Column(
              children: [
                for (var i = 0; i < targets.length; i++) ...[
                  if (i > 0) Divider(height: 1, color: AppColors.divider),
                  _TargetRow(
                      t: targets[i], stateTs: data.stateTs, stale: s.stale),
                ],
              ],
            ),
          ),
      ],
    );

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Hero(s: s),
          const SizedBox(height: 14),
          // Country-wide picture at a glance; tap → the map.
          GestureDetector(
            behavior: HitTestBehavior.opaque,
            onTap: () => context.read<NavController>().select(AppTab.map),
            child: ThreatCounters(data: data, alerts: alerts, onSurface: true),
          ),
          gap,
          ...events,
          LayoutBuilder(builder: (context, c) {
            if (c.maxWidth >= 860) {
              return Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    flex: 5,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [radar, if (prob != null) ...[gap, prob]],
                    ),
                  ),
                  const SizedBox(width: 20),
                  Expanded(
                    flex: 6,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [places, gap, targetList],
                    ),
                  ),
                ],
              );
            }
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                radar,
                if (prob != null) ...[gap, prob],
                gap,
                places,
                gap,
                targetList,
              ],
            );
          }),
          gap,
          AnalyticsDashboard(rows: data.rows, targetsNow: data.targets.length),
          const SizedBox(height: 8),
        ],
      ),
    );
  }

  /// Night summary, power outages and the latest launch wave.
  List<Widget> _eventNotices(Situation s) {
    final data = s.data;
    final list = <Widget>[
      if (data.isDemo)
        Notice(
          color: AppColors.warn,
          icon: Icons.science_outlined,
          title: 'Демо-дані — це приклад, а не реальні загрози',
          body: 'Монітор не підключено.',
        ),
      // 🚀 Остання хвиля пусків (з каналу моніторів через бота).
      if (data.hasRecentLaunch)
        Notice(
          color: const Color(0xFFFF7A3C),
          emoji: '🚀',
          title: 'Пуск ~${data.launched} шахедів'
              '${data.launchedAt.isNotEmpty ? ' о ${data.launchedAt}' : ''}',
          body: data.launchOrigins.isNotEmpty ? data.launchOrigins : null,
        ),
      // 🔌 Світло (відключення/відновлення).
      if (data.powerStatus.isNotEmpty)
        Notice(
          color: data.powerStatus == 'off' ? AppColors.warn : AppColors.safe,
          emoji: data.powerStatus == 'off' ? '🔌' : '💡',
          title: data.powerStatus == 'off'
              ? 'Світло: відключення — ${data.powerArea}'
              : 'Світло відновлюють — ${data.powerArea}',
          // Світло по всьому Києву: які черги зараз без світла.
          body: data.powerGroupsOff.isNotEmpty
              ? 'Без світла черги: ${data.powerGroupsOff.join(', ')}'
                  '${data.powerAt.isNotEmpty ? ' (з ${data.powerAt})' : ''}'
              : null,
        ),
      // 🌙 Підсумок ночі (останній, що зібрав бот).
      if (data.hasNight)
        Notice(
          color: AppColors.night,
          emoji: '🌙',
          title: 'Підсумок ночі · ${data.nightPeriod}',
          lines: [
            if (data.nightLaunched > 0)
              'Запущено ~${data.nightLaunched} (хвиль: ${data.nightWaves})',
            if (data.nightMaxAir > 0) 'Максимум у повітрі ~${data.nightMaxAir}',
            if (data.nightTop.isNotEmpty) 'Найбільше: ${data.nightTop}',
            if (data.nightImpacts > 0)
              'Вибухи: ${data.nightImpacts} — ${data.nightImpactAt}',
          ],
        ),
    ];
    if (list.isEmpty) return const [];
    return [
      const SectionLabel('Події'),
      for (final w in list)
        Padding(padding: const EdgeInsets.only(bottom: 8), child: w),
      const SizedBox(height: 12),
    ];
  }
}


/// The top of «Зараз»: the place card as a hero — tone glow, status, strike
/// chance, place chips, key numbers and the shelter button.
class _Hero extends StatelessWidget {
  const _Hero({required this.s});
  final Situation s;

  @override
  Widget build(BuildContext context) {
    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(AppRadii.xl + 4),
        border: Border.all(
            color: s.active
                ? AppColors.threat.withValues(alpha: 0.6)
                : AppColors.glassBorder),
        gradient: RadialGradient(
          center: const Alignment(-0.9, -1.0),
          radius: 1.4,
          colors: [
            Color.alphaBlend(
                s.tone.withValues(alpha: AppColors.isDark ? 0.18 : 0.12),
                AppColors.surface),
            AppColors.surface,
          ],
        ),
        boxShadow: [
          BoxShadow(
              color: s.tone.withValues(alpha: 0.12),
              blurRadius: 40,
              spreadRadius: -12,
              offset: const Offset(0, 18)),
        ],
      ),
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          PlaceHeader(s: s, big: true),
          const SizedBox(height: 16),
          const PlaceSelector(),
          const SizedBox(height: 14),
          _Metrics(s: s),
          const SizedBox(height: 14),
          FilledButton.icon(
            onPressed: () => showSheltersSheet(
                context, s.place.lat, s.place.lon, s.place.name),
            style: FilledButton.styleFrom(
              backgroundColor: s.active ? AppColors.threat : AppColors.primary,
              padding: const EdgeInsets.symmetric(vertical: 15),
            ),
            icon: const Icon(Icons.shield_outlined, size: 19),
            label: const Text('Найближче укриття'),
          ),
        ],
      ),
    );
  }
}
/// Collapsed-sheet card: place name, status dot + word, strike chance.
class PlaceHeader extends StatelessWidget {
  const PlaceHeader({super.key, required this.s, this.big = false});
  final Situation s;

  /// Hero size on «Зараз» (bigger name and ring).
  final bool big;

  @override
  Widget build(BuildContext context) {
    final pct = s.prob?.percent;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(s.place.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: big ? 30 : 24,
                      height: 1.1,
                      fontWeight: FontWeight.w800,
                      letterSpacing: big ? -0.9 : -0.6,
                      color: AppColors.textPrimary)),
              const SizedBox(height: 6),
              Row(
                children: [
                  LiveDot(
                      color: s.tone,
                      size: 8,
                      pulse: s.active || (s.data.fresh && !s.threat)),
                  const SizedBox(width: 2),
                  Flexible(
                    child: Text(s.status,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                            fontSize: 15,
                            fontWeight: FontWeight.w700,
                            color: s.active
                                ? AppColors.threat
                                : AppColors.textPrimary)),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(s.message,
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: 13,
                      height: 1.35,
                      color: AppColors.textSecondary,
                      fontFeatures: AppText.tabular)),
            ],
          ),
        ),
        if (pct != null && !s.stale) ...[
          const SizedBox(width: 12),
          _PercentRing(
              percent: pct,
              color: AppColors.forCls(s.prob!.cls),
              size: big ? 78 : 64),
        ],
      ],
    );
  }
}

/// Small ring with the strike chance in the middle.
class _PercentRing extends StatelessWidget {
  const _PercentRing(
      {required this.percent, required this.color, this.size = 64});
  final int percent;
  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: size,
      height: size,
      child: Stack(
        alignment: Alignment.center,
        children: [
          SizedBox(
            width: size,
            height: size,
            child: CircularProgressIndicator(
              value: percent / 100,
              strokeWidth: 5,
              strokeCap: StrokeCap.round,
              backgroundColor: AppColors.textMuted.withValues(alpha: 0.18),
              color: color,
            ),
          ),
          Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('$percent%', style: AppText.number(size / 4, color: color)),
              const SizedBox(height: 2),
              Text('удар',
                  style: TextStyle(fontSize: 9.5, color: AppColors.textMuted)),
            ],
          ),
        ],
      ),
    );
  }
}

class _Metrics extends StatelessWidget {
  const _Metrics({required this.s});
  final Situation s;

  @override
  Widget build(BuildContext context) {
    final n = s.nearest;
    final eta = (n == null || s.stale) ? null : etaNow(n, s.data.stateTs);
    return _Panel(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      child: Row(
        children: [
          Expanded(
              child: Metric(
                  value: s.stale ? '—' : '${s.focus.length}', label: 'Поруч')),
          Expanded(
            child: Metric(
                value: s.stale ? '—' : (n?.pdist?.round().toString() ?? '—'),
                unit: !s.stale && n?.pdist != null ? 'км' : null,
                label: 'Найближча'),
          ),
          Expanded(
              child: Metric(
                  value: eta == null ? '—' : fmtEta(eta),
                  label: 'Підліт',
                  size: 18)),
        ],
      ),
    );
  }
}

/// Glass-friendly inner panel (slightly raised surface).
class _Panel extends StatelessWidget {
  const _Panel({required this.child, this.padding = const EdgeInsets.all(14)});
  final Widget child;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return Container(
      clipBehavior: Clip.antiAlias,
      padding: padding,
      decoration: BoxDecoration(
        color: AppColors.textPrimary
            .withValues(alpha: AppColors.isDark ? 0.04 : 0.03),
        borderRadius: BorderRadius.circular(AppRadii.lg),
        border: Border.all(color: AppColors.glassBorder),
      ),
      child: child,
    );
  }
}

class _EmptyTargets extends StatelessWidget {
  const _EmptyTargets({required this.live});
  final bool live;

  @override
  Widget build(BuildContext context) {
    final c = live ? AppColors.safe : AppColors.textMuted;
    return _Panel(
      padding: const EdgeInsets.symmetric(vertical: 24, horizontal: 16),
      child: Column(
        children: [
          Icon(live ? Icons.verified_outlined : Icons.cloud_off_outlined,
              color: c, size: 28),
          const SizedBox(height: 8),
          Text(
              live
                  ? 'Наразі чисто — активних цілей немає'
                  : 'Немає свіжих даних від монітора',
              textAlign: TextAlign.center,
              style: TextStyle(
                  fontWeight: FontWeight.w700, color: AppColors.textPrimary)),
        ],
      ),
    );
  }
}

class _PlaceRow extends StatelessWidget {
  const _PlaceRow({
    required this.place,
    required this.data,
    required this.stale,
    this.prob,
  });
  final MonitoredPlace place;
  final MapDataController data;
  final bool stale;
  final ThreatProbability? prob;

  @override
  Widget build(BuildContext context) {
    final arr =
        stale ? const <MapTarget>[] : placeThreats(place, data.alarmTargets);
    final s = statusOf(worstOf(arr));
    final accent = stale ? AppColors.textMuted : AppColors.forCls(s.cls);

    String detail;
    if (stale) {
      detail = 'Немає свіжих даних';
    } else if (arr.isNotEmpty) {
      final n = arr.first;
      detail = '${n.label}${n.amount > 1 ? ' ×${n.amount}' : ''} · '
          '${n.pdist?.round() ?? '?'} км · ${fmtEta(etaNow(n, data.stateTs))}'
          '${arr.length > 1 ? ' · ще ${arr.length - 1}' : ''}';
    } else {
      detail = 'Загроз поруч немає';
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: _Panel(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
        child: Row(
          children: [
            Container(
              width: 10,
              height: 10,
              decoration: BoxDecoration(
                color: accent,
                shape: BoxShape.circle,
                boxShadow: [
                  BoxShadow(color: accent.withValues(alpha: 0.5), blurRadius: 8),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(place.name,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 14.5,
                          fontWeight: FontWeight.w700,
                          color: AppColors.textPrimary)),
                  const SizedBox(height: 3),
                  Text(detail,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 12.5,
                          color: AppColors.textSecondary,
                          fontFeatures: AppText.tabular)),
                ],
              ),
            ),
            if (prob != null && !stale) ...[
              const SizedBox(width: 10),
              Text('${prob!.percent}%',
                  style: AppText.number(18, color: accent)),
            ] else if (!stale) ...[
              const SizedBox(width: 10),
              Tag(s.title, color: accent),
            ],
          ],
        ),
      ),
    );
  }
}

class _TargetRow extends StatelessWidget {
  const _TargetRow(
      {required this.t, required this.stateTs, required this.stale});
  final MapTarget t;
  final int stateTs;
  final bool stale;

  @override
  Widget build(BuildContext context) {
    final threat = !stale && (kRank[t.stage] ?? 0) >= 3;
    final kind = kThreatColors[t.cat] ?? kThreatColors['other']!;
    final dist = t.pdist ?? t.distKm;
    return Opacity(
      opacity: stale ? 0.55 : 1,
      child: Container(
        color: threat ? AppColors.threat.withValues(alpha: 0.07) : null,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
        child: Row(
          children: [
            Container(
              width: 36,
              height: 36,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: const Color(0xF0101419),
                shape: BoxShape.circle,
                border: Border.all(
                    color: threat ? AppColors.threat : kind, width: 1.6),
              ),
              child: ThreatGlyph(t.cat, size: 20, heading: t.heading ?? 0),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          t.label,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w700,
                              color: threat
                                  ? AppColors.threatDark
                                  : AppColors.textPrimary),
                        ),
                      ),
                      if (t.amount > 1) ...[
                        const SizedBox(width: 6),
                        Text('×${t.amount}',
                            style: AppText.number(13, color: kind)),
                      ],
                    ],
                  ),
                  const SizedBox(height: 3),
                  Text(
                    [
                      if (t.stageLabel.isNotEmpty) t.stageLabel,
                      if (t.dir.isNotEmpty) 'напрям ${t.dir}',
                      if (t.place.isNotEmpty) t.place,
                    ].join(' · '),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style:
                        TextStyle(fontSize: 12, color: AppColors.textSecondary),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 10),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(dist != null ? '${dist.round()} км' : '—',
                    style: AppText.number(15,
                        color:
                            threat ? AppColors.threat : AppColors.textPrimary)),
                if (!stale) ...[
                  const SizedBox(height: 4),
                  Text(fmtEta(etaNow(t, stateTs)),
                      style: TextStyle(
                          fontSize: 11.5,
                          color: AppColors.textMuted,
                          fontFeatures: AppText.tabular)),
                ],
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Floating counters over the map: one glass chip per threat kind (with the
/// map's own silhouette), official alerts, and the snapshot time.
class ThreatCounters extends StatelessWidget {
  const ThreatCounters(
      {super.key, required this.data, required this.alerts, this.onSurface = false});
  final MapDataController data;
  final AlertsController alerts;

  /// On a plain page (not over the map): skip the timestamp chip — the hero
  /// card already shows freshness.
  final bool onSurface;

  static const _order = ['shahed', 'missile', 'balist', 'kab', 'avia', 'other'];

  @override
  Widget build(BuildContext context) {
    final counts = <String, int>{};
    for (final t in data.targets) {
      final k = _order.contains(t.cat) ? t.cat : 'other';
      counts[k] = (counts[k] ?? 0) + t.amount;
    }
    final stale = data.stale;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final k in _order)
              if ((counts[k] ?? 0) > 0)
                Glass(
                  radius: 14,
                  padding: const EdgeInsets.fromLTRB(10, 7, 12, 7),
                  child: Opacity(
                    opacity: stale ? 0.5 : 1,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        ThreatGlyph(k, size: 20),
                        const SizedBox(width: 8),
                        Text('${counts[k]}', style: AppText.number(20)),
                      ],
                    ),
                  ),
                ),
            if (counts.isEmpty && data.fresh)
              Glass(
                radius: 14,
                padding: const EdgeInsets.fromLTRB(10, 8, 12, 8),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(Icons.verified_outlined,
                        size: 18, color: AppColors.safe),
                    const SizedBox(width: 6),
                    Text('Цілей немає',
                        style: TextStyle(
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary)),
                  ],
                ),
              ),
            if (alerts.ok && alerts.redCount + alerts.yellowCount > 0)
              Glass(
                radius: 14,
                padding: const EdgeInsets.fromLTRB(10, 8, 12, 8),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 10,
                      height: 10,
                      decoration: BoxDecoration(
                          color: AppColors.threat,
                          borderRadius: BorderRadius.circular(3)),
                    ),
                    const SizedBox(width: 7),
                    Text('${alerts.redCount}', style: AppText.number(16)),
                    if (alerts.yellowCount > 0) ...[
                      const SizedBox(width: 10),
                      Container(
                        width: 10,
                        height: 10,
                        decoration: BoxDecoration(
                            color: AppColors.highlight,
                            borderRadius: BorderRadius.circular(3)),
                      ),
                      const SizedBox(width: 7),
                      Text('${alerts.yellowCount}', style: AppText.number(16)),
                    ],
                    const SizedBox(width: 5),
                    Text('обл.',
                        style: TextStyle(
                            fontSize: 12, color: AppColors.textSecondary)),
                  ],
                ),
              ),
          ],
        ),
        if (!onSurface) ...[
        const SizedBox(height: 8),
        Glass(
          radius: 10,
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
          color: stale ? AppColors.warn.withValues(alpha: 0.88) : null,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (stale) ...[
                const Icon(Icons.warning_amber_rounded,
                    size: 14, color: Colors.black87),
                const SizedBox(width: 5),
              ],
              Text(
                !data.live
                    ? 'немає зв\'язку'
                    : stale
                        ? 'дані застарілі · ${fmtAge(data.dataAge)} тому'
                        : 'станом на ${fmtClock(data.stateTs)}',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                  color: stale ? Colors.black87 : AppColors.textSecondary,
                  fontFeatures: AppText.tabular,
                ),
              ),
            ],
          ),
        ),
        ],
      ],
    );
  }
}
