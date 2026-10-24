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
import '../widgets/glass_container.dart';
import '../widgets/place_selector.dart';
import '../widgets/probability_gauge.dart';
import '../widgets/radar_scope.dart';
import '../widgets/responsive.dart';
import '../widgets/shelters_sheet.dart';
import '../widgets/ui_kit.dart';
import 'home_shell.dart';

class NowScreen extends StatelessWidget {
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

  /// Union of place-relevant threats across the scoped places (deduped).
  List<MapTarget> _selThreats(MapDataController d, List<MonitoredPlace> scope) {
    final seen = <String>{};
    final out = <MapTarget>[];
    for (final pl in scope) {
      for (final t in placeThreats(pl, d.targets)) {
        final k = '${t.zone}|${t.place}|${t.label}';
        if (seen.add(k)) out.add(t);
      }
    }
    return out;
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final alerts = context.watch<AlertsController>();
    final scope = scopeOf(data);
    final sel = _selThreats(data, scope);

    // Attack-probability per selected place, strongest first.
    final probs = scope
        .map((p) => probabilityFor(p, data.targets,
            officialLevel: officialLevelFor(p, alerts.levels)))
        .toList()
      ..sort((a, b) => b.percent.compareTo(a.percent));
    final probByPlace = {for (final pr in probs) pr.place.id: pr};

    final targets = data.selectedId == 'all' ? data.targets : sel;
    final events = _events(data);

    final blocks = (
      prob: probs.isEmpty
          ? null
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SectionLabel('Ймовірність удару',
                    trailing: Tag('через API')),
                ProbabilityGauge(prob: probs.first),
              ],
            ),
      places: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionLabel('Мої місця', count: '${scope.length}'),
          ...scope.map(
              (p) => _PlaceCard(place: p, data: data, prob: probByPlace[p.id])),
        ],
      ),
      events: events.isEmpty
          ? null
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SectionLabel('Події'),
                for (final e in events)
                  Padding(padding: const EdgeInsets.only(bottom: 8), child: e),
              ],
            ),
      targets: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionLabel(
            data.selectedId == 'all' ? 'Цілі · вся Україна' : 'Цілі поруч',
            // «Вся Україна» → скільки бортів у повітрі всього (сума ×N).
            count: data.selectedId == 'all' ? '~${data.airborne}' : '${sel.length}',
          ),
          if (targets.isEmpty)
            _EmptyTargets(live: data.live)
          else
            GlassContainer(
              padding: EdgeInsets.zero,
              child: Column(
                children: [
                  for (var i = 0; i < targets.length; i++) ...[
                    if (i > 0) Divider(height: 1, color: AppColors.divider),
                    _TargetRow(t: targets[i], stateTs: data.stateTs),
                  ],
                ],
              ),
            ),
        ],
      ),
    );

    const gap = SizedBox(height: 22);

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const PlaceSelector(),
          const SizedBox(height: 14),
          if (data.isDemo) ...[
            Notice(
              color: AppColors.warn,
              icon: Icons.science_outlined,
              title: 'Демо-дані — це приклад, а не реальні загрози',
              body: 'Монітор не підключено.',
            ),
            const SizedBox(height: 14),
          ] else if (!data.live) ...[
            Notice(
              color: AppColors.primary,
              title: 'Очікую монітор… Дані ще не отримано',
              body: 'Це не означає «чисто» — звук тривоги вмикається, '
                  'коли надійдуть живі дані.',
              trailing: SizedBox(
                width: 16,
                height: 16,
                child: CircularProgressIndicator(
                    strokeWidth: 2, color: AppColors.primary),
              ),
            ),
            const SizedBox(height: 14),
          ],
          _StatusHero(
            data: data,
            alarm: alarm,
            sel: sel,
            prob: probs.isEmpty ? null : probs.first,
          ),
          gap,
          LayoutBuilder(builder: (context, c) {
            if (c.maxWidth >= 900) {
              return Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    flex: 5,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (blocks.prob != null) ...[blocks.prob!, gap],
                        blocks.places,
                      ],
                    ),
                  ),
                  const SizedBox(width: 20),
                  Expanded(
                    flex: 6,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (blocks.events != null) ...[blocks.events!, gap],
                        blocks.targets,
                      ],
                    ),
                  ),
                ],
              );
            }
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (blocks.events != null) ...[blocks.events!, gap],
                if (blocks.prob != null) ...[blocks.prob!, gap],
                blocks.places,
                gap,
                blocks.targets,
              ],
            );
          }),
          gap,
          AnalyticsDashboard(rows: data.rows, targetsNow: data.targets.length),
          const SizedBox(height: 12),
        ],
      ),
    );
  }

  /// Night summary, power outages and the latest launch wave.
  List<Widget> _events(MapDataController data) => [
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
              if (data.nightMaxAir > 0)
                'Максимум у повітрі ~${data.nightMaxAir}',
              if (data.nightTop.isNotEmpty) 'Найбільше: ${data.nightTop}',
              if (data.nightImpacts > 0)
                'Вибухи: ${data.nightImpacts} — ${data.nightImpactAt}',
            ],
          ),
      ];
}

