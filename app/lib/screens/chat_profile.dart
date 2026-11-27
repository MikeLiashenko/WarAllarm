import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';
import 'package:share_plus/share_plus.dart';

import '../controllers/chat_controller.dart';
import '../services/chat_media.dart';
import '../theme/app_colors.dart';
import '../widgets/chat_widgets.dart';

bool get chatHasCamera =>
    !kIsWeb && (defaultTargetPlatform == TargetPlatform.android || defaultTargetPlatform == TargetPlatform.iOS);

const _months = [
  'січня', 'лютого', 'березня', 'квітня', 'травня', 'червня',
  'липня', 'серпня', 'вересня', 'жовтня', 'листопада', 'грудня',
];

const chatRules = [
  ('🛡', 'Не пиши, де стоїть ППО, техніка чи військові, і куди саме влучило — це допомагає ворогу і карається законом.'),
  ('🤝', 'Без образ, цькування, погроз і мови ворожнечі.'),
  ('🚫', 'Без реклами, спаму, шахрайства та контенту 18+.'),
  ('🔒', 'Не публікуй чужі телефони, адреси й фото без згоди. Фото очищаються від геоміток автоматично.'),
  ('📢', 'Тривоги й загрози — тільки з офіційних джерел або те, що бачиш сам. Без паніки.'),
];

/// Step 1: the date of birth. Chat is 13+; an under-13 answer is kept.
class ChatAgeGate extends StatefulWidget {
  const ChatAgeGate({super.key});

  @override
  State<ChatAgeGate> createState() => _ChatAgeGateState();
}

class _ChatAgeGateState extends State<ChatAgeGate> {
  int? _d, _m, _y;
  bool _rules = false;

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    final ready = _d != null && _m != null && _y != null && _rules;
    final days = (_m != null && _y != null) ? DateUtils.getDaysInMonth(_y!, _m!) : 31;
    if (_d != null && _d! > days) _d = days;
    return _GateCard(
      icon: Icons.verified_user_rounded,
      title: 'Перевірка віку',
      subtitle: 'Чат доступний з 13 років. Вкажи свою дату народження — вона лишається тільки на цьому пристрої.',
      children: [
        Row(children: [
          Expanded(
            flex: 3,
            child: DropdownButtonFormField<int>(
              initialValue: _d,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'День'),
              items: [for (var i = 1; i <= days; i++) DropdownMenuItem(value: i, child: Text('$i'))],
              onChanged: (v) => setState(() => _d = v),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            flex: 5,
            child: DropdownButtonFormField<int>(
              initialValue: _m,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Місяць'),
              items: [for (var i = 1; i <= 12; i++) DropdownMenuItem(value: i, child: Text(_months[i - 1]))],
              onChanged: (v) => setState(() => _m = v),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            flex: 4,
            child: DropdownButtonFormField<int>(
              initialValue: _y,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Рік'),
              items: [for (var y = now.year; y >= now.year - 100; y--) DropdownMenuItem(value: y, child: Text('$y'))],
              onChanged: (v) => setState(() => _y = v),
            ),
          ),
        ]),
        const SizedBox(height: 18),
        Text('Правила чату', style: TextStyle(fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
        const SizedBox(height: 8),
        for (final r in chatRules)
          Padding(
            padding: const EdgeInsets.only(bottom: 6),
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(r.$1, style: const TextStyle(fontSize: 15)),
              const SizedBox(width: 8),
              Expanded(child: Text(r.$2, style: TextStyle(height: 1.35, color: AppColors.textSecondary))),
            ]),
          ),
        CheckboxListTile(
          value: _rules,
          contentPadding: EdgeInsets.zero,
          controlAffinity: ListTileControlAffinity.leading,
          onChanged: (v) => setState(() => _rules = v ?? false),
          title: Text('Мені є 13 років, я прочитав(-ла) правила й дотримуватимусь їх',
              style: TextStyle(fontSize: 13.5, color: AppColors.textPrimary)),
        ),
        const SizedBox(height: 8),
        FilledButton(
          onPressed: !ready
              ? null
              : () async {
                  final dob = DateTime(_y!, _m!, _d!);
                  final ok = await showDialog<bool>(
                    context: context,
                    builder: (c) => AlertDialog(
                      title: const Text('Підтвердити дату?'),
                      content: Text('${_d!} ${_months[_m! - 1]} ${_y!}\n\nЗмінити її потім не вийде.'),
                      actions: [
                        TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Назад')),
                        FilledButton(onPressed: () => Navigator.pop(c, true), child: const Text('Так, вірно')),
                      ],
                    ),
                  );
                  if (ok == true && context.mounted) await context.read<ChatController>().setBirthDate(dob);
                },
          child: const Text('Продовжити'),
        ),
      ],
    );
  }
}

