import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../controllers/theme_controller.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import 'ui_kit.dart';

/// Live feed state shown in the navigation chrome (sidebar card / phone bar).
({String label, String detail, Color color, bool pulse}) feedStatus(
    MapDataController map, AlarmController alarm) {
  if (map.isDemo) {
    return (
      label: 'Демо-дані',
      detail: 'монітор не підключено',
      color: AppColors.warn,
      pulse: false,
    );
  }
  if (!map.live) {
    return (
      label: 'Немає зв\'язку',
      detail: 'чекаю дані монітора',
      color: AppColors.textMuted,
      pulse: true,
    );
  }
  if (alarm.level == 'alarm' || map.threatActive) {
    return (
      label: 'Активна загроза',
      detail: '${map.targets.length} цілей у повітрі',
      color: AppColors.threat,
      pulse: true,
    );
  }
  return (
    label: 'Все чисто',
    detail: map.targets.isEmpty
        ? 'цілей немає'
        : '${map.targets.length} цілей далеко',
    color: AppColors.safe,
    pulse: true,
  );
}

/// The WarAlarm mark: a small radar glyph in a rounded tile.
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 34});
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: AppColors.threat,
        borderRadius: BorderRadius.circular(size * 0.3),
        boxShadow: [
          BoxShadow(
              color: AppColors.threat.withValues(alpha: 0.35),
              blurRadius: 14,
              spreadRadius: -4),
        ],
      ),
      child: Icon(Icons.crisis_alert_rounded,
          color: Colors.white, size: size * 0.6),
    );
  }
}

/// Desktop / tablet navigation: a full sidebar, or an icon rail when narrow.
class AppSidebar extends StatelessWidget {
  const AppSidebar({super.key, required this.collapsed});

  final bool collapsed;

  static const _groups = <(String, List<AppTab>)>[
    ('Моніторинг', [AppTab.now, AppTab.map, AppTab.myPlaces]),
    ('Спільнота', [AppTab.chat, AppTab.ai]),
    ('Система', [AppTab.settings, AppTab.apiKeys]),
  ];

  @override
  Widget build(BuildContext context) {
    final width = collapsed ? 76.0 : 244.0;
    final nav = context.watch<NavController>();
    final map = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final st = feedStatus(map, alarm);

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
            _brand(),
            _statusCard(st),
            const SizedBox(height: 6),
            Expanded(
              child: ListView(
                padding: EdgeInsets.symmetric(horizontal: collapsed ? 12 : 14),
                children: [
                  for (final g in _groups) ...[
                    if (!collapsed)
                      Padding(
                        padding: const EdgeInsets.fromLTRB(10, 18, 0, 6),
                        child: Text(g.$1.toUpperCase(),
                            style: AppText.eyebrow().copyWith(fontSize: 10)),
                      )
                    else
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 10),
                        child: Divider(color: AppColors.divider, height: 1),
                      ),
                    for (final tab in g.$2)
                      _NavTile(
                        tab: tab,
                        active: nav.tab == tab,
                        collapsed: collapsed,
                        onTap: () => nav.select(tab),
                      ),
                  ],
                ],
              ),
            ),
            _footer(context, map),
          ],
        ),
      ),
    );
  }

  Widget _brand() {
    return Padding(
      padding: EdgeInsets.fromLTRB(collapsed ? 0 : 20, 20, 0, 16),
      child: Row(
        mainAxisAlignment:
            collapsed ? MainAxisAlignment.center : MainAxisAlignment.start,
        children: [
          const BrandMark(),
          if (!collapsed) ...[
            const SizedBox(width: 11),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('WarAlarm',
                    style: TextStyle(
                        fontSize: 17,
                        height: 1.1,
                        fontWeight: FontWeight.w800,
                        color: AppColors.textPrimary,
                        letterSpacing: -0.4)),
                Text('радар загроз',
                    style:
                        TextStyle(fontSize: 11.5, color: AppColors.textMuted)),
              ],
            ),
          ],
        ],
      ),
    );
  }

  Widget _statusCard(
      ({String label, String detail, Color color, bool pulse}) st) {
    final dot = LiveDot(color: st.color, pulse: st.pulse);
    if (collapsed) {
      return Tooltip(
        message: '${st.label} · ${st.detail}',
        child: Center(
          child: Container(
            width: 44,
            height: 34,
            decoration: BoxDecoration(
              color: st.color.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(AppRadii.md - 2),
            ),
            child: Center(child: dot),
          ),
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14),
      child: Container(
        padding: const EdgeInsets.fromLTRB(8, 9, 12, 9),
        decoration: BoxDecoration(
          color: st.color.withValues(alpha: AppColors.isDark ? 0.09 : 0.08),
          borderRadius: BorderRadius.circular(AppRadii.md),
          border: Border.all(color: st.color.withValues(alpha: 0.25)),
        ),
        child: Row(
          children: [
            dot,
            const SizedBox(width: 6),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(st.label,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w800,
                          color: AppColors.textPrimary)),
                  Text(st.detail,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 11.5,
                          color: AppColors.textSecondary,
                          fontFeatures: AppText.tabular)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _footer(BuildContext context, MapDataController map) {
    final theme = context.watch<ThemeController>();
    final toggle = IconButton(
      tooltip: theme.dark ? 'Світла тема' : 'Темна тема',
      onPressed: theme.toggle,
      icon: Icon(theme.dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined,
          size: 19, color: AppColors.textSecondary),
    );
    if (collapsed) {
      return Padding(padding: const EdgeInsets.all(10), child: toggle);
    }
    return Container(
      margin: const EdgeInsets.fromLTRB(14, 0, 14, 12),
      padding: const EdgeInsets.only(left: 12),
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: AppColors.divider)),
      ),
      child: Row(
        children: [
          Icon(Icons.home_outlined, size: 15, color: AppColors.textMuted),
          const SizedBox(width: 7),
          Expanded(
            child: Text(map.homeName,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 12, color: AppColors.textMuted)),
          ),
          toggle,
        ],
      ),
    );
  }
}

