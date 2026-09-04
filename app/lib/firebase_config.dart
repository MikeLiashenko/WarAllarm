/// Central configuration for the WarAlarm Firebase Realtime Database backend.
///
/// The app connects directly to the RTDB **public REST + SSE streaming API**
/// (`<url>/<path>.json`). This is the only approach that works *identically* on
/// Android and Windows desktop — the native `firebase_database` plugin has no
/// Windows implementation. See README for details.
class FirebaseConfig {
  const FirebaseConfig._();

  /// Realtime Database root URL (no trailing slash).
  static const String databaseUrl =
      'https://waralarm-c8c1f-default-rtdb.europe-west1.firebasedatabase.app';

  static const String projectId = 'waralarm-c8c1f';
  static const String projectNumber = '504356589519';
  static const String apiKey = 'AIzaSyBi049Tr_-FCSe7gQLRlZhJcBFOTceLCRY';
  static const String storageBucket = 'waralarm-c8c1f.firebasestorage.app';
  static const String androidAppId =
      '1:504356589519:android:cef7f5661934119ff74cba';

  // --- RTDB node paths (public / guest access) --------------------------------

  /// Live threat / alert status node.
  static const String statusPath = 'status';

  /// Feed of alert + chat events shown on the Now screen and Chat screen.
  static const String feedPath = 'feed';

  /// Community chat messages.
  static const String chatPath = 'chat';

  /// Map markers / monitored zones.
  static const String markersPath = 'markers';
}
