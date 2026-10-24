// The bell in «Мої місця» decides which places sound the siren — not the place
// being viewed. Regression: 603-й (bell off) rang while Теремки (bell on) was
// the one the user wanted to be warned about.
import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:waralarm/controllers/alarm_controller.dart';
import 'package:waralarm/controllers/map_data_controller.dart';
import 'package:waralarm/controllers/settings_controller.dart';
import 'package:waralarm/services/alarm_service.dart';
import 'package:waralarm/services/local_store.dart';
import 'package:waralarm/services/rtdb_service.dart';

class _FakeRtdb extends RtdbService {
  final ctrl = StreamController<dynamic>.broadcast();
  @override
  Stream<dynamic> stream(String path) => ctrl.stream;
}

class _FakeSiren implements AlarmService {
  int starts = 0;
  @override
  Future<void> startSiren({required bool sound, required bool vibrate, double? volume, String? cat, bool impact = false}) async =>
      starts++;
  @override
  Future<void> stopAll() async {}
  @override
  Future<void> setVolume(double v) async {}
  @override
  dynamic noSuchMethod(Invocation i) => Future<void>.value();
}

Map<String, dynamic> _drone(double lat, double lon, String id) => {
      'id': id, 'label': 'Шахед', 'cat': 'shahed', 'lat': lat, 'lon': lon,
      'stage': 'Approaching', 'kmh': 185, 'heading': 0,
      'seen': DateTime.now().millisecondsSinceEpoch,
    };

void main() {
  late _FakeRtdb rtdb;
  late MapDataController map;
  late AlarmController alarm;
  late _FakeSiren siren;

  setUp(() async {
    SharedPreferences.setMockInitialValues({
      'show_demo': false,
      // The user views 603-й, but only Теремки has the bell on.
      'sel_place': 'u1',
      'saved_places': jsonEncode([
        {'id': 'a', 'name': 'Теремки', 'region': '', 'lat': 50.370, 'lng': 30.459, 'notify': true},
        {'id': 'b', 'name': '603-й мікрорайон', 'region': '', 'lat': 50.066, 'lng': 36.316, 'notify': false},
      ]),
    });
    final store = await LocalStore.create();
    rtdb = _FakeRtdb();
    map = MapDataController(rtdb, store)..start();
    siren = _FakeSiren();
    alarm = AlarmController(map, SettingsController(store), service: siren);
  });

  tearDown(() {
    alarm.dispose();
    map.dispose();
  });

  Future<void> publish(List<Map<String, dynamic>> threats) async {
    rtdb.ctrl.add({'ts': DateTime.now().millisecondsSinceEpoch, 'threats': threats});
    await Future<void>.delayed(Duration.zero);
  }

  test('a drone over muted 603-й does not sound the siren', () async {
    await publish([_drone(50.07, 36.32, 'x')]);
    expect(alarm.level, isNot('alarm'));
    expect(siren.starts, 0);
  });

  test('a drone over Теремки (bell on) sounds it, even while viewing 603-й', () async {
    await publish([_drone(50.372, 30.46, 'y')]);
    expect(alarm.level, 'alarm');
    expect(siren.starts, 1);
  });
}
