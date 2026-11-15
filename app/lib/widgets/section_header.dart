import 'package:flutter/material.dart';

import '../theme/app_colors.dart';

/// Screen heading: big title, optional muted subtitle, optional trailing
/// action. The icon renders as a small tile to the left.
class SectionHeader extends StatelessWidget {
  const SectionHeader({
    super.key,
    required this.title,
    this.subtitle,
    this.icon,
    this.iconColor,
    this.trailing,
  });

  final String title;
  final String? subtitle;
  final IconData? icon;

  /// Tone of the icon tile (defaults to the interactive blue).
  final Color? iconColor;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        if (icon != null) ...[
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(15),
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: [
                  (iconColor ?? AppColors.primary).withValues(alpha: AppColors.isDark ? 0.32 : 0.20),
                  (iconColor ?? AppColors.primary).withValues(alpha: AppColors.isDark ? 0.10 : 0.06),
                ],
              ),
              border: Border.all(
                  color: (iconColor ?? AppColors.primary).withValues(alpha: 0.35)),
            ),
            child: Icon(icon, color: iconColor ?? AppColors.primary, size: 24),
          ),
          const SizedBox(width: 14),
        ],
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: TextStyle(
                  fontSize: 27,
                  height: 1.12,
                  fontWeight: FontWeight.w800,
                  color: AppColors.textPrimary,
                  letterSpacing: -0.6,
                ),
              ),
              if (subtitle != null) ...[
                const SizedBox(height: 3),
                Text(
                  subtitle!,
                  style: TextStyle(
                    fontSize: 13.5,
                    height: 1.3,
                    color: AppColors.textSecondary,
                  ),
                ),
              ],
            ],
          ),
        ),
        ?trailing,
      ],
    );
  }
}
