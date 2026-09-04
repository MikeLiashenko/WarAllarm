/// A generated project API key.
class ApiKey {
  final String id;
  final String label;
  final String key;
  final DateTime createdAt;
  bool active;

  ApiKey({
    required this.id,
    required this.label,
    required this.key,
    required this.createdAt,
    this.active = true,
  });

  /// Masked representation for display, e.g. `wa_live_ab12••••••••ef90`.
  String get masked {
    if (key.length <= 12) return key;
    return '${key.substring(0, 10)}${'•' * 8}${key.substring(key.length - 4)}';
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'label': label,
        'key': key,
        'createdAt': createdAt.millisecondsSinceEpoch,
        'active': active,
      };

  factory ApiKey.fromJson(Map<String, dynamic> j) => ApiKey(
        id: j['id'] as String,
        label: j['label'] as String,
        key: j['key'] as String,
        createdAt:
            DateTime.fromMillisecondsSinceEpoch((j['createdAt'] as num).toInt()),
        active: (j['active'] ?? true) as bool,
      );
}
