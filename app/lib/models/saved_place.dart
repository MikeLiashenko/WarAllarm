/// A user-saved monitoring location (city / region).
class SavedPlace {
  final String id;
  final String name;
  final String region;
  final double lat;
  final double lng;
  final bool notify;

  const SavedPlace({
    required this.id,
    required this.name,
    required this.region,
    required this.lat,
    required this.lng,
    this.notify = true,
  });

  SavedPlace copyWith({String? name, String? region, bool? notify}) {
    return SavedPlace(
      id: id,
      name: name ?? this.name,
      region: region ?? this.region,
      lat: lat,
      lng: lng,
      notify: notify ?? this.notify,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'region': region,
        'lat': lat,
        'lng': lng,
        'notify': notify,
      };

  factory SavedPlace.fromJson(Map<String, dynamic> j) => SavedPlace(
        id: j['id'] as String,
        name: j['name'] as String,
        region: (j['region'] ?? '') as String,
        lat: (j['lat'] as num).toDouble(),
        lng: (j['lng'] as num).toDouble(),
        notify: (j['notify'] ?? true) as bool,
      );
}
