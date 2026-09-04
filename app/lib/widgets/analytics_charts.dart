import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import 'glass_container.dart';

/// Analytics block for the Now screen: light-blue themed area chart of alert
/// activity over 24h plus stat tiles. Strictly blue palette.
class AnalyticsSection extends StatelessWidget {
  const AnalyticsSection({
    super.key,
    required this.hourly,
    required this.eventsToday,
    required this.threatsToday,
    required this.connected,
  });

  final List<int> hourly;
  final int eventsToday;
  final int threatsToday;
  final bool connected;

  @override
  Widget build(BuildContext context) {
    return GlassContainer(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Text(
                'Activity · last 24h',
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                  color: AppColors.textPrimary,
                ),
              ),
              const Spacer(),
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                decoration: BoxDecoration(
                  color: connected ? AppColors.safeSoft : AppColors.primarySoft,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Row(
                  children: [
                    Icon(
                      connected ? Icons.wifi_rounded : Icons.wifi_off_rounded,
                      size: 13,
                      color: connected ? AppColors.safe : AppColors.primary,
                    ),
                    const SizedBox(width: 5),
                    Text(
                      connected ? 'Live' : 'Demo',
                      style: TextStyle(
                        fontSize: 11.5,
                        fontWeight: FontWeight.w700,
                        color: connected ? AppColors.safe : AppColors.primary,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 18),
          SizedBox(height: 130, child: _AreaChart(values: hourly)),
          const SizedBox(height: 18),
          Row(
            children: [
              _StatTile(
                label: 'Events today',
                value: '$eventsToday',
                icon: Icons.timeline_rounded,
              ),
              const SizedBox(width: 12),
              _StatTile(
                label: 'Threats today',
                value: '$threatsToday',
                icon: Icons.crisis_alert_rounded,
                emphasize: threatsToday > 0,
              ),
              const SizedBox(width: 12),
              _StatTile(
                label: 'Peak / h',
                value: '${hourly.isEmpty ? 0 : hourly.reduce((a, b) => a > b ? a : b)}',
                icon: Icons.show_chart_rounded,
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _AreaChart extends StatelessWidget {
  const _AreaChart({required this.values});
  final List<int> values;

  @override
  Widget build(BuildContext context) {
    final maxY = (values.isEmpty
            ? 1
            : values.reduce((a, b) => a > b ? a : b))
        .toDouble()
        .clamp(1, double.infinity);

    final spots = <FlSpot>[
      for (var i = 0; i < values.length; i++)
        FlSpot(i.toDouble(), values[i].toDouble()),
    ];

    return LineChart(
      LineChartData(
        minY: 0,
        maxY: maxY + 1,
        gridData: FlGridData(
          show: true,
          drawVerticalLine: false,
          horizontalInterval: (maxY / 2).ceilToDouble().clamp(1, double.infinity),
          getDrawingHorizontalLine: (_) =>
              const FlLine(color: AppColors.divider, strokeWidth: 1),
        ),
        titlesData: FlTitlesData(
          topTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          rightTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          leftTitles:
              const AxisTitles(sideTitles: SideTitles(showTitles: false)),
          bottomTitles: AxisTitles(
            sideTitles: SideTitles(
              showTitles: true,
              interval: 6,
              getTitlesWidget: (value, meta) {
                final h = value.toInt();
                final labels = {0: '-24h', 6: '-18h', 12: '-12h', 18: '-6h'};
                if (!labels.containsKey(h)) return const SizedBox.shrink();
                return Padding(
                  padding: const EdgeInsets.only(top: 6),
                  child: Text(
                    labels[h]!,
                    style: const TextStyle(
                        fontSize: 10, color: AppColors.textMuted),
                  ),
                );
              },
            ),
          ),
        ),
        borderData: FlBorderData(show: false),
        lineTouchData: LineTouchData(
          touchTooltipData: LineTouchTooltipData(
            getTooltipColor: (_) => AppColors.primaryDark,
            getTooltipItems: (spots) => spots
                .map((s) => LineTooltipItem(
                      '${s.y.toInt()} events',
                      const TextStyle(
                          color: Colors.white,
                          fontWeight: FontWeight.w600,
                          fontSize: 12),
                    ))
                .toList(),
          ),
        ),
        lineBarsData: [
          LineChartBarData(
            spots: spots,
            isCurved: true,
            curveSmoothness: 0.28,
            barWidth: 3,
            gradient: const LinearGradient(colors: AppColors.chartGradient),
            dotData: const FlDotData(show: false),
            belowBarData: BarAreaData(
              show: true,
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [
                  AppColors.primary.withValues(alpha: 0.28),
                  AppColors.primary.withValues(alpha: 0.02),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.label,
    required this.value,
    required this.icon,
    this.emphasize = false,
  });

  final String label;
  final String value;
  final IconData icon;
  final bool emphasize;

  @override
  Widget build(BuildContext context) {
    final color = emphasize ? AppColors.threat : AppColors.primary;
    final bg = emphasize ? AppColors.threatSoft : AppColors.primarySoft;
    return Expanded(
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 14),
        decoration: BoxDecoration(
          color: bg.withValues(alpha: 0.55),
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: AppColors.border),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 18, color: color),
            const SizedBox(height: 10),
            Text(
              value,
              style: TextStyle(
                fontSize: 22,
                fontWeight: FontWeight.w800,
                color: emphasize ? AppColors.threatDark : AppColors.textPrimary,
              ),
            ),
            const SizedBox(height: 2),
            Text(
              label,
              style: const TextStyle(
                  fontSize: 11.5, color: AppColors.textSecondary),
            ),
          ],
        ),
      ),
    );
  }
}
