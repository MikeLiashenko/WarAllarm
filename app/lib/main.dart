import 'package:flutter/material.dart';

import 'app.dart';
import 'services/background_alarm.dart';
import 'services/local_store.dart';
import 'services/rtdb_service.dart';
import 'services/secrets_loader.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final store = await LocalStore.create();
  final rtdb = RtdbService();

  // Reuse the Reader's existing API key from secrets.local.txt when present
  // (desktop). Never let an optional startup step crash the app on any device.
  Map<String, String> secrets = const {};
  try {
    secrets = await loadLocalSecrets();
  } catch (_) {/* no secrets file — fine */}

  // Register the background watcher (Android). Wrapped so a device that rejects
  // the foreground service / notifications never takes the whole app down —
  // the app still opens; background alarms just won't run there.
  try {
    await BackgroundAlarm.init();
  } catch (_) {/* background watcher unavailable on this device */}

  runApp(WarAlarmApp(store: store, rtdb: rtdb, secrets: secrets));
}
