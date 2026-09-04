/// An AI chat message within a session.
class AiMessage {
  final String role; // 'user' | 'assistant'
  final String content;
  final DateTime timestamp;

  const AiMessage({
    required this.role,
    required this.content,
    required this.timestamp,
  });

  bool get isUser => role == 'user';

  Map<String, dynamic> toJson() => {
        'role': role,
        'content': content,
        'timestamp': timestamp.millisecondsSinceEpoch,
      };

  factory AiMessage.fromJson(Map<String, dynamic> j) => AiMessage(
        role: j['role'] as String,
        content: j['content'] as String,
        timestamp:
            DateTime.fromMillisecondsSinceEpoch((j['timestamp'] as num).toInt()),
      );
}

/// A single AI conversation session (multi-session history).
class AiSession {
  final String id;
  String title;
  final List<AiMessage> messages;
  final DateTime createdAt;
  DateTime updatedAt;

  AiSession({
    required this.id,
    required this.title,
    required this.messages,
    required this.createdAt,
    required this.updatedAt,
  });

  String get preview =>
      messages.isEmpty ? 'New conversation' : messages.last.content;

  Map<String, dynamic> toJson() => {
        'id': id,
        'title': title,
        'createdAt': createdAt.millisecondsSinceEpoch,
        'updatedAt': updatedAt.millisecondsSinceEpoch,
        'messages': messages.map((m) => m.toJson()).toList(),
      };

  factory AiSession.fromJson(Map<String, dynamic> j) => AiSession(
        id: j['id'] as String,
        title: j['title'] as String,
        createdAt:
            DateTime.fromMillisecondsSinceEpoch((j['createdAt'] as num).toInt()),
        updatedAt:
            DateTime.fromMillisecondsSinceEpoch((j['updatedAt'] as num).toInt()),
        messages: (j['messages'] as List<dynamic>)
            .map((e) => AiMessage.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );
}
