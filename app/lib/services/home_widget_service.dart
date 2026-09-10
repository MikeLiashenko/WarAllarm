import '../models/map_target.dart';

/// No-op build of the home-screen widget bridge. The native App Widget plugin
/// (home_widget) crashed at launch on some devices (Galaxy Tab S10+), so it is
/// disabled here; the Telegram bot covers the at-a-glance status instead.
class HomeWidgetService {
  static Future<void> push({
    required String placeName,
    required double lat,
    required double lon,
    required List<MapTarget> targets,
    required double radiusKm,
    String? officialLevel,
    bool live = true,
  }) async {
    // intentionally does nothing
  }
}
