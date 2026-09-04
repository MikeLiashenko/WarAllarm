/// A community chat message.
class ChatMessage {
  final String id;
  final String author;
  final String text;
  final DateTime timestamp;
  final bool mine;

  const ChatMessage({
    required this.id,
    required this.author,
    required this.text,
    required this.timestamp,
    this.mine = false,
  });

  factory ChatMessage.fromMap(String id, Map<dynamic, dynamic> map,
      {String? currentUser}) {
    final author = (map['author'] ?? map['user'] ?? map['name'] ?? 'Guest')
        .toString();
    return ChatMessage(
      id: id,
      author: author,
      text: (map['text'] ?? map['message'] ?? '').toString(),
      timestamp: _asTime(map['timestamp'] ?? map['time'] ?? map['ts']),
      mine: currentUser != null && author == currentUser,
    );
  }

  Map<String, dynamic> toMap() => {
        'author': author,
        'text': text,
        'timestamp': timestamp.millisecondsSinceEpoch,
      };

  static DateTime _asTime(dynamic v) {
    if (v is int) {
      return DateTime.fromMillisecondsSinceEpoch(
          v < 100000000000 ? v * 1000 : v);
    }
    if (v is String) return DateTime.tryParse(v) ?? DateTime.now();
    return DateTime.now();
  }
}
