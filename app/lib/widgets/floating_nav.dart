import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../controllers/theme_controller.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import 'brand_icon.dart';
import 'glass.dart';
import 'ui_kit.dart';

/// Live feed state shown in the navigation chrome and the place card.
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
  if (map.stale) {
    return (
      label: 'Дані застаріли',
      detail: 'монітор не оновлює ${fmtAge(map.dataAge)}',
      color: AppColors.warn,
      pulse: false,
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

/// "5 хв", "3 год", "2 дн" — how long ago.
String fmtAge(Duration d) {
  if (d.inMinutes < 60) return '${d.inMinutes} хв';
  if (d.inHours < 48) return '${d.inHours} год';
  return '${d.inDays} дн';
}

/// Phone navigation, floating over the content: a round menu button and a
/// glass capsule of the main destinations (icon + short label, active pill).
class FloatingNavBar extends StatelessWidget {
  const FloatingNavBar({super.key});

  static const _items = [
    AppTab.now,
    AppTab.map,
    AppTab.analytics,
    AppTab.myPlaces,
    AppTab.settings,
  ];

  @override
  Widget build(BuildContext context) {
    final nav = context.watch<NavController>();
    return Row(
      children: [
        GlassIconButton(
          icon: Icons.menu_rounded,
          size: 58,
          tooltip: 'Меню',
          onTap: () => showAppMenu(context),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Glass(
            radius: 29,
            padding: const EdgeInsets.symmetric(horizontal: 5),
            child: SizedBox(
              height: 58,
              child: Row(
                children: [
                  for (final t in _items)
                    Expanded(
                      child: _NavIcon(
                        tab: t,
                        active: nav.tab == t,
                        onTap: () => nav.select(t),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

/// Short labels that fit under a nav icon.
String _shortLabel(AppTab t) => switch (t) {
      AppTab.myPlaces => 'Місця',
      AppTab.settings => 'Опції',
      AppTab.ai => 'ШІ',
      AppTab.apiKeys => 'API',
      _ => t.label,
    };

class _NavIcon extends StatelessWidget {
  const _NavIcon({
    required this.tab,
    required this.active,
    required this.onTap,
    this.vertical = false,
  });

  final AppTab tab;
  final bool active;
  final VoidCallback onTap;
  final bool vertical;

  @override
  Widget build(BuildContext context) {
    final fg = active ? AppColors.primary : AppColors.textSecondary;
    final body = Column(
      mainAxisSize: MainAxisSize.min,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        AnimatedContainer(
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOutCubic,
          width: vertical ? 54 : 46,
          height: vertical ? 34 : 28,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(17),
            gradient: active
                ? LinearGradient(
                    colors: [
                      AppColors.primary.withValues(alpha: AppColors.isDark ? 0.30 : 0.18),
                      AppColors.accent.withValues(alpha: AppColors.isDark ? 0.18 : 0.10),
                    ],
                  )
                : null,
          ),
          child: Icon(active ? tab.activeIcon : tab.icon, size: vertical ? 22 : 21, color: fg),
        ),
        const SizedBox(height: 3),
        Text(
          _shortLabel(tab),
          maxLines: 1,
          overflow: TextOverflow.fade,
          softWrap: false,
          style: TextStyle(
            fontSize: vertical ? 11 : 10.5,
            height: 1.1,
            fontWeight: active ? FontWeight.w800 : FontWeight.w600,
            color: active ? AppColors.textPrimary : AppColors.textMuted,
          ),
        ),
      ],
    );
    return Tooltip(
      message: tab.label,
      waitDuration: const Duration(milliseconds: 600),
      child: InkResponse(
        onTap: onTap,
        radius: 30,
        highlightShape: BoxShape.rectangle,
        borderRadius: BorderRadius.circular(16),
        child: SizedBox(height: vertical ? 64 : 58, width: vertical ? 80 : null, child: body),
      ),
    );
  }
}

/// Tablet / desktop navigation: a vertical glass rail with labelled icons.
class FloatingRail extends StatelessWidget {
  const FloatingRail({super.key});

  static const width = 88.0;

  static const _items = [
    AppTab.now,
    AppTab.map,
    AppTab.analytics,
    AppTab.myPlaces,
    AppTab.chat,
    AppTab.ai,
    AppTab.settings,
    AppTab.apiKeys,
  ];

  @override
  Widget build(BuildContext context) {
    final nav = context.watch<NavController>();
    final theme = context.watch<ThemeController>();
    return Glass(
      radius: 30,
      padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 4),
      child: SizedBox(
        width: width - 8,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Padding(
                padding: EdgeInsets.only(top: 2, bottom: 10),
                child: BrandMark(size: 40),
              ),
              for (final t in _items)
                _NavIcon(
                  tab: t,
                  active: nav.tab == t,
                  vertical: true,
                  onTap: () => nav.select(t),
                ),
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 6, horizontal: 18),
                child: Divider(height: 1, color: AppColors.glassBorder),
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
        ),
      ),
    );
  }
}

/// Full menu (phone «≡»): status, every destination, theme switch.
void showAppMenu(BuildContext context) {
  final nav = context.read<NavController>();
  showModalBottomSheet<void>(
    context: context,
    builder: (ctx) {
      final map = ctx.watch<MapDataController>();
      final alarm = ctx.watch<AlarmController>();
      final theme = ctx.watch<ThemeController>();
      final st = feedStatus(map, alarm);
      return SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  const BrandMark(size: 40),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('WarAlarm',
                            style: TextStyle(
                                fontSize: 18,
                                fontWeight: FontWeight.w800,
                                letterSpacing: -0.4,
                                color: AppColors.textPrimary)),
                        Row(
                          children: [
                            LiveDot(color: st.color, size: 6, pulse: st.pulse),
                            Flexible(
                              child: Text('${st.label} · ${st.detail}',
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                      fontSize: 12.5,
                                      color: AppColors.textSecondary)),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 14),
              for (final t in AppTab.values)
                ListTile(
                  contentPadding: const EdgeInsets.symmetric(horizontal: 8),
                  leading: IconTile(t.activeIcon,
                      color: nav.tab == t
                          ? AppColors.highlight
                          : AppColors.textSecondary,
                      size: 36),
                  title: Text(t.label,
                      style: const TextStyle(fontWeight: FontWeight.w700)),
                  trailing: nav.tab == t
                      ? Icon(Icons.check_rounded, color: AppColors.highlight)
                      : Icon(Icons.chevron_right_rounded,
                          color: AppColors.textMuted),
                  onTap: () {
                    Navigator.pop(ctx);
                    nav.select(t);
                  },
                ),
              const Divider(height: 18),
              SwitchListTile(
                contentPadding: const EdgeInsets.symmetric(horizontal: 8),
                secondary: IconTile(Icons.dark_mode_outlined,
                    color: AppColors.textSecondary, size: 36),
                title: const Text('Темна тема',
                    style: TextStyle(fontWeight: FontWeight.w700)),
                value: theme.dark,
                onChanged: (v) {
                  Navigator.pop(ctx);
                  theme.setDark(v);
                },
              ),
              Padding(
                padding: const EdgeInsets.only(top: 4, left: 8),
                child: Text('Оновлено ${fmtClock(map.stateTs)}',
                    style: AppText.eyebrow()),
              ),
            ],
          ),
        ),
      );
    },
  );
}

/// HH:mm:ss of an epoch-ms timestamp.
String fmtClock(int ms) {
  final t = DateTime.fromMillisecondsSinceEpoch(ms);
  String two(int v) => v.toString().padLeft(2, '0');
  return '${two(t.hour)}:${two(t.minute)}:${two(t.second)}';
}
