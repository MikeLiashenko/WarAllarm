import 'package:flutter/material.dart';

/// Static geographic data for the Ukraine threat map, ported 1:1 from
/// `src/Reader/MapWriter.cs` so the Flutter map matches the website.

/// A city dot with a detail tier: 0 = major (always), 1 = oblast centre
/// (zoom ≥ 1.7), 2 = town (zoom ≥ 2.8).
class MapCity {
  final String name;
  final double lon;
  final double lat;
  final int tier;
  const MapCity(this.name, this.lon, this.lat, this.tier);
}

const List<MapCity> kCities = [
  MapCity('Київ', 30.52, 50.45, 0),
  MapCity('Харків', 36.23, 49.99, 0),
  MapCity('Дніпро', 35.05, 48.47, 0),
  MapCity('Одеса', 30.73, 46.48, 0),
  MapCity('Львів', 24.03, 49.84, 0),
  MapCity('Запоріжжя', 35.14, 47.84, 0),
  MapCity('Донецьк', 37.80, 48.00, 0),
  MapCity('Миколаїв', 31.99, 46.98, 0),
  MapCity('Полтава', 34.55, 49.59, 1),
  MapCity('Суми', 34.80, 50.91, 1),
  MapCity('Чернігів', 31.29, 51.50, 1),
  MapCity('Черкаси', 32.06, 49.44, 1),
  MapCity('Житомир', 28.66, 50.25, 1),
  MapCity('Вінниця', 28.47, 49.23, 1),
  MapCity('Хмельницький', 26.99, 49.42, 1),
  MapCity('Рівне', 26.25, 50.62, 1),
  MapCity('Луцьк', 25.34, 50.75, 1),
  MapCity('Тернопіль', 25.59, 49.55, 1),
  MapCity('Ів.-Франківськ', 24.71, 48.92, 1),
  MapCity('Ужгород', 22.29, 48.62, 1),
  MapCity('Чернівці', 25.94, 48.29, 1),
  MapCity('Кропивницький', 32.26, 48.51, 1),
  MapCity('Кривий Ріг', 33.39, 47.91, 1),
  MapCity('Херсон', 32.61, 46.64, 1),
  MapCity('Маріуполь', 37.54, 47.10, 1),
  MapCity('Кременчук', 33.42, 49.07, 1),
  MapCity('Мелітополь', 35.37, 46.85, 1),
  MapCity('Бердянськ', 36.79, 46.76, 1),
  MapCity('Чугуїв', 36.68, 49.84, 2),
  MapCity('Ізюм', 37.25, 49.21, 2),
  MapCity('Лозова', 36.32, 48.89, 2),
  MapCity("Куп'янськ", 37.62, 49.71, 2),
  MapCity('Балаклія', 36.86, 49.46, 2),
  MapCity('Богодухів', 35.53, 50.16, 2),
  MapCity('Первомайський', 36.22, 49.39, 2),
  MapCity('Люботин', 35.94, 49.94, 2),
  MapCity('Дергачі', 36.12, 50.10, 2),
  MapCity('Мерефа', 36.05, 49.82, 2),
  MapCity('Пісочин', 36.03, 49.98, 2),
  MapCity('Вовчанськ', 36.94, 50.29, 2),
  MapCity('Золочів', 35.98, 50.28, 2),
  MapCity('Валки', 35.62, 49.83, 2),
  MapCity('Красноград', 35.44, 49.38, 2),
  MapCity('Бровари', 30.79, 50.51, 2),
  MapCity('Бориспіль', 30.95, 50.35, 2),
  MapCity('Біла Церква', 30.11, 49.80, 2),
  MapCity('Фастів', 29.92, 50.08, 2),
  MapCity('Ірпінь', 30.25, 50.52, 2),
  MapCity('Буча', 30.22, 50.54, 2),
  MapCity('Вишневе', 30.37, 50.39, 2),
  MapCity('Васильків', 30.31, 50.18, 2),
  MapCity('Обухів', 30.62, 50.11, 2),
  MapCity('Вишгород', 30.49, 50.58, 2),
  MapCity('Славутич', 30.75, 51.52, 2),
  MapCity('Переяслав', 31.45, 50.07, 2),
];

/// Category → blip colour (matches CSS vars in the website).
const Map<String, Color> kThreatColors = {
  'balist': Color(0xFFFF3B30),
  'missile': Color(0xFFFF7A3C),
  'shahed': Color(0xFFFF9F45),
  'kab': Color(0xFFC77DFF),
  'avia': Color(0xFFF4C550),
  'other': Color(0xFF9AA6B2),
};

/// A single vector part of a target silhouette.
class FigPart {
  final String d;
  final FigStyle style;
  const FigPart(this.d, [this.style = FigStyle.body]);
}

/// Rendering style for a silhouette part.
enum FigStyle { body, dark, light }

/// Style helpers.
bool figPartIsDark(FigPart p) => p.style == FigStyle.dark;
bool figPartIsLight(FigPart p) => p.style == FigStyle.light;
bool figPartIsBody(FigPart p) => p.style == FigStyle.body;

