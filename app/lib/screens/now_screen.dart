import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/gestures.dart' show PointerDeviceKind;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/settings_controller.dart';
import '../data/ukraine_geo.dart';
import '../domain/probability.dart';
import '../domain/situation.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import '../widgets/analytics_dashboard.dart';
import '../widgets/floating_nav.dart';
import '../widgets/glass.dart';
import '../widgets/place_selector.dart';
import '../widgets/probability_gauge.dart';
import '../widgets/radar_scope.dart';
import '../widgets/responsive.dart';
import '../widgets/shelters_sheet.dart';
import '../widgets/ui_kit.dart';
import '../widgets/ukraine_map.dart';

/// Everything the home screen shows, derived once per build.
class _Situation {
  _Situation(this.data, this.alarm, AlertsController alerts) {
    stale = data.stale;
    scope = NowScreen.scopeOf(data);
    // Place-level danger uses only real positions — a target pinned to an
    // oblast centre ("… (обл.)") isn't 0 km from that city. Same rule as the
    // server's RiskModel (state.risk) and the siren.
    final real = data.alarmTargets;
    final seen = <String>{};
    for (final pl in scope) {
      for (final t in placeThreats(pl, real)) {
        if (seen.add('${t.zone}|${t.place}|${t.label}')) sel.add(t);
      }
    }
    probs = scope
        .map((p) => probabilityFor(p, real,
            officialLevel: officialLevelFor(p, alerts.levels)))
        .toList()
      ..sort((a, b) => b.percent.compareTo(a.percent));
    prob = probs.isEmpty ? null : probs.first;
    // The card / radar follow the most endangered place.
    place = prob?.place ?? scope.first;
    focus = placeThreats(place, real);
    nearest = focus.isEmpty ? null : focus.first;

    final worst = worstOf(sel);
    final rank = worst == null ? 0 : (kRank[worst.stage] ?? 0);
    active = !stale && (alarm.level == 'alarm' || rank >= 3);
    threat = !stale && worst != null;

    if (stale) {
      tone = AppColors.warn;
      status = 'Дані застаріли';
      message = 'Монітор не оновлює дані ${fmtAge(data.dataAge)} '
          '(останні — о ${fmtClock(data.stateTs).substring(0, 5)}). '
          'Тривога за цими даними не вмикається.';
    } else if (!data.live && !data.isDemo) {
      tone = AppColors.textMuted;
      status = 'Немає даних';
      message = 'Чекаю монітор… Це не означає «чисто».';
    } else if (active) {
      tone = AppColors.threat;
      status = 'Тривога — в укриття';
      message = '${worst?.label ?? 'Загроза'} поруч · ${worst?.stageLabel ?? ''}';
    } else if (threat) {
      tone = AppColors.warn;
      status = 'Загроза поруч';
      message = '${worst!.label} · ${worst.pdist?.round() ?? '?'} км · '
          '${fmtEta(etaNow(worst, data.stateTs))}';
    } else {
      tone = AppColors.safe;
      status = 'Спокійно';
      message = data.targets.isEmpty
          ? 'Загроз для ваших місць немає'
          : 'По країні цілей: ${data.targets.length} — далеко';
    }
  }

  final MapDataController data;
  final AlarmController alarm;
  late final bool stale;
  late final List<MonitoredPlace> scope;
  final List<MapTarget> sel = [];
  late final List<ThreatProbability> probs;
  late final ThreatProbability? prob;
  late final MonitoredPlace place;
  late final List<MapTarget> focus;
  late final MapTarget? nearest;
  late final bool active;
  late final bool threat;
  late final Color tone;
  late final String status;
  late final String message;
}

class NowScreen extends StatefulWidget {
  const NowScreen({super.key});

  /// The places the screen reports on: the selection, or home when the user
  /// hasn't saved any places yet (otherwise everything would read "calm").
  static List<MonitoredPlace> scopeOf(MapDataController d) {
    final sel = d.selectedPlaces;
    if (sel.isNotEmpty) return sel;
    return [
      MonitoredPlace(
          id: 'home',
          icon: '🏠',
          name: d.homeName,
          lat: d.home.lat,
          lon: d.home.lon),
    ];
  }

  @override
  State<NowScreen> createState() => _NowScreenState();
}

