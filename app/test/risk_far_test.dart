import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/domain/probability.dart';
import 'package:waralarm/domain/situation.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/models/monitored_place.dart';

// Стадія цілі («на підльоті», «над районом», «приліт») — щодо міста, над яким вона,
// а не щодо твого місця. Шахед «над районом» за 233 км — не тривога.
const kyiv = MonitoredPlace(id: 'ky', icon: '🏠', name: 'Київ', lat: 50.45, lon: 30.52, zone: 'UA');

MapTarget at(double km, String cat, String stage, {double? heading}) => MapTarget(
    label: cat, cat: cat, lat: 50.45, lon: 30.52 + km / (111.2 * 0.637), stage: stage, zone: 'UA', heading: heading);

void main() {
  test('far shahed over its own town does not raise the alarm score', () {
    for (final st in ['Imminent', 'Overhead', 'Impact']) {
      expect(riskOf(kyiv, [at(233, 'shahed', st)]).score, lessThan(35), reason: st);
      expect(probabilityFor(kyiv, [at(233, 'shahed', st)]).percent, lessThan(10), reason: st);
    }
  });
  test('far ballistic is not 84% for every town', () {
    expect(probabilityFor(kyiv, [at(300, 'balist', 'Imminent')]).percent, lessThan(10));
  });
  test('far drones heading this way do not add up to an alarm', () {
    final many = List.generate(8, (_) => at(233, 'shahed', 'Approaching', heading: 270));
    expect(probabilityFor(kyiv, many).percent, lessThan(35));
  });
  test('the same stage nearby still alarms', () {
    expect(riskOf(kyiv, [at(5, 'shahed', 'Overhead')]).score, greaterThanOrEqualTo(55));
    expect(probabilityFor(kyiv, [at(5, 'shahed', 'Overhead')]).percent, greaterThanOrEqualTo(55));
  });
}
