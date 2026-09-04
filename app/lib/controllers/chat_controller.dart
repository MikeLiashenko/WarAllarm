import 'dart:async';

import 'package:flutter/foundation.dart';

import '../firebase_config.dart';
import '../models/chat_message.dart';
import '../services/rtdb_service.dart';

/// Community chat backed by the RTDB `chat` node (real-time via SSE).
class ChatController extends ChangeNotifier {
  ChatController(this._rtdb, {required this.currentUser});

  final RtdbService _rtdb;
  final String currentUser;

  StreamSubscription<dynamic>? _sub;
  List<ChatMessage> _messages = const [];
  List<ChatMessage> get messages => _messages;

  bool _sending = false;
  bool get sending => _sending;

  void start() {
    _sub = _rtdb.stream(FirebaseConfig.chatPath).listen(_onData, onError: (_) {});
  }

  void _onData(dynamic data) {
    final items = <ChatMessage>[];
    if (data is Map) {
      data.forEach((key, value) {
        if (value is Map) {
          items.add(ChatMessage.fromMap(key.toString(), value,
              currentUser: currentUser));
        }
      });
    }
    items.sort((a, b) => a.timestamp.compareTo(b.timestamp));
    _messages = items.isEmpty ? _demo() : items;
    notifyListeners();
  }

  Future<void> send(String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty || _sending) return;
    _sending = true;
    notifyListeners();
    final msg = ChatMessage(
      id: 'local',
      author: currentUser,
      text: trimmed,
      timestamp: DateTime.now(),
      mine: true,
    );
    try {
      await _rtdb.push(FirebaseConfig.chatPath, msg.toMap());
    } catch (_) {
      // Optimistically append locally if the write fails (read-only backend).
      _messages = [..._messages, msg];
    } finally {
      _sending = false;
      notifyListeners();
    }
  }

  List<ChatMessage> _demo() {
    final now = DateTime.now();
    return [
      ChatMessage(
        id: '1',
        author: 'Olena',
        text: 'Stay safe everyone, hearing sirens in the north.',
        timestamp: now.subtract(const Duration(minutes: 22)),
      ),
      ChatMessage(
        id: '2',
        author: 'Dmytro',
        text: 'All quiet in the centre right now.',
        timestamp: now.subtract(const Duration(minutes: 12)),
      ),
    ];
  }

  @override
  void dispose() {
    _sub?.cancel();
    super.dispose();
  }
}
