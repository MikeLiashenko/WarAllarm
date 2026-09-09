import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:ui';

import 'package:flutter_background_service/flutter_background_service.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import '../domain/alarm_decision.dart';
import '../firebase_config.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import 'home_widget_service.dart';

/// Keeps WarAlarm watching the sky even when the app is CLOSED or the phone is
/// LOCKED (Android). A foreground service polls the live RTDB `state` and, on a
/// real alarm near a notified place, fires a full-screen, high-importance
/// notification with the siren sound + vibration — enough to wake a locked
/// phone at night. Opening the notification launches the app, which then plays
/// the continuous in-app siren.
///
/// Self-contained: no push server / FCM. Only runs while the toggle in Settings
/// is on. Never alarms on demo data (background never loads demo).
class BackgroundAlarm {
  static const _watchChannel = 'waralarm_watch'; // ongoing, silent
  static const _alarmChannel = 'waralarm_alarm'; // loud, full-screen
  static const _watchNotifId = 8801;
  static const _alarmNotifId = 8802;
  static const prefEnabled = 'set_bg_alarm';

  static final _fln = FlutterLocalNotificationsPlugin();

  /// Call once at startup to register the service (does not start it).
  static Future<void> init() async {
    if (!Platform.isAndroid) return;
    await _ensureChannels(_fln);
    final service = FlutterBackgroundService();
    await service.configure(
      androidConfiguration: AndroidConfiguration(
        onStart: onStart,
        autoStart: false,
        autoStartOnBoot: true,
        isForegroundMode: true,
        notificationChannelId: _watchChannel,
        initialNotificationTitle: 'WarAlarm',
        initialNotificationContent: 'Стежу за небом…',
        foregroundServiceNotificationId: _watchNotifId,
        foregroundServiceTypes: const [AndroidForegroundType.dataSync],
      ),
      iosConfiguration: IosConfiguration(autoStart: false),
    );
    // If the user had it enabled, make sure it is running again after a restart.
    final prefs = await SharedPreferences.getInstance();
    if (prefs.getBool(prefEnabled) ?? false) {
      if (!await service.isRunning()) service.startService();
    }
  }

  static Future<bool> isRunning() =>
      Platform.isAndroid ? FlutterBackgroundService().isRunning() : Future.value(false);

  /// Turn the background watcher on: ask for notification permission, start the
  /// foreground service. Returns false if notifications were denied.
  static Future<bool> enable() async {
    if (!Platform.isAndroid) return false;
    final android = _fln.resolvePlatformSpecificImplementation<
        AndroidFlutterLocalNotificationsPlugin>();
    final granted = await android?.requestNotificationsPermission() ?? true;
    // Best-effort: full-screen intent (Android 14+) so it can pop over the lock screen.
    try {
      await android?.requestFullScreenIntentPermission();
    } catch (_) {}
    final service = FlutterBackgroundService();
    if (!await service.isRunning()) service.startService();
    // Enabling happens from inside the open app — tell the service to stay quiet
    // until we go to the background (the in-app siren covers the foreground).
    await Future<void>.delayed(const Duration(milliseconds: 400));
    setAppForeground(true);
    return granted != false;
  }

  static Future<void> disable() async {
    if (!Platform.isAndroid) return;
    final service = FlutterBackgroundService();
    if (await service.isRunning()) service.invoke('stopService');
    await _fln.cancel(_alarmNotifId);
  }

  /// Let the service know the app UI is (not) in front, so it doesn't double up
  /// the siren while the user already has the app open and ringing.
  static void setAppForeground(bool front) {
    if (!Platform.isAndroid) return;
    FlutterBackgroundService().invoke('appFront', {'v': front});
  }

  static Future<void> _ensureChannels(FlutterLocalNotificationsPlugin fln) async {
    const initSettings = InitializationSettings(
      android: AndroidInitializationSettings('@mipmap/ic_launcher'),
    );
    await fln.initialize(initSettings);
    final android = fln.resolvePlatformSpecificImplementation<
        AndroidFlutterLocalNotificationsPlugin>();
    await android?.createNotificationChannel(const AndroidNotificationChannel(
      _watchChannel,
      'Моніторинг неба',
      description: 'Постійне стеження за загрозами у фоні',
      importance: Importance.low,
      playSound: false,
      enableVibration: false,
      showBadge: false,
    ));
    await android?.createNotificationChannel(const AndroidNotificationChannel(
      _alarmChannel,
      'Тривога',
      description: 'Гучне сповіщення про загрозу поруч',
      importance: Importance.max,
      playSound: true,
      sound: RawResourceAndroidNotificationSound('waralarm_siren'),
      audioAttributesUsage: AudioAttributesUsage.alarm,
      enableVibration: true,
      vibrationPattern: null,
    ));
  }

  // --- Background isolate entry point ----------------------------------------

