import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';

/// Full-screen alert that appears over everything while the siren is ringing,
/// plus a slim "sound paused" bar while snoozed. This is what actually makes an
/// air-raid alarm impossible to miss when the app is open.
class AlarmOverlay extends StatefulWidget {
  const AlarmOverlay({super.key});

  @override
  State<AlarmOverlay> createState() => _AlarmOverlayState();
}

class _AlarmOverlayState extends State<AlarmOverlay>
    with SingleTickerProviderStateMixin {
  late final AnimationController _pulse = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 700),
  )..repeat(reverse: true);

  // Fixed (theme-independent) alarm reds so white text always reads well.
  static const _red = Color(0xFFB3170F);

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final alarm = context.watch<AlarmController>();

    // Snoozed → slim bar with a countdown and a "resume" action.
    if (!alarm.ringing) {
      if (alarm.snoozed) {
        return SafeArea(
          child: Align(
            alignment: Alignment.topCenter,
            child: Container(
              margin: const EdgeInsets.all(10),
              padding: const EdgeInsets.fromLTRB(14, 8, 8, 8),
              decoration: BoxDecoration(
                color: _red,
                borderRadius: BorderRadius.circular(24),
                boxShadow: const [
                  BoxShadow(
                      color: Color(0x55000000),
                      blurRadius: 16,
                      offset: Offset(0, 6)),
                ],
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.volume_off_rounded,
                      color: Colors.white, size: 18),
                  const SizedBox(width: 8),
                  Text('Звук на паузі · ${alarm.snoozeLeftSec} с',
                      style: const TextStyle(
                          color: Colors.white,
                          fontWeight: FontWeight.w700,
                          fontFeatures: [FontFeature.tabularFigures()],
                          fontSize: 13)),
                  const SizedBox(width: 10),
                  TextButton(
                    onPressed: () => context.read<AlarmController>().resume(),
                    style: TextButton.styleFrom(
                      backgroundColor: Colors.white.withValues(alpha: 0.18),
                      foregroundColor: Colors.white,
                      padding: const EdgeInsets.symmetric(
                          horizontal: 12, vertical: 6),
                      minimumSize: Size.zero,
                      shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(20)),
                    ),
                    child: const Text('Увімкнути',
                        style: TextStyle(
                            fontWeight: FontWeight.w800, fontSize: 13)),
                  ),
                ],
              ),
            ),
          ),
        );
      }
      return const SizedBox.shrink();
    }

    // Ringing → full-screen unmissable alarm. The background and the button stay
    // steady (opaque) so the button is always crisp and instantly tappable —
    // only the rings behind the icon pulse, for the "alarm" feel.
    final nuclear = alarm.nuclearAlarm;
    final impact = alarm.impactAlarm;
    // Радіація — окремий, темний «не-червоний» фон, щоб миттєво відрізнявся.
    final colors = nuclear
        ? const [Color(0xFF1A1500), Color(0xFF3A2E00)]
        : const [Color(0xFF5E0B0B), Color(0xFFD3261D)];
    final ringColor = nuclear ? const Color(0xFFFFD60A) : Colors.white;

    return Positioned.fill(
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.bottomCenter,
            end: Alignment.topCenter,
            colors: colors,
          ),
        ),
        child: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 520),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SizedBox(
                      height: 150,
                      child: AnimatedBuilder(
                        animation: _pulse,
                        builder: (_, _) => Stack(
                          alignment: Alignment.center,
                          children: [
                            for (final k in const [1.0, 0.72])
                              Container(
                                width: 150 * k * (0.86 + 0.14 * _pulse.value),
                                height: 150 * k * (0.86 + 0.14 * _pulse.value),
                                decoration: BoxDecoration(
                                  shape: BoxShape.circle,
                                  border: Border.all(
                                      color: ringColor.withValues(
                                          alpha: 0.18 + 0.2 * _pulse.value),
                                      width: 2),
                                ),
                              ),
                            Container(
                              width: 84,
                              height: 84,
                              alignment: Alignment.center,
                              decoration: BoxDecoration(
                                shape: BoxShape.circle,
                                color: ringColor.withValues(alpha: 0.16),
                              ),
                              child: Text(
                                  nuclear
                                      ? '☢️'
                                      : impact
                                          ? '💥'
                                          : '🚨',
                                  style: const TextStyle(fontSize: 46)),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                        nuclear
                            ? 'РАДІАЦІЙНА НЕБЕЗПЕКА'
                            : impact
                                ? 'ПРИЛІТ ПОРУЧ'
                                : 'ТРИВОГА',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                            color: Colors.white,
                            fontSize: nuclear
                                ? 30
                                : impact
                                    ? 42
                                    : 52,
                            height: 1.05,
                            fontWeight: FontWeight.w900,
                            letterSpacing: 2)),
                    const SizedBox(height: 14),
                    Text(
                      alarm.detail.isEmpty
                          ? 'Загроза поруч — в укриття!'
                          : alarm.detail,
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                          color: Colors.white,
                          fontSize: 17,
                          height: 1.35,
                          fontWeight: FontWeight.w600),
                    ),
                    // Прилёт над домом — предупреждаем, что сейчас может быть громко.
                    if (impact && !nuclear) ...[
                      const SizedBox(height: 18),
                      _card(
                        '⚠️ Зараз може бути гучно',
                        'Вибухи або робота ППО поруч. Тримайся, це нормально боятися.',
                      ),
                    ],
                    // Радіація — дії ІНШІ, ніж при звичайній тривозі (не в підвал за стіни,
                    // а укритися в приміщенні й загерметизуватися).
                    if (nuclear) ...[
                      const SizedBox(height: 18),
                      _card(
                        'Негайно в приміщення',
                        '• Зачини вікна, двері, вимкни вентиляцію/кондиціонер\n'
                            '• Углиб будівлі, подалі від вікон і зовнішніх стін\n'
                            '• Не виходь надвір. Був надворі — зніми верхній одяг, прийми душ\n'
                            '• Йод (калію йодид) — ЛИШЕ за вказівкою ДСНС\n'
                            '• Слухай офіційні джерела (ДСНС, ОВА), чекай відбою',
                        left: true,
                      ),
                    ],
                    if (!nuclear) ...[
                      const SizedBox(height: 18),
                      Wrap(
                        alignment: WrapAlignment.center,
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (final s in const [
                            'Подалі від вікон',
                            'Правило двох стін',
                            'Будь з дорослими',
                          ])
                            Container(
                              padding: const EdgeInsets.symmetric(
                                  horizontal: 12, vertical: 7),
                              decoration: BoxDecoration(
                                color: Colors.white.withValues(alpha: 0.12),
                                borderRadius: BorderRadius.circular(20),
                                border: Border.all(
                                    color: Colors.white.withValues(alpha: 0.25)),
                              ),
                              child: Text(s,
                                  style: const TextStyle(
                                      color: Colors.white,
                                      fontSize: 13,
                                      fontWeight: FontWeight.w700)),
                            ),
                        ],
                      ),
                    ],
                    const SizedBox(height: 30),
                    ElevatedButton(
                      onPressed: () => context.read<AlarmController>().hush(),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.white,
                        foregroundColor:
                            nuclear ? const Color(0xFF3A2E00) : _red,
                        padding: const EdgeInsets.symmetric(vertical: 20),
                        shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(16)),
                      ),
                      child: const Text('Я в укритті — стоп звук',
                          style: TextStyle(
                              fontSize: 17, fontWeight: FontWeight.w900)),
                    ),
                    const SizedBox(height: 10),
                    const Text(
                      'Звук сам увімкнеться знову, якщо небезпека триває.',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: Colors.white70, fontSize: 12.5),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _card(String title, String body, {bool left = false}) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Colors.black.withValues(alpha: 0.22),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: Colors.white.withValues(alpha: 0.18)),
      ),
      child: Column(
        crossAxisAlignment:
            left ? CrossAxisAlignment.start : CrossAxisAlignment.center,
        children: [
          Text(title,
              textAlign: TextAlign.center,
              style: const TextStyle(
                  color: Colors.white,
                  fontSize: 19,
                  fontWeight: FontWeight.w800)),
          const SizedBox(height: 8),
          Text(body,
              textAlign: left ? TextAlign.left : TextAlign.center,
              style: const TextStyle(
                  color: Colors.white, fontSize: 14.5, height: 1.5)),
        ],
      ),
    );
  }
}
