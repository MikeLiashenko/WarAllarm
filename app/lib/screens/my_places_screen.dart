import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/alerts_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/places_controller.dart';
import '../controllers/settings_controller.dart';
import '../domain/motion.dart';
import '../domain/probability.dart';
import '../models/monitored_place.dart';
import '../data/places_gazetteer.dart';
import '../data/ukraine_geo.dart';
import '../models/saved_place.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import '../widgets/glass_container.dart';
import '../widgets/ui_kit.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

/// Everything searchable when adding a place: all ~5300 cities/towns/villages
/// (kCities, sorted big→small) plus our curated Kharkiv/Kyiv neighborhoods
/// (kGazetteer), de-duplicated by name.
final List<GeoPlace> _searchPlaces = () {
  final out = <GeoPlace>[];
  final seen = <String>{};
  for (final c in kCities) {
    if (seen.add(c.name.toLowerCase())) out.add(GeoPlace(c.name, c.lat, c.lon));
  }
  for (final g in kGazetteer) {
    if (seen.add(g.name.toLowerCase())) out.add(g);
  }
  return out;
}();

class MyPlacesScreen extends StatelessWidget {
  const MyPlacesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final controller = context.watch<PlacesController>();
    final places = controller.places;

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            title: 'Мої місця',
            subtitle: 'Міста й райони, за якими стежить тривога',
            icon: Icons.place_rounded,
            iconColor: AppColors.safe,
            trailing: FilledButton.icon(
              onPressed: () => _showAddDialog(context, controller),
              icon: const Icon(Icons.add, size: 18),
              label: const Text('Додати'),
            ),
          ),
          const SizedBox(height: 18),
          if (places.isEmpty)
            GlassContainer(
              padding: const EdgeInsets.symmetric(vertical: 46),
              child: Column(
                children: [
                  Icon(Icons.add_location_alt_outlined,
                      size: 40, color: AppColors.textMuted),
                  const SizedBox(height: 12),
                  Text('Ще немає збережених місць',
                      style: TextStyle(
                          fontWeight: FontWeight.w600,
                          color: AppColors.textPrimary)),
                  const SizedBox(height: 4),
                  Text('Додай місто чи район — і тривога стежитиме за ним.',
                      style: TextStyle(color: AppColors.textSecondary)),
                  const SizedBox(height: 16),
                  ElevatedButton(
                    onPressed: () => _showAddDialog(context, controller),
                    child: const Text('Додати перше місце'),
                  ),
                ],
              ),
            )
          else
            LayoutBuilder(
              builder: (context, c) {
                final columns = c.maxWidth > 760 ? 2 : 1;
                return GridView.builder(
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  itemCount: places.length,
                  gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                    crossAxisCount: columns,
                    mainAxisSpacing: 12,
                    crossAxisSpacing: 12,
                    mainAxisExtent: 124,
                  ),
                  itemBuilder: (context, i) =>
                      _PlaceCard(place: places[i], controller: controller),
                );
              },
            ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

  void _showAddDialog(BuildContext context, PlacesController controller) {
    showDialog<void>(
      context: context,
      builder: (context) => _AddPlaceDialog(controller: controller),
    );
  }
}

/// Add a place by typing a city name — coordinates are resolved automatically
/// from the built-in Ukrainian city list. No manual latitude/longitude.
class _AddPlaceDialog extends StatefulWidget {
  const _AddPlaceDialog({required this.controller});
  final PlacesController controller;

  @override
  State<_AddPlaceDialog> createState() => _AddPlaceDialogState();
}

class _AddPlaceDialogState extends State<_AddPlaceDialog> {
  final _ctl = TextEditingController();
  String _q = '';

  @override
  void dispose() {
    _ctl.dispose();
    super.dispose();
  }

  // Нормалізація для пошуку: прибираємо пробіли/дефіси/апострофи/«й», щоб
  // «603 мікрорайон», «603-мікрорайон», «603-й мкр» знаходили «603-й мікрорайон».
  static String _norm(String s) => s
      .toLowerCase()
      .replaceAll('мікрорайон', 'мкр')
      .replaceAll('-й', '')
      .replaceAll(RegExp(r"[\s\-'ʼ’]"), '');

  List<GeoPlace> get _matches {
    final q = _q.trim().toLowerCase();
    if (q.isEmpty) {
      // Biggest cities as a starting set.
      return _searchPlaces.take(12).toList();
    }
    final nq = _norm(q);
    final starts = <GeoPlace>[];
    final contains = <GeoPlace>[];
    for (final c in _searchPlaces) {
      final n = c.name.toLowerCase();
      if (n.startsWith(q)) {
        starts.add(c);
      } else if (n.contains(q) || (nq.length >= 2 && _norm(n).contains(nq))) {
        contains.add(c);
      }
      if (starts.length >= 30) break; // enough prefix matches
    }
    return [...starts, ...contains].take(20).toList();
  }

