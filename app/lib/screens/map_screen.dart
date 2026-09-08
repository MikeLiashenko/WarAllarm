import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/settings_controller.dart';
import '../data/ukraine_geo.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import '../widgets/section_header.dart';
import '../widgets/ukraine_map.dart';
import 'home_shell.dart';

/// Key on the map container so we can capture it to a PNG for sharing.
final GlobalKey _mapBoundaryKey = GlobalKey();

Future<void> _shareMap(BuildContext context) async {
  final messenger = ScaffoldMessenger.of(context);
  try {
    final bnd = _mapBoundaryKey.currentContext?.findRenderObject()
        as RenderRepaintBoundary?;
    if (bnd == null) return;
    final image = await bnd.toImage(pixelRatio: 2.5);
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    if (data == null) return;
    final dir = await getTemporaryDirectory();
    final f = File('${dir.path}/waralarm_map.png');
    await f.writeAsBytes(data.buffer.asUint8List(), flush: true);
    await Share.shareXFiles([XFile(f.path)], text: 'WarAlarm — карта загроз');
  } catch (_) {
    messenger.showSnackBar(
        const SnackBar(content: Text('Не вдалося зробити скрін карти')));
  }
}

class MapScreen extends StatelessWidget {
  const MapScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final map = context.watch<MapDataController>();
    final settings = context.watch<SettingsController>();
    final alerts = context.watch<AlertsController>();

    return PageBody(
      scrollable: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            title: 'Карта загроз',
            subtitle: alerts.ok
                ? 'Офіційні тривоги: 🔴 ${alerts.redCount} · 🟡 ${alerts.yellowCount} обл.'
                : 'Офіційні тривоги — завантажую…',
            icon: Icons.map_rounded,
            trailing: _liveBadge(map),
          ),
          const SizedBox(height: 16),
          Expanded(
            child: Stack(
              children: [
                RepaintBoundary(
                  key: _mapBoundaryKey,
                  child: Container(
                    clipBehavior: Clip.antiAlias,
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(24),
                      border: Border.all(color: AppColors.border),
                      gradient: LinearGradient(
                        begin: Alignment.topCenter,
                        end: Alignment.bottomCenter,
                        colors: [AppColors.mapSea, AppColors.background],
                      ),
                      boxShadow: const [
                        BoxShadow(
                          color: Color(0x14123A6B),
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
                      audibilityKm: settings.audibilityKm,
                      showAudibility: settings.showAudibility,
                      oblastLevels: alerts.levels,
                    ),
                  ),
                ),
                if (map.isDemo)
                  Positioned(
                    top: 12,
                    left: 0,
                    right: 0,
                    child: Center(child: _demoRibbon()),
                  ),
                // Share the current map view as a picture.
                Positioned(
                  left: 12,
                  top: 12,
                  child: Material(
                    color: AppColors.surface,
                    elevation: 2,
                    shadowColor: const Color(0x22123A6B),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(9),
                      side: BorderSide(color: AppColors.border),
                    ),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(9),
                      onTap: () => _shareMap(context),
                      child: SizedBox(
                        width: 34,
                        height: 34,
                        child: Icon(Icons.ios_share_rounded,
                            color: AppColors.primaryDark, size: 18),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          _TargetStrip(targets: map.targets, live: map.live),
        ],
      ),
    );
  }

  Widget _liveBadge(MapDataController map) {
    if (map.isDemo) {
      return Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
        decoration: BoxDecoration(
          color: const Color(0xFFFFF2D6),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: const Color(0xFFE9B949)),
        ),
        child: const Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.science_rounded, size: 14, color: Color(0xFFB8860B)),
            SizedBox(width: 6),
            Text('ДЕМО',
                style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w800,
                    color: Color(0xFF8A6D0B))),
          ],
        ),
      );
    }
    final count = map.targets.length;
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

  Widget _demoRibbon() {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      decoration: BoxDecoration(
        color: const Color(0xE6B8860B),
        borderRadius: BorderRadius.circular(20),
        boxShadow: const [
          BoxShadow(color: Color(0x33000000), blurRadius: 10, offset: Offset(0, 3)),
        ],
      ),
      child: const Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.science_rounded, size: 15, color: Colors.white),
          SizedBox(width: 7),
          Text('ДЕМО-ДАНІ · не реальні загрози',
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w800,
                  color: Colors.white,
                  letterSpacing: 0.2)),
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
  const _TargetStrip({required this.targets, required this.live});
  final List<MapTarget> targets;
  final bool live;

  @override
  Widget build(BuildContext context) {
    if (targets.isEmpty) {
      final honest = live; // only claim "clear" when we actually have a feed
      return Container(
        padding: const EdgeInsets.symmetric(vertical: 14),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: honest ? AppColors.safeSoft : AppColors.primarySoft,
          borderRadius: BorderRadius.circular(14),
        ),
        child: Text(
          honest
              ? '✅ Наразі чисто — активних цілей немає'
              : 'Немає підключення до монітора — даних поки немає',
          style: TextStyle(
              color: honest ? AppColors.safe : AppColors.primaryDark,
              fontWeight: FontWeight.w600),
        ),
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
                        style: TextStyle(
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
                  style: TextStyle(
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
