import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../domain/probability.dart';
import '../domain/situation.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import '../theme/app_colors.dart';
import '../widgets/analytics_dashboard.dart';
import '../widgets/glass_container.dart';
import '../widgets/place_selector.dart';
import '../widgets/probability_gauge.dart';
import '../widgets/responsive.dart';
import '../widgets/shelters_sheet.dart';
import '../widgets/threat_circle.dart';
import 'home_shell.dart';

class NowScreen extends StatelessWidget {
  const NowScreen({super.key});

  /// Union of place-relevant threats across the selected places (deduped).
  List<MapTarget> _selThreats(MapDataController d) {
    final seen = <String>{};
    final out = <MapTarget>[];
    for (final pl in d.selectedPlaces) {
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
    final sel = _selThreats(data);

    // Attack-probability per selected place, strongest first.
    final probs = data.selectedPlaces
        .map((p) => probabilityFor(p, data.targets,
            officialLevel: officialLevelFor(p, alerts.levels)))
        .toList()
      ..sort((a, b) => b.percent.compareTo(a.percent));
    final probByPlace = {for (final pr in probs) pr.place.id: pr};
    final worst = worstOf(sel);
    final rank = worst == null ? 0 : (kRank[worst.stage] ?? 0);
    // Red when the real alarm logic (distance to the selected place) fires,
    // not only when the home-relative stage says so.
    final active = alarm.level == 'alarm' || rank >= 3;
    final region = data.selectedId == 'all'
        ? data.homeName
        : data.selectedPlaces.isEmpty
            ? data.homeName
            : data.selectedPlaces.first.name;

    String message;
    if (active) {
      message = 'В УКРИТТЯ — ${worst!.label} поруч';
    } else if (worst != null) {
      message = '${worst.label} · ${worst.pdist?.round() ?? '?'} км · '
          '${fmtEta(etaNow(worst, data.stateTs))}';
    } else {
      message = data.targets.isEmpty
          ? 'Загроз для ваших місць немає'
          : 'По країні цілей: ${data.targets.length} (далеко)';
    }

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const PlaceSelector(),
          const SizedBox(height: 14),

          if (data.isDemo) ...[
            _DemoBanner(),
            const SizedBox(height: 14),
          ] else if (!data.live) ...[
            const _ConnBanner(),
            const SizedBox(height: 14),
          ],

          // Status hero
          GlassContainer(
            padding: const EdgeInsets.symmetric(vertical: 28, horizontal: 24),
            child: Column(
              children: [
                ThreatCircle(
                  active: active,
                  label: active ? 'ТРИВОГА' : (worst != null ? 'ЗАГРОЗА' : 'СПОКІЙ'),
                  sublabel: region,
                  size: Responsive.isCompact(context) ? 200 : 236,
                ),
                const SizedBox(height: 18),
                Text(
                  active
                      ? 'ТРИВОГА · ${worst!.stageLabel}'
                      : worst != null
                          ? 'Загроза поруч'
                          : 'Спокійно',
                  style: TextStyle(
                    fontSize: 17,
                    fontWeight: FontWeight.w700,
                    color: active ? AppColors.threatDark : AppColors.textPrimary,
                  ),
                ),
                const SizedBox(height: 4),
                Text(message,
                    textAlign: TextAlign.center,
                    style: TextStyle(
                        fontSize: 13.5, color: AppColors.textSecondary)),
              ],
            ),
          ),
          const SizedBox(height: 14),

          // Nearest shelters (OSM) around the selected place / home.
          Builder(builder: (ctx) {
            final p = data.selectedPlaces.isNotEmpty
                ? data.selectedPlaces.first
                : null;
            final lat = p?.lat ?? data.home.lat;
            final lon = p?.lon ?? data.home.lon;
            final name = p?.name ?? data.homeName;
            return SizedBox(
              width: double.infinity,
              child: OutlinedButton.icon(
                onPressed: () => showSheltersSheet(ctx, lat, lon, name),
                icon: Icon(Icons.home_work_rounded, color: AppColors.primaryDark),
                label: const Text('Найближче укриття',
                    style: TextStyle(fontWeight: FontWeight.w700)),
                style: OutlinedButton.styleFrom(
                  foregroundColor: AppColors.primaryDark,
                  side: BorderSide(color: AppColors.primaryTint),
                  padding: const EdgeInsets.symmetric(vertical: 13),
                  shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(14)),
                ),
              ),
            );
          }),
          const SizedBox(height: 18),

          // Attack probability (also published over the API as state.risk)
          if (probs.isNotEmpty) ...[
            Row(
              children: [
                _sectionLabel('Ймовірність удару'),
                const SizedBox(width: 8),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                  decoration: BoxDecoration(
                    color: AppColors.primarySoft,
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: Text('через API',
                      style: TextStyle(
                          fontSize: 10.5,
                          fontWeight: FontWeight.w700,
                          color: AppColors.primaryDark)),
                ),
              ],
            ),
            const SizedBox(height: 10),
            ProbabilityGauge(prob: probs.first),
            const SizedBox(height: 18),
          ],

          // Place status cards
          _sectionLabel('Мої місця'),
          const SizedBox(height: 10),
          ...data.selectedPlaces.map(
              (p) => _PlaceCard(place: p, data: data, prob: probByPlace[p.id])),
          const SizedBox(height: 18),

          // Analytics
          AnalyticsDashboard(rows: data.rows, targetsNow: data.targets.length),
          const SizedBox(height: 20),

          // Active targets
          Row(
            children: [
              _sectionLabel(data.selectedId == 'all'
                  ? 'Активні цілі · вся Україна'
                  : 'Цілі поруч'),
              const SizedBox(width: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: AppColors.primarySoft,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Text(
                  // «Вся Україна» → скільки бортів у повітрі всього (сума ×N).
                  data.selectedId == 'all' ? '~${data.airborne}' : '${sel.length}',
                  style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w700,
                      color: AppColors.primaryDark),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          ..._targetList(data, sel).map((t) => Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: _TargetRow(t: t, stateTs: data.stateTs),
              )),
          if (_targetList(data, sel).isEmpty)
            Container(
              padding: const EdgeInsets.symmetric(vertical: 22),
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: AppColors.safeSoft,
                borderRadius: BorderRadius.circular(14),
              ),
              child: Text('✅ Наразі чисто — активних цілей немає',
                  style: TextStyle(
                      color: AppColors.safe, fontWeight: FontWeight.w600)),
            ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

  List<MapTarget> _targetList(MapDataController d, List<MapTarget> sel) =>
      d.selectedId == 'all' ? d.targets : sel;

  Widget _sectionLabel(String text) => Text(
        text.toUpperCase(),
        style: TextStyle(
          fontSize: 12,
          fontWeight: FontWeight.w800,
          letterSpacing: 0.6,
          color: AppColors.textMuted,
        ),
      );
}

class _DemoBanner extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        color: const Color(0xFFFFF2D6),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: const Color(0xFFE9B949)),
      ),
      child: Row(
        children: [
          const Icon(Icons.science_rounded, color: Color(0xFFB8860B), size: 20),
          const SizedBox(width: 10),
          const Expanded(
            child: Text(
              'ДЕМО-ДАНІ — це приклад, а не реальні загрози. '
              'Монітор не підключено.',
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w600,
                  color: Color(0xFF6B540A)),
            ),
          ),
        ],
      ),
    );
  }
}

