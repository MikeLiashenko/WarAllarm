import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../theme/app_colors.dart';

/// Fixed left navigation. Shows a smooth animated active-tab indicator and a
/// live threat pill at the top. Collapses to icons-only on narrow windows.
class AppSidebar extends StatelessWidget {
  const AppSidebar({super.key, required this.collapsed});

  final bool collapsed;

  static const _items = <(AppTab, IconData)>[
    (AppTab.now, Icons.dashboard_rounded),
    (AppTab.map, Icons.map_rounded),
    (AppTab.myPlaces, Icons.place_rounded),
    (AppTab.settings, Icons.settings_rounded),
    (AppTab.chat, Icons.chat_bubble_rounded),
    (AppTab.ai, Icons.auto_awesome_rounded),
    (AppTab.apiKeys, Icons.vpn_key_rounded),
  ];

  @override
  Widget build(BuildContext context) {
    final width = collapsed ? 76.0 : 248.0;
    final nav = context.watch<NavController>();
    final threat = context.watch<MapDataController>();

    return AnimatedContainer(
      duration: const Duration(milliseconds: 240),
      curve: Curves.easeOut,
      width: width,
      decoration: BoxDecoration(
        color: AppColors.sidebarBg,
        border: Border(right: BorderSide(color: AppColors.border)),
      ),
      child: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _brand(collapsed),
            _threatPill(threat, collapsed),
            const SizedBox(height: 8),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                children: [
                  for (final item in _items)
                    _NavTile(
                      tab: item.$1,
                      icon: item.$2,
                      active: nav.tab == item.$1,
                      collapsed: collapsed,
                      onTap: () => nav.select(item.$1),
                    ),
                ],
              ),
            ),
            _footer(collapsed),
          ],
        ),
      ),
    );
  }

  Widget _brand(bool collapsed) {
    return Padding(
      padding: EdgeInsets.fromLTRB(collapsed ? 0 : 22, 22, 0, 14),
      child: Row(
        mainAxisAlignment:
            collapsed ? MainAxisAlignment.center : MainAxisAlignment.start,
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [AppColors.accent, AppColors.primary],
              ),
              borderRadius: BorderRadius.circular(12),
            ),
            child: const Icon(Icons.shield_rounded,
                color: Colors.white, size: 22),
          ),
          if (!collapsed) ...[
            const SizedBox(width: 12),
            Text(
              'WarAlarm',
              style: TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w800,
                color: AppColors.textPrimary,
                letterSpacing: -0.5,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _threatPill(MapDataController threat, bool collapsed) {
    final demo = threat.isDemo;
    final active = threat.threatActive;
    final color = demo
        ? const Color(0xFFB8860B)
        : active
            ? AppColors.threat
            : AppColors.safe;
    final bg = demo
        ? const Color(0xFFFFF2D6)
        : active
            ? AppColors.threatSoft
            : AppColors.safeSoft;
    final label = demo
        ? 'Демо-дані'
        : active
            ? 'Активна загроза'
            : 'Все чисто';
    return Padding(
      padding: EdgeInsets.symmetric(horizontal: collapsed ? 14 : 18),
      child: Container(
        padding: EdgeInsets.symmetric(
            horizontal: collapsed ? 0 : 12, vertical: 10),
        decoration: BoxDecoration(
          color: bg,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          mainAxisAlignment:
              collapsed ? MainAxisAlignment.center : MainAxisAlignment.start,
          children: [
            Container(
              width: 9,
              height: 9,
              decoration: BoxDecoration(color: color, shape: BoxShape.circle),
            ),
            if (!collapsed) ...[
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  label,
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w700,
                    color: demo
                        ? const Color(0xFF8A6D0B)
                        : active
                            ? AppColors.threatDark
                            : AppColors.textPrimary,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _footer(bool collapsed) {
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Row(
        mainAxisAlignment:
            collapsed ? MainAxisAlignment.center : MainAxisAlignment.start,
        children: [
          Icon(Icons.circle, size: 8, color: AppColors.primary),
          if (!collapsed) ...[
            const SizedBox(width: 8),
            Text(
              'Live · Kharkiv',
              style: TextStyle(fontSize: 12, color: AppColors.textMuted),
            ),
          ],
        ],
      ),
    );
  }
}

class _NavTile extends StatelessWidget {
  const _NavTile({
    required this.tab,
    required this.icon,
    required this.active,
    required this.collapsed,
    required this.onTap,
  });

  final AppTab tab;
  final IconData icon;
  final bool active;
  final bool collapsed;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(14),
          onTap: onTap,
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 200),
            curve: Curves.easeOut,
            padding: EdgeInsets.symmetric(
                horizontal: collapsed ? 0 : 14, vertical: 12),
            decoration: BoxDecoration(
              color: active ? AppColors.sidebarActive : Colors.transparent,
              borderRadius: BorderRadius.circular(14),
            ),
            child: Row(
              mainAxisAlignment: collapsed
                  ? MainAxisAlignment.center
                  : MainAxisAlignment.start,
              children: [
                Icon(
                  icon,
                  size: 22,
                  color: active ? AppColors.primaryDark : AppColors.textSecondary,
                ),
                if (!collapsed) ...[
                  const SizedBox(width: 14),
                  Expanded(
                    child: Text(
                      tab.label,
                      style: TextStyle(
                        fontSize: 14.5,
                        fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                        color: active
                            ? AppColors.primaryDark
                            : AppColors.textSecondary,
                      ),
                    ),
                  ),
                  if (active)
                    Container(
                      width: 6,
                      height: 6,
                      decoration: BoxDecoration(
                        color: AppColors.primary,
                        shape: BoxShape.circle,
                      ),
                    ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
