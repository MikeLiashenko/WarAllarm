import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/ai_controller.dart';
import '../models/ai_session.dart';
import '../theme/app_colors.dart';
import '../utils/formatting.dart';
import '../widgets/responsive.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

class AiScreen extends StatefulWidget {
  const AiScreen({super.key});

  @override
  State<AiScreen> createState() => _AiScreenState();
}

class _AiScreenState extends State<AiScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();

  @override
  void dispose() {
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _send(AiController controller) async {
    final text = _input.text;
    if (text.trim().isEmpty) return;
    _input.clear();
    await controller.sendMessage(text);
    if (_scroll.hasClients) {
      _scroll.animateTo(
        _scroll.position.maxScrollExtent + 200,
        duration: const Duration(milliseconds: 250),
        curve: Curves.easeOut,
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final controller = context.watch<AiController>();
    final showSidePanel = !Responsive.railCollapsed(context);

    return PageBody(
      scrollable: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            title: 'AI assistant',
            subtitle: 'Ask about status, places and integration',
            icon: Icons.auto_awesome_rounded,
            trailing: showSidePanel
                ? null
                : IconButton(
                    tooltip: 'Sessions',
                    onPressed: () => _openSessionsSheet(context, controller),
                    icon: const Icon(Icons.history_rounded),
                  ),
          ),
          const SizedBox(height: 16),
          Expanded(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (showSidePanel) ...[
                  SizedBox(
                    width: 260,
                    child: _SessionList(controller: controller),
                  ),
                  const SizedBox(width: 14),
                ],
                Expanded(child: _conversation(controller)),
              ],
            ),
          ),
          const SizedBox(height: 12),
          _Composer(
            controller: _input,
            thinking: controller.thinking,
            onSend: () => _send(controller),
          ),
        ],
      ),
    );
  }

  Widget _conversation(AiController controller) {
    final session = controller.active;
    final messages = session?.messages ?? const <AiMessage>[];
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(22),
        border: Border.all(color: AppColors.border),
      ),
      child: messages.isEmpty && !controller.thinking
          ? _emptyState()
          : ListView.builder(
              controller: _scroll,
              padding: const EdgeInsets.all(18),
              itemCount: messages.length + (controller.thinking ? 1 : 0),
              itemBuilder: (context, i) {
                if (i >= messages.length) return const _ThinkingBubble();
                return _AiBubble(message: messages[i]);
              },
            ),
    );
  }

  Widget _emptyState() {
    final suggestions = [
      'What\'s the current threat status?',
      'How do I add a place to monitor?',
      'Generate an API integration snippet',
    ];
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 64,
            height: 64,
            decoration: BoxDecoration(
              gradient: const LinearGradient(
                  colors: [AppColors.accent, AppColors.primary]),
              borderRadius: BorderRadius.circular(20),
            ),
            child: const Icon(Icons.auto_awesome_rounded,
                color: Colors.white, size: 30),
          ),
          const SizedBox(height: 16),
          const Text('How can I help?',
              style: TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w700,
                  color: AppColors.textPrimary)),
          const SizedBox(height: 6),
          const Text('Ask the WarAlarm assistant anything.',
              style: TextStyle(color: AppColors.textSecondary)),
          const SizedBox(height: 18),
          Wrap(
            alignment: WrapAlignment.center,
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final s in suggestions)
                ActionChip(
                  label: Text(s),
                  backgroundColor: AppColors.primarySoft,
                  side: BorderSide.none,
                  labelStyle: const TextStyle(
                      color: AppColors.primaryDark,
                      fontWeight: FontWeight.w600,
                      fontSize: 12.5),
                  onPressed: () {
                    _input.text = s;
                    _send(context.read<AiController>());
                  },
                ),
            ],
          ),
        ],
      ),
    );
  }

  void _openSessionsSheet(BuildContext context, AiController controller) {
    showModalBottomSheet<void>(
      context: context,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
      ),
      builder: (context) => SizedBox(
        height: MediaQuery.sizeOf(context).height * 0.6,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: _SessionList(
            controller: controller,
            onSelected: () => Navigator.pop(context),
          ),
        ),
      ),
    );
  }
}

class _SessionList extends StatelessWidget {
  const _SessionList({required this.controller, this.onSelected});
  final AiController controller;
  final VoidCallback? onSelected;

