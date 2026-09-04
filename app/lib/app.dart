import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'controllers/ai_controller.dart';
import 'controllers/api_keys_controller.dart';
import 'controllers/chat_controller.dart';
import 'controllers/map_data_controller.dart';
import 'controllers/nav_controller.dart';
import 'controllers/places_controller.dart';
import 'controllers/settings_controller.dart';
import 'controllers/threat_controller.dart';
import 'services/local_store.dart';
import 'services/rtdb_service.dart';
import 'screens/home_shell.dart';
import 'theme/app_theme.dart';

/// Root widget. Owns the shared services and wires up all controllers.
class WarAlarmApp extends StatelessWidget {
  const WarAlarmApp({
    super.key,
    required this.store,
    required this.rtdb,
  });

  final LocalStore store;
  final RtdbService rtdb;

  @override
  Widget build(BuildContext context) {
    final settings = SettingsController(store);

    return MultiProvider(
      providers: [
        Provider<RtdbService>.value(value: rtdb),
        Provider<LocalStore>.value(value: store),
        ChangeNotifierProvider<NavController>(create: (_) => NavController()),
        ChangeNotifierProvider<SettingsController>.value(value: settings),
        ChangeNotifierProvider<ThreatController>(
          create: (_) => ThreatController(rtdb)..start(),
        ),
        ChangeNotifierProvider<PlacesController>(
          create: (_) => PlacesController(store),
        ),
        ChangeNotifierProvider<MapDataController>(
          create: (_) => MapDataController(rtdb)..start(),
        ),
        ChangeNotifierProvider<ChatController>(
          create: (_) =>
              ChatController(rtdb, currentUser: settings.displayName)..start(),
        ),
        ChangeNotifierProvider<AiController>(create: (_) => AiController(store)),
        ChangeNotifierProvider<ApiKeysController>(
          create: (_) => ApiKeysController(store),
        ),
      ],
      child: MaterialApp(
        title: 'WarAlarm',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light(),
        home: const HomeShell(),
      ),
    );
  }
}