class _NavTile extends StatelessWidget {
  const _NavTile({
    required this.tab,
    required this.active,
    required this.collapsed,
    required this.onTap,
  });

  final AppTab tab;
  final bool active;
  final bool collapsed;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final fg = active ? AppColors.textPrimary : AppColors.textSecondary;
    final tile = Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(AppRadii.md - 2),
          onTap: onTap,
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 180),
            curve: Curves.easeOut,
            padding: EdgeInsets.symmetric(
                horizontal: collapsed ? 0 : 10, vertical: 10),
            decoration: BoxDecoration(
              color: active ? AppColors.surface2 : Colors.transparent,
              borderRadius: BorderRadius.circular(AppRadii.md - 2),
              border: Border.all(
                  color: active ? AppColors.border : Colors.transparent),
            ),
            child: Row(
              mainAxisAlignment: collapsed
                  ? MainAxisAlignment.center
                  : MainAxisAlignment.start,
              children: [
                Icon(active ? tab.activeIcon : tab.icon,
                    size: 20, color: active ? AppColors.primary : fg),
                if (!collapsed) ...[
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(
                      tab.label,
                      style: TextStyle(
                        fontSize: 14,
                        fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                        color: fg,
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
    return collapsed ? Tooltip(message: tab.label, child: tile) : tile;
  }
}

/// Phone header: brand + live status, above every screen.
class CompactTopBar extends StatelessWidget {
  const CompactTopBar({super.key});

  @override
  Widget build(BuildContext context) {
    final map = context.watch<MapDataController>();
    final alarm = context.watch<AlarmController>();
    final theme = context.watch<ThemeController>();
    final st = feedStatus(map, alarm);
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 8, 6, 8),
      decoration: BoxDecoration(
        color: AppColors.sidebarBg,
        border: Border(bottom: BorderSide(color: AppColors.border)),
      ),
      child: Row(
        children: [
          const BrandMark(size: 28),
          const SizedBox(width: 10),
          Text('WarAlarm',
              style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w800,
                  letterSpacing: -0.4,
                  color: AppColors.textPrimary)),
          const Spacer(),
          Container(
            padding: const EdgeInsets.fromLTRB(4, 3, 10, 3),
            decoration: BoxDecoration(
              color: st.color.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(20),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                LiveDot(color: st.color, size: 7, pulse: st.pulse),
                const SizedBox(width: 2),
                Text(st.label,
                    style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textPrimary)),
              ],
            ),
          ),
          IconButton(
            tooltip: theme.dark ? 'Світла тема' : 'Темна тема',
            onPressed: theme.toggle,
            icon: Icon(
                theme.dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined,
                size: 20),
          ),
        ],
      ),
    );
  }
}

/// Phone navigation: four main tabs + «Ще» (AI, settings, API).
class AppBottomBar extends StatelessWidget {
  const AppBottomBar({super.key});

  static const _main = [AppTab.now, AppTab.map, AppTab.myPlaces, AppTab.chat];
  static const _more = [AppTab.ai, AppTab.settings, AppTab.apiKeys];

  @override
  Widget build(BuildContext context) {
    final nav = context.watch<NavController>();
    final moreActive = _more.contains(nav.tab);
    return Container(
      decoration: BoxDecoration(
        color: AppColors.sidebarBg,
        border: Border(top: BorderSide(color: AppColors.border)),
      ),
      child: SafeArea(
        top: false,
        child: SizedBox(
          height: 62,
          child: Row(
            children: [
              for (final t in _main)
                _BarItem(
                  icon: nav.tab == t ? t.activeIcon : t.icon,
                  label: t.shortLabel,
                  active: nav.tab == t,
                  onTap: () => nav.select(t),
                ),
              _BarItem(
                icon: moreActive ? nav.tab.activeIcon : Icons.more_horiz_rounded,
                label: moreActive ? nav.tab.shortLabel : 'Ще',
                active: moreActive,
                onTap: () => _openMore(context, nav),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _openMore(BuildContext context, NavController nav) {
    showModalBottomSheet<void>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final t in _more)
                ListTile(
                  leading: IconTile(t.activeIcon,
                      color: nav.tab == t ? AppColors.primary : AppColors.textSecondary,
                      size: 36),
                  title: Text(t.label,
                      style: const TextStyle(fontWeight: FontWeight.w700)),
                  trailing: nav.tab == t
                      ? Icon(Icons.check_rounded, color: AppColors.primary)
                      : Icon(Icons.chevron_right_rounded,
                          color: AppColors.textMuted),
                  onTap: () {
                    Navigator.pop(ctx);
                    nav.select(t);
                  },
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _BarItem extends StatelessWidget {
  const _BarItem({
    required this.icon,
    required this.label,
    required this.active,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final c = active ? AppColors.primary : AppColors.textMuted;
    return Expanded(
      child: InkWell(
        onTap: onTap,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            AnimatedContainer(
              duration: const Duration(milliseconds: 180),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
              decoration: BoxDecoration(
                color: active
                    ? AppColors.primary.withValues(alpha: 0.14)
                    : Colors.transparent,
                borderRadius: BorderRadius.circular(20),
              ),
              child: Icon(icon, size: 22, color: c),
            ),
            const SizedBox(height: 3),
            Text(label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                    fontSize: 11,
                    fontWeight: active ? FontWeight.w800 : FontWeight.w600,
                    color: active ? AppColors.textPrimary : AppColors.textMuted)),
          ],
        ),
      ),
    );
  }
}
