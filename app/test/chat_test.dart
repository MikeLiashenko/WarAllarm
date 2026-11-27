// Chat: 13+ gate, profile step, and messages from the relay render (text, reply,
// reactions, own pending message, rejection reason from chat/res).
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:waralarm/controllers/chat_controller.dart';
import 'package:waralarm/screens/chat_screen.dart';
import 'package:waralarm/services/local_store.dart';
import 'package:waralarm/services/rtdb_service.dart';

class _FakeRtdb extends RtdbService {
  final streams = <String, StreamController<dynamic>>{};
  final pushed = <String, Map<String, dynamic>>{};
  int _n = 0;

  StreamController<dynamic> of(String path) => streams.putIfAbsent(path, () => StreamController<dynamic>.broadcast());

  @override
  Stream<dynamic> stream(String path) => of(path).stream;

  @override
  Future<String> push(String path, Map<String, dynamic> value) async {
    final id = '-req${_n++}';
    pushed[id] = {'path': path, ...value};
    return id;
  }

  @override
  Future<dynamic> getOnce(String path) async => null;
}

Future<(ChatController, _FakeRtdb)> _make(Map<String, Object> prefs) async {
  SharedPreferences.setMockInitialValues(prefs);
  final store = await LocalStore.create();
  final rtdb = _FakeRtdb();
  return (ChatController(rtdb, store, suggestedName: 'Guest'), rtdb);
}

Widget _app(ChatController c) => ChangeNotifierProvider<ChatController>.value(
      value: c,
      child: const MaterialApp(home: Scaffold(body: ChatScreen())),
    );

void main() {
  test('age is counted by birthday', () {
    final now = DateTime(2026, 11, 27);
    expect(ChatController.ageAt(DateTime(2013, 11, 27), now), 13);
    expect(ChatController.ageAt(DateTime(2013, 11, 28), now), 12);
    expect(ChatController.ageAt(DateTime(1990, 1, 1), now), 36);
  });

  test('gate: no date → age, under 13 → blocked, 13+ → profile, saved → chat', () async {
    final (c, rtdb) = await _make({});
    expect(c.gate, ChatGate.needAge);
    expect(c.uid.length, greaterThanOrEqualTo(16));
    final now = DateTime.now();
    await c.setBirthDate(DateTime(now.year - 10, 1, 1));
    expect(c.gate, ChatGate.tooYoung);

    final (c2, rtdb2) = await _make({});
    await c2.setBirthDate(DateTime(now.year - 20, 1, 1));
    expect(c2.gate, ChatGate.needProfile);
    expect(await c2.saveProfile(name: 'Олег', bio: '', city: 'Київ'), isTrue);
    expect(c2.gate, ChatGate.ready);
    final req = rtdb2.pushed.values.single;
    expect(req['t'], 'profile');
    expect(req['age13'], true);
    expect(req['path'], 'chat/in/${c2.uid}');
    expect((req['k'] as String).length, greaterThanOrEqualTo(24));
    expect(rtdb.pushed, isEmpty);
  });

  test('under 13: parental permission for this phone opens the chat', () async {
    final (c, rtdb) = await _make({});
    await c.setBirthDate(DateTime(2014, 11, 19));
    expect(c.gate, ChatGate.tooYoung);
    c.watchParentalPermission();
    rtdb.of('chat/kids/${c.uid}').add(true);
    await Future<void>.delayed(Duration.zero);
    expect(c.gate, ChatGate.needProfile);
    await c.saveProfile(name: 'Син', bio: '', city: '');
    expect(rtdb.pushed.values.last['age13'], false);
    expect(c.gate, ChatGate.ready);
    rtdb.of('chat/kids/${c.uid}').add(null); // permission withdrawn
    await Future<void>.delayed(Duration.zero);
    expect(c.gate, ChatGate.tooYoung);
  });

  testWidgets('age gate is shown first', (tester) async {
    final (c, _) = await _make({});
    await tester.pumpWidget(_app(c));
    expect(find.text('Перевірка віку'), findsOneWidget);
    expect(find.text('Продовжити'), findsOneWidget);
  });

  testWidgets('messages, replies, reactions and a rejected message render', (tester) async {
    final (c, rtdb) = await _make({
      'chat_dob': DateTime(2000, 1, 1).toIso8601String(),
      'chat_profile_saved': true,
      'chat_profile': '{"name":"Я"}',
    });
    await tester.binding.setSurfaceSize(const Size(500, 900));
    await tester.pumpWidget(_app(c));
    expect(c.gate, ChatGate.ready);
    final ts = DateTime.now().millisecondsSinceEpoch;
    rtdb.of('chat/profiles').add({
      'other000000000000000': {'name': 'Марія', 'age13': true, 'since': ts},
    });
    rtdb.of('chat/msgs').add({
      '-a1': {'uid': 'other000000000000000', 'name': 'Марія', 't': 'text', 'text': 'Чути вибухи на лівому?', 'ts': ts - 60000},
      '-a2': {
        'uid': 'other000000000000000',
        'name': 'Марія',
        't': 'text',
        'text': 'Вже тихо',
        'ts': ts - 30000,
        're': '-a1',
        'reName': 'Марія',
        'reText': 'Чути вибухи на лівому?',
        'rx': {
          'heart': {'x1': true, 'x2': true},
        },
      },
    });
    await tester.pump();
    expect(find.text('Чути вибухи на лівому?'), findsWidgets);
    expect(find.text('Вже тихо'), findsOneWidget);
    expect(find.text('❤️ 2'), findsOneWidget);
    expect(find.text('Марія'), findsWidgets);

    // Own message goes into the inbox and shows as pending.
    await tester.enterText(find.byType(TextField), 'Привіт усім');
    await tester.pump();
    await tester.tap(find.byIcon(Icons.send_rounded));
    await tester.pump();
    final id = rtdb.pushed.keys.last;
    expect(rtdb.pushed[id]!['t'], 'msg');
    expect(rtdb.pushed[id]!['text'], 'Привіт усім');
    expect(find.text('Привіт усім'), findsOneWidget);
    expect(find.byIcon(Icons.schedule_rounded), findsOneWidget);

    // The relay rejects it with a reason.
    rtdb.of('chat/res/${c.uid}').add({
      id: {'why': 'Забагато повідомлень — зачекай хвилинку', 'ts': ts},
    });
    await tester.pump();
    expect(find.textContaining('Забагато повідомлень'), findsOneWidget);
    expect(find.text('Повторити'), findsOneWidget);
    await tester.pump(const Duration(seconds: 30));
  });
}
