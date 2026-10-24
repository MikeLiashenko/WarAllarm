import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../controllers/settings_controller.dart';
import '../theme/app_colors.dart';
import '../widgets/glass.dart';
import '../widgets/responsive.dart';
import '../widgets/ukraine_map.dart';
import 'now_screen.dart';

/// Full-screen threat map: our vector map with the live counters, map
/// controls and a compact glass place card floating over it (tap → «Зараз»).
class MapScreen extends StatefulWidget {
  const MapScreen({super.key});

  @override
  State<MapScreen> createState() => _MapScreenState();
}

class _MapScreenState extends State<MapScreen> {
  final _mapKey = GlobalKey<UkraineMapState>();
  final _shotKey = GlobalKey();

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final alerts = context.watch<AlertsController>();
    final settings = context.watch<SettingsController>();
    final s = Situation(data, alarm, alerts);
    final pad = MediaQuery.paddingOf(context);
    final phone = Responsive.useBottomNav(context);

    // Place card: bottom-left on desktop, full width above the nav on phones.
    const cardH = 118.0;
    final card = Glass(
      radius: 24,
      padding: const EdgeInsets.fromLTRB(18, 14, 14, 14),
      onTap: () => context.read<NavController>().select(AppTab.now),
      child: PlaceHeader(s: s),
    );

    return Stack(
      children: [
        Positioned.fill(
          child: RepaintBoundary(
            key: _shotKey,
            child: ColoredBox(
              color: AppColors.mapSea,
              child: UkraineMap(
                key: _mapKey,
                targets: data.targets,
                home: (lat: s.place.lat, lon: s.place.lon),
                homeName: s.place.name,
                audibilityKm: settings.audibilityKm,
                showAudibility: settings.showAudibility,
                oblastLevels: alerts.levels,
                raionLevels: alerts.raions,
                oblastWhole: alerts.oblastWhole,
                showControls: false,
                showLegend: false,
                dimTargets: s.stale,
                insets: EdgeInsets.fromLTRB(
                    pad.left + (phone ? 0 : 8),
                    pad.top + (phone ? 96 : 70),
                    phone ? 0 : 70,
                    pad.bottom + cardH + (phone ? 12 : 24)),
              ),
            ),
          ),
        ),
        Positioned(
          left: pad.left + 12,
          right: 72,
          top: pad.top + 12,
          child: ThreatCounters(data: data, alerts: alerts),
        ),
        Positioned(
          right: 12,
          top: pad.top + 12,
          child: _controls(context),
        ),
        Positioned(
          left: pad.left + 12,
          right: phone ? 12 : null,
          width: phone ? null : 420,
          bottom: pad.bottom + 12,
          child: card,
        ),
      ],
    );
  }

  Widget _controls(BuildContext context) {
    return Column(
      children: [
        GlassIconButton(
            icon: Icons.add_rounded,
            tooltip: 'Наблизити',
            onTap: () => _mapKey.currentState?.zoomIn()),
        const SizedBox(height: 8),
        GlassIconButton(
            icon: Icons.remove_rounded,
            tooltip: 'Віддалити',
            onTap: () => _mapKey.currentState?.zoomOut()),
        const SizedBox(height: 8),
        GlassIconButton(
            icon: Icons.center_focus_strong_outlined,
            tooltip: 'Уся Україна',
            onTap: () => _mapKey.currentState?.resetView()),
        const SizedBox(height: 8),
        GlassIconButton(
            icon: Icons.ios_share_rounded,
            tooltip: 'Поділитися картою',
            onTap: () => _shareMap(context)),
      ],
    );
  }

  Future<void> _shareMap(BuildContext context) async {
    final messenger = ScaffoldMessenger.of(context);
    try {
      final bnd =
          _shotKey.currentContext?.findRenderObject() as RenderRepaintBoundary?;
      if (bnd == null) return;
      final image = await bnd.toImage(pixelRatio: 2.5);
      final bytes = (await image.toByteData(format: ui.ImageByteFormat.png))
          ?.buffer
          .asUint8List();
      if (bytes == null || kIsWeb) return;
      final stamp = DateTime.now().millisecondsSinceEpoch;
      if (defaultTargetPlatform == TargetPlatform.android ||
          defaultTargetPlatform == TargetPlatform.iOS) {
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
            SnackBar(content: Text('Карту збережено: ${f.path}')));
      }
    } catch (e) {
      messenger.showSnackBar(
          SnackBar(content: Text('Не вдалося зробити скрін: $e')));
    }
  }
}
