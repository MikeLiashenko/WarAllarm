import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:home_widget/home_widget.dart';

import '../domain/alarm_decision.dart';
import '../domain/probability.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';

/// Pushes the current home status + attack probability to the Android
/// home-screen widget, so the user sees it without opening the app. Written from
/// both the foreground app and the background service, so it stays fresh even
/// when the app is closed.
class HomeWidgetService {
  static const _androidName = 'WarAlarmWidgetProvider';

  /// Compute the home picture and update the widget. [officialLevel] is the
  /// oblast air-raid status when known (foreground only).
  static Future<void> push({
    required String placeName,
    required double lat,
    required double lon,
    required List<MapTarget> targets,
    required double radiusKm,
    String? officialLevel,
    bool live = true,
  }) async {
    if (kIsWeb || !Platform.isAndroid) return;
    final home = MonitoredPlace(
        id: 'home', icon: '🏠', name: placeName, lat: lat, lon: lon);
    final prob = probabilityFor(home, targets, officialLevel: officialLevel);
    final decision =
        evaluateAlarm(targets: targets, places: [home], radiusKm: radiusKm);

    final now = DateTime.now();
    final hh = now.hour.toString().padLeft(2, '0');
    final mm = now.minute.toString().padLeft(2, '0');

    try {
      await HomeWidget.saveWidgetData<String>('w_level', decision.level);
      await HomeWidget.saveWidgetData<String>('w_title', decision.title);
      await HomeWidget.saveWidgetData<String>('w_place', placeName);
      await HomeWidget.saveWidgetData<String>('w_percent', '${prob.percent}');
      await HomeWidget.saveWidgetData<String>('w_verdict', prob.verdict);
      await HomeWidget.saveWidgetData<String>(
          'w_detail',
          decision.level == 'calm'
              ? (live ? 'Загроз поруч немає' : 'Очікую дані…')
              : decision.detail);
      await HomeWidget.saveWidgetData<String>('w_updated', 'оновлено $hh:$mm');
      await HomeWidget.updateWidget(androidName: _androidName);
    } catch (_) {
      // widget not added / plugin unavailable — ignore
    }
  }
}
