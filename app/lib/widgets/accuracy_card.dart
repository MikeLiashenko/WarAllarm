import 'package:flutter/material.dart';

import '../domain/accuracy.dart';
import '../theme/app_colors.dart';

/// "How accurate is our forecast" — a friendly quality score, hit rate and a
/// calibration chart (predicted % vs. what actually happened per bucket).
class AccuracyCard extends StatelessWidget {
  const AccuracyCard({super.key, required this.stats, required this.qualityPct});

  final AccuracyStats stats;
  final int qualityPct;

  @override
  Widget build(BuildContext context) {
    if (!stats.enough) {
      return Container(
        width: double.infinity,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: AppColors.primarySoft.withValues(alpha: 0.4),
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: AppColors.border),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Збираю дані для оцінки точності…',
                style: TextStyle(
                    fontSize: 13.5,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textPrimary)),
            const SizedBox(height: 6),
            Text(
              'Кожні кілька хвилин записую прогноз для дому й перевіряю через '
              '30 хв, чи справді була тривога. Точність зʼявиться, коли '
              'накопичиться достатньо перевірок (зараз ${stats.samples}/8).',
              style: TextStyle(
                  fontSize: 12.5, height: 1.4, color: AppColors.textSecondary),
            ),
          ],
        ),
      );
    }

    final color = qualityPct >= 70
        ? AppColors.safe
        : qualityPct >= 45
            ? AppColors.warn
            : AppColors.threat;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('$qualityPct%',
                      style: TextStyle(
                          fontSize: 34,
                          height: 1,
                          fontWeight: FontWeight.w800,
                          color: color)),
                  Text('точність моделі',
                      style: TextStyle(
                          fontSize: 11.5, color: AppColors.textMuted)),
                ],
              ),
              const SizedBox(width: 18),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _stat('Правильних передбачень', '${stats.correctPct}%'),
                    const SizedBox(height: 6),
                    _stat('Збулося тривог', '${stats.hits} з ${stats.samples}'),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Text('КАЛІБРУВАННЯ — ПРОГНОЗ vs РЕАЛЬНІСТЬ',
              style: TextStyle(
                  fontSize: 10.5,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.6,
                  color: AppColors.textMuted)),
          const SizedBox(height: 10),
          ...stats.buckets.where((b) => b.n > 0).map(_bucketRow),
          const SizedBox(height: 8),
          Text(
            'Ідеально, коли «прогноз» ≈ «реальність»: сказали 70% — і справді '
            'сталося приблизно у 70% випадків.',
            style: TextStyle(fontSize: 11.5, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }

  Widget _stat(String label, String value) => Row(
        children: [
          Expanded(
            child: Text(label,
                style: TextStyle(
                    fontSize: 12.5, color: AppColors.textSecondary)),
          ),
          Text(value,
              style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w800,
                  color: AppColors.textPrimary)),
        ],
      );

  Widget _bucketRow(CalBucket b) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 9),
      child: Row(
        children: [
          SizedBox(
            width: 58,
            child: Text('${b.lo}–${b.hi}%',
                style: TextStyle(
                    fontSize: 11.5,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textSecondary)),
          ),
          Expanded(
            child: Column(
              children: [
                _miniBar(b.predictedAvg, AppColors.primary, 'прогноз'),
                const SizedBox(height: 3),
                _miniBar(b.actualRate, AppColors.threat, 'реально'),
              ],
            ),
          ),
          SizedBox(
            width: 34,
            child: Text('×${b.n}',
                textAlign: TextAlign.right,
                style: TextStyle(fontSize: 11, color: AppColors.textMuted)),
          ),
        ],
      ),
    );
  }

  Widget _miniBar(double pct, Color c, String tag) {
    return Row(
      children: [
        SizedBox(
          width: 52,
          child: Text(tag,
              style: TextStyle(fontSize: 9.5, color: AppColors.textMuted)),
        ),
        Expanded(
          child: ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: (pct / 100).clamp(0.0, 1.0),
              minHeight: 7,
              backgroundColor: AppColors.divider,
              valueColor: AlwaysStoppedAnimation(c),
            ),
          ),
        ),
        SizedBox(
          width: 34,
          child: Text('${pct.round()}%',
              textAlign: TextAlign.right,
              style: TextStyle(
                  fontSize: 10.5,
                  fontWeight: FontWeight.w700,
                  color: AppColors.textPrimary)),
        ),
      ],
    );
  }
}
