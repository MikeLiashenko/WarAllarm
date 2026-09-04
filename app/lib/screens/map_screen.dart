import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/map_data_controller.dart';
import '../data/ukraine_geo.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import '../widgets/section_header.dart';
import '../widgets/ukraine_map.dart';
import 'home_shell.dart';

class MapScreen extends StatelessWidget {
  const MapScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final map = context.watch<MapDataController>();

    return PageBody(
      scrollable: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            title: 'Карта загроз',
            subtitle: 'Живі цілі, курс та ІІ-прогноз · ${map.homeName}',
            icon: Icons.map_rounded,
            trailing: _liveBadge(map.live, map.targets.length),
          ),
          const SizedBox(height: 16),
          Expanded(
            child: Container(
              clipBehavior: Clip.antiAlias,
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(24),
                border: Border.all(color: AppColors.border),
                gradient: const LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [Color(0xFF0B1424), Color(0xFF0A1020)],
                ),
                boxShadow: const [
                  BoxShadow(
                    color: Color(0x1A123A6B),
                    blurRadius: 30,
                    spreadRadius: -6,
                    offset: Offset(0, 14),
                  ),
                ],
              ),
              child: UkraineMap(
                targets: map.targets,
                home: map.home,
                homeName: map.homeName,
              ),
            ),
          ),
          const SizedBox(height: 12),
          _TargetStrip(targets: map.targets),
        ],
      ),
    );
  }

  Widget _liveBadge(bool live, int count) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
      decoration: BoxDecoration(
        color: count > 0 ? AppColors.threatSoft : AppColors.safeSoft,
        borderRadius: BorderRadius.circular(20),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 9,
            height: 9,
            decoration: BoxDecoration(
              color: count > 0 ? AppColors.threat : AppColors.safe,
              shape: BoxShape.circle,
            ),
          ),
          const SizedBox(width: 7),
          Text(
            count > 0 ? '$count ${_plural(count)}' : 'Чисто',
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w700,
              color: count > 0 ? AppColors.threatDark : AppColors.safe,
            ),
          ),
        ],
      ),
    );
  }

  String _plural(int n) {
    final m10 = n % 10, m100 = n % 100;
    if (m10 == 1 && m100 != 11) return 'ціль';
    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return 'цілі';
    return 'цілей';
  }
}

/// Horizontal strip of active targets under the map.
class _TargetStrip extends StatelessWidget {
  const _TargetStrip({required this.targets});
  final List<MapTarget> targets;

  @override
  Widget build(BuildContext context) {
    if (targets.isEmpty) {
      return Container(
        padding: const EdgeInsets.symmetric(vertical: 14),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: AppColors.safeSoft,
          borderRadius: BorderRadius.circular(14),
        ),
        child: const Text('✅ Наразі чисто — активних цілей немає',
            style: TextStyle(
                color: AppColors.safe, fontWeight: FontWeight.w600)),
      );
    }
    return SizedBox(
      height: 74,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: targets.length,
        separatorBuilder: (_, _) => const SizedBox(width: 10),
        itemBuilder: (context, i) => _card(targets[i]),
      ),
    );
  }

  Widget _card(MapTarget t) {
    final color = kThreatColors[t.cat] ?? kThreatColors['other']!;
    return Container(
      width: 210,
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: color.withValues(alpha: 0.14),
              borderRadius: BorderRadius.circular(11),
            ),
            child: Icon(Icons.navigation_rounded, color: color, size: 20),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        t.label,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                            fontSize: 13.5,
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary),
                      ),
                    ),
                    if (t.amount > 1)
                      Padding(
                        padding: const EdgeInsets.only(left: 4),
                        child: Text('×${t.amount}',
                            style: TextStyle(
                                fontSize: 12.5,
                                fontWeight: FontWeight.w800,
                                color: color)),
                      ),
                  ],
                ),
                const SizedBox(height: 2),
                Text(
                  [
                    if (t.distKm != null) '${t.distKm!.round()} км',
                    if (t.place.isNotEmpty) t.place,
                    if (t.dir.isNotEmpty) t.dir,
                  ].join(' · '),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                      fontSize: 11.5, color: AppColors.textSecondary),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
