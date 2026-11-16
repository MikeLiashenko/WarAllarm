import 'package:flutter/material.dart';

import '../domain/danger_stars.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';

/// Колір рівня: 0–1 спокійно, 2–3 обережно, 4–5 небезпечно.
Color starsTone(int stars) => stars >= 4
    ? AppColors.threat
    : stars >= 2
        ? AppColors.warn
        : AppColors.safe;

/// ★★★☆☆ — ряд із п'яти зірок.
class StarsRow extends StatelessWidget {
  const StarsRow({super.key, required this.stars, this.size = 22});
  final int stars;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tone = starsTone(stars);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 0; i < 5; i++)
          Icon(
            i < stars ? Icons.star_rounded : Icons.star_outline_rounded,
            size: size,
            color: i < stars ? tone : AppColors.textMuted.withValues(alpha: 0.55),
          ),
      ],
    );
  }
}

/// ⭐ «Небезпека надворі»: зірки 0–5, рівень, порада і що найнебезпечніше.
class DangerStarsCard extends StatelessWidget {
  const DangerStarsCard({super.key, required this.d, this.stale = false});
  final DangerStars d;
  final bool stale;

  @override
  Widget build(BuildContext context) {
    final tone = stale ? AppColors.textMuted : starsTone(d.stars);
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      decoration: BoxDecoration(
        color: Color.alphaBlend(tone.withValues(alpha: AppColors.isDark ? 0.10 : 0.07), AppColors.surface2),
        borderRadius: BorderRadius.circular(AppRadii.lg),
        border: Border.all(color: tone.withValues(alpha: 0.35)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Небезпека надворі',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textSecondary,
                  ),
                ),
              ),
              Text(
                stale ? '—' : '${d.stars}/5 · ${d.level.toLowerCase()}',
                style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: tone),
              ),
            ],
          ),
          const SizedBox(height: 6),
          StarsRow(stars: stale ? 0 : d.stars, size: 30),
          const SizedBox(height: 8),
          Text(
            stale ? 'Дані застаріли — оцінки немає.' : d.advice,
            style: TextStyle(
              fontSize: 14,
              height: 1.3,
              fontWeight: FontWeight.w600,
              color: AppColors.textPrimary,
            ),
          ),
          if (!stale && d.reason.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text(
              d.reason,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 12.5, color: AppColors.textMuted),
            ),
          ],
        ],
      ),
    );
  }
}