class _NowScreenState extends State<NowScreen> {
  final _mapKey = GlobalKey<UkraineMapState>();
  final _shotKey = GlobalKey();
  final _sheet = DraggableScrollableController();
  double _minFrac = 0.2;

  @override
  void dispose() {
    _sheet.dispose();
    super.dispose();
  }

  /// Tap on the place card: open the details / fold back to the card.
  void _toggleSheet() {
    if (!_sheet.isAttached) return;
    final open = _sheet.size > _minFrac + 0.05;
    _sheet.animateTo(open ? _minFrac : 0.6,
        duration: const Duration(milliseconds: 280), curve: Curves.easeOutCubic);
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final alerts = context.watch<AlertsController>();
    final settings = context.watch<SettingsController>();
    final s = _Situation(data, alarm, alerts);
    final pad = MediaQuery.paddingOf(context);

    return LayoutBuilder(builder: (context, c) {
      final wide = c.maxWidth >= 900 && !Responsive.useBottomNav(context);
      const panelW = 410.0;

      if (wide) {
        final panelLeft = pad.left + 8;
        final mapLeft = panelLeft + panelW + 16;
        return Stack(
          children: [
            Positioned.fill(
              child: _map(data, alerts, settings, s,
                  EdgeInsets.fromLTRB(mapLeft, 70, 70, 16)),
            ),
            Positioned(
              left: panelLeft,
              top: pad.top + 16,
              bottom: 16,
              width: panelW,
              child: Glass(
                radius: 26,
                padding: EdgeInsets.zero,
                // The nav inset is already applied by the panel position — strip
                // it so inner (horizontal) lists don't indent by it again.
                child: MediaQuery.removePadding(
                  context: context,
                  removeLeft: true,
                  removeTop: true,
                  removeRight: true,
                  removeBottom: true,
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(18, 18, 18, 22),
                    children: _details(context, s, settings),
                  ),
                ),
              ),
            ),
            Positioned(
              left: mapLeft,
              right: 80,
              top: pad.top + 16,
              child: _TopChips(data: data, alerts: alerts),
            ),
            Positioned(
              right: 16,
              top: pad.top + 16,
              child: _controls(context),
            ),
          ],
        );
      }

      // Phone: map full-screen, chips on top, a draggable sheet whose collapsed
      // state is the place card (like a maps app).
      const cardH = 140.0;
      final minPx = cardH + pad.bottom;
      final minFrac = (minPx / c.maxHeight).clamp(0.15, 0.6);
      _minFrac = minFrac;
      return Stack(
        children: [
          Positioned.fill(
            child: _map(data, alerts, settings, s,
                EdgeInsets.fromLTRB(0, pad.top + 92, 0, minPx - 8)),
          ),
          Positioned(
            left: 12,
            right: 64,
            top: pad.top + 12,
            child: _TopChips(data: data, alerts: alerts),
          ),
          Positioned(
            right: 12,
            top: pad.top + 12,
            child: _controls(context),
          ),
          DraggableScrollableSheet(
            controller: _sheet,
            initialChildSize: minFrac,
            minChildSize: minFrac,
            maxChildSize: 0.94,
            snap: true,
            snapSizes: [minFrac, 0.6],
            builder: (context, sc) => Glass(
              radius: 28,
              padding: EdgeInsets.zero,
              color: AppColors.isDark
                  ? const Color(0xE60D1015)
                  : const Color(0xF2FFFFFF),
              // Let a mouse drag the sheet too (desktop window at phone width, web).
              child: ScrollConfiguration(
                behavior: ScrollConfiguration.of(context).copyWith(
                    dragDevices: PointerDeviceKind.values.toSet()),
                child: ListView(
                controller: sc,
                padding: EdgeInsets.fromLTRB(16, 8, 16, pad.bottom + 20),
                children: [
                  Center(
                    child: Container(
                      width: 38,
                      height: 4,
                      margin: const EdgeInsets.only(bottom: 12),
                      decoration: BoxDecoration(
                        color: AppColors.textMuted.withValues(alpha: 0.5),
                        borderRadius: BorderRadius.circular(2),
                      ),
                    ),
                  ),
                  ..._details(context, s, settings, onHeaderTap: _toggleSheet),
                ],
              ),
              ),
            ),
          ),
        ],
      );
    });
  }

  Widget _map(MapDataController data, AlertsController alerts,
      SettingsController settings, _Situation s, EdgeInsets insets) {
    return RepaintBoundary(
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
          insets: insets,
          dimTargets: s.stale,
        ),
      ),
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

  /// The detail stack shared by the phone sheet and the desktop panel.
  List<Widget> _details(
      BuildContext context, _Situation s, SettingsController settings,
      {VoidCallback? onHeaderTap}) {
    final data = s.data;
    final targets = data.selectedId == 'all' ? data.targets : s.sel;
    const gap = SizedBox(height: 20);
    return [
      GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onHeaderTap,
        child: _PlaceHeader(s: s),
      ),
      const SizedBox(height: 14),
      const PlaceSelector(),
      const SizedBox(height: 16),
      _Metrics(s: s),
      const SizedBox(height: 14),
      SizedBox(
        width: double.infinity,
        child: FilledButton.icon(
          onPressed: () => showSheltersSheet(
              context, s.place.lat, s.place.lon, s.place.name),
          style: FilledButton.styleFrom(
            backgroundColor: s.active ? AppColors.threat : AppColors.primary,
            padding: const EdgeInsets.symmetric(vertical: 15),
          ),
          icon: const Icon(Icons.shield_outlined, size: 19),
          label: const Text('Найближче укриття'),
        ),
      ),
      gap,
      ..._eventNotices(s),
      const SectionLabel('Радар'),
      _Panel(
        child: Center(
          child: RadarScope(
            lat: s.place.lat,
            lon: s.place.lon,
            targets: s.stale
                ? const []
                : (data.isDemo ? data.targets : data.alarmTargets),
            tone: s.tone,
            active: s.active,
            alarmRadiusKm: settings.alarmRadiusKm,
            size: 250,
          ),
        ),
      ),
      if (s.prob != null && !s.stale) ...[
        gap,
        const SectionLabel('Ймовірність удару', trailing: Tag('через API')),
        ProbabilityGauge(prob: s.prob!),
      ],
      gap,
      SectionLabel('Мої місця', count: '${s.scope.length}'),
      for (final p in s.scope)
        _PlaceRow(
          place: p,
          data: data,
          stale: s.stale,
          prob: s.probs.where((x) => x.place.id == p.id).firstOrNull,
        ),
      gap,
      SectionLabel(
        data.selectedId == 'all' ? 'Цілі · вся Україна' : 'Цілі поруч',
        // «Вся Україна» → скільки бортів у повітрі всього (сума ×N).
        count: data.selectedId == 'all' ? '~${data.airborne}' : '${s.sel.length}',
        trailing: s.stale ? Tag('застарілі', color: AppColors.warn) : null,
      ),
      if (targets.isEmpty)
        _EmptyTargets(live: data.fresh)
      else
        _Panel(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              for (var i = 0; i < targets.length; i++) ...[
                if (i > 0) Divider(height: 1, color: AppColors.divider),
                _TargetRow(t: targets[i], stateTs: data.stateTs, stale: s.stale),
              ],
            ],
          ),
        ),
      gap,
      AnalyticsDashboard(rows: data.rows, targetsNow: data.targets.length),
    ];
  }

  /// Night summary, power outages and the latest launch wave.
  List<Widget> _eventNotices(_Situation s) {
    final data = s.data;
    final list = <Widget>[
      if (data.isDemo)
        Notice(
          color: AppColors.warn,
          icon: Icons.science_outlined,
          title: 'Демо-дані — це приклад, а не реальні загрози',
          body: 'Монітор не підключено.',
        ),
      // 🚀 Остання хвиля пусків (з каналу моніторів через бота).
      if (data.hasRecentLaunch)
        Notice(
          color: const Color(0xFFFF7A3C),
          emoji: '🚀',
          title: 'Пуск ~${data.launched} шахедів'
              '${data.launchedAt.isNotEmpty ? ' о ${data.launchedAt}' : ''}',
          body: data.launchOrigins.isNotEmpty ? data.launchOrigins : null,
        ),
      // 🔌 Світло (відключення/відновлення).
      if (data.powerStatus.isNotEmpty)
        Notice(
          color: data.powerStatus == 'off' ? AppColors.warn : AppColors.safe,
          emoji: data.powerStatus == 'off' ? '🔌' : '💡',
          title: data.powerStatus == 'off'
              ? 'Світло: відключення — ${data.powerArea}'
              : 'Світло відновлюють — ${data.powerArea}',
          // Світло по всьому Києву: які черги зараз без світла.
          body: data.powerGroupsOff.isNotEmpty
              ? 'Без світла черги: ${data.powerGroupsOff.join(', ')}'
                  '${data.powerAt.isNotEmpty ? ' (з ${data.powerAt})' : ''}'
              : null,
        ),
      // 🌙 Підсумок ночі (останній, що зібрав бот).
      if (data.hasNight)
        Notice(
          color: AppColors.night,
          emoji: '🌙',
          title: 'Підсумок ночі · ${data.nightPeriod}',
          lines: [
            if (data.nightLaunched > 0)
              'Запущено ~${data.nightLaunched} (хвиль: ${data.nightWaves})',
            if (data.nightMaxAir > 0) 'Максимум у повітрі ~${data.nightMaxAir}',
            if (data.nightTop.isNotEmpty) 'Найбільше: ${data.nightTop}',
            if (data.nightImpacts > 0)
              'Вибухи: ${data.nightImpacts} — ${data.nightImpactAt}',
          ],
        ),
    ];
    if (list.isEmpty) return const [];
    return [
      const SectionLabel('Події'),
      for (final w in list)
        Padding(padding: const EdgeInsets.only(bottom: 8), child: w),
      const SizedBox(height: 12),
    ];
  }
}