/// The big status block: radar around the place + verdict + key numbers.
class _StatusHero extends StatelessWidget {
  const _StatusHero({
    required this.data,
    required this.alarm,
    required this.sel,
    required this.prob,
  });

  final MapDataController data;
  final AlarmController alarm;
  final List<MapTarget> sel;
  final ThreatProbability? prob;

  @override
  Widget build(BuildContext context) {
    final settings = context.watch<SettingsController>();
    final worst = worstOf(sel);
    final rank = worst == null ? 0 : (kRank[worst.stage] ?? 0);
    // Red when the real alarm logic (distance to the selected place) fires,
    // not only when the home-relative stage says so.
    final active = alarm.level == 'alarm' || rank >= 3;
    final scope = NowScreen.scopeOf(data);
    // Centre the radar on the most endangered place (highest strike chance).
    final place = prob?.place ?? scope.first;
    final region = scope.length > 1
        ? '${place.name} · найнебезпечніше з ${scope.length}'
        : place.name;
    final lat = place.lat;
    final lon = place.lon;

    final tone = active
        ? AppColors.threat
        : worst != null
            ? AppColors.warn
            : AppColors.safe;
    final word = active ? 'ТРИВОГА' : (worst != null ? 'ЗАГРОЗА' : 'СПОКІЙ');

    String headline;
    String message;
    if (active) {
      headline = 'В укриття';
      message = '${worst?.label ?? 'Загроза'} поруч · ${worst?.stageLabel ?? ''}';
    } else if (worst != null) {
      headline = 'Загроза поруч';
      message = '${worst.label} · ${worst.pdist?.round() ?? '?'} км · '
          '${fmtEta(etaNow(worst, data.stateTs))}';
    } else {
      headline = 'Спокійно';
      message = data.targets.isEmpty
          ? 'Загроз для ваших місць немає'
          : 'По країні цілей: ${data.targets.length} — далеко';
    }

    // Metrics describe the radar's place (placeThreats is sorted by distance).
    final focus = placeThreats(place, data.targets);
    final nearest = focus.isEmpty ? null : focus.first;
    final nearestEta = nearest == null ? null : etaNow(nearest, data.stateTs);
    final upd = DateTime.fromMillisecondsSinceEpoch(data.stateTs);
    final updStr =
        '${upd.hour.toString().padLeft(2, '0')}:${upd.minute.toString().padLeft(2, '0')}';

    final compact = Responsive.isCompact(context);
    final radar = RadarScope(
      lat: lat,
      lon: lon,
      targets: data.isDemo ? data.targets : data.alarmTargets,
      tone: tone,
      active: active,
      alarmRadiusKm: settings.alarmRadiusKm,
      size: compact ? 250 : 260,
    );

    final info = Column(
      crossAxisAlignment:
          compact ? CrossAxisAlignment.center : CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          mainAxisSize: compact ? MainAxisSize.min : MainAxisSize.max,
          children: [
            Tag(word, color: tone, solid: true),
            const SizedBox(width: 10),
            Icon(Icons.place_outlined, size: 14, color: AppColors.textMuted),
            const SizedBox(width: 3),
            Flexible(
              child: Text(region,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                      color: AppColors.textSecondary)),
            ),
          ],
        ),
        const SizedBox(height: 12),
        Text(headline,
            textAlign: compact ? TextAlign.center : TextAlign.start,
            style: TextStyle(
                fontSize: compact ? 32 : 40,
                height: 1.05,
                fontWeight: FontWeight.w900,
                letterSpacing: -1.2,
                color: active ? AppColors.threat : AppColors.textPrimary)),
        const SizedBox(height: 6),
        Text(message,
            textAlign: compact ? TextAlign.center : TextAlign.start,
            style: TextStyle(
                fontSize: 14.5,
                height: 1.35,
                color: AppColors.textSecondary,
                fontFeatures: AppText.tabular)),
        const SizedBox(height: 20),
        Wrap(
          spacing: 28,
          runSpacing: 14,
          alignment: compact ? WrapAlignment.center : WrapAlignment.start,
          children: [
            if (prob != null)
              Metric(
                  value: '${prob!.percent}',
                  unit: '%',
                  label: 'Ймовірність',
                  color: AppColors.forCls(prob!.cls)),
            Metric(value: '${focus.length}', label: 'Поруч'),
            Metric(
                value: nearest?.pdist?.round().toString() ?? '—',
                unit: nearest?.pdist != null ? 'км' : null,
                label: 'Найближча'),
            Metric(
                value: nearestEta == null ? '—' : fmtEta(nearestEta),
                label: 'Підліт',
                size: 18),
          ],
        ),
        const SizedBox(height: 20),
        Wrap(
          spacing: 10,
          runSpacing: 10,
          alignment: compact ? WrapAlignment.center : WrapAlignment.start,
          children: [
            FilledButton.icon(
              onPressed: () => showSheltersSheet(
                  context, lat, lon, place.name),
              style: FilledButton.styleFrom(
                backgroundColor: active ? AppColors.threat : AppColors.primary,
              ),
              icon: const Icon(Icons.shield_outlined, size: 18),
              label: const Text('Найближче укриття'),
            ),
            OutlinedButton.icon(
              onPressed: () => context.read<NavController>().select(AppTab.map),
              icon: const Icon(Icons.map_outlined, size: 18),
              label: const Text('Карта'),
            ),
          ],
        ),
      ],
    );

    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(AppRadii.xl),
        border: Border.all(
            color: active
                ? AppColors.threat.withValues(alpha: 0.6)
                : AppColors.border),
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [
            tone.withValues(alpha: AppColors.isDark ? 0.10 : 0.07),
            AppColors.surface,
            AppColors.surface,
          ],
          stops: const [0, 0.55, 1],
        ),
      ),
      child: Stack(
        children: [
          Positioned(
            right: 16,
            top: 14,
            child: Row(
              children: [
                if (data.live) ...[
                  LiveDot(color: AppColors.safe, size: 6),
                  const SizedBox(width: 2),
                ],
                Text(data.live ? 'оновлено $updStr' : 'офлайн',
                    style: TextStyle(
                        fontSize: 11.5,
                        color: AppColors.textMuted,
                        fontFeatures: AppText.tabular)),
              ],
            ),
          ),
          Padding(
            padding: EdgeInsets.fromLTRB(
                compact ? 16 : 24, compact ? 36 : 26, compact ? 16 : 28, 24),
            child: compact
                ? Column(children: [radar, const SizedBox(height: 14), info])
                : Row(
                    children: [
                      radar,
                      const SizedBox(width: 32),
                      Expanded(child: info),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}

class _EmptyTargets extends StatelessWidget {
  const _EmptyTargets({required this.live});
  final bool live;

  @override
  Widget build(BuildContext context) {
    final c = live ? AppColors.safe : AppColors.textMuted;
    return GlassContainer(
      padding: const EdgeInsets.symmetric(vertical: 26, horizontal: 16),
      child: Column(
        children: [
          Icon(live ? Icons.verified_outlined : Icons.cloud_off_outlined,
              color: c, size: 28),
          const SizedBox(height: 8),
          Text(
              live
                  ? 'Наразі чисто — активних цілей немає'
                  : 'Немає даних від монітора',
              style: TextStyle(
                  fontWeight: FontWeight.w700, color: AppColors.textPrimary)),
        ],
      ),
    );
  }
}

class _PlaceCard extends StatelessWidget {
  const _PlaceCard({required this.place, required this.data, this.prob});
  final MonitoredPlace place;
  final MapDataController data;
  final ThreatProbability? prob;

  @override
  Widget build(BuildContext context) {
    final arr = placeThreats(place, data.targets);
    final s = statusOf(worstOf(arr));
    final accent = AppColors.forCls(s.cls);

    String detail;
    if (arr.isNotEmpty) {
      final n = arr.first;
      detail = '${n.label}${n.amount > 1 ? ' ×${n.amount}' : ''} · '
          '${n.pdist?.round() ?? '?'} км · ${fmtEta(etaNow(n, data.stateTs))} · ${n.stageLabel}'
          '${arr.length > 1 ? ' · ще ${arr.length - 1}' : ''}';
    } else {
      detail = 'Загроз поруч немає';
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Container(
        clipBehavior: Clip.antiAlias,
        decoration: BoxDecoration(
          color: AppColors.surface,
          borderRadius: BorderRadius.circular(AppRadii.lg),
          border: Border.all(color: AppColors.border),
        ),
        child: IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(width: 3, color: accent),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
                  child: Row(
                    children: [
                      Text(place.icon, style: const TextStyle(fontSize: 20)),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Flexible(
                                  child: Text(place.name,
                                      overflow: TextOverflow.ellipsis,
                                      style: TextStyle(
                                          fontSize: 14.5,
                                          fontWeight: FontWeight.w700,
                                          color: AppColors.textPrimary)),
                                ),
                                const SizedBox(width: 8),
                                Tag(s.title, color: accent),
                              ],
                            ),
                            const SizedBox(height: 4),
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
                      if (prob != null) ...[
                        const SizedBox(width: 12),
                        Column(
                          crossAxisAlignment: CrossAxisAlignment.end,
                          children: [
                            Text('${prob!.percent}%',
                                style: AppText.number(20, color: accent)),
                            const SizedBox(height: 3),
                            Text('удар',
                                style: TextStyle(
                                    fontSize: 10.5,
                                    color: AppColors.textMuted)),
                          ],
                        ),
                      ],
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _TargetRow extends StatelessWidget {
  const _TargetRow({required this.t, required this.stateTs});
  final MapTarget t;
  final int stateTs;

  @override
  Widget build(BuildContext context) {
    final threat = (kRank[t.stage] ?? 0) >= 3;
    final kind = kThreatColors[t.cat] ?? kThreatColors['other']!;
    final dist = t.pdist ?? t.distKm;
    return Container(
      color: threat ? AppColors.threat.withValues(alpha: 0.06) : null,
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      child: Row(
        children: [
          IconTile(
              threat ? Icons.warning_amber_rounded : Icons.navigation_rounded,
              color: threat ? AppColors.threat : kind,
              size: 38),
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
                    const SizedBox(width: 8),
                    if (t.stageLabel.isNotEmpty)
                      Tag(t.stageLabel,
                          color: threat ? AppColors.threat : AppColors.textSecondary),
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  [
                    if (t.dir.isNotEmpty) 'напрям ${t.dir}',
                    if (t.place.isNotEmpty) t.place,
                  ].join(' · '),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 12.5, color: AppColors.textSecondary),
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
                      color: threat ? AppColors.threat : AppColors.textPrimary)),
              const SizedBox(height: 4),
              Text(fmtEta(etaNow(t, stateTs)),
                  style: TextStyle(
                      fontSize: 11.5,
                      color: AppColors.textMuted,
                      fontFeatures: AppText.tabular)),
            ],
          ),
        ],
      ),
    );
  }
}
