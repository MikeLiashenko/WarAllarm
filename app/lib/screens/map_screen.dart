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
import '../controllers/target_focus_controller.dart';
import '../domain/motion.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import '../widgets/glass.dart';
import '../widgets/responsive.dart';
import '../services/local_store.dart';
import '../widgets/map3d.dart';
import '../widgets/map_legend.dart';
import '../widgets/target_card.dart';
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
  final _map3dKey = GlobalKey<Map3DState>();
  final _shotKey = GlobalKey();

  /// 3D view on/off (remembered between launches).
  late bool _is3d = context.read<LocalStore>().getBool(
    'map_3d',
    fallback: false,
  );

  bool _legend = false;

  /// Re-draws the target card every second (distance, ETA, flight progress).
  final _tick = Stream<int>.periodic(
    const Duration(seconds: 1),
    (i) => i,
  ).asBroadcastStream();

  void _toggle3d() {
    setState(() => _is3d = !_is3d);
    context.read<LocalStore>().setBool('map_3d', _is3d);
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final alerts = context.watch<AlertsController>();
    final settings = context.watch<SettingsController>();
    final focus = context.watch<TargetFocusController>();
    final s = Situation(data, alarm, alerts);
    final selected = focus.id == null
        ? null
        : data.targets.where((t) => t.id == focus.id).firstOrNull;
    // The followed target left the feed — nothing to ride along with any more.
    if (focus.id != null && selected == null && focus.follow) {
      WidgetsBinding.instance.addPostFrameCallback(
        (_) => focus.setFollow(false),
      );
    }
    void onTargetTap(MapTarget? t) =>
        t == null ? focus.clear() : focus.select(t.id, follow: focus.follow);
    void onUserMove() => focus.setFollow(false);
    final pad = MediaQuery.paddingOf(context);
    final phone = Responsive.useBottomNav(context);

    // Place card: bottom-left on desktop, full width above the nav on phones.
    const cardH = 118.0;
    // A selected target replaces it with the full target card (live, 1 s).
    final Widget card = focus.id == null
        ? Glass(
            radius: 24,
            padding: const EdgeInsets.fromLTRB(18, 14, 14, 14),
            onTap: () => context.read<NavController>().select(AppTab.now),
            child: PlaceHeader(s: s),
          )
        : ConstrainedBox(
            constraints: BoxConstraints(
              maxHeight:
                  MediaQuery.sizeOf(context).height * (phone ? 0.46 : 0.7),
            ),
            child: SingleChildScrollView(
              child: StreamBuilder<int>(
                stream: _tick,
                builder: (context, _) => TargetCard(
                  target: selected,
                  place: s.place,
                  following: focus.follow,
                  stateTs: data.stateTs,
                  onFollow: (v) => v
                      ? focus.select(focus.id!, follow: true)
                      : focus.setFollow(false),
                  onShow: () => _show(selected),
                  onClose: focus.clear,
                ),
              ),
            ),
          );

    return Stack(
      children: [
        Positioned.fill(
          child: RepaintBoundary(
            key: _shotKey,
            child: ColoredBox(
              color: AppColors.mapSea,
              child: _is3d
                  ? Map3D(
                      key: _map3dKey,
                      targets: data.targets,
                      home: (lat: s.place.lat, lon: s.place.lon),
                      homeName: s.place.name,
                      oblastWhole: alerts.oblastWhole,
                      raionLevels: alerts.raions,
                      dimTargets: s.stale,
                      selectedId: focus.id,
                      followId: focus.follow ? focus.id : null,
                      onTargetTap: onTargetTap,
                      onUserMove: onUserMove,
                      insets: EdgeInsets.fromLTRB(
                        pad.left,
                        pad.top + (phone ? 90 : 60),
                        phone ? 0 : 60,
                        pad.bottom + cardH + (phone ? 12 : 24),
                      ),
                    )
                  : UkraineMap(
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
                      selectedId: focus.id,
                      followId: focus.follow ? focus.id : null,
                      onTargetTap: onTargetTap,
                      onUserMove: onUserMove,
                      insets: EdgeInsets.fromLTRB(
                        pad.left + (phone ? 0 : 8),
                        pad.top + (phone ? 96 : 70),
                        phone ? 0 : 70,
                        pad.bottom + cardH + (phone ? 12 : 24),
                      ),
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
        Positioned(right: 12, top: pad.top + 12, child: _controls(context)),
        if (_legend)
          Positioned(
            right: 66,
            top: pad.top + 12,
            child: MapLegend(onClose: () => setState(() => _legend = false)),
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

  /// «Показати»: fly the camera to the target once (no following).
  void _show(MapTarget? t) {
    if (t == null) return;
    final m = motionOf(t, DateTime.now().millisecondsSinceEpoch);
    _is3d
        ? _map3dKey.currentState?.focusOn(m.lat, m.lon)
        : _mapKey.currentState?.focusOn(m.lat, m.lon);
  }

  Widget _controls(BuildContext context) {
    return Column(
      children: [
        GlassIconButton(
          icon: Icons.add_rounded,
          tooltip: 'Наблизити',
          onTap: () => _is3d
              ? _map3dKey.currentState?.zoomIn()
              : _mapKey.currentState?.zoomIn(),
        ),
        const SizedBox(height: 8),
        GlassIconButton(
          icon: Icons.remove_rounded,
          tooltip: 'Віддалити',
          onTap: () => _is3d
              ? _map3dKey.currentState?.zoomOut()
              : _mapKey.currentState?.zoomOut(),
        ),
        const SizedBox(height: 8),
        GlassIconButton(
          icon: Icons.center_focus_strong_outlined,
          tooltip: 'Уся Україна',
          onTap: () => _is3d
              ? _map3dKey.currentState?.resetView()
              : _mapKey.currentState?.resetView(),
        ),
        const SizedBox(height: 8),
        // 2D ↔ 3D.
        Tooltip(
          message: _is3d ? 'Плоска карта' : '3D-карта',
          child: Glass(
            padding: EdgeInsets.zero,
            radius: 22,
            onTap: _toggle3d,
            color: _is3d ? AppColors.primary.withValues(alpha: 0.85) : null,
            child: SizedBox(
              width: 44,
              height: 44,
              child: Center(
                child: Text(
                  _is3d ? '2D' : '3D',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w900,
                    color: _is3d ? Colors.white : AppColors.textPrimary,
                  ),
                ),
              ),
            ),
          ),
        ),
        const SizedBox(height: 8),
        GlassIconButton(
          icon: _legend ? Icons.info_rounded : Icons.info_outline_rounded,
          tooltip: 'Легенда',
          onTap: () => setState(() => _legend = !_legend),
        ),
        const SizedBox(height: 8),
        GlassIconButton(
          icon: Icons.ios_share_rounded,
          tooltip: 'Поділитися картою',
          onTap: () => _shareMap(context),
        ),
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
      final bytes = (await image.toByteData(
        format: ui.ImageByteFormat.png,
      ))?.buffer.asUint8List();
      if (bytes == null || kIsWeb) return;
      final stamp = DateTime.now().millisecondsSinceEpoch;
      if (defaultTargetPlatform == TargetPlatform.android ||
          defaultTargetPlatform == TargetPlatform.iOS) {
        // Phone: open the share sheet (Telegram, save to gallery, …).
        final dir = await getTemporaryDirectory();
        final f = File('${dir.path}/waralarm_map_$stamp.png');
        await f.writeAsBytes(bytes, flush: true);
        await Share.shareXFiles([
          XFile(f.path),
        ], text: 'WarAlarm — карта загроз');
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
          SnackBar(content: Text('Карту збережено: ${f.path}')),
        );
      }
    } catch (e) {
      messenger.showSnackBar(
        SnackBar(content: Text('Не вдалося зробити скрін: $e')),
      );
    }
  }
}