/// Collapsed-sheet card: place name, status dot + word, strike chance.
class _PlaceHeader extends StatelessWidget {
  const _PlaceHeader({required this.s});
  final _Situation s;

  @override
  Widget build(BuildContext context) {
    final pct = s.prob?.percent;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(s.place.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: 24,
                      height: 1.1,
                      fontWeight: FontWeight.w800,
                      letterSpacing: -0.6,
                      color: AppColors.textPrimary)),
              const SizedBox(height: 6),
              Row(
                children: [
                  LiveDot(
                      color: s.tone,
                      size: 8,
                      pulse: s.active || (s.data.fresh && !s.threat)),
                  const SizedBox(width: 2),
                  Flexible(
                    child: Text(s.status,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                            fontSize: 15,
                            fontWeight: FontWeight.w700,
                            color: s.active
                                ? AppColors.threat
                                : AppColors.textPrimary)),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(s.message,
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: 13,
                      height: 1.35,
                      color: AppColors.textSecondary,
                      fontFeatures: AppText.tabular)),
            ],
          ),
        ),
        if (pct != null && !s.stale) ...[
          const SizedBox(width: 12),
          _PercentRing(percent: pct, color: AppColors.forCls(s.prob!.cls)),
        ],
      ],
    );
  }
}

/// Small ring with the strike chance in the middle.
class _PercentRing extends StatelessWidget {
  const _PercentRing({required this.percent, required this.color});
  final int percent;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 64,
      height: 64,
      child: Stack(
        alignment: Alignment.center,
        children: [
          SizedBox(
            width: 64,
            height: 64,
            child: CircularProgressIndicator(
              value: percent / 100,
              strokeWidth: 5,
              strokeCap: StrokeCap.round,
              backgroundColor: AppColors.textMuted.withValues(alpha: 0.18),
              color: color,
            ),
          ),
          Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('$percent%', style: AppText.number(16, color: color)),
              const SizedBox(height: 2),
              Text('удар',
                  style: TextStyle(fontSize: 9.5, color: AppColors.textMuted)),
            ],
          ),
        ],
      ),
    );
  }
}

