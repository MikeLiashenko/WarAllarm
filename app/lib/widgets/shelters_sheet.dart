import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../services/shelters_service.dart';
import '../theme/app_colors.dart';

/// Opens a bottom sheet listing the nearest shelters/metro around [lat],[lon].
Future<void> showSheltersSheet(
    BuildContext context, double lat, double lon, String placeName) {
  return showModalBottomSheet(
    context: context,
    backgroundColor: AppColors.surface,
    isScrollControlled: true,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
    ),
    builder: (_) => _SheltersSheet(lat: lat, lon: lon, placeName: placeName),
  );
}

class _SheltersSheet extends StatefulWidget {
  const _SheltersSheet(
      {required this.lat, required this.lon, required this.placeName});
  final double lat, lon;
  final String placeName;

  @override
  State<_SheltersSheet> createState() => _SheltersSheetState();
}

class _SheltersSheetState extends State<_SheltersSheet> {
  final _svc = SheltersService();
  List<Shelter>? _list;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final r = await _svc.near(widget.lat, widget.lon);
      if (mounted) setState(() => _list = r);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(
          left: 18, right: 18, top: 14,
          bottom: MediaQuery.of(context).viewInsets.bottom + 18),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Center(
            child: Container(
              width: 40, height: 4,
              decoration: BoxDecoration(
                color: AppColors.divider,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Icon(Icons.home_work_rounded, color: AppColors.primaryDark),
              const SizedBox(width: 10),
              Expanded(
                child: Text('Найближчі укриття · ${widget.placeName}',
                    style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w800,
                        color: AppColors.textPrimary)),
              ),
            ],
          ),
          const SizedBox(height: 14),
          _body(),
          const SizedBox(height: 12),
          Text(
            '⚠️ Дані з OpenStreetMap, можуть бути неповні. Найнадійніше — '
            'підземка/підвали, правило двох стін. Знай своє укриття заздалегідь.',
            style: TextStyle(fontSize: 12, color: AppColors.textMuted, height: 1.4),
          ),
        ],
      ),
    );
  }

  Widget _body() {
    if (_error != null) {
      return _note('Не вдалося отримати дані зараз. Перевір інтернет і спробуй ще.');
    }
    if (_list == null) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 28),
        child: Center(child: CircularProgressIndicator()),
      );
    }
    if (_list!.isEmpty) {
      return _note('Поблизу не знайшов позначених укриттів. Найнадійніше — '
          'підземка, підвали, паркінги.');
    }
    return Column(
      children: [
        for (final s in _list!)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Material(
              color: AppColors.background,
              borderRadius: BorderRadius.circular(14),
              child: InkWell(
                borderRadius: BorderRadius.circular(14),
                onTap: () => _open(s),
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                  child: Row(
                    children: [
                      Container(
                        width: 40, height: 40,
                        decoration: BoxDecoration(
                          color: AppColors.primary.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(11),
                        ),
                        child: Icon(Icons.shield_rounded,
                            color: AppColors.primary, size: 20),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(s.name,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                    fontSize: 14.5,
                                    fontWeight: FontWeight.w700,
                                    color: AppColors.textPrimary)),
                            Text('${s.meters} м · маршрут пішки',
                                style: TextStyle(
                                    fontSize: 12.5,
                                    color: AppColors.textSecondary)),
                          ],
                        ),
                      ),
                      Icon(Icons.directions_walk_rounded,
                          color: AppColors.primaryDark),
                    ],
                  ),
                ),
              ),
            ),
          ),
      ],
    );
  }

  Future<void> _open(Shelter s) async {
    final uri = Uri.parse(s.mapsUrl);
    try {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    } catch (_) {}
  }

  Widget _note(String text) => Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppColors.primarySoft.withValues(alpha: 0.4),
          borderRadius: BorderRadius.circular(14),
        ),
        child: Text(text,
            style: TextStyle(fontSize: 13, color: AppColors.textSecondary)),
      );
}
