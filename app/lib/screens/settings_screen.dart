import 'dart:io';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/settings_controller.dart';
import '../controllers/theme_controller.dart';
import '../firebase_config.dart';
import '../services/ai_service.dart';
import '../services/background_alarm.dart';
import '../theme/app_colors.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();
    final map = context.watch<MapDataController>();

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionHeader(
            title: 'Налаштування',
            subtitle: 'Тривога, сповіщення, вигляд',
            icon: Icons.tune_rounded,
          ),
          const SizedBox(height: 18),

          _Group(
            title: 'Профіль',
            children: [
              _NameField(
                value: s.displayName,
                onChanged: s.setDisplayName,
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'Сповіщення',
            children: [
              _ToggleRow(
                icon: Icons.notifications_active_rounded,
                title: 'Push-сповіщення',
                subtitle: 'Отримувати тривоги на цьому пристрої',
                value: s.pushNotifications,
                onChanged: s.setPush,
              ),
              _ToggleRow(
                icon: Icons.volume_up_rounded,
                title: 'Звук тривоги',
                subtitle: 'Сирена при новій загрозі',
                value: s.soundAlerts,
                onChanged: s.setSound,
              ),
              _ToggleRow(
                icon: Icons.vibration_rounded,
                title: 'Вібрація',
                subtitle: 'Вібрувати при активній загрозі',
                value: s.vibration,
                onChanged: s.setVibration,
              ),
              _ToggleRow(
                icon: Icons.crisis_alert_rounded,
                title: 'Лише загрози',
                subtitle: 'Ховати в стрічці все, що не є загрозою',
                value: s.threatOnly,
                onChanged: s.setThreatOnly,
                accent: AppColors.threat,
              ),
              const _VolumeSlider(),
              const _TestSirenButton(),
            ],
          ),
          const SizedBox(height: 14),

          if (Platform.isAndroid) ...[
            _Group(
              title: 'Фонові тривоги',
              children: const [_BackgroundAlarmToggle()],
            ),
            const SizedBox(height: 14),
          ],

          _Group(
            title: 'Тривога та карта',
            children: const [
              _AlarmRadiusSlider(),
              _SnoozeChoice(),
              _AudibilitySetting(),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'ШІ · AI',
            children: [
              if (s.keyFromSecrets)
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
                  child: Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color: AppColors.safeSoft,
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Row(
                      children: [
                        Icon(Icons.check_circle_rounded,
                            color: AppColors.safe, size: 20),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Text(
                            'Ключ ІІ взято з secrets.local.txt (${AiService.providerLabel(s.aiApiKey)}). '
                            'Нижче можна перевизначити для цього пристрою.',
                            style: TextStyle(
                                fontSize: 12.5, color: AppColors.textSecondary),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              _AiKeyField(
                value: s.keyFromSecrets ? '' : s.aiApiKey,
                onChanged: s.setAiApiKey,
              ),
              _AiModelField(
                value: s.keyFromSecrets ? '' : s.aiModel,
                apiKey: s.aiApiKey,
                onChanged: s.setAiModel,
              ),
              Padding(
                padding: EdgeInsets.fromLTRB(16, 2, 16, 10),
                child: Text(
                  'Ключ Anthropic (sk-ant-…) або Google (AIza…/AQ…) вмикає '
                  'справжній ІІ у вкладці ШІ. Зберігається лише на цьому пристрої.',
                  style: TextStyle(fontSize: 12, color: AppColors.textMuted),
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'Дані',
            children: [
              _ToggleRow(
                icon: Icons.science_rounded,
                title: 'Демо-дані',
                subtitle: map.live
                    ? 'Підключено до монітора — реальні дані'
                    : 'Показувати приклад загроз (вимкни для чистого екрана)',
                value: map.showDemo,
                onChanged: map.setShowDemo,
                accent: AppColors.warn,
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'Вигляд',
            children: [
              _ToggleRow(
                icon: Icons.dark_mode_rounded,
                title: 'Темна тема',
                subtitle: 'Темний вигляд додатку',
                value: context.watch<ThemeController>().dark,
                onChanged: (v) =>
                    context.read<ThemeController>().setDark(v),
              ),
              _ToggleRow(
                icon: Icons.view_agenda_rounded,
                title: 'Компактна стрічка',
                subtitle: 'Щільніший список подій',
                value: s.compactFeed,
                onChanged: s.setCompactFeed,
              ),
              _ToggleRow(
                icon: Icons.screen_lock_portrait_rounded,
                title: 'Не вимикати екран',
                subtitle: 'Під час активної тривоги екран не гасне',
                value: s.keepScreenOn,
                onChanged: s.setKeepScreenOn,
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'Про застосунок',
            children: const [
              _InfoRow(label: 'Проєкт', value: FirebaseConfig.projectId),
              _InfoRow(label: 'Бекенд', value: 'Realtime Database (REST + SSE)'),
              _InfoRow(label: 'Версія', value: '1.1.0 · новий дизайн'),
              _InfoRow(
                  label: 'Платформи', value: 'Android · Windows Desktop'),
            ],
          ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }
}

class _VolumeSlider extends StatelessWidget {
  const _VolumeSlider();

  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();
    final pct = (s.alarmVolume * 100).round();
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 4, 12, 2),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.10),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(Icons.graphic_eq_rounded,
                color: AppColors.primary, size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text('Гучність сирени',
                        style: TextStyle(
                            fontSize: 14.5,
                            fontWeight: FontWeight.w600,
                            color: AppColors.textPrimary)),
                    const Spacer(),
                    Text('$pct%',
                        style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: AppColors.primaryDark)),
                  ],
                ),
                Slider(
                  value: s.alarmVolume.clamp(0.0, 1.0),
                  min: 0.0,
                  max: 1.0,
                  divisions: 20,
                  activeColor: AppColors.primary,
                  label: '$pct%',
                  onChanged: (v) =>
                      context.read<SettingsController>().setAlarmVolume(v),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _SettingSlider extends StatelessWidget {
  const _SettingSlider({
    required this.icon,
    required this.title,
    required this.value,
    required this.min,
    required this.max,
    required this.divisions,
    required this.valueLabel,
    required this.onChanged,
  });
  final IconData icon;
  final String title;
  final double value, min, max;
  final int divisions;
  final String valueLabel;
  final ValueChanged<double> onChanged;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 4, 12, 2),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.10),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(icon, color: AppColors.primary, size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text(title,
                        style: TextStyle(
                            fontSize: 14.5,
                            fontWeight: FontWeight.w600,
                            color: AppColors.textPrimary)),
                    const Spacer(),
                    Text(valueLabel,
                        style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: AppColors.primaryDark)),
                  ],
                ),
                Slider(
                  value: value.clamp(min, max),
                  min: min,
                  max: max,
                  divisions: divisions,
                  activeColor: AppColors.primary,
                  label: valueLabel,
                  onChanged: onChanged,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _AlarmRadiusSlider extends StatelessWidget {
  const _AlarmRadiusSlider();
  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();
    return _SettingSlider(
      icon: Icons.crisis_alert_rounded,
      title: 'Радіус тривоги',
      value: s.alarmRadiusKm,
      min: 5,
      max: 100,
      divisions: 19,
      valueLabel: '${s.alarmRadiusKm.round()} км',
      onChanged: (v) => context.read<SettingsController>().setAlarmRadiusKm(v),
    );
  }
}

class _AudibilitySetting extends StatelessWidget {
  const _AudibilitySetting();
  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();
    return Column(
      children: [
        _ToggleRow(
          icon: Icons.hearing_rounded,
          title: 'Радіус чутності на карті',
          subtitle: 'Коло навколо дому — що ти реально почуєш',
          value: s.showAudibility,
          onChanged: context.read<SettingsController>().setShowAudibility,
        ),
        if (s.showAudibility)
          _SettingSlider(
            icon: Icons.graphic_eq_rounded,
            title: 'Радіус чутності',
            value: s.audibilityKm,
            min: 3,
            max: 60,
            divisions: 57,
            valueLabel: '${s.audibilityKm.round()} км',
            onChanged: (v) =>
                context.read<SettingsController>().setAudibilityKm(v),
          ),
      ],
    );
  }
}

class _SnoozeChoice extends StatelessWidget {
  const _SnoozeChoice();
  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();
    const opts = [(30, '30 с'), (90, '1½ хв'), (300, '5 хв')];
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.10),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(Icons.snooze_rounded,
                color: AppColors.primary, size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Text('Пауза звуку',
                style: TextStyle(
                    fontSize: 14.5,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textPrimary)),
          ),
          Wrap(
            spacing: 6,
            children: [
              for (final o in opts)
                ChoiceChip(
                  label: Text(o.$2),
                  selected: s.snoozeSeconds == o.$1,
                  onSelected: (_) =>
                      context.read<SettingsController>().setSnoozeSeconds(o.$1),
                  selectedColor: AppColors.primary,
                  labelStyle: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w700,
                    color: s.snoozeSeconds == o.$1
                        ? Colors.white
                        : AppColors.textSecondary,
                  ),
                  backgroundColor: AppColors.surface,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(20),
                    side: BorderSide(color: AppColors.border),
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Turns the Android foreground watcher on/off. On enable it requests the
/// notification permission and warns about battery optimisation.
class _BackgroundAlarmToggle extends StatefulWidget {
  const _BackgroundAlarmToggle();
  @override
  State<_BackgroundAlarmToggle> createState() => _BackgroundAlarmToggleState();
}

class _BackgroundAlarmToggleState extends State<_BackgroundAlarmToggle> {
  bool _busy = false;

  Future<void> _toggle(bool v) async {
    if (_busy) return;
    setState(() => _busy = true);
    final settings = context.read<SettingsController>();
    final messenger = ScaffoldMessenger.of(context);
    if (v) {
      final ok = await BackgroundAlarm.enable();
      settings.setBackgroundAlarm(true);
      messenger.showSnackBar(SnackBar(
        content: Text(ok
            ? 'Увімкнено. Щоб будило вночі — вимкни «оптимізацію батареї» для WarAlarm у налаштуваннях телефону.'
            : 'Дозвіл на сповіщення не надано — увімкни його в налаштуваннях телефону.'),
        duration: const Duration(seconds: 6),
      ));
    } else {
      await BackgroundAlarm.disable();
      settings.setBackgroundAlarm(false);
      messenger.showSnackBar(
          const SnackBar(content: Text('Фонові тривоги вимкнено')));
    }
    if (mounted) setState(() => _busy = false);
  }

  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();
    return Column(
      children: [
        _ToggleRow(
          icon: Icons.shield_moon_rounded,
          title: 'Стежити із закритим застосунком',
          subtitle: 'Розбудить навіть уночі, коли телефон заблоковано',
          value: s.backgroundAlarm,
          onChanged: _busy ? (_) {} : _toggle,
          accent: AppColors.threat,
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
          child: Text(
            'WarAlarm працюватиме у фоні й покаже гучну повноекранну тривогу, '
            'коли загроза підійде до твого місця. Потрібні: дозвіл на сповіщення '
            'і вимкнена «оптимізація батареї». Не тривожить на демо-даних.',
            style: TextStyle(fontSize: 12, color: AppColors.textMuted),
          ),
        ),
      ],
    );
  }
}

class _TestSirenButton extends StatelessWidget {
  const _TestSirenButton();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 6, 16, 10),
      child: SizedBox(
        width: double.infinity,
        child: OutlinedButton.icon(
          onPressed: () {
            context.read<AlarmController>().testSiren();
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(
                content: Text('Тест сирени · 5 секунд'),
                duration: Duration(seconds: 5),
              ),
            );
          },
          icon: Icon(Icons.campaign_rounded, color: AppColors.threat),
          label: const Text('Перевірити сирену',
              style: TextStyle(fontWeight: FontWeight.w700)),
          style: OutlinedButton.styleFrom(
            foregroundColor: AppColors.threatDark,
            side: BorderSide(color: AppColors.threatSoft),
            padding: const EdgeInsets.symmetric(vertical: 14),
          ),
        ),
      ),
    );
  }
}

class _Group extends StatelessWidget {
  const _Group({required this.title, required this.children});
  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return GlassContainer(
      padding: const EdgeInsets.fromLTRB(6, 8, 6, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 8, 14, 6),
            child: Text(
              title.toUpperCase(),
              style: TextStyle(
                fontSize: 11.5,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.8,
                color: AppColors.textMuted,
              ),
            ),
          ),
          ...children,
        ],
      ),
    );
  }
}