class _Metrics extends StatelessWidget {
  const _Metrics({required this.s});
  final _Situation s;

  @override
  Widget build(BuildContext context) {
    final n = s.nearest;
    final eta = (n == null || s.stale) ? null : etaNow(n, s.data.stateTs);
    return _Panel(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      child: Row(
        children: [
          Expanded(
              child: Metric(
                  value: s.stale ? '—' : '${s.focus.length}', label: 'Поруч')),
          Expanded(
            child: Metric(
                value: s.stale ? '—' : (n?.pdist?.round().toString() ?? '—'),
                unit: !s.stale && n?.pdist != null ? 'км' : null,
                label: 'Найближча'),
          ),
          Expanded(
              child: Metric(
                  value: eta == null ? '—' : fmtEta(eta),
                  label: 'Підліт',
                  size: 18)),
        ],
      ),
    );
  }
}

/// Glass-friendly inner panel (slightly raised surface).
class _Panel extends StatelessWidget {
  const _Panel({required this.child, this.padding = const EdgeInsets.all(14)});
  final Widget child;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return Container(
      clipBehavior: Clip.antiAlias,
      padding: padding,
      decoration: BoxDecoration(
        color: AppColors.textPrimary
            .withValues(alpha: AppColors.isDark ? 0.04 : 0.03),
        borderRadius: BorderRadius.circular(AppRadii.lg),
        border: Border.all(color: AppColors.glassBorder),
      ),
      child: child,
    );
  }
}

