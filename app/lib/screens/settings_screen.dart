import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/settings_controller.dart';
import '../firebase_config.dart';
import '../services/ai_service.dart';
import '../theme/app_colors.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final s = context.watch<SettingsController>();

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionHeader(
            title: 'Settings',
            subtitle: 'App preferences and notifications',
            icon: Icons.settings_rounded,
          ),
          const SizedBox(height: 18),

          _Group(
            title: 'Profile',
            children: [
              _NameField(
                value: s.displayName,
                onChanged: s.setDisplayName,
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'Notifications',
            children: [
              _ToggleRow(
                icon: Icons.notifications_active_rounded,
                title: 'Push notifications',
                subtitle: 'Receive alerts on this device',
                value: s.pushNotifications,
                onChanged: s.setPush,
              ),
              _ToggleRow(
                icon: Icons.volume_up_rounded,
                title: 'Sound alerts',
                subtitle: 'Play a sound on new threats',
                value: s.soundAlerts,
                onChanged: s.setSound,
              ),
              _ToggleRow(
                icon: Icons.vibration_rounded,
                title: 'Vibration',
                subtitle: 'Vibrate on active threats',
                value: s.vibration,
                onChanged: s.setVibration,
              ),
              _ToggleRow(
                icon: Icons.crisis_alert_rounded,
                title: 'Threats only',
                subtitle: 'Hide non-threat events from the feed',
                value: s.threatOnly,
                onChanged: s.setThreatOnly,
                accent: AppColors.threat,
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'ШІ · AI',
            children: [
              _AiKeyField(
                value: s.aiApiKey,
                onChanged: s.setAiApiKey,
              ),
              _AiModelField(
                value: s.aiModel,
                apiKey: s.aiApiKey,
                onChanged: s.setAiModel,
              ),
              const Padding(
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
            title: 'Display',
            children: [
              _ToggleRow(
                icon: Icons.view_agenda_rounded,
                title: 'Compact feed',
                subtitle: 'Denser event list',
                value: s.compactFeed,
                onChanged: s.setCompactFeed,
              ),
              _ToggleRow(
                icon: Icons.screen_lock_portrait_rounded,
                title: 'Keep screen on',
                subtitle: 'Prevent sleep during active alerts',
                value: s.keepScreenOn,
                onChanged: s.setKeepScreenOn,
              ),
            ],
          ),
          const SizedBox(height: 14),

          _Group(
            title: 'About',
            children: const [
              _InfoRow(label: 'Project', value: FirebaseConfig.projectId),
              _InfoRow(label: 'Backend', value: 'Realtime Database (REST + SSE)'),
              _InfoRow(label: 'Version', value: '1.0.0 (MVP)'),
              _InfoRow(
                  label: 'Platforms', value: 'Android · Windows Desktop'),
            ],
          ),
          const SizedBox(height: 20),
        ],
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
              style: const TextStyle(
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
    this.accent = AppColors.primary,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final bool value;
  final ValueChanged<bool> onChanged;
  final Color accent;

  @override
  Widget build(BuildContext context) {
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
                    style: const TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w600,
                        color: AppColors.textPrimary)),
                Text(subtitle,
                    style: const TextStyle(
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
            child: const Icon(Icons.person_rounded,
                color: AppColors.primary, size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: TextField(
              controller: _ctl,
              onSubmitted: widget.onChanged,
              onTapOutside: (_) => widget.onChanged(_ctl.text),
              decoration: const InputDecoration(
                labelText: 'Display name',
                hintText: 'Guest',
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
              style: const TextStyle(
                  fontSize: 13.5, color: AppColors.textSecondary)),
          const Spacer(),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.right,
              style: const TextStyle(
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