  @override
  Widget build(BuildContext context) {
    final sessions = controller.sessions;
    final activeId = controller.active?.id;
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 14, 14, 8),
            child: ElevatedButton.icon(
              onPressed: () {
                controller.newSession();
                onSelected?.call();
              },
              icon: const Icon(Icons.add, size: 18),
              label: const Text('New chat'),
              style: ElevatedButton.styleFrom(
                minimumSize: const Size.fromHeight(44),
              ),
            ),
          ),
          const Divider(height: 1, color: AppColors.divider),
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.all(8),
              itemCount: sessions.length,
              itemBuilder: (context, i) {
                final s = sessions[i];
                final active = s.id == activeId;
                return Material(
                  color: Colors.transparent,
                  child: InkWell(
                    borderRadius: BorderRadius.circular(12),
                    onTap: () {
                      controller.select(s.id);
                      onSelected?.call();
                    },
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 12, vertical: 10),
                      decoration: BoxDecoration(
                        color: active
                            ? AppColors.primarySoft
                            : Colors.transparent,
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: Row(
                        children: [
                          Icon(Icons.chat_bubble_outline_rounded,
                              size: 16,
                              color: active
                                  ? AppColors.primaryDark
                                  : AppColors.textMuted),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  s.title,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                    fontSize: 13.5,
                                    fontWeight: active
                                        ? FontWeight.w700
                                        : FontWeight.w500,
                                    color: AppColors.textPrimary,
                                  ),
                                ),
                                Text(
                                  timeAgo(s.updatedAt),
                                  style: const TextStyle(
                                      fontSize: 11,
                                      color: AppColors.textMuted),
                                ),
                              ],
                            ),
                          ),
                          InkWell(
                            borderRadius: BorderRadius.circular(8),
                            onTap: () => controller.deleteSession(s.id),
                            child: const Padding(
                              padding: EdgeInsets.all(4),
                              child: Icon(Icons.close_rounded,
                                  size: 15, color: AppColors.textMuted),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _AiBubble extends StatelessWidget {
  const _AiBubble({required this.message});
  final AiMessage message;

  @override
  Widget build(BuildContext context) {
    final user = message.isUser;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        mainAxisAlignment:
            user ? MainAxisAlignment.end : MainAxisAlignment.start,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (!user) ...[
            Container(
              width: 30,
              height: 30,
              decoration: BoxDecoration(
                gradient: const LinearGradient(
                    colors: [AppColors.accent, AppColors.primary]),
                borderRadius: BorderRadius.circular(9),
              ),
              child: const Icon(Icons.auto_awesome_rounded,
                  color: Colors.white, size: 16),
            ),
            const SizedBox(width: 10),
          ],
          Flexible(
            child: Container(
              constraints: const BoxConstraints(maxWidth: 560),
              padding:
                  const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              decoration: BoxDecoration(
                color: user ? AppColors.primary : AppColors.primarySoft,
                borderRadius: BorderRadius.circular(16),
              ),
              child: Text(
                message.content,
                style: TextStyle(
                  fontSize: 14,
                  height: 1.4,
                  color: user ? Colors.white : AppColors.textPrimary,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ThinkingBubble extends StatelessWidget {
  const _ThinkingBubble();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.symmetric(vertical: 6),
      child: Row(
        children: [
          SizedBox(
            width: 30,
            height: 30,
            child: DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                    colors: [AppColors.accent, AppColors.primary]),
                borderRadius: BorderRadius.all(Radius.circular(9)),
              ),
              child: Icon(Icons.auto_awesome_rounded,
                  color: Colors.white, size: 16),
            ),
          ),
          SizedBox(width: 10),
          Text('Thinking…',
              style: TextStyle(
                  color: AppColors.textSecondary,
                  fontStyle: FontStyle.italic)),
        ],
      ),
    );
  }
}

class _Composer extends StatelessWidget {
  const _Composer({
    required this.controller,
    required this.thinking,
    required this.onSend,
  });

  final TextEditingController controller;
  final bool thinking;
  final VoidCallback onSend;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: TextField(
            controller: controller,
            minLines: 1,
            maxLines: 5,
            textInputAction: TextInputAction.send,
            onSubmitted: (_) => onSend(),
            decoration: const InputDecoration(
              hintText: 'Message the assistant…',
            ),
          ),
        ),
        const SizedBox(width: 10),
        SizedBox(
          width: 52,
          height: 52,
          child: ElevatedButton(
            onPressed: thinking ? null : onSend,
            style: ElevatedButton.styleFrom(
              padding: EdgeInsets.zero,
              shape: const CircleBorder(),
            ),
            child: const Icon(Icons.arrow_upward_rounded, size: 20),
          ),
        ),
      ],
    );
  }
}