  @pragma('vm:entry-point')
  static Future<void> onStart(ServiceInstance service) async {
    DartPluginRegistrant.ensureInitialized();
    final fln = FlutterLocalNotificationsPlugin();
    await _ensureChannels(fln);

    var appFront = false;
    var wasAlarm = false;
    DateTime? lastNotified;
    var lastWidgetMs = 0;

    service.on('stopService').listen((_) => service.stopSelf());
    service.on('appFront').listen((e) => appFront = (e?['v'] as bool?) ?? false);

    final client = http.Client();
    Timer.periodic(const Duration(seconds: 6), (_) async {
      try {
        final prefs = await SharedPreferences.getInstance();
        await prefs.reload();
        if (!(prefs.getBool(prefEnabled) ?? false)) return;

        final res = await client
            .get(Uri.parse('${FirebaseConfig.databaseUrl}/state.json'))
            .timeout(const Duration(seconds: 10));
        if (res.statusCode != 200) return;
        final data = jsonDecode(utf8.decode(res.bodyBytes));
        if (data is! Map) return;

        // Live threats (never demo in the background).
        final targets = <MapTarget>[];
        final rawT = data['threats'];
        if (rawT is List) {
          for (final e in rawT) {
            if (e is Map) targets.add(MapTarget.fromMap(e));
          }
        }

        final radius = _getDouble(prefs, 'set_alarm_radius', 25).clamp(5, 100);

        // Keep the home-screen widget fresh even with the app closed (~30 s).
        final nowMs = DateTime.now().millisecondsSinceEpoch;
        final home = data['home'];
        if (home is Map && home['lat'] != null && nowMs - lastWidgetMs > 30000) {
          lastWidgetMs = nowMs;
          await HomeWidgetService.push(
            placeName: (data['homeName'] ?? 'Дім').toString(),
            lat: (home['lat'] as num).toDouble(),
            lon: (home['lon'] as num).toDouble(),
            targets: targets,
            radiusKm: radius.toDouble(),
          );
        }

        // Notified places from the same store the UI writes.
        final places = _loadNotifiedPlaces(prefs);
        if (places.isEmpty) return;

        final d = evaluateAlarm(
            targets: targets, places: places, radiusKm: radius.toDouble());

        // Keep the ongoing notification informative.
        service.invoke('update');
        if (service is AndroidServiceInstance) {
          service.setForegroundNotificationInfo(
            title: d.level == 'alarm' ? '🔴 ТРИВОГА' : 'WarAlarm — стежу за небом',
            content: d.level == 'alarm'
                ? '${d.placeName}: ${d.detail}'
                : 'Загроз для ваших місць немає',
          );
        }

        final now = DateTime.now();
        final reNotify = lastNotified == null ||
            now.difference(lastNotified!).inSeconds >= 90;
        if (d.level == 'alarm') {
          // Fire on the rising edge, and re-fire every 90 s while it lasts, but
          // stay quiet if the user already has the app open (it rings there).
          if (!appFront && (!wasAlarm || reNotify)) {
            await _fireAlarm(fln, d);
            lastNotified = now;
          }
          wasAlarm = true;
        } else {
          if (wasAlarm) await fln.cancel(_alarmNotifId);
          wasAlarm = false;
          lastNotified = null;
        }
      } catch (_) {
        // transient (offline / parse) — try again next tick
      }
    });
  }

  static Future<void> _fireAlarm(
      FlutterLocalNotificationsPlugin fln, AlarmDecision d) async {
    final details = AndroidNotificationDetails(
      _alarmChannel,
      'Тривога',
      channelDescription: 'Гучне сповіщення про загрозу поруч',
      importance: Importance.max,
      priority: Priority.max,
      category: AndroidNotificationCategory.alarm,
      fullScreenIntent: true,
      ongoing: true,
      autoCancel: false,
      visibility: NotificationVisibility.public,
      icon: '@mipmap/ic_launcher',
      ticker: 'ТРИВОГА',
    );
    await fln.show(
      _alarmNotifId,
      '🔴 ТРИВОГА · ${d.placeName}',
      d.detail.isEmpty ? 'Загроза поруч — у безпечне місце!' : d.detail,
      NotificationDetails(android: details),
    );
  }

  static List<MonitoredPlace> _loadNotifiedPlaces(SharedPreferences prefs) {
    final raw = prefs.getString('saved_places');
    if (raw == null || raw.isEmpty) return const [];
    final out = <MonitoredPlace>[];
    try {
      final list = jsonDecode(raw) as List<dynamic>;
      for (var i = 0; i < list.length; i++) {
        final p = list[i] as Map<String, dynamic>;
        if ((p['notify'] ?? true) == false) continue;
        final lat = (p['lat'] as num).toDouble();
        final lon = ((p['lng'] ?? p['lon']) as num).toDouble();
        out.add(MonitoredPlace(
          id: 'u$i',
          icon: '📍',
          name: (p['name'] ?? 'Місце').toString(),
          lat: lat,
          lon: lon,
        ));
      }
    } catch (_) {}
    return out;
  }

  static double _getDouble(SharedPreferences p, String k, double fb) {
    final v = p.get(k);
    if (v is double) return v;
    if (v is int) return v.toDouble();
    return fb;
  }
}
