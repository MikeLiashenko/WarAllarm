import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/boom_controller.dart';
import '../theme/app_colors.dart';

/// Sits above every screen and, when [BoomController] raises a pending question
/// after an alarm ends, shows a small centered window: "Чи було чути вибух?".
/// It's optional — the user can just skip it.
class BoomPromptWatcher extends StatefulWidget {
  const BoomPromptWatcher({super.key});

  @override
  State<BoomPromptWatcher> createState() => _BoomPromptWatcherState();
}

class _BoomPromptWatcherState extends State<BoomPromptWatcher> {
  bool _showing = false;

  @override
  Widget build(BuildContext context) {
    final boom = context.watch<BoomController>();
    if (boom.pending != null && !_showing) {
      _showing = true;
      WidgetsBinding.instance.addPostFrameCallback((_) => _open(boom));
    }
    return const SizedBox.shrink();
  }

  Future<void> _open(BoomController boom) async {
    final p = boom.pending;
    if (p == null || !mounted) {
      _showing = false;
      return;
    }
    await showDialog<void>(
      context: context,
      barrierDismissible: true,
      builder: (dialogCtx) => _BoomDialog(place: p.place),
    );
    // Whatever closed it (answer / skip / tap-outside) — clear the flag and
    // make sure the controller isn't left holding a stale prompt.
    if (mounted) boom.dismiss();
    _showing = false;
  }
}

class _BoomDialog extends StatelessWidget {
  const _BoomDialog({required this.place});
  final String place;

  @override
  Widget build(BuildContext context) {
    final boom = context.read<BoomController>();
    return Dialog(
      backgroundColor: AppColors.surface,
      insetPadding: const EdgeInsets.symmetric(horizontal: 40, vertical: 24),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 340),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 14),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text('💥', style: TextStyle(fontSize: 34),
                  textAlign: TextAlign.center),
              const SizedBox(height: 10),
              Text('Чи було чути вибух?',
                  textAlign: TextAlign.center,
                  style: TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w800,
                      color: AppColors.textPrimary)),
              const SizedBox(height: 6),
              Text(
                place.isEmpty
                    ? 'Тривога щойно завершилась.'
                    : 'Тривога у «$place» щойно завершилась.',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 13, color: AppColors.textSecondary),
              ),
              const SizedBox(height: 18),
              Row(
                children: [
                  Expanded(
                    child: _AnswerButton(
                      label: '💥 Так',
                      color: AppColors.threat,
                      onTap: () {
                        boom.answer(true);
                        Navigator.of(context).pop();
                      },
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: _AnswerButton(
                      label: '🤫 Ні',
                      color: AppColors.safe,
                      onTap: () {
                        boom.answer(false);
                        Navigator.of(context).pop();
                      },
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              TextButton(
                onPressed: () {
                  boom.dismiss();
                  Navigator.of(context).pop();
                },
                child: Text('Пропустити',
                    style: TextStyle(color: AppColors.textMuted)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _AnswerButton extends StatelessWidget {
  const _AnswerButton(
      {required this.label, required this.color, required this.onTap});
  final String label;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: color.withValues(alpha: 0.14),
      borderRadius: BorderRadius.circular(14),
      child: InkWell(
        borderRadius: BorderRadius.circular(14),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 13),
          child: Text(label,
              textAlign: TextAlign.center,
              style: TextStyle(
                  fontSize: 15, fontWeight: FontWeight.w800, color: color)),
        ),
      ),
    );
  }
}