class ChatTooYoung extends StatefulWidget {
  const ChatTooYoung({super.key});

  @override
  State<ChatTooYoung> createState() => _ChatTooYoungState();
}

class _ChatTooYoungState extends State<ChatTooYoung> {
  @override
  void initState() {
    super.initState();
    // The chat opens by itself as soon as a parent allows this phone.
    context.read<ChatController>().watchParentalPermission();
  }

  @override
  Widget build(BuildContext context) {
    final c = context.read<ChatController>();
    final dob = c.dob!;
    final open = DateTime(dob.year + 13, dob.month, dob.day);
    return _GateCard(
      icon: Icons.lock_clock_rounded,
      title: 'Чат — з 13 років',
      subtitle: 'Він відкриється ${open.day} ${_months[open.month - 1]} ${open.year}. '
          'Усе інше в застосунку — тривоги, карта, укриття — працює як завжди. Бережи себе 💙',
      children: [
        Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: AppColors.surface,
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: AppColors.border),
          ),
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Row(children: [
              const Text('👪', style: TextStyle(fontSize: 18)),
              const SizedBox(width: 8),
              Text('Дозвіл батьків',
                  style: TextStyle(fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
            ]),
            const SizedBox(height: 6),
            Text(
              'Батьки можуть відкрити чат раніше. Надішли їм цей код — '
              'вони дозволять саме цей телефон, і чат відкриється сам.',
              style: TextStyle(height: 1.35, color: AppColors.textSecondary),
            ),
            const SizedBox(height: 10),
            SelectableText(c.uid,
                textAlign: TextAlign.center,
                style: TextStyle(
                    fontFamily: 'monospace', fontSize: 15, fontWeight: FontWeight.w700, color: AppColors.textPrimary)),
            const SizedBox(height: 8),
            Wrap(alignment: WrapAlignment.center, spacing: 8, children: [
              OutlinedButton.icon(
                icon: const Icon(Icons.copy_rounded, size: 18),
                label: const Text('Копіювати'),
                onPressed: () {
                  Clipboard.setData(ClipboardData(text: c.uid));
                  ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Код скопійовано')));
                },
              ),
              FilledButton.icon(
                icon: const Icon(Icons.share_rounded, size: 18),
                label: const Text('Надіслати батькам'),
                onPressed: () => Share.share('Код мого телефона для чату WarAlarm: ${c.uid}'),
              ),
            ]),
          ]),
        ),
      ],
    );
  }
}

class _GateCard extends StatelessWidget {
  const _GateCard({required this.icon, required this.title, required this.subtitle, required this.children});
  final IconData icon;
  final String title, subtitle;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisSize: MainAxisSize.min,
            children: [
              Center(
                child: Container(
                  width: 72,
                  height: 72,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: AppColors.primary.withValues(alpha: 0.12),
                    border: Border.all(color: AppColors.primary.withValues(alpha: 0.3)),
                  ),
                  child: Icon(icon, size: 34, color: AppColors.primary),
                ),
              ),
              const SizedBox(height: 14),
              Text(title,
                  textAlign: TextAlign.center,
                  style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
              const SizedBox(height: 6),
              Text(subtitle,
                  textAlign: TextAlign.center, style: TextStyle(height: 1.4, color: AppColors.textSecondary)),
              const SizedBox(height: 20),
              ...children,
            ],
          ),
        ),
      ),
    );
  }
}

/// Step 2 (and later from the chat header): name, avatar, about, city.
class ChatProfileEditor extends StatefulWidget {
  const ChatProfileEditor({super.key, this.onDone});
  final VoidCallback? onDone;

  @override
  State<ChatProfileEditor> createState() => _ChatProfileEditorState();
}

