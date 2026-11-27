import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';
import 'package:provider/provider.dart';

import '../controllers/chat_controller.dart';
import '../models/chat_message.dart';
import '../theme/app_colors.dart';
import '../utils/formatting.dart';
import '../widgets/chat_widgets.dart';
import '../widgets/section_header.dart';
import 'chat_profile.dart';
import 'home_shell.dart';

class ChatScreen extends StatelessWidget {
  const ChatScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final gate = context.select<ChatController, ChatGate>((c) => c.gate);
    return PageBody(
      scrollable: false,
      child: switch (gate) {
        ChatGate.needAge => const ChatAgeGate(),
        ChatGate.tooYoung => const ChatTooYoung(),
        ChatGate.needProfile => const ChatProfileEditor(),
        ChatGate.ready => const _ChatView(),
      },
    );
  }
}

class _ChatView extends StatefulWidget {
  const _ChatView();

  @override
  State<_ChatView> createState() => _ChatViewState();
}

class _ChatViewState extends State<_ChatView> {
  final _input = TextEditingController();
  final _focus = FocusNode();
  final _scroll = ScrollController();
  ChatMessage? _reply;
  ChatMessage? _editing;

  VoiceRecorder? _rec;
  Timer? _recTick;

  @override
  void initState() {
    super.initState();
    context.read<ChatController>().start();
    _input.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _recTick?.cancel();
    _rec?.cancel();
    _rec?.dispose();
    _input.dispose();
    _focus.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _snack(String t) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(t)));

  void _toBottom() {
    if (_scroll.hasClients) {
      _scroll.animateTo(0, duration: const Duration(milliseconds: 250), curve: Curves.easeOut);
    }
  }

  Future<void> _send() async {
    final c = context.read<ChatController>();
    final text = _input.text.trim();
    if (text.isEmpty) return;
    if (_editing != null) {
      final m = _editing!;
      setState(() => _editing = null);
      _input.clear();
      if (text != m.text) await c.edit(m, text);
      return;
    }
    final reply = _reply;
    setState(() => _reply = null);
    _input.clear();
    _toBottom();
    await c.sendText(text, replyTo: reply);
  }

  Future<void> _attach() async {
    final src = chatHasCamera
        ? await showModalBottomSheet<ImageSource>(
            context: context,
            builder: (c) => SafeArea(
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                ListTile(
                    leading: const Icon(Icons.photo_library_rounded),
                    title: const Text('Фото з галереї'),
                    onTap: () => Navigator.pop(c, ImageSource.gallery)),
                ListTile(
                    leading: const Icon(Icons.photo_camera_rounded),
                    title: const Text('Камера'),
                    onTap: () => Navigator.pop(c, ImageSource.camera)),
              ]),
            ),
          )
        : ImageSource.gallery;
    if (src == null || !mounted) return;
    XFile? x;
    try {
      x = await ImagePicker().pickImage(source: src, maxWidth: 2048, maxHeight: 2048, imageQuality: 92);
    } catch (_) {
      _snack('Не вдалося відкрити фото');
      return;
    }
    if (x == null || !mounted) return;
    final bytes = await x.readAsBytes();
    if (!mounted) return;
    final caption = TextEditingController(text: _input.text);
    final send = await showDialog<bool>(
      context: context,
      builder: (d) => AlertDialog(
        contentPadding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
        content: SizedBox(
          width: 420,
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 340),
              child: ClipRRect(borderRadius: BorderRadius.circular(12), child: Image.memory(bytes, fit: BoxFit.contain)),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: caption,
              maxLength: 1000,
              minLines: 1,
              maxLines: 3,
              decoration: const InputDecoration(hintText: 'Підпис…'),
            ),
          ]),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(d, false), child: const Text('Скасувати')),
          FilledButton.icon(
            onPressed: () => Navigator.pop(d, true),
            icon: const Icon(Icons.send_rounded, size: 18),
            label: const Text('Надіслати'),
          ),
        ],
      ),
    );
    final text = caption.text;
    caption.dispose();
    if (send != true || !mounted) return;
    final reply = _reply;
    setState(() => _reply = null);
    _input.clear();
    _toBottom();
    final ok = await context.read<ChatController>().sendImage(bytes, caption: text, replyTo: reply);
    if (!ok) _snack('Це не схоже на фото');
  }

  // ── Voice ──────────────────────────────────────────────────────────────────
  Future<void> _recStart() async {
    final r = VoiceRecorder();
    final err = await r.start();
    if (!mounted) {
      await r.cancel();
      r.dispose();
      return;
    }
    if (err != null) {
      r.dispose();
      _snack(err);
      return;
    }
    HapticFeedback.mediumImpact();
    setState(() => _rec = r);
    _recTick = Timer.periodic(const Duration(milliseconds: 200), (_) {
      if (!mounted) return;
      if (r.elapsed >= VoiceRecorder.maxLength) {
        _recSend();
      } else {
        setState(() {});
      }
    });
  }

  Future<void> _recCancel() async {
    final r = _rec;
    _recTick?.cancel();
    setState(() => _rec = null);
    if (r != null) {
      await r.cancel();
      r.dispose();
    }
  }

  Future<void> _recSend() async {
    final r = _rec;
    if (r == null) return;
    _recTick?.cancel();
    setState(() => _rec = null);
    final res = await r.finish();
    r.dispose();
    if (!mounted) return;
    if (res == null) {
      _snack('Занадто коротко — тримай запис хоча б секунду');
      return;
    }
    final reply = _reply;
    setState(() => _reply = null);
    _toBottom();
    await context.read<ChatController>().sendVoice(res.$1, res.$2, res.$3, replyTo: reply);
  }

  // ── Message menu ───────────────────────────────────────────────────────────
  Future<void> _menu(ChatMessage m) async {
    final c = context.read<ChatController>();
    if (m.pending || m.error != null || m.id.startsWith('local-')) return;
    final mine = m.uid == c.uid;
    final v = await showModalBottomSheet<String>(
      context: context,
      builder: (s) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 14, 12, 6),
            child: Wrap(
              alignment: WrapAlignment.center,
              spacing: 4,
              children: [
                for (final e in chatReactions.entries)
                  InkResponse(
                    onTap: () => Navigator.pop(s, 'rx:${e.key}'),
                    radius: 26,
                    child: Container(
                      padding: const EdgeInsets.all(8),
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: (m.rx[e.key]?.contains(c.uid) ?? false)
                            ? AppColors.primary.withValues(alpha: 0.2)
                            : Colors.transparent,
                      ),
                      child: Text(e.value, style: const TextStyle(fontSize: 26)),
                    ),
                  ),
              ],
            ),
          ),
          const Divider(height: 1),
          ListTile(
              leading: const Icon(Icons.reply_rounded), title: const Text('Відповісти'), onTap: () => Navigator.pop(s, 'reply')),
          if (m.text.isNotEmpty)
            ListTile(
                leading: const Icon(Icons.copy_rounded), title: const Text('Копіювати текст'), onTap: () => Navigator.pop(s, 'copy')),
          if (mine && !m.isVoice)
            ListTile(
                leading: const Icon(Icons.edit_rounded), title: const Text('Редагувати'), onTap: () => Navigator.pop(s, 'edit')),
          if (mine || c.isAdmin)
            ListTile(
                leading: Icon(Icons.delete_outline_rounded, color: AppColors.threat),
                title: Text(mine ? 'Видалити' : 'Видалити (модерація)', style: TextStyle(color: AppColors.threat)),
                onTap: () => Navigator.pop(s, 'del')),
          if (!mine) ...[
            ListTile(
                leading: const Icon(Icons.person_outline_rounded),
                title: Text('Профіль: ${m.name}'),
                onTap: () => Navigator.pop(s, 'profile')),
            ListTile(
                leading: const Icon(Icons.flag_outlined), title: const Text('Поскаржитися'), onTap: () => Navigator.pop(s, 'report')),
          ],
        ]),
      ),
    );
    if (v == null || !mounted) return;
    if (v.startsWith('rx:')) {
      await c.react(m, v.substring(3));
    } else if (v == 'reply') {
      setState(() {
        _editing = null;
        _reply = m;
      });
      _focus.requestFocus();
    } else if (v == 'copy') {
      await Clipboard.setData(ClipboardData(text: m.text));
      _snack('Скопійовано');
    } else if (v == 'edit') {
      setState(() {
        _reply = null;
        _editing = m;
        _input.text = m.text;
      });
      _focus.requestFocus();
    } else if (v == 'del') {
      final ok = await showDialog<bool>(
        context: context,
        builder: (d) => AlertDialog(
          title: const Text('Видалити повідомлення?'),
          content: const Text('Його буде видалено для всіх.'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(d, false), child: const Text('Скасувати')),
            FilledButton(onPressed: () => Navigator.pop(d, true), child: const Text('Видалити')),
          ],
        ),
      );
      if (ok == true) await c.delete(m);
    } else if (v == 'profile') {
      if (mounted) await showChatProfile(context, m.uid, m.name);
    } else if (v == 'report') {
      await _report(m);
    }
  }

  Future<void> _report(ChatMessage m) async {
    final c = context.read<ChatController>();
    const reasons = ['Спам або реклама', 'Образи чи погрози', 'Розкриває позиції / наслідки ударів', 'Фейк або паніка', '18+ або жорстокість', 'Інше'];
    final why = await showModalBottomSheet<String>(
      context: context,
      builder: (s) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          const ListTile(title: Text('Що не так?', style: TextStyle(fontWeight: FontWeight.w800))),
          for (final r in reasons) ListTile(title: Text(r), onTap: () => Navigator.pop(s, r)),
        ]),
      ),
    );
    if (why == null || !mounted) return;
    await c.report(m, why);
    if (!mounted) return;
    final block = await showDialog<bool>(
      context: context,
      builder: (d) => AlertDialog(
        title: const Text('Дякуємо, скаргу надіслано'),
        content: Text('Заблокувати ${m.name}, щоб більше не бачити їхніх повідомлень?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(d, false), child: const Text('Ні')),
          FilledButton(onPressed: () => Navigator.pop(d, true), child: const Text('Заблокувати')),
        ],
      ),
    );
    if (block == true) await c.setBlocked(m.uid, true);
  }

  void _openMyProfile() {
    Navigator.of(context).push(MaterialPageRoute<void>(
      builder: (r) => Scaffold(
        appBar: AppBar(title: const Text('Профіль')),
        body: ChatProfileEditor(onDone: () => Navigator.of(r).pop()),
      ),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final c = context.watch<ChatController>();
    final notice = c.takeNotice();
    if (notice != null) WidgetsBinding.instance.addPostFrameCallback((_) => mounted ? _snack(notice) : null);
    final messages = c.messages;
    c.markSeen();
    final people = messages.map((m) => m.uid).toSet().length;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          title: 'Чат',
          subtitle: c.connected
              ? (people > 0 ? 'Спільнота WarAlarm · у розмові $people' : 'Спільнота WarAlarm')
              : 'Підключення…',
          icon: Icons.forum_rounded,
          iconColor: AppColors.accent,
          trailing: Tooltip(
            message: 'Мій профіль',
            child: InkResponse(
              onTap: _openMyProfile,
              radius: 26,
              child: ChatAvatar(uid: c.uid, name: c.myName, size: 40),
            ),
          ),
        ),
        const SizedBox(height: 14),
        Expanded(
          child: Container(
            clipBehavior: Clip.antiAlias,
            decoration: BoxDecoration(
              color: AppColors.surface,
              borderRadius: BorderRadius.circular(22),
              border: Border.all(color: AppColors.border),
            ),
            child: messages.isEmpty ? _empty(c.connected) : _list(c, messages),
          ),
        ),
        const SizedBox(height: 10),
        if (_reply != null || _editing != null) _replyBar(),
        _rec != null ? _recordingBar() : _composer(),
      ],
    );
  }

  Widget _empty(bool connected) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(Icons.forum_rounded, size: 40, color: AppColors.primary),
            const SizedBox(height: 12),
            Text(connected ? 'Поки що тихо' : 'Завантаження…',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
            const SizedBox(height: 6),
            Text('Напиши першим — що чути й видно у твоєму районі.\nБез паніки та без позицій ЗСУ 🇺🇦',
                textAlign: TextAlign.center, style: TextStyle(height: 1.4, color: AppColors.textSecondary)),
          ]),
        ),
      );

  Widget _list(ChatController c, List<ChatMessage> messages) {
    final n = messages.length;
    return ListView.builder(
      controller: _scroll,
      reverse: true,
      padding: const EdgeInsets.fromLTRB(10, 12, 10, 12),
      itemCount: n,
      itemBuilder: (context, ri) {
        final i = n - 1 - ri;
        final m = messages[i];
        final prev = i > 0 ? messages[i - 1] : null;
        final next = i < n - 1 ? messages[i + 1] : null;
        final newDay = prev == null || !_sameDay(prev.timestamp, m.timestamp);
        final first = newDay || prev.uid != m.uid || m.timestamp.difference(prev.timestamp).inMinutes > 5;
        final last = next == null || next.uid != m.uid || !_sameDay(next.timestamp, m.timestamp) ||
            next.timestamp.difference(m.timestamp).inMinutes > 5;
        return Column(children: [
          if (newDay) _DayChip(m.timestamp),
          _Bubble(
            message: m,
            mine: m.uid == c.uid,
            first: first,
            last: last,
            onMenu: () => _menu(m),
            onReply: () {
              setState(() {
                _editing = null;
                _reply = m;
              });
              _focus.requestFocus();
            },
          ),
        ]);
      },
    );
  }

  static bool _sameDay(DateTime a, DateTime b) => a.year == b.year && a.month == b.month && a.day == b.day;

  Widget _replyBar() {
    final edit = _editing != null;
    final m = edit ? _editing! : _reply!;
    return Container(
      margin: const EdgeInsets.only(bottom: 6),
      padding: const EdgeInsets.fromLTRB(12, 6, 4, 6),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(14),
        border: Border(left: BorderSide(color: AppColors.primary, width: 3)),
      ),
      child: Row(children: [
        Icon(edit ? Icons.edit_rounded : Icons.reply_rounded, size: 18, color: AppColors.primary),
        const SizedBox(width: 8),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(edit ? 'Редагування' : 'Відповідь ${m.name}',
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: AppColors.primary)),
            Text(m.preview, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12.5, color: AppColors.textSecondary)),
          ]),
        ),
        IconButton(
          icon: const Icon(Icons.close_rounded, size: 18),
          onPressed: () => setState(() {
            if (edit) _input.clear();
            _reply = null;
            _editing = null;
          }),
        ),
      ]),
    );
  }

  Widget _composer() {
    final hasText = _input.text.trim().isNotEmpty;
    return Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
      if (_editing == null)
        IconButton(
          tooltip: 'Фото',
          onPressed: _attach,
          icon: Icon(Icons.add_photo_alternate_rounded, color: AppColors.textSecondary),
        ),
      Expanded(
        child: CallbackShortcuts(
          bindings: {
            const SingleActivator(LogicalKeyboardKey.enter): _send,
            const SingleActivator(LogicalKeyboardKey.numpadEnter): _send,
          },
          child: TextField(
            controller: _input,
            focusNode: _focus,
            minLines: 1,
            maxLines: 5,
            maxLength: 2000,
            buildCounter: (_, {required currentLength, required isFocused, maxLength}) => null,
            textCapitalization: TextCapitalization.sentences,
            keyboardType: TextInputType.multiline,
            decoration: const InputDecoration(hintText: 'Повідомлення…'),
          ),
        ),
      ),
      const SizedBox(width: 8),
      SizedBox(
        width: 50,
        height: 50,
        child: ElevatedButton(
          onPressed: hasText || _editing != null ? _send : _recStart,
          style: ElevatedButton.styleFrom(padding: EdgeInsets.zero, shape: const CircleBorder()),
          child: Icon(
            _editing != null ? Icons.check_rounded : (hasText ? Icons.send_rounded : Icons.mic_rounded),
            size: 22,
          ),
        ),
      ),
    ]);
  }

  Widget _recordingBar() {
    final r = _rec!;
    final secs = r.elapsed.inMilliseconds / 1000;
    final lv = r.levels;
    final recent = lv.length > 30 ? lv.sublist(lv.length - 30) : lv;
    return Container(
      height: 54,
      padding: const EdgeInsets.symmetric(horizontal: 6),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(27),
        border: Border.all(color: AppColors.threat.withValues(alpha: 0.5)),
      ),
      child: Row(children: [
        IconButton(
          tooltip: 'Скасувати',
          onPressed: _recCancel,
          icon: Icon(Icons.delete_outline_rounded, color: AppColors.threat),
        ),
        Container(width: 10, height: 10, decoration: BoxDecoration(color: AppColors.threat, shape: BoxShape.circle)),
        const SizedBox(width: 8),
        Text(fmtDur(secs), style: TextStyle(fontWeight: FontWeight.w700, color: AppColors.textPrimary, fontFeatures: const [FontFeature.tabularFigures()])),
        const SizedBox(width: 12),
        Expanded(
          child: Row(mainAxisAlignment: MainAxisAlignment.end, children: [
            for (final v in recent)
              Container(
                width: 3,
                height: 4 + 28 * (v / 100),
                margin: const EdgeInsets.symmetric(horizontal: 1),
                decoration: BoxDecoration(color: AppColors.primary, borderRadius: BorderRadius.circular(2)),
              ),
          ]),
        ),
        const SizedBox(width: 8),
        SizedBox(
          width: 44,
          height: 44,
          child: ElevatedButton(
            onPressed: _recSend,
            style: ElevatedButton.styleFrom(padding: EdgeInsets.zero, shape: const CircleBorder()),
            child: const Icon(Icons.send_rounded, size: 20),
          ),
        ),
      ]),
    );
  }
}

