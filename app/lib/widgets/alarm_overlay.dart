import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alarm_controller.dart';
import '../theme/app_colors.dart';

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
              padding:
                  const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              decoration: BoxDecoration(
                color: AppColors.threatDark,
                borderRadius: BorderRadius.circular(14),
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
                          fontSize: 13)),
                  const SizedBox(width: 12),
                  GestureDetector(
                    onTap: () => context.read<AlarmController>().resume(),
                    child: const Text('Увімкнути',
                        style: TextStyle(
                            color: Colors.white,
                            decoration: TextDecoration.underline,
                            fontWeight: FontWeight.w800,
                            fontSize: 13)),
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
    // only the 🚨 icon pulses, for the "alarm" feel.
    return Positioned.fill(
      child: Container(
        color: AppColors.threat.withValues(alpha: 0.97),
        child: SafeArea(
            child: Padding(
              padding: const EdgeInsets.all(28),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  FadeTransition(
                    opacity: _pulse.drive(Tween(begin: 0.45, end: 1.0)),
                    child: Text(alarm.impactAlarm ? '💥' : '🚨',
                        style: const TextStyle(fontSize: 72),
                        textAlign: TextAlign.center),
                  ),
                  const SizedBox(height: 8),
                  Text(alarm.impactAlarm ? 'ПРИЛІТ ПОРУЧ' : 'ТРИВОГА',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                          color: Colors.white,
                          fontSize: alarm.impactAlarm ? 40 : 46,
                          fontWeight: FontWeight.w900,
                          letterSpacing: 2)),
                  // Прилёт над домом — предупреждаем, что сейчас может быть громко
                  // (взрывы / работа ППО), чтобы грохот не был неожиданностью.
                  if (alarm.impactAlarm) ...[
                    const SizedBox(height: 12),
                    const Text('⚠️ Зараз може бути гучно',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                            color: Colors.white,
                            fontSize: 22,
                            fontWeight: FontWeight.w800)),
                    const SizedBox(height: 4),
                    const Text('Вибухи або робота ППО поруч. Тримайся, це нормально боятися.',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                            color: Colors.white, fontSize: 14.5, height: 1.35)),
                  ],
                  const SizedBox(height: 12),
                  Text(
                    alarm.detail.isEmpty
                        ? 'Загроза поруч — в укриття!'
                        : alarm.detail,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                        color: Colors.white, fontSize: 17,
                        fontWeight: FontWeight.w600),
                  ),
                  const SizedBox(height: 22),
                  const Text(
                    'Подалі від вікон · правило двох стін · будь з дорослими',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                        color: Colors.white, fontSize: 14.5, height: 1.4),
                  ),
                  const SizedBox(height: 34),
                  ElevatedButton(
                    onPressed: () =>
                        context.read<AlarmController>().hush(),
                    style: ElevatedButton.styleFrom(
                      backgroundColor: Colors.white,
                      foregroundColor: AppColors.threatDark,
                      padding: const EdgeInsets.symmetric(vertical: 20),
                      shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(18)),
                    ),
                    child: const Text('Я в укритті — стоп звук',
                        style: TextStyle(
                            fontSize: 17, fontWeight: FontWeight.w800)),
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
    );
  }
}
