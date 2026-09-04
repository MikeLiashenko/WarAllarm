/// A single alert / event item shown in the Now feed.
class AlertEvent {
  final String id;
  final String title;
  final String message;
  final String region;
  final bool isThreat;
  final DateTime timestamp;

  const AlertEvent({
    required this.id,
    required this.title,
    required this.message,
    required this.region,
    required this.isThreat,
    required this.timestamp,
  });

  factory AlertEvent.fromMap(String id, Map<dynamic, dynamic> map) {
    return AlertEvent(
      id: id,
      title: (map['title'] ?? map['type'] ?? 'Alert').toString(),
      message: (map['message'] ?? map['text'] ?? '').toString(),
      region: (map['region'] ?? map['city'] ?? 'Kharkiv').toString(),
      isThreat: _asBool(map['threat'] ?? map['isThreat'] ?? map['danger']),
      timestamp: _asTime(map['timestamp'] ?? map['time'] ?? map['ts']),
    );
  }

  Map<String, dynamic> toMap() => {
        'title': title,
        'message': message,
        'region': region,
        'threat': isThreat,
        'timestamp': timestamp.millisecondsSinceEpoch,
      };

  static bool _asBool(dynamic v) {
    if (v is bool) return v;
    if (v is num) return v != 0;
    if (v is String) {
      final s = v.toLowerCase();
      return s == 'true' || s == '1' || s == 'yes' || s == 'alert';
    }
    return false;
  }

  static DateTime _asTime(dynamic v) {
    if (v is int) {
      // Heuristic: seconds vs milliseconds.
      return DateTime.fromMillisecondsSinceEpoch(v < 100000000000 ? v * 1000 : v);
    }
    if (v is String) {
      return DateTime.tryParse(v) ?? DateTime.now();
    }
    return DateTime.now();
  }
}
