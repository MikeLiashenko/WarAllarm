import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../firebase_config.dart';

/// Direct client for the Firebase Realtime Database public REST + streaming API.
///
/// Works identically on Android and Windows desktop (no native Firebase plugin,
/// which has no Windows implementation). Real-time updates use Server-Sent
/// Events (`text/event-stream`) exactly like the official RTDB `.on('value')`.
class RtdbService {
  RtdbService({http.Client? client}) : _client = client ?? http.Client();

  final http.Client _client;

  Uri _uri(String path, [Map<String, String>? query]) {
    final clean = path.replaceAll(RegExp(r'^/+|/+$'), '');
    return Uri.parse('${FirebaseConfig.databaseUrl}/$clean.json')
        .replace(queryParameters: query);
  }

  // --- One-shot reads / writes ------------------------------------------------

  /// Reads the value at [path] once. Returns decoded JSON (Map/List/primitive).
  Future<dynamic> getOnce(String path) async {
    final res = await _client.get(_uri(path));
    if (res.statusCode >= 200 && res.statusCode < 300) {
      if (res.body.isEmpty || res.body == 'null') return null;
      return jsonDecode(res.body);
    }
    throw RtdbException('GET $path failed (${res.statusCode})', res.body);
  }

  /// Pushes a new child under [path] with an auto-generated key. Returns the key.
  Future<String> push(String path, Map<String, dynamic> value) async {
    final res = await _client.post(_uri(path), body: jsonEncode(value));
    if (res.statusCode >= 200 && res.statusCode < 300) {
      final body = jsonDecode(res.body);
      return (body is Map && body['name'] != null)
          ? body['name'].toString()
          : '';
    }
    throw RtdbException('POST $path failed (${res.statusCode})', res.body);
  }

  /// Overwrites the value at [path].
  Future<void> set(String path, dynamic value) async {
    final res = await _client.put(_uri(path), body: jsonEncode(value));
    if (res.statusCode < 200 || res.statusCode >= 300) {
      throw RtdbException('PUT $path failed (${res.statusCode})', res.body);
    }
  }

  // --- Real-time streaming (SSE) ----------------------------------------------

  /// Streams the full value at [path], emitting a fresh snapshot on every
  /// change. Mirrors `ref(path).onValue`. Auto-reconnects on transient errors.
  Stream<dynamic> stream(String path) {
    late StreamController<dynamic> controller;
    StreamSubscription<String>? sub;
    http.Client? streamClient;
    bool closed = false;

    // Local cache we mutate as SSE patch/put events arrive.
    dynamic cache;

    Future<void> connect() async {
      try {
        streamClient = http.Client();
        final request = http.Request('GET', _uri(path))
          ..headers['Accept'] = 'text/event-stream';
        final response = await streamClient!.send(request);

        if (response.statusCode < 200 || response.statusCode >= 300) {
          throw RtdbException('SSE $path failed (${response.statusCode})', '');
        }

        String currentEvent = '';
        sub = response.stream
            .transform(utf8.decoder)
            .transform(const LineSplitter())
            .listen((line) {
          if (line.startsWith('event:')) {
            currentEvent = line.substring(6).trim();
          } else if (line.startsWith('data:')) {
            final data = line.substring(5).trim();
            _applyEvent(currentEvent, data, (updated) => cache = updated,
                () => cache);
            if (currentEvent == 'put' || currentEvent == 'patch') {
              if (!closed) controller.add(cache);
            }
          }
        }, onError: (Object e) {
          _scheduleReconnect(closed, connect);
        }, onDone: () {
          _scheduleReconnect(closed, connect);
        });
      } catch (e) {
        _scheduleReconnect(closed, connect);
      }
    }

    controller = StreamController<dynamic>(
      onListen: connect,
      onCancel: () async {
        closed = true;
        await sub?.cancel();
        streamClient?.close();
      },
    );

    return controller.stream;
  }

  void _scheduleReconnect(bool closed, Future<void> Function() connect) {
    if (closed) return;
    Future<void>.delayed(const Duration(seconds: 3), () {
      if (!closed) connect();
    });
  }

  /// Applies an RTDB SSE `put`/`patch` payload to the local [read] cache.
  void _applyEvent(String event, String data, void Function(dynamic) write,
      dynamic Function() read) {
    if (data == 'null') return;
    dynamic decoded;
    try {
      decoded = jsonDecode(data);
    } catch (_) {
      return;
    }
    if (decoded is! Map) return;
    final path = (decoded['path'] ?? '/').toString();
    final value = decoded['data'];

    if (path == '/') {
      write(value);
      return;
    }
    // Nested path patch — merge into cache.
    final current = read();
    final root = current is Map ? Map<dynamic, dynamic>.from(current) : {};
    final parts = path.split('/').where((p) => p.isNotEmpty).toList();
    Map<dynamic, dynamic> node = root;
    for (var i = 0; i < parts.length - 1; i++) {
      node[parts[i]] = node[parts[i]] is Map
          ? Map<dynamic, dynamic>.from(node[parts[i]])
          : <dynamic, dynamic>{};
      node = node[parts[i]] as Map<dynamic, dynamic>;
    }
    if (value == null) {
      node.remove(parts.last);
    } else {
      node[parts.last] = value;
    }
    write(root);
  }

  void dispose() => _client.close();
}

class RtdbException implements Exception {
  final String message;
  final String body;
  RtdbException(this.message, this.body);
  @override
  String toString() => 'RtdbException: $message';
}
