import 'dart:convert';

import 'package:http/http.dart' as http;

import '../models/ai_session.dart';

/// Runs a tool by [name] with decoded [args]; returns a JSON-serialisable map.
typedef ToolRunner = Future<Map<String, dynamic>> Function(
    String name, Map<String, dynamic> args);

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

  /// Like [complete], but gives the model a set of [tools] it can call to read
  /// live data and change the app. Runs the multi-turn tool loop and returns the
  /// final natural-language reply. [runTool] actually executes each call.
  Future<String> completeWithTools({
    required String apiKey,
    required String model,
    required String systemPrompt,
    required List<AiMessage> history,
    required List<Map<String, dynamic>> tools,
    required ToolRunner runTool,
    int maxRounds = 6,
  }) async {
    final provider = detectProvider(apiKey);
    switch (provider) {
      case AiProvider.anthropic:
        return _anthropicTools(
            apiKey, model, systemPrompt, history, tools, runTool, maxRounds);
      case AiProvider.google:
        return _googleTools(
            apiKey, model, systemPrompt, history, tools, runTool, maxRounds);
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

  // --- Anthropic tool-use loop ----------------------------------------------
  Future<String> _anthropicTools(
    String apiKey,
    String model,
    String system,
    List<AiMessage> history,
    List<Map<String, dynamic>> tools,
    ToolRunner runTool,
    int maxRounds,
  ) async {
    final messages = <Map<String, dynamic>>[
      for (final m in history)
        {'role': m.isUser ? 'user' : 'assistant', 'content': m.content},
    ];
    for (var round = 0; round < maxRounds; round++) {
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
          'tools': tools,
          'messages': messages,
        }),
      );
      if (res.statusCode != 200) {
        throw AiException(_error(res.body, res.statusCode));
      }
      final json =
          jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
      final content = (json['content'] as List<dynamic>?) ?? const [];
      final toolUses = content
          .whereType<Map>()
          .where((b) => b['type'] == 'tool_use')
          .toList();
      if (json['stop_reason'] == 'tool_use' && toolUses.isNotEmpty) {
        messages.add({'role': 'assistant', 'content': content});
        final results = <Map<String, dynamic>>[];
        for (final u in toolUses) {
          final out = await runTool(u['name'].toString(),
              Map<String, dynamic>.from(u['input'] as Map? ?? const {}));
          results.add({
            'type': 'tool_result',
            'tool_use_id': u['id'],
            'content': jsonEncode(out),
          });
        }
        messages.add({'role': 'user', 'content': results});
        continue;
      }
      final text = content
          .whereType<Map>()
          .where((b) => b['type'] == 'text')
          .map((b) => b['text'].toString())
          .join('\n')
          .trim();
      return text.isEmpty ? '(готово)' : text;
    }
    return 'Забагато кроків поспіль — зупинив ланцюжок дій.';
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

  // --- Gemini function-calling loop -----------------------------------------
  Future<String> _googleTools(
    String apiKey,
    String model,
    String system,
    List<AiMessage> history,
    List<Map<String, dynamic>> tools,
    ToolRunner runTool,
    int maxRounds,
  ) async {
    // Gemini calls the schema field "parameters" and omits empty ones.
    final decls = tools.map((t) {
      final schema = t['input_schema'] as Map<String, dynamic>;
      final props = schema['properties'] as Map? ?? const {};
      return {
        'name': t['name'],
        'description': t['description'],
        if (props.isNotEmpty) 'parameters': schema,
      };
    }).toList();
    final contents = <Map<String, dynamic>>[
      for (final m in history)
        {
          'role': m.isUser ? 'user' : 'model',
          'parts': [
            {'text': m.content}
          ]
        },
    ];
    for (var round = 0; round < maxRounds; round++) {
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
          'tools': [
            {'functionDeclarations': decls}
          ],
          'contents': contents,
          'generationConfig': {'maxOutputTokens': 1024},
        }),
      );
      if (res.statusCode != 200) {
        throw AiException(_error(res.body, res.statusCode));
      }
      final json =
          jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
      final cands = json['candidates'] as List<dynamic>?;
      if (cands == null || cands.isEmpty) {
        throw AiException('Порожня відповідь Gemini');
      }
      final parts =
          ((cands.first as Map)['content']?['parts'] as List<dynamic>?) ??
              const [];
      final calls = parts
          .whereType<Map>()
          .where((p) => p['functionCall'] != null)
          .toList();
      if (calls.isNotEmpty) {
        contents.add({'role': 'model', 'parts': parts});
        final responses = <Map<String, dynamic>>[];
        for (final c in calls) {
          final fc = c['functionCall'] as Map;
          final out = await runTool(fc['name'].toString(),
              Map<String, dynamic>.from(fc['args'] as Map? ?? const {}));
          responses.add({
            'functionResponse': {'name': fc['name'], 'response': out}
          });
        }
        contents.add({'role': 'user', 'parts': responses});
        continue;
      }
      final text = parts
          .whereType<Map>()
          .map((p) => p['text']?.toString() ?? '')
          .join()
          .trim();
      return text.isEmpty ? '(готово)' : text;
    }
    return 'Забагато кроків поспіль — зупинив ланцюжок дій.';
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
