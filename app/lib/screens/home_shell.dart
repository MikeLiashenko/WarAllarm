import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/nav_controller.dart';
import '../theme/app_colors.dart';
import '../widgets/alarm_overlay.dart';
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
class HomeShell extends StatelessWidget {
  const HomeShell({super.key});

  @override
  Widget build(BuildContext context) {
    final nav = context.watch<NavController>();
    final collapsed = Responsive.railCollapsed(context);
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

    return Scaffold(
      backgroundColor: AppColors.background,
      body: Stack(
        children: [
          Row(
            children: [
              AppSidebar(collapsed: collapsed),
              Expanded(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment.topCenter,
                      end: Alignment.bottomCenter,
                      colors: [AppColors.background, AppColors.scaffold],
                    ),
                  ),
                  child: AnimatedSwitcher(
                    duration: const Duration(milliseconds: 220),
                    switchInCurve: Curves.easeOut,
                    transitionBuilder: (child, animation) => FadeTransition(
                      opacity: animation,
                      child: SlideTransition(
                        position: Tween<Offset>(
                          begin: const Offset(0, 0.02),
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
                ),
              ),
            ],
          ),
          // Siren overlay + snooze bar, above every screen.
          const AlarmOverlay(),
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
    final padding = EdgeInsets.symmetric(
      horizontal: Responsive.isCompact(context) ? 16 : 28,
      vertical: 22,
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