class _ChatProfileEditorState extends State<ChatProfileEditor> {
  late final TextEditingController _name, _bio, _city;
  Uint8List? _newAva;
  bool _removeAva = false;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    final c = context.read<ChatController>();
    _name = TextEditingController(text: c.myName.isNotEmpty ? c.myName : c.suggestedName);
    _bio = TextEditingController(text: c.myBio);
    _city = TextEditingController(text: c.myCity);
  }

  @override
  void dispose() {
    _name.dispose();
    _bio.dispose();
    _city.dispose();
    super.dispose();
  }

  Future<void> _pick(ImageSource src) async {
    try {
      final x = await ImagePicker().pickImage(source: src, maxWidth: 1024, maxHeight: 1024, imageQuality: 90);
      if (x == null) return;
      setState(() => _busy = true);
      final p = await ChatMedia.avatar(await x.readAsBytes());
      if (!mounted) return;
      setState(() {
        _busy = false;
        if (p != null) {
          _newAva = p.bytes;
          _removeAva = false;
        }
      });
      if (p == null) _snack('Це не схоже на фото');
    } catch (_) {
      if (mounted) setState(() => _busy = false);
      _snack('Не вдалося відкрити фото');
    }
  }

  void _snack(String t) {
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(t)));
  }

  Future<void> _avatarMenu(bool hasAva) async {
    final v = await showModalBottomSheet<String>(
      context: context,
      builder: (c) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          ListTile(
              leading: const Icon(Icons.photo_library_rounded),
              title: const Text('Вибрати з галереї'),
              onTap: () => Navigator.pop(c, 'g')),
          if (chatHasCamera)
            ListTile(
                leading: const Icon(Icons.photo_camera_rounded),
                title: const Text('Зробити фото'),
                onTap: () => Navigator.pop(c, 'c')),
          if (hasAva)
            ListTile(
                leading: Icon(Icons.delete_outline_rounded, color: AppColors.threat),
                title: Text('Прибрати фото', style: TextStyle(color: AppColors.threat)),
                onTap: () => Navigator.pop(c, 'd')),
        ]),
      ),
    );
    if (v == 'g') await _pick(ImageSource.gallery);
    if (v == 'c') await _pick(ImageSource.camera);
    if (v == 'd') {
      setState(() {
        _newAva = null;
        _removeAva = true;
      });
    }
  }

  Future<void> _save() async {
    final name = _name.text.trim();
    if (name.length < 2) return _snack('Ім\'я — хоча б 2 символи');
    final c = context.read<ChatController>();
    final ok = await c.saveProfile(
        name: name, bio: _bio.text, city: _city.text, avatar: _newAva, removeAvatar: _removeAva);
    if (!mounted) return;
    if (ok) {
      widget.onDone?.call();
    } else {
      _snack(c.profileError ?? 'Не вдалося зберегти');
    }
  }

  @override
  Widget build(BuildContext context) {
    final c = context.watch<ChatController>();
    final current = _removeAva ? null : (_newAva ?? c.myAvatar);
    return _GateCard(
      icon: Icons.person_rounded,
      title: c.myName.isEmpty ? 'Твій профіль' : 'Редагувати профіль',
      subtitle: 'Так тебе бачитимуть у чаті. Справжнє ім\'я не обов\'язкове.',
      children: [
        Center(
          child: GestureDetector(
            onTap: _busy ? null : () => _avatarMenu(current != null),
            child: Stack(children: [
              current != null
                  ? ClipOval(child: Image.memory(current, width: 104, height: 104, fit: BoxFit.cover))
                  : ChatAvatar(uid: c.uid, name: _name.text, size: 104),
              Positioned(
                right: 0,
                bottom: 0,
                child: Container(
                  padding: const EdgeInsets.all(7),
                  decoration: BoxDecoration(color: AppColors.primary, shape: BoxShape.circle),
                  child: _busy
                      ? const SizedBox(
                          width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                      : const Icon(Icons.photo_camera_rounded, size: 18, color: Colors.white),
                ),
              ),
            ]),
          ),
        ),
        const SizedBox(height: 18),
        TextField(
          controller: _name,
          maxLength: 32,
          inputFormatters: [FilteringTextInputFormatter.deny(RegExp(r'[\n\r\t]'))],
          decoration: const InputDecoration(labelText: 'Ім\'я або нікнейм'),
          onChanged: (_) => setState(() {}),
        ),
        const SizedBox(height: 6),
        TextField(
          controller: _city,
          maxLength: 40,
          decoration: const InputDecoration(labelText: 'Місто (необов\'язково)'),
        ),
        const SizedBox(height: 6),
        TextField(
          controller: _bio,
          maxLength: 300,
          minLines: 2,
          maxLines: 4,
          decoration: const InputDecoration(labelText: 'Про себе (необов\'язково)'),
        ),
        if (c.profileError != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(c.profileError!, style: TextStyle(color: AppColors.threat)),
          ),
        const SizedBox(height: 6),
        FilledButton(
          onPressed: c.savingProfile || _busy ? null : _save,
          child: c.savingProfile
              ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Зберегти'),
        ),
        if (c.isAdmin) ...[
          const SizedBox(height: 18),
          const _ChildPermission(),
        ],
        if (c.myName.isNotEmpty) ...[
          const SizedBox(height: 18),
          Center(
            child: TextButton.icon(
              icon: const Icon(Icons.badge_outlined, size: 18),
              label: Text('Мій ID: ${c.uid.substring(0, 8)}… (копіювати)'),
              onPressed: () {
                Clipboard.setData(ClipboardData(text: c.uid));
                _snack('ID скопійовано');
              },
            ),
          ),
        ],
      ],
    );
  }
}

