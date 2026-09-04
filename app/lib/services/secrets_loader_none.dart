/// Web fallback: no local file system, so no secrets to load.
Future<Map<String, String>> loadLocalSecrets() async => const {};
