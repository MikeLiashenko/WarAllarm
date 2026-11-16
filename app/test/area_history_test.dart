import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/domain/area_history.dart';
import 'package:waralarm/domain/analytics.dart';
import 'package:waralarm/models/monitored_place.dart';

// 📈 Історія тривог місця з areaLog (Reader AreaLog.cs).
const kharkiv = MonitoredPlace(id: 'kh', icon: '📍', name: 'Харків', lat: 49.9935, lon: 36.2304);

int m(DateTime t) => t.millisecondsSinceEpoch ~/ 60000;

void main() {
  final t0 = DateTime(2026, 11, 15, 8, 0);
  final node = {
    '2026-11-15': {
      'a': [
        ['Харківський район', m(t0), m(t0.add(const Duration(minutes: 90))), 2],
        // громада Харків — перекривається з районом: не друга тривога
        ['Харків', m(t0.add(const Duration(minutes: 30))), m(t0.add(const Duration(minutes: 120))), 2, 49.99, 36.23],
        ['Чугуївський район', m(t0), m(t0.add(const Duration(minutes: 60))), 2], // чужий район
        ['Харківський район', m(t0.add(const Duration(hours: 5))), m(t0.add(const Duration(hours: 5, minutes: 20))), 1],
      ],
      't': [
        [m(t0.add(const Duration(minutes: 5))), 'Drone', 'Харків', 49.99, 36.23],
        [m(t0.add(const Duration(minutes: 40))), 'Kab', 'Дергачі', 50.11, 36.12],
        [m(t0.add(const Duration(minutes: 50))), 'impact', 'Харків', 49.99, 36.23],
        [m(t0.add(const Duration(minutes: 50))), 'Drone', 'Суми', 50.9, 34.8], // далеко
      ],
    },
  };

  test('raion + nearby hromada merge into one alarm; other raions ignored', () {
    final rows = placeRows(kharkiv, node, raion: 'Харківський район');
    final alarms = rows.where((r) => r.tag == 'alarm').toList();
    expect(alarms.length, 2);
    expect(alarms[0].minutes, 120);
    expect(alarms[0].hour, 8);
    expect(alarms[0].kind, 'Kab'); // найнебезпечніше поруч під час тривоги
    expect(alarms[1].kind, 'Official');
    expect(alarms[1].level, 1);
    final impacts = rows.where((r) => r.isImpact).toList();
    expect(impacts.length, 1);
    expect(impacts[0].kind, 'Kab');
  });

  test('summary counts minutes under alarm', () {
    final rows = placeRows(kharkiv, node, raion: 'Харківський район');
    final s = AnalyticsSummary.compute(rows, 'all');
    expect(s.alarms, 2);
    expect(s.alarmMinutes, 140);
    expect(s.zones.first.$1, 'Харків');
  });

  test('no node — no rows', () {
    expect(placeRows(kharkiv, null), isEmpty);
  });
}