class _EmptyTargets extends StatelessWidget {
  const _EmptyTargets({required this.live});
  final bool live;

  @override
  Widget build(BuildContext context) {
    final c = live ? AppColors.safe : AppColors.textMuted;
    return _Panel(
      padding: const EdgeInsets.symmetric(vertical: 24, horizontal: 16),
      child: Column(
        children: [
          Icon(live ? Icons.verified_outlined : Icons.cloud_off_outlined,
              color: c, size: 28),
          const SizedBox(height: 8),
          Text(
              live
                  ? 'Наразі чисто — активних цілей немає'
                  : 'Немає свіжих даних від монітора',
              textAlign: TextAlign.center,
              style: TextStyle(
                  fontWeight: FontWeight.w700, color: AppColors.textPrimary)),
        ],
      ),
    );
  }
}

class _PlaceRow extends StatelessWidget {
  const _PlaceRow({
    required this.place,
    required this.data,
    required this.stale,
    this.prob,
  });
  final MonitoredPlace place;
  final MapDataController data;
  final bool stale;
  final ThreatProbability? prob;

  @override
  Widget build(BuildContext context) {
    final arr =
        stale ? const <MapTarget>[] : placeThreats(place, data.alarmTargets);
    final s = statusOf(worstOf(arr));
    final accent = stale ? AppColors.textMuted : AppColors.forCls(s.cls);

    String detail;
    if (stale) {
      detail = 'Немає свіжих даних';
    } else if (arr.isNotEmpty) {
      final n = arr.first;
      detail = '${n.label}${n.amount > 1 ? ' ×${n.amount}' : ''} · '
          '${n.pdist?.round() ?? '?'} км · ${fmtEta(etaNow(n, data.stateTs))}'
          '${arr.length > 1 ? ' · ще ${arr.length - 1}' : ''}';
    } else {
      detail = 'Загроз поруч немає';
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: _Panel(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
        child: Row(
          children: [
            Container(
              width: 10,
              height: 10,
              decoration: BoxDecoration(
                color: accent,
                shape: BoxShape.circle,
                boxShadow: [
                  BoxShadow(color: accent.withValues(alpha: 0.5), blurRadius: 8),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(place.name,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 14.5,
                          fontWeight: FontWeight.w700,
                          color: AppColors.textPrimary)),
                  const SizedBox(height: 3),
                  Text(detail,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 12.5,
                          color: AppColors.textSecondary,
                          fontFeatures: AppText.tabular)),
                ],
              ),
            ),
            if (prob != null && !stale) ...[
              const SizedBox(width: 10),
              Text('${prob!.percent}%',
                  style: AppText.number(18, color: accent)),
            ] else if (!stale) ...[
              const SizedBox(width: 10),
              Tag(s.title, color: accent),
            ],
          ],
        ),
      ),
    );
  }
}

class _TargetRow extends StatelessWidget {
  const _TargetRow(
      {required this.t, required this.stateTs, required this.stale});
  final MapTarget t;
  final int stateTs;
  final bool stale;

