import 'dart:ui';

import 'package:flutter/material.dart';

import '../theme/app_colors.dart';

/// Elegant frosted-glass container: subtle blur + translucent white overlay.
class GlassContainer extends StatelessWidget {
  const GlassContainer({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(20),
    this.margin,
    this.borderRadius = 22,
    this.blur = 18,
    this.color,
    this.borderColor,
    this.onTap,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final EdgeInsetsGeometry? margin;
  final double borderRadius;
  final double blur;
  final Color? color;
  final Color? borderColor;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final radius = BorderRadius.circular(borderRadius);
    final content = ClipRRect(
      borderRadius: radius,
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: blur, sigmaY: blur),
        child: Container(
          padding: padding,
          decoration: BoxDecoration(
            color: color ?? AppColors.glassFill,
            borderRadius: radius,
            border: Border.all(
              color: borderColor ?? AppColors.glassStroke,
              width: 1.2,
            ),
            boxShadow: const [
              BoxShadow(
                color: AppColors.glassShadow,
                blurRadius: 30,
                spreadRadius: -6,
                offset: Offset(0, 14),
              ),
            ],
          ),
          child: child,
        ),
      ),
    );

    if (onTap == null) return Padding(padding: margin ?? EdgeInsets.zero, child: content);
    return Padding(
      padding: margin ?? EdgeInsets.zero,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: radius,
          onTap: onTap,
          child: content,
        ),
      ),
    );
  }
}
