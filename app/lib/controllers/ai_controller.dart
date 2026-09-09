import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../models/ai_session.dart';
import '../services/ai_service.dart';
import '../services/ai_tools.dart';
import '../services/local_store.dart';

/// Manages multi-session AI conversations with persisted history.
///
/// The MVP uses a local, rule-based assistant so the screen is fully functional
/// offline. Swap [_generateReply] for a real API call (e.g. the Claude API) to
/// go live — the session/history plumbing stays unchanged.
class AiController extends ChangeNotifier {
  AiController(this._store) {
    _load();
  }

  static const _key = 'ai_sessions';
  final LocalStore _store;
  final _uuid = const Uuid();
  final _ai = AiService();

  /// True on the last turn if a real LLM produced the reply (vs local fallback).
  bool lastReplyWasLive = false;

  List<AiSession> _sessions = [];
  List<AiSession> get sessions => List.unmodifiable(_sessions);

  String? _activeId;
  AiSession? get active {
    if (_sessions.isEmpty) return null;
    return _sessions.firstWhere(
      (s) => s.id == _activeId,
      orElse: () => _sessions.first,
    );
  }

  bool _thinking = false;
  bool get thinking => _thinking;

  void _load() {
    final rows = _store.readJsonList(_key);
    _sessions = rows.map(AiSession.fromJson).toList()
      ..sort((a, b) => b.updatedAt.compareTo(a.updatedAt));
    if (_sessions.isEmpty) {
      newSession(select: true);
    } else {
      _activeId = _sessions.first.id;
    }
    notifyListeners();
  }

  void select(String id) {
    _activeId = id;
    notifyListeners();
  }

  AiSession newSession({bool select = true}) {
    final now = DateTime.now();
    final session = AiSession(
      id: _uuid.v4(),
      title: 'New chat',
      messages: [],
      createdAt: now,
      updatedAt: now,
    );
    _sessions.insert(0, session);
    if (select) _activeId = session.id;
    _persist();
    notifyListeners();
    return session;
  }

  Future<void> deleteSession(String id) async {
    _sessions.removeWhere((s) => s.id == id);
    if (_activeId == id) {
      _activeId = _sessions.isNotEmpty ? _sessions.first.id : null;
    }
    if (_sessions.isEmpty) newSession(select: true);
    await _persist();
    notifyListeners();
  }

  /// Sends a message. When [apiKey] is set, calls the real LLM (Claude/Gemini)
  /// with the WarAlarm system prompt + live [situation]; otherwise falls back
  /// to the local rule-based responder.
  Future<void> sendMessage(
    String text, {
    String apiKey = '',
    String model = '',
    String situation = '',
    AiToolbox? toolbox,
  }) async {
    final trimmed = text.trim();
    final session = active;
    if (trimmed.isEmpty || session == null || _thinking) return;

    session.messages.add(AiMessage(
      role: 'user',
      content: trimmed,
      timestamp: DateTime.now(),
    ));
    if (session.title == 'New chat') {
      session.title =
          trimmed.length > 32 ? '${trimmed.substring(0, 32)}…' : trimmed;
    }
    session.updatedAt = DateTime.now();
    _thinking = true;
    _resort();
    notifyListeners();

    String reply;
    if (apiKey.trim().isNotEmpty) {
      // Real LLM call with the WarAlarm system prompt + live situation.
      final canAct = toolbox != null;
      final system =
          'Ти — асистент застосунку повітряної тривоги WarAlarm для мешканця '
          'України. Тобі дають зведення про поточні повітряні загрози (тип, '
          'місце, відстань, ETA, стадія, напрямок). Відповідай КОРОТКО, '
          'спокійно і по суті українською, як досвідчений фахівець із безпеки. '
          'Без вступів і markdown. Ніколи не вигадуй загроз, яких немає у зведенні.'
          '${canAct ? '\n\nУ тебе Є ІНСТРУМЕНТИ: можеш подивитися свіжі цілі '
              '(get_targets), місця й налаштування, а також ЗМІНЮВАТИ застосунок — '
              'додавати/видаляти місця, вмикати сповіщення, змінювати радіуси, '
              'гучність, паузу й тему. Коли користувач просить щось зробити чи '
              'уточнити цілі — виклич потрібний інструмент, а не вигадуй. Роби рівно '
              'те, що просять; після дії коротко підтверди українською.' : ''}'
          '${situation.isEmpty ? '' : '\n\nПоточна обстановка:\n$situation'}';
      final resolvedModel =
          model.trim().isNotEmpty ? model : AiService.defaultModel(apiKey);
      try {
        if (canAct) {
          toolbox.log.clear();
          reply = await _ai.completeWithTools(
            apiKey: apiKey,
            model: resolvedModel,
            systemPrompt: system,
            history: session.messages,
            tools: toolbox.specs,
            runTool: toolbox.run,
          );
          if (toolbox.log.isNotEmpty) {
            reply = '$reply\n\n🛠 ${toolbox.log.join('; ')}.';
          }
        } else {
          reply = await _ai.complete(
            apiKey: apiKey,
            model: resolvedModel,
            systemPrompt: system,
            history: session.messages,
          );
        }
        lastReplyWasLive = true;
      } on AiException catch (e) {
        reply = '⚠ ${e.message}\n\n(Перевір ключ/модель у Налаштуваннях. '
            'Поки що — локальна відповідь.)\n\n${_generateReply(trimmed)}';
        lastReplyWasLive = false;
      } catch (e) {
        reply = '⚠ Помилка звʼязку з ІІ: $e\n\n${_generateReply(trimmed)}';
        lastReplyWasLive = false;
      }
    } else {
      await Future<void>.delayed(const Duration(milliseconds: 500));
      reply = _generateReply(trimmed);
      lastReplyWasLive = false;
    }

    session.messages.add(AiMessage(
      role: 'assistant',
      content: reply,
      timestamp: DateTime.now(),
    ));
    session.updatedAt = DateTime.now();
    _thinking = false;
    await _persist();
    notifyListeners();
  }

  String _generateReply(String prompt) {
    final p = prompt.toLowerCase();
    if (p.contains('threat') || p.contains('alert') || p.contains('danger')) {
      return 'I monitor the WarAlarm feed in real time. When a threat is '
          'active, the Now tab turns red and your notified places will alert '
          'you. Ask me “status” any time for a summary.';
    }
    if (p.contains('status')) {
      return 'You can see the live status on the Now tab — the central circle '
          'is blue when calm and pulses red during an active alert.';
    }
    if (p.contains('place') || p.contains('city') || p.contains('region')) {
      return 'Add locations under My Places to monitor them. Toggle the bell '
          'to control which places send you notifications.';
    }
    if (p.contains('api') || p.contains('key') || p.contains('integrat')) {
      return 'Head to the API Keys tab to generate a project key and copy '
          'ready-made Python, JavaScript or cURL snippets for the RTDB.';
    }
    return 'Got it. I\'m the WarAlarm assistant — ask me about the current '
        'threat status, your saved places, or integrating the API.';
  }

  void _resort() {
    _sessions.sort((a, b) => b.updatedAt.compareTo(a.updatedAt));
  }

  Future<void> _persist() {
    _resort();
    return _store.writeJsonList(_key, _sessions.map((s) => s.toJson()).toList());
  }
}
