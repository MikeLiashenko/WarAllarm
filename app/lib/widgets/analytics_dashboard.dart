import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/nav_controller.dart';
import '../domain/analytics.dart';
import '../models/analytics_row.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import 'charts.dart';
import 'glass_container.dart';

/// Compact «last 7 days» analytics card for the home screen; the full
/// dashboard lives on its own tab (AnalyticsScreen).
class AnalyticsDashboard extends StatelessWidget {
  const AnalyticsDashboard({super.key, required this.rows, required this.targetsNow});

  final List<AnalyticsRow> rows;
  final int targetsNow;

  @override
  Widget build(BuildContext context) {
    final s = AnalyticsSummary.compute(rows, '7');
    final d = AnalyticsSummary.delta(s.alarms, s.prevAlarms);
    void open() => context.read<NavController>().select(AppTab.analytics);

    Widget stat(String v, String l, Color c) => Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(v, style: AppText.number(24, color: c)),
              const SizedBox(height: 3),
              Text(l, style: TextStyle(fontSize: 12, color: AppColors.textMuted)),
            ],
          ),
        );

    return GlassContainer(
      onTap: open,
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(Icons.insights_rounded, size: 20, color: AppColors.primary),
              const SizedBox(width: 8),
              Expanded(
                child: Text('Аналітика · 7 днів',
                    style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w800,
                        letterSpacing: -0.2,
                        color: AppColors.textPrimary)),
              ),
              TextButton.icon(
                onPressed: open,
                iconAlignment: IconAlignment.end,
                icon: const Icon(Icons.arrow_forward_rounded, size: 18),
                label: const Text('Відкрити'),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              stat('${s.alarms}', d == null ? 'тривог' : 'тривог · ${d > 0 ? '+' : ''}$d%', AppColors.warn),
              stat('${s.impacts}', 'прильотів', AppColors.threat),
              stat('${(s.nightShare * 100).round()}%', 'вночі', AppColors.night),
              stat('$targetsNow', 'цілей зараз', AppColors.primary),
            ],
          ),
          const SizedBox(height: 14),
          SizedBox(
            height: 56,
            child: Sparkline(s.daily.map((e) => e.$2).toList(), color: AppColors.warn, height: 56),
          ),
        ],
      ),
    );
  }
}
