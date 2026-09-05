import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/places_controller.dart';
import '../data/ukraine_geo.dart';
import '../models/saved_place.dart';
import '../theme/app_colors.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

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
            title: 'My Places',
            subtitle: 'Pin and manage the regions you monitor',
            icon: Icons.place_rounded,
            trailing: ElevatedButton.icon(
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
                  const Icon(Icons.map_outlined,
                      size: 42, color: AppColors.textMuted),
                  const SizedBox(height: 12),
                  const Text('No saved places yet',
                      style: TextStyle(
                          fontWeight: FontWeight.w600,
                          color: AppColors.textPrimary)),
                  const SizedBox(height: 4),
                  const Text('Add a city or region to start monitoring it.',
                      style: TextStyle(color: AppColors.textSecondary)),
                  const SizedBox(height: 16),
                  ElevatedButton(
                    onPressed: () => _showAddDialog(context, controller),
                    child: const Text('Add your first place'),
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
                    mainAxisExtent: 116,
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

  List<MapCity> get _matches {
    final q = _q.trim().toLowerCase();
    if (q.isEmpty) {
      // Show the biggest cities as a starting point.
      return kCities.where((c) => c.tier == 0).toList();
    }
    final starts = <MapCity>[];
    final contains = <MapCity>[];
    for (final c in kCities) {
      final n = c.name.toLowerCase();
      if (n.startsWith(q)) {
        starts.add(c);
      } else if (n.contains(q)) {
        contains.add(c);
      }
    }
    return [...starts, ...contains].take(8).toList();
  }

  void _pick(MapCity c) {
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
                  ? const Padding(
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
                          const Divider(height: 1, color: AppColors.divider),
                      itemBuilder: (context, i) {
                        final c = matches[i];
                        return ListTile(
                          contentPadding:
                              const EdgeInsets.symmetric(horizontal: 6),
                          leading: const Icon(Icons.location_on_rounded,
                              color: AppColors.primary),
                          title: Text(c.name,
                              style: const TextStyle(
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
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.border),
        boxShadow: const [
          BoxShadow(
            color: Color(0x0A1B3A66),
            blurRadius: 18,
            offset: Offset(0, 8),
            spreadRadius: -8,
          ),
        ],
      ),
      child: Row(
        children: [
          Container(
            width: 46,
            height: 46,
            decoration: BoxDecoration(
              color: AppColors.primarySoft,
              borderRadius: BorderRadius.circular(14),
            ),
            child: const Icon(Icons.location_city_rounded,
                color: AppColors.primary),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  place.name,
                  style: const TextStyle(
                    fontSize: 15.5,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textPrimary,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  place.region.isEmpty
                      ? '${place.lat.toStringAsFixed(2)}, ${place.lng.toStringAsFixed(2)}'
                      : place.region,
                  style: const TextStyle(
                      fontSize: 12.5, color: AppColors.textSecondary),
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
          IconButton(
            tooltip: place.notify ? 'Notifications on' : 'Notifications off',
            onPressed: () => controller.toggleNotify(place.id),
            icon: Icon(
              place.notify
                  ? Icons.notifications_active_rounded
                  : Icons.notifications_off_rounded,
              color: place.notify ? AppColors.primary : AppColors.textMuted,
            ),
          ),
          IconButton(
            tooltip: 'Remove',
            onPressed: () => controller.remove(place.id),
            icon: const Icon(Icons.delete_outline_rounded,
                color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }
}
