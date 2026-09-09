import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'controllers/accuracy_controller.dart';
import 'controllers/ai_controller.dart';
import 'controllers/alarm_controller.dart';
import 'controllers/alerts_controller.dart';
import 'controllers/api_keys_controller.dart';
import 'controllers/chat_controller.dart';
import 'controllers/map_data_controller.dart';
import 'controllers/nav_controller.dart';
import 'controllers/places_controller.dart';
import 'controllers/settings_controller.dart';
import 'controllers/theme_controller.dart';
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
    this.secrets = const {},
  });

  final LocalStore store;
  final RtdbService rtdb;
  final Map<String, String> secrets;

  @override
  Widget build(BuildContext context) {
    final settings = SettingsController(store, externalSecrets: secrets);

    return MultiProvider(
      providers: [
        Provider<RtdbService>.value(value: rtdb),
        Provider<LocalStore>.value(value: store),
        ChangeNotifierProvider<NavController>(create: (_) => NavController()),
        ChangeNotifierProvider<ThemeController>(
          create: (_) => ThemeController(store)..applyNow(),
          lazy: false,
        ),
        ChangeNotifierProvider<SettingsController>.value(value: settings),
        ChangeNotifierProvider<PlacesController>(
          create: (_) => PlacesController(store),
        ),
        ChangeNotifierProvider<MapDataController>(
          create: (_) => MapDataController(rtdb, store)..start(),
        ),
        // Official per-oblast air-raid status (red/yellow), no token needed.
        ChangeNotifierProvider<AlertsController>(
          create: (_) => AlertsController(),
        ),
        // The real alarm: watches live threats + settings and drives the siren.
        ChangeNotifierProvider<AlarmController>(
          create: (ctx) => AlarmController(
            ctx.read<MapDataController>(),
            ctx.read<SettingsController>(),
          ),
        ),
        // Records forecast vs. reality to measure how accurate the model is.
        ChangeNotifierProvider<AccuracyController>(
          create: (ctx) => AccuracyController(
            ctx.read<MapDataController>(),
            ctx.read<SettingsController>(),
            store,
          ),
          lazy: false,
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
      child: Consumer<ThemeController>(
        builder: (context, theme, _) => MaterialApp(
          title: 'WarAlarm',
          debugShowCheckedModeBanner: false,
          theme: AppTheme.build(),
          home: HomeShell(),
        ),
      ),
    );
  }
}