class _ToggleRow extends StatelessWidget {
  const _ToggleRow({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.value,
    required this.onChanged,
    this.accent,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final bool value;
  final ValueChanged<bool> onChanged;
  final Color? accent;

  @override
  Widget build(BuildContext context) {
    final accent = this.accent ?? AppColors.primary;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: accent.withValues(alpha: 0.10),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(icon, color: accent, size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title,
                    style: TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w600,
                        color: AppColors.textPrimary)),
                Text(subtitle,
                    style: TextStyle(
                        fontSize: 12.5, color: AppColors.textSecondary)),
              ],
            ),
          ),
          Switch(
            value: value,
            onChanged: onChanged,
            activeTrackColor: accent,
            activeThumbColor: Colors.white,
          ),
        ],
      ),
    );
  }
}

class _NameField extends StatefulWidget {
  const _NameField({required this.value, required this.onChanged});
  final String value;
  final ValueChanged<String> onChanged;

  @override
  State<_NameField> createState() => _NameFieldState();
}

class _NameFieldState extends State<_NameField> {
  late final TextEditingController _ctl =
      TextEditingController(text: widget.value);

  @override
  void dispose() {
    _ctl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.10),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(Icons.person_rounded,
                color: AppColors.primary, size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: TextField(
              controller: _ctl,
              onSubmitted: widget.onChanged,
              onTapOutside: (_) => widget.onChanged(_ctl.text),
              decoration: const InputDecoration(
                labelText: 'Імʼя в чаті',
                hintText: 'Гість',
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _AiKeyField extends StatefulWidget {
  const _AiKeyField({required this.value, required this.onChanged});
  final String value;
  final ValueChanged<String> onChanged;

  @override
  State<_AiKeyField> createState() => _AiKeyFieldState();
}

class _AiKeyFieldState extends State<_AiKeyField> {
  late final TextEditingController _ctl =
      TextEditingController(text: widget.value);
  bool _obscure = true;

  @override
  void dispose() {
    _ctl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
      child: TextField(
        controller: _ctl,
        obscureText: _obscure,
        onChanged: widget.onChanged,
        decoration: InputDecoration(
          labelText: 'API-ключ ІІ',
          hintText: 'sk-ant-…  /  AIza…',
          prefixIcon: const Icon(Icons.key_rounded, size: 20),
          suffixIcon: IconButton(
            icon: Icon(_obscure ? Icons.visibility_rounded : Icons.visibility_off_rounded,
                size: 20),
            onPressed: () => setState(() => _obscure = !_obscure),
          ),
        ),
      ),
    );
  }
}

class _AiModelField extends StatefulWidget {
  const _AiModelField({
    required this.value,
    required this.apiKey,
    required this.onChanged,
  });
  final String value;
  final String apiKey;
  final ValueChanged<String> onChanged;

  @override
  State<_AiModelField> createState() => _AiModelFieldState();
}

class _AiModelFieldState extends State<_AiModelField> {
  late final TextEditingController _ctl =
      TextEditingController(text: widget.value);

  @override
  void dispose() {
    _ctl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final hint = widget.apiKey.trim().isEmpty
        ? 'claude-haiku-4-5 / gemini-2.5-flash'
        : AiService.defaultModel(widget.apiKey);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 4),
      child: TextField(
        controller: _ctl,
        onChanged: widget.onChanged,
        decoration: InputDecoration(
          labelText: 'Модель (необовʼязково)',
          hintText: hint,
          prefixIcon: const Icon(Icons.memory_rounded, size: 20),
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      child: Row(
        children: [
          Text(label,
              style: TextStyle(
                  fontSize: 13.5, color: AppColors.textSecondary)),
          const Spacer(),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.right,
              style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w600,
                  color: AppColors.textPrimary),
            ),
          ),
        ],
      ),
    );
  }
}
