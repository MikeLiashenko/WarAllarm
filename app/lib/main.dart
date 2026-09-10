import 'dart:async';

import 'package:flutter/material.dart';

import 'app.dart';
import 'services/background_alarm.dart';
import 'services/local_store.dart';
import 'services/rtdb_service.dart';
import 'services/secrets_loader.dart';

void main() {
  // Catch any startup/async error and SHOW it on screen instead of a silent
  // crash ("app keeps stopping"), so problems on a specific device are visible.
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();

    final store = await LocalStore.create();
    final rtdb = RtdbService();

    Map<String, String> secrets = const {};
    try {
      secrets = await loadLocalSecrets();
    } catch (_) {/* no secrets file — fine */}

    // Background watcher (Android). Does nothing unless the user enabled it;
    // wrapped so a device that rejects it never takes the app down.
    try {
      await BackgroundAlarm.init();
    } catch (_) {/* background watcher unavailable on this device */}

    runApp(WarAlarmApp(store: store, rtdb: rtdb, secrets: secrets));
  }, (error, stack) {
    runApp(_StartupError('$error'));
  });
}

/// Shown only if startup crashed — turns a silent "app keeps stopping" into a
/// readable message the user can screenshot.
class _StartupError extends StatelessWidget {
  const _StartupError(this.message);
  final String message;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      home: Scaffold(
        backgroundColor: const Color(0xFF0E1424),
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('WarAlarm — помилка запуску',
                    style: TextStyle(
                        color: Colors.white,
                        fontSize: 18,
                        fontWeight: FontWeight.w800)),
                const SizedBox(height: 12),
                Text(message,
                    style: const TextStyle(
                        color: Color(0xFFFFC4C4), fontSize: 13, height: 1.4)),
                const SizedBox(height: 16),
                const Text('Зроби скрін цього екрана і надішли — це підкаже причину.',
                    style: TextStyle(color: Color(0xFF8792A6), fontSize: 12.5)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
