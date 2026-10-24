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
import '../theme/app_theme.dart';
import '../widgets/ui_kit.dart';
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
    final bytes = data.buffer.asUint8List();
    final stamp = DateTime.now().millisecondsSinceEpoch;

    if (Platform.isAndroid || Platform.isIOS) {
      // Phone: open the share sheet (Telegram, save to gallery, …).
      final dir = await getTemporaryDirectory();
      final f = File('${dir.path}/waralarm_map_$stamp.png');
      await f.writeAsBytes(bytes, flush: true);
      await Share.shareXFiles([XFile(f.path)], text: 'WarAlarm — карта загроз');
    } else {
      // Desktop: sharing files isn't supported — save to Downloads instead.
      Directory? dir;
      try {
        dir = await getDownloadsDirectory();
      } catch (_) {}
      dir ??= await getApplicationDocumentsDirectory();
      final f = File('${dir.path}/waralarm_map_$stamp.png');
      await f.writeAsBytes(bytes, flush: true);
      messenger.showSnackBar(
          SnackBar(content: Text('Скрін збережено: ${f.path}')));
    }
  } catch (e) {
    messenger.showSnackBar(
        SnackBar(content: Text('Не вдалося зробити скрін: $e')));
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
                      borderRadius: BorderRadius.circular(AppRadii.xl),
                      border: Border.all(color: AppColors.border),
                      color: AppColors.mapSea,
                    ),
                    child: UkraineMap(
                      targets: map.targets,
                      home: map.home,
                      homeName: map.homeName,
                      audibilityKm: settings.audibilityKm,
                      showAudibility: settings.showAudibility,
                      oblastLevels: alerts.levels,
                      raionLevels: alerts.raions,
                      oblastWhole: alerts.oblastWhole,
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
                    color: AppColors.surface2,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(AppRadii.sm + 2),
                      side: BorderSide(color: AppColors.border),
                    ),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(AppRadii.sm + 2),
                      onTap: () => _shareMap(context),
                      child: SizedBox(
                        width: 36,
                        height: 36,
                        child: Icon(Icons.ios_share_rounded,
                            color: AppColors.textPrimary, size: 18),
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
      return Tag('ДЕМО', color: AppColors.warn, icon: Icons.science_outlined);
    }
    final count = map.targets.length;
    final c = count > 0 ? AppColors.threat : AppColors.safe;
    return Container(
      padding: const EdgeInsets.fromLTRB(4, 4, 12, 4),
      decoration: BoxDecoration(
        color: c.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          LiveDot(color: c, size: 7, pulse: map.live),
          const SizedBox(width: 2),
          Text(
            count > 0 ? '$count ${_plural(count)}' : 'Чисто',
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w800,
              color: AppColors.textPrimary,
              fontFeatures: AppText.tabular,
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
        color: AppColors.warn,
        borderRadius: BorderRadius.circular(20),
      ),
      child: const Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.science_outlined, size: 15, color: Colors.black87),
          SizedBox(width: 7),
          Text('ДЕМО-ДАНІ · не реальні загрози',
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w800,
                  color: Colors.black87,
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
      return Notice(
        color: honest ? AppColors.safe : AppColors.textMuted,
        icon: honest ? Icons.verified_outlined : Icons.cloud_off_outlined,
        title: honest
            ? 'Наразі чисто — активних цілей немає'
            : 'Немає підключення до монітора — даних поки немає',
      );
    }
    return SizedBox(
      height: 66,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: targets.length,
        separatorBuilder: (_, _) => const SizedBox(width: 8),
        itemBuilder: (context, i) => _card(targets[i]),
      ),
    );
  }

  Widget _card(MapTarget t) {
    final color = kThreatColors[t.cat] ?? kThreatColors['other']!;
    return Container(
      width: 220,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 10),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(AppRadii.md),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        children: [
          IconTile(Icons.navigation_rounded, color: color, size: 38),
          const SizedBox(width: 10),
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
                            style: AppText.number(12.5, color: color)),
                      ),
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  [
                    if (t.distKm != null) '${t.distKm!.round()} км',
                    if (t.place.isNotEmpty) t.place,
                    if (t.dir.isNotEmpty) t.dir,
                  ].join(' · '),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: 11.5,
                      color: AppColors.textSecondary,
                      fontFeatures: AppText.tabular),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
