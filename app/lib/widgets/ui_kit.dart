import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_theme.dart';

/// Small building blocks of the WarAlarm design system, shared by screens.

/// Rounded label in a tone colour ("ТРИВОГА", "через API", "×3"…).
class Tag extends StatelessWidget {
  const Tag(this.text, {super.key, this.color, this.icon, this.solid = false});

  final String text;
  final Color? color;
  final IconData? icon;

  /// Solid fill with white text instead of a soft tint.
  final bool solid;

  @override
  Widget build(BuildContext context) {
    final c = color ?? AppColors.textSecondary;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3.5),
      decoration: BoxDecoration(
        color: solid ? c : c.withValues(alpha: AppColors.isDark ? 0.16 : 0.12),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 12, color: solid ? Colors.white : c),
            const SizedBox(width: 4),
          ],
          Text(
            text,
            style: TextStyle(
              fontSize: 10.5,
              height: 1.2,
              fontWeight: FontWeight.w800,
              letterSpacing: 0.4,
              color: solid ? Colors.white : c,
              fontFeatures: AppText.tabular,
            ),
          ),
        ],
      ),
    );
  }
}

/// Uppercase eyebrow heading for a block, with optional count / trailing.
class SectionLabel extends StatelessWidget {
  const SectionLabel(this.text, {super.key, this.count, this.trailing});

  final String text;
  final String? count;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10, left: 2),
      child: Row(
        children: [
          Expanded(
            child: Row(
              children: [
                Flexible(
                  child: Text(text.toUpperCase(),
                      overflow: TextOverflow.ellipsis,
                      style: AppText.eyebrow()),
                ),
                if (count != null) ...[
                  const SizedBox(width: 8),
                  Text(count!,
                      style: AppText.eyebrow(color: AppColors.textSecondary)
                          .copyWith(fontFeatures: AppText.tabular)),
                ],
              ],
            ),
          ),
          ?trailing,
        ],
      ),
    );
  }
}

/// A status dot that softly pulses (live feed, active threat).
class LiveDot extends StatefulWidget {
  const LiveDot({super.key, required this.color, this.size = 8, this.pulse = true});

  final Color color;
  final double size;
  final bool pulse;

  @override
  State<LiveDot> createState() => _LiveDotState();
}

class _LiveDotState extends State<LiveDot> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
      vsync: this, duration: const Duration(milliseconds: 1400));

  @override
  void initState() {
    super.initState();
    if (widget.pulse) _c.repeat();
  }

  @override
  void didUpdateWidget(LiveDot old) {
    super.didUpdateWidget(old);
    if (widget.pulse && !_c.isAnimating) _c.repeat();
    if (!widget.pulse && _c.isAnimating) _c.stop();
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.size;
    return SizedBox(
      width: s * 2.4,
      height: s * 2.4,
      child: AnimatedBuilder(
        animation: _c,
        builder: (_, _) => Stack(
          alignment: Alignment.center,
          children: [
            if (widget.pulse)
              Container(
                width: s * (1 + 1.4 * _c.value),
                height: s * (1 + 1.4 * _c.value),
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: widget.color.withValues(alpha: 0.35 * (1 - _c.value)),
                ),
              ),
            Container(
              width: s,
              height: s,
              decoration:
                  BoxDecoration(shape: BoxShape.circle, color: widget.color),
            ),
          ],
        ),
      ),
    );
  }
}

/// A coloured information card with an accent stripe: connection state,
/// night summary, power outages, launch waves…
class Notice extends StatelessWidget {
  const Notice({
    super.key,
    required this.color,
    required this.title,
    this.icon,
    this.emoji,
    this.body,
    this.lines = const [],
    this.trailing,
  });

  final Color color;
  final String title;
  final IconData? icon;
  final String? emoji;
  final String? body;
  final List<String> lines;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: color.withValues(alpha: AppColors.isDark ? 0.08 : 0.07),
        borderRadius: BorderRadius.circular(AppRadii.md),
        border: Border.all(color: color.withValues(alpha: 0.28)),
      ),
      child: IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Container(width: 3, color: color),
            Expanded(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(12, 11, 12, 11),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (emoji != null)
                      Padding(
                        padding: const EdgeInsets.only(right: 10, top: 1),
                        child: Text(emoji!, style: const TextStyle(fontSize: 17)),
                      )
                    else if (icon != null)
                      Padding(
                        padding: const EdgeInsets.only(right: 10, top: 1),
                        child: Icon(icon, size: 18, color: color),
                      ),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(title,
                              style: TextStyle(
                                  fontSize: 13.5,
                                  height: 1.3,
                                  fontWeight: FontWeight.w700,
                                  color: AppColors.textPrimary)),
                          if (body != null) ...[
                            const SizedBox(height: 3),
                            Text(body!,
                                style: TextStyle(
                                    fontSize: 12.5,
                                    height: 1.4,
                                    color: AppColors.textSecondary)),
                          ],
                          for (final l in lines)
                            Padding(
                              padding: const EdgeInsets.only(top: 4),
                              child: Text(l,
                                  style: TextStyle(
                                      fontSize: 12.5,
                                      height: 1.35,
                                      color: AppColors.textSecondary,
                                      fontFeatures: AppText.tabular)),
                            ),
                        ],
                      ),
                    ),
                    ?trailing,
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// One number + caption, used in hero metrics and KPI rows.
class Metric extends StatelessWidget {
  const Metric({
    super.key,
    required this.value,
    required this.label,
    this.color,
    this.size = 22,
    this.unit,
  });

  final String value;
  final String label;
  final Color? color;
  final double size;
  final String? unit;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(label.toUpperCase(),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: AppText.eyebrow().copyWith(fontSize: 10, letterSpacing: 0.9)),
        const SizedBox(height: 6),
        Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(value, style: AppText.number(size, color: color)),
            if (unit != null) ...[
              const SizedBox(width: 3),
              Text(unit!,
                  style: TextStyle(
                      fontSize: size * 0.5,
                      fontWeight: FontWeight.w700,
                      color: AppColors.textSecondary)),
            ],
          ],
        ),
      ],
    );
  }
}

/// Square icon tile in a tone colour (list leading).
class IconTile extends StatelessWidget {
  const IconTile(this.icon, {super.key, required this.color, this.size = 40});

  final IconData icon;
  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: color.withValues(alpha: AppColors.isDark ? 0.14 : 0.11),
        borderRadius: BorderRadius.circular(AppRadii.md - 2),
      ),
      child: Icon(icon, color: color, size: size * 0.5),
    );
  }
}

/// Segmented control (period pickers, filters).
class Segmented<T> extends StatelessWidget {
  const Segmented({
    super.key,
    required this.value,
    required this.items,
    required this.onChanged,
  });

  final T value;
  final List<(T, String)> items;
  final ValueChanged<T> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        color: AppColors.surface2,
        borderRadius: BorderRadius.circular(AppRadii.md - 2),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final it in items)
            GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: () => onChanged(it.$1),
              child: AnimatedContainer(
                duration: const Duration(milliseconds: 160),
                padding:
                    const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                decoration: BoxDecoration(
                  color: it.$1 == value ? AppColors.surface : Colors.transparent,
                  borderRadius: BorderRadius.circular(7),
                  border: Border.all(
                      color: it.$1 == value
                          ? AppColors.border
                          : Colors.transparent),
                ),
                child: Text(it.$2,
                    style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                        color: it.$1 == value
                            ? AppColors.textPrimary
                            : AppColors.textMuted)),
              ),
            ),
        ],
      ),
    );
  }
}
