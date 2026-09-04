import 'dart:io';

/// Desktop/mobile implementation: finds the Reader's `secrets.local.txt` and
/// parses it, so the app reuses the API key you already configured — the key
/// is never copied into the app, just read from that file at runtime.
Future<Map<String, String>> loadLocalSecrets() async {
  final candidates = <Directory>[];

  void addChain(Directory start) {
    var d = start;
    for (var i = 0; i < 8; i++) {
      candidates.add(d);
      final parent = d.parent;
      if (parent.path == d.path) break;
      d = parent;
    }
  }

  try {
    addChain(Directory.current);
  } catch (_) {}
  try {
    addChain(File(Platform.resolvedExecutable).parent);
  } catch (_) {}

  const names = [
    'secrets.local.txt',
    'src/Reader/secrets.local.txt',
    'Reader/secrets.local.txt',
  ];

  for (final dir in candidates) {
    for (final name in names) {
      final f = File('${dir.path}${Platform.pathSeparator}$name');
      try {
        if (f.existsSync()) return _parse(await f.readAsString());
      } catch (_) {}
    }
  }
  return const {};
}

Map<String, String> _parse(String content) {
  final out = <String, String>{};
  for (final raw in content.split('\n')) {
    final line = raw.trim();
    if (line.isEmpty || line.startsWith('#') || line.startsWith('//')) continue;
    final i = line.indexOf('=');
    if (i <= 0) continue;
    out[line.substring(0, i).trim()] = line.substring(i + 1).trim();
  }
  return out;
}
