import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/ai_session.dart';

/// Real LLM client for the AI tab. Mirrors the C# `AiAdvisor.cs`: auto-detects
/// the provider from the API-key prefix and calls Anthropic (Claude) or Google
/// (Gemini) directly over HTTP. Works on Android + Windows (native).
class AiService {
  AiService({http.Client? client}) : _client = client ?? http.Client();
  final http.Client _client;

  /// Returns the assistant reply for [history] given a [systemPrompt].
  /// Throws [AiException] on failure so the caller can fall back gracefully.
  Future<String> complete({
    required String apiKey,
    required String model,
    required String systemPrompt,
    required List<AiMessage> history,
  }) async {
    final provider = detectProvider(apiKey);
    switch (provider) {
      case AiProvider.anthropic:
        return _anthropic(apiKey, model, systemPrompt, history);
      case AiProvider.google:
        return _google(apiKey, model, systemPrompt, history);
    }
  }

  static AiProvider detectProvider(String key) {
    final k = key.trim();
    if (k.startsWith('AIza') || k.startsWith('AQ')) return AiProvider.google;
    return AiProvider.anthropic; // sk-ant-... and default
  }

  static String defaultModel(String key) =>
      detectProvider(key) == AiProvider.google
          ? 'gemini-2.5-flash'
          : 'claude-haiku-4-5';

  static String providerLabel(String key) =>
      detectProvider(key) == AiProvider.google ? 'Gemini' : 'Claude';

  // --- Anthropic Messages API ------------------------------------------------
  Future<String> _anthropic(String apiKey, String model, String system,
      List<AiMessage> history) async {
    final res = await _client.post(
      Uri.parse('https://api.anthropic.com/v1/messages'),
      headers: {
        'content-type': 'application/json',
        'x-api-key': apiKey.trim(),
        'anthropic-version': '2023-06-01',
        'anthropic-dangerous-direct-browser-access': 'true',
      },
      body: jsonEncode({
        'model': model,
        'max_tokens': 1024,
        'system': system,
        'messages': history
            .map((m) => {'role': m.isUser ? 'user' : 'assistant', 'content': m.content})
            .toList(),
      }),
    );
    if (res.statusCode != 200) {
      throw AiException(_error(res.body, res.statusCode));
    }
    final json = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
    final content = json['content'] as List<dynamic>?;
    final text = content
        ?.whereType<Map>()
        .where((b) => b['type'] == 'text')
        .map((b) => b['text'].toString())
        .join('\n');
    if (text == null || text.isEmpty) throw AiException('Порожня відповідь Claude');
    return text.trim();
  }

  // --- Google Gemini generateContent ----------------------------------------
  Future<String> _google(String apiKey, String model, String system,
      List<AiMessage> history) async {
    final res = await _client.post(
      Uri.parse(
          'https://generativelanguage.googleapis.com/v1beta/models/$model:generateContent'),
      headers: {
        'content-type': 'application/json',
        'x-goog-api-key': apiKey.trim(),
      },
      body: jsonEncode({
        'systemInstruction': {
          'parts': [
            {'text': system}
          ]
        },
        'contents': history
            .map((m) => {
                  'role': m.isUser ? 'user' : 'model',
                  'parts': [
                    {'text': m.content}
                  ]
                })
            .toList(),
        'generationConfig': {'maxOutputTokens': 1024},
      }),
    );
    if (res.statusCode != 200) {
      throw AiException(_error(res.body, res.statusCode));
    }
    final json = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
    final cands = json['candidates'] as List<dynamic>?;
    final parts = cands != null && cands.isNotEmpty
        ? ((cands.first as Map)['content']?['parts'] as List<dynamic>?)
        : null;
    final text = parts?.map((p) => (p as Map)['text']?.toString() ?? '').join();
    if (text == null || text.trim().isEmpty) {
      throw AiException('Порожня відповідь Gemini');
    }
    return text.trim();
  }

  String _error(String body, int code) {
    try {
      final j = jsonDecode(body);
      final msg = j is Map
          ? (j['error'] is Map ? j['error']['message'] : j['error'])
          : null;
      return 'API $code: ${msg ?? body}';
    } catch (_) {
      return 'API $code';
    }
  }

  void dispose() => _client.close();
}

enum AiProvider { anthropic, google }

class AiException implements Exception {
  final String message;
  AiException(this.message);
  @override
  String toString() => message;
}