/// Detailed silhouettes (nose points up, 0° = North; rotated by heading).
/// Ported from `figParts()` in MapWriter.cs.
List<FigPart> figParts(String cat) {
  switch (cat) {
    case 'missile': // cruise missile: long body, straight mid wings, cross tail
      return const [
        FigPart('M0,-11 C1.2,-8.8 1.2,-6 1.2,7.5 C1.2,8.6 -1.2,8.6 -1.2,7.5 C-1.2,-6 -1.2,-8.8 0,-11 Z'),
        FigPart('M-8,0.2 L8,0.2 L8,1.5 L-8,1.5 Z'),
        FigPart('M-3.4,5.4 L3.4,5.4 L3.4,6.8 L-3.4,6.8 Z'),
        FigPart('M0,-11 C0.7,-9.6 0.7,-8.3 0,-7.9 C-0.7,-8.3 -0.7,-9.6 0,-11 Z', FigStyle.dark),
        FigPart('M-0.9,6.6 L0.9,6.6 L0.7,8.7 L-0.7,8.7 Z', FigStyle.dark),
      ];
    case 'balist': // ballistic: very long, sharp nose, no wings, small fins
      return const [
        FigPart('M0,-12.5 C0.9,-9 1,-4 1,7 L2.7,10.6 L0.9,9 L0.9,10.7 L-0.9,10.7 L-0.9,9 L-2.7,10.6 L-1,7 C-1,-4 -0.9,-9 0,-12.5 Z'),
        FigPart('M0,-12.5 C0.7,-10.6 0.7,-8.7 0,-8.1 C-0.7,-8.7 -0.7,-10.6 0,-12.5 Z', FigStyle.dark),
        FigPart('M-0.28,-8 L0.28,-8 L0.28,7 L-0.28,7 Z', FigStyle.dark),
      ];
    case 'kab': // KAB (UMPK): short thick body, mid wings, boxy tail
      return const [
        FigPart('M0,-6.6 C2.2,-6 2.2,-1 1.8,3.2 L1.8,5.2 L-1.8,5.2 L-1.8,3.2 C-2.2,-1 -2.2,-6 0,-6.6 Z'),
        FigPart('M-5.6,-0.4 L5.6,-0.4 L5.6,0.9 L-5.6,0.9 Z'),
        FigPart('M-2.8,4.6 L2.8,4.6 L2.3,7.1 L-2.3,7.1 Z'),
        FigPart('M0,-6.6 C1,-5.6 1,-4.4 0,-4 C-1,-4.4 -1,-5.6 0,-6.6 Z', FigStyle.dark),
      ];
    case 'avia': // aircraft: fuselage, swept wings, tailplane, canopy
      return const [
        FigPart('M0,-10.5 L1,-4.5 L1.3,6 L2.1,9.2 L0,8.2 L-2.1,9.2 L-1.3,6 L-1,-4.5 Z'),
        FigPart('M-1.1,-1 L-9,5 L-9,6.3 L-1.1,3.1 L1.1,3.1 L9,6.3 L9,5 L1.1,-1 Z'),
        FigPart('M-1,5.6 L-4.3,7.7 L-4.3,8.4 L-1,6.9 L1,6.9 L4.3,8.4 L4.3,7.7 L1,5.6 Z'),
        FigPart('M0,-6 C0.8,-6 0.9,-3.6 0,-3.2 C-0.9,-3.6 -0.8,-6 0,-6 Z', FigStyle.light),
      ];
    default: // Shahed-136 / target: delta wing, fuselage, twin fins, pusher prop
      return const [
        FigPart('M0,-9.5 L1.6,-7.5 L9.5,5.2 L1.3,4.2 L1.3,5.4 L-1.3,5.4 L-1.3,4.2 L-9.5,5.2 L-1.6,-7.5 Z'),
        FigPart('M-0.7,-8.6 L0.7,-8.6 L0.9,6.6 L-0.9,6.6 Z', FigStyle.dark),
        FigPart('M1.3,4.4 L3.2,6.7 L1.3,5.4 Z'),
        FigPart('M-1.3,4.4 L-3.2,6.7 L-1.3,5.4 Z'),
        FigPart('M-2.5,6.9 L2.5,6.9 L2.5,7.35 L-2.5,7.35 Z', FigStyle.light),
      ];
  }
}

/// Fixed on-screen scale per category (ported from figScale()).
double figScale(String cat) => switch (cat) {
      'balist' => 1.12,
      'missile' => 1.0,
      'avia' => 1.0,
      'kab' => 0.98,
      'shahed' => 0.9,
      _ => 0.8,
    };

/// Speed (km/min) used for dead-reckoning + course prediction (speedKmMin()).
double speedKmMin(String cat, String label) {
  final a = label.toLowerCase();
  if (cat == 'balist') return 67;
  if (cat == 'missile') return a.contains('циркон') ? 100 : 13;
  if (cat == 'kab') return 15;
  return a.contains('реакт') ? 8 : 3; // shahed ~3 km/min
}