class _ConnBanner extends StatelessWidget {
  const _ConnBanner();
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        color: AppColors.primarySoft,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.primaryTint),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 18,
            height: 18,
            child: CircularProgressIndicator(
                strokeWidth: 2.2, color: AppColors.primaryDark),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              'Очікую монітор… Дані ще не отримано. Це не означає «чисто» — '
              'звук тривоги вмикається, коли надійдуть живі дані.',
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w600,
                  color: AppColors.primaryDark),
            ),
          ),
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
    final accent = s.cls == 'alarm'
        ? AppColors.threat
        : s.cls == 'warn'
            ? const Color(0xFFF4B740)
            : AppColors.safe;

    String detail;
    if (arr.isNotEmpty) {
      final n = arr.first;
      detail = 'найближча: ${n.label}${n.amount > 1 ? ' ×${n.amount}' : ''} · '
          '${n.pdist?.round() ?? '?'} км · ${fmtEta(etaNow(n, data.stateTs))} · ${n.stageLabel}'
          '${arr.length > 1 ? ' · ще ${arr.length - 1} поруч' : ''}';
    } else {
      detail = 'Загроз поруч немає — спокійно ✅';
    }

    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 9,
                height: 9,
                margin: const EdgeInsets.only(right: 8),
                decoration: BoxDecoration(color: accent, shape: BoxShape.circle),
              ),
              Flexible(
                child: Text('${place.icon} ${place.name}',
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textPrimary)),
              ),
              const Spacer(),
              if (prob != null)
                Padding(
                  padding: const EdgeInsets.only(right: 6),
                  child: Text('${prob!.percent}%',
                      style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w800,
                          color: accent)),
                ),
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(
                  color: accent.withValues(alpha: 0.14),
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Text(s.title,
                    style: TextStyle(
                        fontSize: 11.5,
                        fontWeight: FontWeight.w700,
                        color: accent)),
              ),
            ],
          ),
          const SizedBox(height: 5),
          Text(detail,
              style: TextStyle(
                  fontSize: 12.5, color: AppColors.textSecondary)),
        ],
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
    final accent = threat ? AppColors.threat : AppColors.primary;
    final dist = t.pdist ?? t.distKm;
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: threat ? AppColors.threatSoft : AppColors.border),
      ),
      child: Row(
        children: [
          Container(
            width: 42,
            height: 42,
            decoration: BoxDecoration(
              color: threat ? AppColors.threatSoft : AppColors.primarySoft,
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(
                threat ? Icons.warning_amber_rounded : Icons.navigation_rounded,
                color: accent,
                size: 20),
          ),
          const SizedBox(width: 13),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        '${t.label}${t.amount > 1 ? '  ×${t.amount}' : ''}',
                        style: TextStyle(
                            fontSize: 14.5,
                            fontWeight: FontWeight.w700,
                            color: threat
                                ? AppColors.threatDark
                                : AppColors.textPrimary),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 8, vertical: 2),
                      decoration: BoxDecoration(
                        color: accent.withValues(alpha: 0.14),
                        borderRadius: BorderRadius.circular(20),
                      ),
                      child: Text(t.stageLabel,
                          style: TextStyle(
                              fontSize: 10.5,
                              fontWeight: FontWeight.w700,
                              color: accent)),
                    ),
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  [
                    if (dist != null) '${dist.round()} км',
                    fmtEta(etaNow(t, stateTs)),
                    if (t.dir.isNotEmpty) 'напрям ${t.dir}',
                    if (t.place.isNotEmpty) t.place,
                  ].join(' · '),
                  style: TextStyle(
                      fontSize: 12.5, color: AppColors.textSecondary),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
