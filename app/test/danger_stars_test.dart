import 'package:flutter_test/flutter_test.dart';
import 'package:waralarm/domain/danger_stars.dart';
import 'package:waralarm/models/map_target.dart';
import 'package:waralarm/models/monitored_place.dart';

// ⭐ Ті самі правила, що в Reader DangerStars.cs.
const kharkiv = MonitoredPlace(id: 'kh', icon: '🏠', name: 'Харків', lat: 50.0, lon: 36.23);

/// Ціль на [km] км на північ від Харкова.
MapTarget at(double km, String cat, {double kmh = 0, String stage = 'Approaching', int n = 1}) =>
    MapTarget(label: cat, cat: cat, lat: 50.0 + km / 111.2, lon: 36.23, kmh: kmh, stage: stage, amount: n);

int stars(List<MapTarget> t, {String? official, String? ball}) =>
    dangerStarsFor(kharkiv, t, officialLevel: official, ballisticFrom: ball).stars;

void main() {
  test('calm without threats, 1 star under an official alarm', () {
    expect(stars([]), 0);
    expect(stars([], official: 'red'), 1);
  });
  test('shahed: 1 far, 2 near, nothing beyond reach', () {
    expect(stars([at(30, 'shahed', kmh: 180)]), 1);
    expect(stars([at(8, 'shahed', kmh: 180)]), 2);
    expect(stars([at(60, 'shahed', kmh: 180)]), 0);
  });
  test('jet shahed is one star more', () {
    expect(stars([at(40, 'shahed', kmh: 500)]), 2);
    expect(stars([at(10, 'shahed', kmh: 500)]), 3);
  });
  test('kab, missile, ballistic', () {
    expect(stars([at(40, 'kab')]), 3);
    expect(stars([at(10, 'kab')]), 4);
    expect(stars([at(60, 'missile')]), 3);
    expect(stars([at(150, 'balist')]), 4);
    expect(stars([at(50, 'balist')]), 5);
  });
  test('mass raid +1, passed -1, impact +1', () {
    expect(stars([at(30, 'shahed', kmh: 180, n: 6)]), 2);
    expect(stars([at(8, 'shahed', kmh: 180, stage: 'Passed')]), 1);
    expect(stars([at(5, 'kab', stage: 'Impact')]), 5);
  });
  test('ballistic threat in the oblast is at least 4', () {
    expect(stars([], ball: 'Бєлгород'), 4);
    expect(stars([at(50, 'balist')], ball: ''), 5);
  });
}
