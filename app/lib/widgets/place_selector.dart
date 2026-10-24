import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/map_data_controller.dart';
import '../theme/app_colors.dart';

/// Horizontal place-filter chips (Всі місця + each monitored place), shared by
/// the Now and AI screens. Mirrors the Reader's selector.
class PlaceSelector extends StatelessWidget {
  const PlaceSelector({super.key});

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final chips = <(String, String)>[
      ('all', '🇺🇦 Всі місця'),
      for (final p in data.places) (p.id, '${p.icon} ${p.name}'),
    ];

    return SizedBox(
      height: 36,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: chips.length,
        separatorBuilder: (_, _) => const SizedBox(width: 6),
        itemBuilder: (context, i) {
          final active = data.selectedId == chips[i].$1;
          return Material(
            color: Colors.transparent,
            child: InkWell(
              borderRadius: BorderRadius.circular(18),
              onTap: () => data.select(chips[i].$1),
              child: AnimatedContainer(
                duration: const Duration(milliseconds: 160),
                padding: const EdgeInsets.symmetric(horizontal: 14),
                decoration: BoxDecoration(
                  color: active ? AppColors.textPrimary : AppColors.surface,
                  borderRadius: BorderRadius.circular(18),
                  border: Border.all(
                      color: active ? AppColors.textPrimary : AppColors.border),
                ),
                child: Center(
                  child: Text(
                    chips[i].$2,
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: active
                          ? AppColors.background
                          : AppColors.textSecondary,
                    ),
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}
