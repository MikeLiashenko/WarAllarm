import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../controllers/settings_controller.dart';
import '../domain/probability.dart';
import '../models/monitored_place.dart';
import '../services/background_alarm.dart';
import '../services/home_widget_service.dart';
import '../theme/app_colors.dart';
import '../widgets/alarm_overlay.dart';
import '../widgets/boom_prompt.dart';
import '../widgets/app_sidebar.dart';
import '../widgets/responsive.dart';
import 'ai_screen.dart';
import 'api_keys_screen.dart';
import 'chat_screen.dart';
import 'map_screen.dart';
import 'my_places_screen.dart';
import 'now_screen.dart';
import 'settings_screen.dart';

/// Root layout: fixed left sidebar + active screen. Responsive between desktop
/// windows and Android phone ratios.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key});

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> with WidgetsBindingObserver {
  MapDataController? _map;
  AlertsController? _alerts;
  SettingsController? _settings;
  DateTime _lastWidget = DateTime.fromMillisecondsSinceEpoch(0);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // The in-app siren handles alerts while we're open; tell the background
    // watcher to stay quiet so it doesn't double up.
    BackgroundAlarm.setAppForeground(true);
    // Keep the home-screen widget fresh while the app is open.
    _map = context.read<MapDataController>()..addListener(_pushWidget);
    _alerts = context.read<AlertsController>()..addListener(_pushWidget);
    _settings = context.read<SettingsController>();
    WidgetsBinding.instance.addPostFrameCallback((_) => _pushWidget());
  }

  void _pushWidget() {
    final map = _map, alerts = _alerts, settings = _settings;
    if (map == null || alerts == null || settings == null) return;
    final now = DateTime.now();
    if (now.difference(_lastWidget).inSeconds < 20) return; // throttle
    _lastWidget = now;
    final home = MonitoredPlace(
        id: 'home', icon: '🏠', name: map.homeName, lat: map.home.lat, lon: map.home.lon);
    HomeWidgetService.push(
      placeName: map.homeName,
      lat: map.home.lat,
      lon: map.home.lon,
      targets: map.isDemo ? const [] : map.targets,
      radiusKm: settings.alarmRadiusKm,
      officialLevel: officialLevelFor(home, alerts.levels),
      live: map.live,
    );
  }

  @override
  void dispose() {
    _map?.removeListener(_pushWidget);
    _alerts?.removeListener(_pushWidget);
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    BackgroundAlarm.setAppForeground(state == AppLifecycleState.resumed);
  }

  @override
  Widget build(BuildContext context) {
    final nav = context.watch<NavController>();
    final collapsed = Responsive.railCollapsed(context);
    final phone = Responsive.useBottomNav(context);
    // Built fresh (non-const) each build so a theme change rebuilds every screen
    // and it re-reads the (mutable) AppColors palette.
    final screens = <Widget>[
      NowScreen(),
      MapScreen(),
      MyPlacesScreen(),
      SettingsScreen(),
      ChatScreen(),
      AiScreen(),
      ApiKeysScreen(),
    ];

    final content = ColoredBox(
      color: AppColors.background,
      child: AnimatedSwitcher(
        duration: const Duration(milliseconds: 200),
        switchInCurve: Curves.easeOut,
        transitionBuilder: (child, animation) => FadeTransition(
          opacity: animation,
          child: SlideTransition(
            position: Tween<Offset>(
              begin: const Offset(0, 0.015),
              end: Offset.zero,
            ).animate(animation),
            child: child,
          ),
        ),
        child: KeyedSubtree(
          key: ValueKey(nav.tab),
          child: screens[nav.tab.index],
        ),
      ),
    );

    final scaffold = Scaffold(
      backgroundColor: AppColors.background,
      bottomNavigationBar: phone ? const AppBottomBar() : null,
      body: phone
          ? Column(
              children: [
                ColoredBox(
                  color: AppColors.sidebarBg,
                  child: const SafeArea(bottom: false, child: CompactTopBar()),
                ),
                Expanded(
                  child: MediaQuery.removePadding(
                      context: context, removeTop: true, child: content),
                ),
              ],
            )
          : Row(
              children: [
                AppSidebar(collapsed: collapsed),
                Expanded(child: content),
              ],
            ),
    );

    // Overlays sit above the whole scaffold (bottom bar included).
    return Material(
      type: MaterialType.transparency,
      child: Stack(
        children: [
          Positioned.fill(child: scaffold),
          // Siren overlay + snooze bar, above every screen.
          const AlarmOverlay(),
          // After an alarm ends, offer the optional "чи було чути вибух?" window.
          const BoomPromptWatcher(),
        ],
      ),
    );
  }
}

/// Shared page scaffold: centered, max-width, scroll-safe content padding.
class PageBody extends StatelessWidget {
  const PageBody({super.key, required this.child, this.scrollable = true});

  final Widget child;
  final bool scrollable;

  @override
  Widget build(BuildContext context) {
    final compact = Responsive.isCompact(context);
    final padding = EdgeInsets.symmetric(
      horizontal: compact ? 14 : 32,
      vertical: compact ? 14 : 26,
    );
    final content = ConstrainedBox(
      constraints:
          BoxConstraints(maxWidth: Responsive.contentMaxWidth(context)),
      child: child,
    );

    if (!scrollable) {
      // Fixed-height screens (Map, Chat) manage their own internal scrolling.
      return SafeArea(
        child: Align(
          alignment: Alignment.topCenter,
          child: Padding(padding: padding, child: content),
        ),
      );
    }
    return SafeArea(
      child: SingleChildScrollView(
        child: Padding(
          padding: padding,
          child: Center(child: content),
        ),
      ),
    );
  }
}