  @override
  Widget build(BuildContext context) {
    final threat = !stale && (kRank[t.stage] ?? 0) >= 3;
    final kind = kThreatColors[t.cat] ?? kThreatColors['other']!;
    final dist = t.pdist ?? t.distKm;
    return Opacity(
      opacity: stale ? 0.55 : 1,
      child: Container(
        color: threat ? AppColors.threat.withValues(alpha: 0.07) : null,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
        child: Row(
          children: [
            Container(
              width: 36,
              height: 36,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: const Color(0xF0101419),
                shape: BoxShape.circle,
                border: Border.all(
                    color: threat ? AppColors.threat : kind, width: 1.6),
              ),
              child: ThreatGlyph(t.cat, size: 20, heading: t.heading ?? 0),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          t.label,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w700,
                              color: threat
                                  ? AppColors.threatDark
                                  : AppColors.textPrimary),
                        ),
                      ),
                      if (t.amount > 1) ...[
                        const SizedBox(width: 6),
                        Text('×${t.amount}',
                            style: AppText.number(13, color: kind)),
                      ],
                    ],
                  ),
                  const SizedBox(height: 3),
                  Text(
                    [
                      if (t.stageLabel.isNotEmpty) t.stageLabel,
                      if (t.dir.isNotEmpty) 'напрям ${t.dir}',
                      if (t.place.isNotEmpty) t.place,
                    ].join(' · '),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style:
                        TextStyle(fontSize: 12, color: AppColors.textSecondary),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 10),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(dist != null ? '${dist.round()} км' : '—',
                    style: AppText.number(15,
                        color:
                            threat ? AppColors.threat : AppColors.textPrimary)),
                if (!stale) ...[
                  const SizedBox(height: 4),
                  Text(fmtEta(etaNow(t, stateTs)),
                      style: TextStyle(
                          fontSize: 11.5,
                          color: AppColors.textMuted,
                          fontFeatures: AppText.tabular)),
                ],
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Floating counters over the map: one glass chip per threat kind (with the
/// map's own silhouette), official alerts, and the snapshot time.
class _TopChips extends StatelessWidget {
  const _TopChips({required this.data, required this.alerts});
  final MapDataController data;
  final AlertsController alerts;

  static const _order = ['shahed', 'missile', 'balist', 'kab', 'avia', 'other'];

  @override
  Widget build(BuildContext context) {
    final counts = <String, int>{};
    for (final t in data.targets) {
      final k = _order.contains(t.cat) ? t.cat : 'other';
      counts[k] = (counts[k] ?? 0) + t.amount;
    }
    final stale = data.stale;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final k in _order)
              if ((counts[k] ?? 0) > 0)
                Glass(
                  radius: 14,
                  padding: const EdgeInsets.fromLTRB(10, 7, 12, 7),
                  child: Opacity(
                    opacity: stale ? 0.5 : 1,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        ThreatGlyph(k, size: 20),
                        const SizedBox(width: 8),
                        Text('${counts[k]}', style: AppText.number(20)),
                      ],
                    ),
                  ),
                ),
            if (counts.isEmpty && data.fresh)
              Glass(
                radius: 14,
                padding: const EdgeInsets.fromLTRB(10, 8, 12, 8),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(Icons.verified_outlined,
                        size: 18, color: AppColors.safe),
                    const SizedBox(width: 6),
                    Text('Цілей немає',
                        style: TextStyle(
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary)),
                  ],
                ),
              ),
            if (alerts.ok && alerts.redCount + alerts.yellowCount > 0)
              Glass(
                radius: 14,
                padding: const EdgeInsets.fromLTRB(10, 8, 12, 8),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 10,
                      height: 10,
                      decoration: BoxDecoration(
                          color: AppColors.threat,
                          borderRadius: BorderRadius.circular(3)),
                    ),
                    const SizedBox(width: 7),
                    Text('${alerts.redCount}', style: AppText.number(16)),
                    if (alerts.yellowCount > 0) ...[
                      const SizedBox(width: 10),
                      Container(
                        width: 10,
                        height: 10,
                        decoration: BoxDecoration(
                            color: AppColors.highlight,
                            borderRadius: BorderRadius.circular(3)),
                      ),
                      const SizedBox(width: 7),
                      Text('${alerts.yellowCount}', style: AppText.number(16)),
                    ],
                    const SizedBox(width: 5),
                    Text('обл.',
                        style: TextStyle(
                            fontSize: 12, color: AppColors.textSecondary)),
                  ],
                ),
              ),
          ],
        ),
        const SizedBox(height: 8),
        Glass(
          radius: 10,
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
          color: stale ? AppColors.warn.withValues(alpha: 0.88) : null,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (stale) ...[
                const Icon(Icons.warning_amber_rounded,
                    size: 14, color: Colors.black87),
                const SizedBox(width: 5),
              ],
              Text(
                !data.live
                    ? 'немає зв\'язку'
                    : stale
                        ? 'дані застарілі · ${fmtAge(data.dataAge)} тому'
                        : 'станом на ${fmtClock(data.stateTs)}',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                  color: stale ? Colors.black87 : AppColors.textSecondary,
                  fontFeatures: AppText.tabular,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
