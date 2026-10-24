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
/// glass capsule of the main destinations with a signal-yellow indicator.
class FloatingNavBar extends StatelessWidget {
  const FloatingNavBar({super.key});

  static const _items = [
    AppTab.now,
    AppTab.map,
    AppTab.myPlaces,
    AppTab.chat,
    AppTab.settings,
  ];

  @override
  Widget build(BuildContext context) {
    final nav = context.watch<NavController>();
    return Row(
      children: [
        GlassIconButton(
          icon: Icons.menu_rounded,
          size: 54,
          tooltip: 'Меню',
          onTap: () => showAppMenu(context),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Glass(
            radius: 27,
            padding: const EdgeInsets.symmetric(horizontal: 6),
            child: SizedBox(
              height: 54,
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
    final indicator = AnimatedContainer(
      duration: const Duration(milliseconds: 200),
      width: vertical ? 3 : (active ? 18 : 0),
      height: vertical ? (active ? 18 : 0) : 3,
      decoration: BoxDecoration(
        color: AppColors.highlight,
        borderRadius: BorderRadius.circular(2),
      ),
    );
    final icon = Icon(active ? tab.activeIcon : tab.icon,
        size: 23,
        color: active ? AppColors.textPrimary : AppColors.textSecondary);
    return Tooltip(
      message: tab.label,
      child: InkResponse(
        onTap: onTap,
        radius: 26,
        child: SizedBox(
          height: 54,
          width: 54,
          child: vertical
              ? Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [indicator, const SizedBox(width: 8), icon, const SizedBox(width: 11)],
                )
              : Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [icon, const SizedBox(height: 5), indicator],
                ),
        ),
      ),
    );
  }
}

/// Desktop navigation: a vertical glass rail floating at the left edge.
class FloatingRail extends StatelessWidget {
  const FloatingRail({super.key});

  static const _items = [
    AppTab.now,
    AppTab.map,
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
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Padding(
            padding: EdgeInsets.only(top: 4, bottom: 10),
            child: BrandMark(size: 38),
          ),
          for (final t in _items)
            _NavIcon(
              tab: t,
              active: nav.tab == t,
              vertical: true,
              onTap: () => nav.select(t),
            ),
          const SizedBox(height: 6),
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
