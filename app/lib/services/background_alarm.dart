/// No-op build of the background watcher. The foreground-service +
/// notification plugins (flutter_background_service / flutter_local_notifications)
/// crashed at launch on some devices (Galaxy Tab S10+), so the in-app background
/// alarm is disabled here — the Telegram bot delivers alerts when the app is
/// closed instead. The public API is kept so callers need no changes.
class BackgroundAlarm {
  static const prefEnabled = 'set_bg_alarm';

  static Future<void> init() async {}
  static Future<bool> isRunning() async => false;
  static Future<bool> enable() async => false;
  static Future<void> disable() async {}
  static void setAppForeground(bool front) {}
}
