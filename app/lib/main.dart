import 'package:flutter/material.dart';

import 'app.dart';
import 'services/local_store.dart';
import 'services/rtdb_service.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final store = await LocalStore.create();
  final rtdb = RtdbService();

  runApp(WarAlarmApp(store: store, rtdb: rtdb));
}
