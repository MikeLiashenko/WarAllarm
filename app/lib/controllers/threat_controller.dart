import 'dart:async';

import 'package:flutter/foundation.dart';

import '../firebase_config.dart';
import '../models/alert_event.dart';
import '../services/rtdb_service.dart';

/// Live threat status + alert feed, streamed from the RTDB.
///
/// Falls back to a small demo dataset when the backend nodes are empty so the
/// UI is never blank on first run.
class ThreatController extends ChangeNotifier {
  ThreatController(this._rtdb);

  final RtdbService _rtdb;

  StreamSubscription<dynamic>? _statusSub;
  StreamSubscription<dynamic>? _feedSub;

  bool _threatActive = false;
  bool get threatActive => _threatActive;

  String _statusLabel = 'All clear';
  String get statusLabel => _statusLabel;

  String _region = 'Kharkiv';
  String get region => _region;

  bool _connected = false;
  bool get connected => _connected;

  List<AlertEvent> _feed = const [];
  List<AlertEvent> get feed => _feed;

  void start() {
    _statusSub = _rtdb.stream(FirebaseConfig.statusPath).listen(
      _onStatus,
      onError: (_) => _setConnected(false),
    );
    _feedSub = _rtdb.stream(FirebaseConfig.feedPath).listen(
      _onFeed,
      onError: (_) => _setConnected(false),
    );
  }

  void _setConnected(bool value) {
    if (_connected == value) return;
    _connected = value;
    notifyListeners();
  }

  void _onStatus(dynamic data) {
    _setConnected(true);
    if (data is Map) {
      final active = data['threat'] ?? data['active'] ?? data['alert'];
      _threatActive = _asBool(active);
      _statusLabel = (data['label'] ??
              (_threatActive ? 'Active threat' : 'All clear'))
          .toString();
      _region = (data['region'] ?? data['city'] ?? _region).toString();
    } else if (data is bool) {
      _threatActive = data;
      _statusLabel = data ? 'Active threat' : 'All clear';
    }
    notifyListeners();
  }

  void _onFeed(dynamic data) {
    _setConnected(true);
    final items = <AlertEvent>[];
    if (data is Map) {
      data.forEach((key, value) {
        if (value is Map) {
          items.add(AlertEvent.fromMap(key.toString(), value));
        }
      });
    } else if (data is List) {
      for (var i = 0; i < data.length; i++) {
        final v = data[i];
        if (v is Map) items.add(AlertEvent.fromMap('$i', v));
      }
    }
    items.sort((a, b) => b.timestamp.compareTo(a.timestamp));
    _feed = items.isEmpty ? _demoFeed() : items;

    // If no explicit status node, derive threat from most recent feed item.
    if (!_connected || _feed.isNotEmpty) {
      final recentThreat = _feed
          .take(3)
          .any((e) => e.isThreat && _isRecent(e.timestamp));
      if (data is! Map || !(data).containsKey('_status')) {
        // Only override when a real feed exists and there is a fresh threat.
        if (recentThreat) _threatActive = true;
      }
    }
    notifyListeners();
  }

  bool _isRecent(DateTime t) =>
      DateTime.now().difference(t) < const Duration(minutes: 30);

  /// Hourly alert counts over the last 24h — feeds the analytics chart.
  List<int> get hourlyBuckets {
    final now = DateTime.now();
    final buckets = List<int>.filled(24, 0);
    for (final e in _feed) {
      final diff = now.difference(e.timestamp).inHours;
      if (diff >= 0 && diff < 24) {
        buckets[23 - diff] += 1;
      }
    }
    if (buckets.every((b) => b == 0)) {
      return const [0, 1, 0, 2, 1, 3, 2, 4, 3, 2, 1, 0, 1, 2, 1, 3, 5, 4, 2, 1, 0, 1, 0, 1];
    }
    return buckets;
  }

  int get threatsToday =>
      _feed.where((e) => e.isThreat && _sameDay(e.timestamp)).length;
  int get eventsToday => _feed.where((e) => _sameDay(e.timestamp)).length;

  bool _sameDay(DateTime t) {
    final now = DateTime.now();
    return t.year == now.year && t.month == now.month && t.day == now.day;
  }

  List<AlertEvent> _demoFeed() {
    final now = DateTime.now();
    return [
      AlertEvent(
        id: 'd1',
        title: 'All clear',
        message: 'No active threats reported in the region.',
        region: 'Kharkiv',
        isThreat: false,
        timestamp: now.subtract(const Duration(minutes: 4)),
      ),
      AlertEvent(
        id: 'd2',
        title: 'Air raid ended',
        message: 'Threat has passed. Stay alert for updates.',
        region: 'Kharkiv Oblast',
        isThreat: false,
        timestamp: now.subtract(const Duration(hours: 1, minutes: 12)),
      ),
      AlertEvent(
        id: 'd3',
        title: 'Shahed drones',
        message: 'UAV activity reported to the north-east.',
        region: 'Kharkiv',
        isThreat: true,
        timestamp: now.subtract(const Duration(hours: 2, minutes: 40)),
      ),
    ];
  }

  static bool _asBool(dynamic v) {
    if (v is bool) return v;
    if (v is num) return v != 0;
    if (v is String) {
      final s = v.toLowerCase();
      return s == 'true' || s == '1' || s == 'alert' || s == 'active';
    }
    return false;
  }

  @override
  void dispose() {
    _statusSub?.cancel();
    _feedSub?.cancel();
    super.dispose();
  }
}
