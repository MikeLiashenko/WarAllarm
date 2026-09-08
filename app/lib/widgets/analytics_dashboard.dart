import 'package:flutter/material.dart';

import '../domain/analytics.dart';
import '../models/analytics_row.dart';
import '../theme/app_colors.dart';
import 'glass_container.dart';

/// Analytics dashboard ported from the Reader's `map.html` — KPIs + charts by
/// day, hour and threat type, computed from the alarm journal (`rows`).
class AnalyticsDashboard extends StatefulWidget {
  const AnalyticsDashboard({
    super.key,
    required this.rows,
    required this.targetsNow,
  });

  final List<AnalyticsRow> rows;
  final int targetsNow;

  @override
  State<AnalyticsDashboard> createState() => _AnalyticsDashboardState();
}

class _AnalyticsDashboardState extends State<AnalyticsDashboard> {
  String _period = '30';
  static const _periods = [('7', '7 днів'), ('30', '30 днів'), ('90', '90 днів'), ('all', 'Усе')];

  @override
  Widget build(BuildContext context) {
    final rows = rowsIn(widget.rows, _period);
    final alarms = rows.where((r) => !r.isImpact).toList();
    final kpi = computeKpis(rows, _period);

    return GlassContainer(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text('Аналітика',
                  style: TextStyle(
                      fontSize: 15.5,
                      fontWeight: FontWeight.w700,
                      color: AppColors.textPrimary)),
              const Spacer(),
              _periodTabs(),
            ],
          ),
          const SizedBox(height: 16),
          _kpiRow(kpi),
          const SizedBox(height: 20),
          _barChart(
            'Тривоги по днях',
            'пік ${byDay(alarms, _period).map((e) => e.$2).fold(0, (a, b) => a > b ? a : b)}/день',
            byDay(alarms, _period).map((e) => e.$2).toList(),
            AppColors.primary,
            firstLabel: byDay(alarms, _period).first.$1.substring(5),
            lastLabel: byDay(alarms, _period).last.$1.substring(5),
          ),
          const SizedBox(height: 18),
          _barChart(
            'За годиною доби',
            'коли найчастіше тривоги',
            byHour(alarms),
            const Color(0xFFF4B740),
            firstLabel: '00',
            midLabel: '12',
            lastLabel: '23',
          ),
          const SizedBox(height: 18),
          _typeBars(alarms),
        ],
      ),
    );
  }

  Widget _periodTabs() {
    return Container(
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        color: AppColors.primarySoft.withValues(alpha: 0.6),
        borderRadius: BorderRadius.circular(11),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final p in _periods)
            GestureDetector(
              onTap: () => setState(() => _period = p.$1),
              child: AnimatedContainer(
                duration: const Duration(milliseconds: 160),
                padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 6),
                decoration: BoxDecoration(
                  color: _period == p.$1 ? AppColors.primary : Colors.transparent,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(p.$2,
                    style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                        color: _period == p.$1
                            ? Colors.white
                            : AppColors.textSecondary)),
              ),
            ),
        ],
      ),
    );
  }

  Widget _kpiRow(AnalyticsKpis k) {
    final tiles = [
      ('${k.alarms}', '🔔 Тривог', AppColors.threat),
      ('${k.impacts}', '💥 Прильотів', AppColors.threat),
      ('${widget.targetsNow}', '🎯 Цілей зараз', AppColors.primary),
      ('${k.daysWithAlarms}', '📅 Днів', AppColors.textPrimary),
      (k.avgPerDay.toStringAsFixed(1), '📈 Сер./день', const Color(0xFFF4B740)),
    ];
    return Wrap(
      spacing: 10,
      runSpacing: 10,
      children: [
        for (final t in tiles)
          Container(
            width: 96,
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
            decoration: BoxDecoration(
              color: AppColors.surface,
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: AppColors.border),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(t.$1,
                    style: TextStyle(
                        fontSize: 22,
                        fontWeight: FontWeight.w800,
                        color: t.$3)),
                const SizedBox(height: 2),
                Text(t.$2,
                    style: TextStyle(
                        fontSize: 11, color: AppColors.textSecondary)),
              ],
            ),
          ),
      ],
    );
  }

  Widget _barChart(String title, String sub, List<int> values, Color color,
      {String? firstLabel, String? midLabel, String? lastLabel}) {
    final max = values.fold(1, (a, b) => a > b ? a : b);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title,
            style: TextStyle(
                fontSize: 13.5,
                fontWeight: FontWeight.w700,
                color: AppColors.textPrimary)),
        Text(sub,
            style: TextStyle(fontSize: 11.5, color: AppColors.textMuted)),
        const SizedBox(height: 12),
        SizedBox(
          height: 90,
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              for (final v in values)
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 0.8),
                    child: Container(
                      height: (v / max * 90).clamp(2, 90).toDouble(),
                      decoration: BoxDecoration(
                        color: color.withValues(alpha: v == 0 ? 0.15 : 0.9),
                        borderRadius: const BorderRadius.vertical(
                            top: Radius.circular(2)),
                      ),
                    ),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 5),
        Row(
          children: [
            Text(firstLabel ?? '',
                style: TextStyle(fontSize: 10, color: AppColors.textMuted)),
            const Spacer(),
            if (midLabel != null)
              Text(midLabel,
                  style:
                      TextStyle(fontSize: 10, color: AppColors.textMuted)),
            const Spacer(),
            Text(lastLabel ?? '',
                style: TextStyle(fontSize: 10, color: AppColors.textMuted)),
          ],
        ),
      ],
    );
  }

  Widget _typeBars(List<AnalyticsRow> alarms) {
    final data = byField(alarms, true);
    final max = data.isEmpty ? 1 : data.first.$2;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('За типами загроз',
            style: TextStyle(
                fontSize: 13.5,
                fontWeight: FontWeight.w700,
                color: AppColors.textPrimary)),
        const SizedBox(height: 12),
        if (data.isEmpty)
          Text('немає даних за період',
              style: TextStyle(fontSize: 12.5, color: AppColors.textMuted))
        else
          for (final e in data)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Row(
                children: [
                  SizedBox(
                    width: 92,
                    child: Text(kKindMeta[e.$1]?.name ?? e.$1,
                        style: TextStyle(
                            fontSize: 12.5, color: AppColors.textSecondary),
                        overflow: TextOverflow.ellipsis),
                  ),
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(6),
                      child: Container(
                        height: 16,
                        color: AppColors.divider,
                        alignment: Alignment.centerLeft,
                        child: FractionallySizedBox(
                          widthFactor: (e.$2 / max).clamp(0.02, 1),
                          child: Container(
                            color: kKindMeta[e.$1]?.color ?? AppColors.primary,
                          ),
                        ),
                      ),
                    ),
                  ),
                  SizedBox(
                    width: 30,
                    child: Text('${e.$2}',
                        textAlign: TextAlign.right,
                        style: TextStyle(
                            fontSize: 12.5,
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary)),
                  ),
                ],
              ),
            ),
      ],
    );
  }
}