  void _pick(GeoPlace c) {
    widget.controller.add(name: c.name, region: '', lat: c.lat, lng: c.lon);
    Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final matches = _matches;
    return AlertDialog(
      backgroundColor: AppColors.surface,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      title: const Text('Додати місто'),
      content: SizedBox(
        width: 360,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: _ctl,
              autofocus: true,
              onChanged: (v) => setState(() => _q = v),
              decoration: const InputDecoration(
                hintText: 'Почни писати місто…',
                prefixIcon: Icon(Icons.search_rounded, size: 20),
              ),
            ),
            const SizedBox(height: 12),
            ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 260),
              child: matches.isEmpty
                  ? Padding(
                      padding: EdgeInsets.symmetric(vertical: 20),
                      child: Text(
                        'Не знайшов такого міста 🤔\nСпробуй іншу назву українською.',
                        textAlign: TextAlign.center,
                        style: TextStyle(color: AppColors.textSecondary),
                      ),
                    )
                  : ListView.separated(
                      shrinkWrap: true,
                      itemCount: matches.length,
                      separatorBuilder: (_, _) =>
                          Divider(height: 1, color: AppColors.divider),
                      itemBuilder: (context, i) {
                        final c = matches[i];
                        return ListTile(
                          contentPadding:
                              const EdgeInsets.symmetric(horizontal: 6),
                          leading: Icon(Icons.location_on_rounded,
                              color: AppColors.primary),
                          title: Text(c.name,
                              style: TextStyle(
                                  fontWeight: FontWeight.w600,
                                  color: AppColors.textPrimary)),
                          onTap: () => _pick(c),
                        );
                      },
                    ),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Скасувати'),
        ),
      ],
    );
  }
}

class _PlaceCard extends StatelessWidget {
  const _PlaceCard({required this.place, required this.controller});
  final SavedPlace place;
  final PlacesController controller;

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final alerts = context.watch<AlertsController>();
    final settings = context.watch<SettingsController>();
    final mp = MonitoredPlace(id: place.id, icon: '', name: place.name, lat: place.lat, lon: place.lng);
    final level = officialLevelFor(mp, alerts.oblastWhole, alerts.raions);
    final dists = data.isDemo
        ? <double>[]
        : (data.targets.map((t) => kmBetween(place.lat, place.lng, t.lat, t.lon)).toList()..sort());
    final near = dists.where((d) => d <= settings.alarmRadiusKm * 2).length;
    final nearest = dists.isEmpty ? null : dists.first;

    final (tone, status) = level == 'red'
        ? (AppColors.threat, 'Повітряна тривога')
        : level == 'yellow'
            ? (AppColors.warn, 'Тривога в частині області')
            : near > 0
                ? (AppColors.warn, 'Цілі поруч: $near')
                : (AppColors.safe, 'Спокійно');

    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: AppColors.surface,
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [
            Color.alphaBlend(tone.withValues(alpha: AppColors.isDark ? 0.10 : 0.06), AppColors.surface),
            AppColors.surface,
          ],
        ),
        borderRadius: BorderRadius.circular(AppRadii.xl - 2),
        border: Border.all(color: tone.withValues(alpha: level == null && near == 0 ? 0.18 : 0.45)),
      ),
      padding: const EdgeInsets.fromLTRB(16, 14, 8, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconTile(Icons.location_city_rounded, color: place.notify ? tone : AppColors.textMuted, size: 44),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      place.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w800,
                        letterSpacing: -0.3,
                        color: AppColors.textPrimary,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Row(
                      children: [
                        LiveDot(color: tone, size: 7, pulse: level != null || near > 0),
                        Flexible(
                          child: Text(status,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: tone)),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              IconButton(
                tooltip: place.notify ? 'Сповіщення увімкнено' : 'Сповіщення вимкнено',
                onPressed: () => controller.toggleNotify(place.id),
                style: IconButton.styleFrom(
                  backgroundColor: place.notify
                      ? AppColors.primary.withValues(alpha: 0.14)
                      : AppColors.surface2,
                ),
                icon: Icon(
                  place.notify ? Icons.notifications_active_rounded : Icons.notifications_off_outlined,
                  color: place.notify ? AppColors.primary : AppColors.textMuted,
                  size: 21,
                ),
              ),
              IconButton(
                tooltip: 'Видалити',
                onPressed: () => controller.remove(place.id),
                icon: Icon(Icons.delete_outline_rounded, color: AppColors.textMuted, size: 21),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              _chip(Icons.near_me_rounded,
                  nearest == null ? 'цілей немає' : 'найближча ${nearest.round()} км'),
              const SizedBox(width: 8),
              _chip(Icons.public_rounded, place.region.isEmpty ? 'Україна' : place.region),
            ],
          ),
        ],
      ),
    );
  }

  Widget _chip(IconData icon, String text) => Flexible(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
          decoration: BoxDecoration(
            color: AppColors.textPrimary.withValues(alpha: AppColors.isDark ? 0.05 : 0.04),
            borderRadius: BorderRadius.circular(8),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 13, color: AppColors.textMuted),
              const SizedBox(width: 5),
              Flexible(
                child: Text(text,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 12, color: AppColors.textSecondary, fontFeatures: AppText.tabular)),
              ),
            ],
          ),
        ),
      );
}
