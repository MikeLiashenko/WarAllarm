import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/places_controller.dart';
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
              label: const Text('Add place'),
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
    final nameCtl = TextEditingController();
    final regionCtl = TextEditingController();
    final latCtl = TextEditingController();
    final lngCtl = TextEditingController();

    showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: AppColors.surface,
        shape:
            RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        title: const Text('Add place'),
        content: SizedBox(
          width: 360,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: nameCtl,
                decoration: const InputDecoration(hintText: 'City name'),
              ),
              const SizedBox(height: 10),
              TextField(
                controller: regionCtl,
                decoration: const InputDecoration(hintText: 'Region / oblast'),
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: latCtl,
                      keyboardType: const TextInputType.numberWithOptions(
                          decimal: true, signed: true),
                      decoration: const InputDecoration(hintText: 'Latitude'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: TextField(
                      controller: lngCtl,
                      keyboardType: const TextInputType.numberWithOptions(
                          decimal: true, signed: true),
                      decoration: const InputDecoration(hintText: 'Longitude'),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          ElevatedButton(
            onPressed: () {
              final name = nameCtl.text.trim();
              if (name.isEmpty) return;
              controller.add(
                name: name,
                region: regionCtl.text.trim(),
                lat: double.tryParse(latCtl.text.trim()) ?? 49.9935,
                lng: double.tryParse(lngCtl.text.trim()) ?? 36.2304,
              );
              Navigator.pop(context);
            },
            child: const Text('Add'),
          ),
        ],
      ),
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
