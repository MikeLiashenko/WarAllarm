/// A place the user monitors (home, guest city, saved or user-added).
/// Ported from the `allPlaces()` objects in the Reader's map.html.
class MonitoredPlace {
  final String id;
  final String icon;
  final String name;
  final String sub;
  final double lat;
  final double lon;
  final String? zone;
  final bool paused;

  /// The bell in «Мої місця»: off → this place never sounds the siren.
  final bool notify;

  const MonitoredPlace({
    required this.id,
    required this.icon,
    required this.name,
    required this.lat,
    required this.lon,
    this.sub = '',
    this.zone,
    this.paused = false,
    this.notify = true,
  });

  MonitoredPlace copyWith({bool? paused}) => MonitoredPlace(
        id: id,
        icon: icon,
        name: name,
        sub: sub,
        lat: lat,
        lon: lon,
        zone: zone,
        paused: paused ?? this.paused,
        notify: notify,
      );
}