class _DayChip extends StatelessWidget {
  const _DayChip(this.day);
  final DateTime day;

  static const _m = ['січня', 'лютого', 'березня', 'квітня', 'травня', 'червня', 'липня', 'серпня', 'вересня', 'жовтня', 'листопада', 'грудня'];

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    final d = DateTime(day.year, day.month, day.day);
    final diff = DateTime(now.year, now.month, now.day).difference(d).inDays;
    final label = diff == 0
        ? 'Сьогодні'
        : diff == 1
            ? 'Вчора'
            : '${day.day} ${_m[day.month - 1]}${day.year != now.year ? ' ${day.year}' : ''}';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Center(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
          decoration: BoxDecoration(color: AppColors.surface2, borderRadius: BorderRadius.circular(12)),
          child: Text(label, style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: AppColors.textSecondary)),
        ),
      ),
    );
  }
}

class _Bubble extends StatelessWidget {
  const _Bubble({
    required this.message,
    required this.mine,
    required this.first,
    required this.last,
    required this.onMenu,
    required this.onReply,
  });

  final ChatMessage message;
  final bool mine, first, last;
  final VoidCallback onMenu, onReply;

  @override
  Widget build(BuildContext context) {
    final c = context.read<ChatController>();
    final m = message;
    final prof = c.profile(m.uid);
    final name = prof?.name ?? m.name;
    final adm = prof?.adm ?? m.adm;
    final fg = mine ? Colors.white : AppColors.textPrimary;
    final sub = mine ? Colors.white70 : AppColors.textMuted;
    const r = Radius.circular(18);
    const rs = Radius.circular(5);
    final radius = BorderRadius.only(
      topLeft: !mine && !first ? rs : r,
      topRight: mine && !first ? rs : r,
      bottomLeft: !mine && last ? rs : r,
      bottomRight: mine && last ? rs : r,
    );
    final media = m.isImage;

    final bubble = Container(
      constraints: const BoxConstraints(maxWidth: 420),
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: mine ? AppColors.primary : AppColors.primarySoft,
        borderRadius: radius,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          if (!mine && first)
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
              child: GestureDetector(
                onTap: () => showChatProfile(context, m.uid, name),
                child: Row(mainAxisSize: MainAxisSize.min, children: [
                  Flexible(
                    child: Text(name,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w800, color: chatUserColor(m.uid))),
                  ),
                  if (adm) ...[
                    const SizedBox(width: 3),
                    Icon(Icons.verified_rounded, size: 13, color: AppColors.primary),
                  ],
                ]),
              ),
            ),
          if (m.re != null)
            Container(
              margin: const EdgeInsets.fromLTRB(8, 6, 8, 0),
              padding: const EdgeInsets.fromLTRB(8, 4, 8, 4),
              decoration: BoxDecoration(
                color: (mine ? Colors.white : AppColors.primary).withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(8),
                border: Border(left: BorderSide(color: mine ? Colors.white : AppColors.primary, width: 3)),
              ),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(m.reName ?? '',
                    style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w800, color: mine ? Colors.white : AppColors.primary)),
                Text(m.reText ?? '', maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12, color: sub)),
              ]),
            ),
          if (media)
            Padding(
              padding: EdgeInsets.only(top: (!mine && first) || m.re != null ? 6 : 0),
              child: ChatImage(message: m, maxWidth: 300),
            ),
          if (m.isVoice)
            Padding(padding: const EdgeInsets.fromLTRB(8, 8, 12, 0), child: VoicePlayer(message: m, mine: mine)),
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 6, 10, 6),
            child: Wrap(
              alignment: WrapAlignment.end,
              crossAxisAlignment: WrapCrossAlignment.end,
              spacing: 8,
              children: [
                if (m.text.isNotEmpty)
                  Text(m.text, style: TextStyle(fontSize: 14.5, height: 1.35, color: fg)),
                Row(mainAxisSize: MainAxisSize.min, children: [
                  if (m.edited) Text('ред. ', style: TextStyle(fontSize: 10.5, color: sub)),
                  Text(clock(m.timestamp), style: TextStyle(fontSize: 10.5, color: sub)),
                  if (mine) ...[
                    const SizedBox(width: 3),
                    Icon(
                      m.error != null
                          ? Icons.error_outline_rounded
                          : (m.pending ? Icons.schedule_rounded : Icons.done_rounded),
                      size: 13,
                      color: m.error != null ? AppColors.highlight : sub,
                    ),
                  ],
                ]),
              ],
            ),
          ),
        ],
      ),
    );

    final rx = m.rx.entries.where((e) => e.value.isNotEmpty && chatReactions.containsKey(e.key)).toList();

    return Padding(
      padding: EdgeInsets.only(top: first ? 6 : 1.5, bottom: last ? 2 : 0),
      child: Opacity(
        opacity: m.hidden ? 0.5 : 1,
        child: Row(
          mainAxisAlignment: mine ? MainAxisAlignment.end : MainAxisAlignment.start,
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            if (!mine)
              SizedBox(
                width: 38,
                child: last
                    ? GestureDetector(
                        onTap: () => showChatProfile(context, m.uid, name),
                        child: ChatAvatar(uid: m.uid, name: name, size: 32),
                      )
                    : null,
              ),
            Flexible(
              child: Column(
                crossAxisAlignment: mine ? CrossAxisAlignment.end : CrossAxisAlignment.start,
                children: [
                  GestureDetector(
                    onLongPress: onMenu,
                    onSecondaryTap: onMenu,
                    onDoubleTap: m.pending || m.error != null ? null : () => c.react(m, 'heart'),
                    onHorizontalDragEnd: (d) {
                      if ((d.primaryVelocity ?? 0).abs() > 300 && !m.pending && m.error == null) onReply();
                    },
                    child: bubble,
                  ),
                  if (m.hidden)
                    Padding(
                      padding: const EdgeInsets.only(top: 2),
                      child: Text('приховано через скарги', style: TextStyle(fontSize: 11, color: AppColors.textMuted)),
                    ),
                  if (rx.isNotEmpty)
                    Padding(
                      padding: const EdgeInsets.only(top: 3),
                      child: Wrap(spacing: 4, runSpacing: 4, children: [
                        for (final e in rx)
                          InkWell(
                            borderRadius: BorderRadius.circular(12),
                            onTap: () => c.react(m, e.key),
                            child: Container(
                              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
                              decoration: BoxDecoration(
                                color: e.value.contains(c.uid)
                                    ? AppColors.primary.withValues(alpha: 0.22)
                                    : AppColors.surface2,
                                borderRadius: BorderRadius.circular(12),
                                border: Border.all(
                                    color: e.value.contains(c.uid) ? AppColors.primary : AppColors.border),
                              ),
                              child: Text('${chatReactions[e.key]} ${e.value.length}',
                                  style: TextStyle(fontSize: 12.5, color: AppColors.textPrimary)),
                            ),
                          ),
                      ]),
                    ),
                  if (m.error != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 3),
                      child: Wrap(crossAxisAlignment: WrapCrossAlignment.center, spacing: 4, children: [
                        Text('⚠ ${m.error}', style: TextStyle(fontSize: 11.5, color: AppColors.threat)),
                        TextButton(
                          style: TextButton.styleFrom(visualDensity: VisualDensity.compact),
                          onPressed: () => c.retry(m),
                          child: const Text('Повторити'),
                        ),
                        TextButton(
                          style: TextButton.styleFrom(visualDensity: VisualDensity.compact),
                          onPressed: () => c.discard(m),
                          child: const Text('Прибрати'),
                        ),
                      ]),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
