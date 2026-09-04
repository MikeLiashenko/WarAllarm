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
      ('all', 'Всі місця'),
      for (final p in data.places) (p.id, '${p.icon} ${p.name}'),
    ];

    return SizedBox(
      height: 38,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: chips.length,
        separatorBuilder: (_, _) => const SizedBox(width: 8),
        itemBuilder: (context, i) {
          final active = data.selectedId == chips[i].$1;
          return GestureDetector(
            onTap: () => data.select(chips[i].$1),
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 160),
              padding: const EdgeInsets.symmetric(horizontal: 15, vertical: 8),
              decoration: BoxDecoration(
                color: active ? AppColors.primary : AppColors.surface,
                borderRadius: BorderRadius.circular(20),
                border: Border.all(
                    color: active ? AppColors.primary : AppColors.border),
              ),
              child: Center(
                child: Text(
                  chips[i].$2,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: active ? Colors.white : AppColors.textSecondary,
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