/// Someone's profile (bottom sheet): photo, about, city, since; block / ban.
Future<void> showChatProfile(BuildContext context, String uid, String fallbackName) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    builder: (sheet) {
      return Consumer<ChatController>(builder: (context, c, _) {
        final p = c.profile(uid);
        final name = p?.name ?? fallbackName;
        final blocked = c.isBlocked(uid);
        final mine = uid == c.uid;
        final count = c.messages.where((m) => m.uid == uid).length;
        return SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 20, 20, 12),
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              ChatAvatar(uid: uid, name: name, size: 96),
              const SizedBox(height: 12),
              Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                Flexible(
                  child: Text(name,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
                ),
                if (p?.adm == true) ...[
                  const SizedBox(width: 6),
                  Icon(Icons.verified_rounded, size: 20, color: AppColors.primary),
                ],
              ]),
              if (p != null && p.city.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text('📍 ${p.city}', style: TextStyle(color: AppColors.textSecondary)),
                ),
              if (p != null && p.bio.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 10),
                  child: Text(p.bio, textAlign: TextAlign.center, style: TextStyle(height: 1.4, color: AppColors.textPrimary)),
                ),
              const SizedBox(height: 10),
              Text(
                [
                  if (p?.since != null) 'У чаті з ${p!.since!.day} ${_months[p.since!.month - 1]} ${p.since!.year}',
                  'повідомлень: $count',
                ].join(' · '),
                style: TextStyle(fontSize: 12, color: AppColors.textMuted),
              ),
              const SizedBox(height: 14),
              if (!mine)
                Wrap(spacing: 8, runSpacing: 8, alignment: WrapAlignment.center, children: [
                  OutlinedButton.icon(
                    icon: Icon(blocked ? Icons.lock_open_rounded : Icons.block_rounded, size: 18),
                    label: Text(blocked ? 'Розблокувати' : 'Заблокувати'),
                    onPressed: () => c.setBlocked(uid, !blocked),
                  ),
                  if (c.isAdmin && p?.adm != true)
                    FilledButton.icon(
                      style: FilledButton.styleFrom(backgroundColor: AppColors.threat),
                      icon: const Icon(Icons.gavel_rounded, size: 18),
                      label: const Text('Бан'),
                      onPressed: () async {
                        final ok = await showDialog<bool>(
                          context: context,
                          builder: (d) => AlertDialog(
                            title: Text('Забанити $name?'),
                            content: const Text('Усі повідомлення користувача буде видалено, писати він більше не зможе.'),
                            actions: [
                              TextButton(onPressed: () => Navigator.pop(d, false), child: const Text('Скасувати')),
                              FilledButton(onPressed: () => Navigator.pop(d, true), child: const Text('Забанити')),
                            ],
                          ),
                        );
                        if (ok == true) {
                          await c.ban(uid);
                          if (sheet.mounted) Navigator.pop(sheet);
                        }
                      },
                    ),
                ]),
              if (blocked)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text('Ти не бачиш повідомлень цього користувача',
                      style: TextStyle(fontSize: 12, color: AppColors.textMuted)),
                ),
            ]),
          ),
        );
      });
    },
  );
}

/// Admin: open the chat for a child's phone (under 13) by the code shown on that phone.
class _ChildPermission extends StatefulWidget {
  const _ChildPermission();

  @override
  State<_ChildPermission> createState() => _ChildPermissionState();
}

class _ChildPermissionState extends State<_ChildPermission> {
  final _id = TextEditingController();

  @override
  void dispose() {
    _id.dispose();
    super.dispose();
  }

  Future<void> _go(bool on) async {
    final id = _id.text.trim().toLowerCase();
    final messenger = ScaffoldMessenger.of(context);
    if (!RegExp(r'^[0-9a-f]{16,40}$').hasMatch(id)) {
      messenger.showSnackBar(const SnackBar(content: Text('Встав код із телефону дитини (екран «Чат — з 13 років»)')));
      return;
    }
    await context.read<ChatController>().allowChild(id, on: on);
    _id.clear();
    messenger.showSnackBar(SnackBar(content: Text(on ? 'Готово: на тому телефоні чат відкриється сам' : 'Дозвіл знято')));
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text('👪 Дозвіл дитині (до 13 років)',
            style: TextStyle(fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
        const SizedBox(height: 6),
        Text('Встав код, який показує телефон дитини на екрані «Чат — з 13 років».',
            style: TextStyle(height: 1.35, color: AppColors.textSecondary)),
        const SizedBox(height: 8),
        TextField(controller: _id, decoration: const InputDecoration(labelText: 'Код телефону дитини')),
        const SizedBox(height: 8),
        Row(children: [
          Expanded(child: FilledButton(onPressed: () => _go(true), child: const Text('Дозволити'))),
          const SizedBox(width: 8),
          TextButton(onPressed: () => _go(false), child: const Text('Зняти')),
        ]),
      ]),
    );
  }
}
