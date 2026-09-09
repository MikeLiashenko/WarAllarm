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
  // (desktop). The key is only read at runtime — never stored in the app.
  final secrets = await loadLocalSecrets();

  // Register the background watcher (Android). Only runs if the user enabled it.
  await BackgroundAlarm.init();

  runApp(WarAlarmApp(store: store, rtdb: rtdb, secrets: secrets));
}
