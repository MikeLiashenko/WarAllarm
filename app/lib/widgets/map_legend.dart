import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../theme/app_colors.dart';
import 'glass.dart';

/// What the map's colours and markers mean — targets by type (the map's own
/// silhouettes), official alarm fills, trajectories and your place.
class MapLegend extends StatelessWidget {
  const MapLegend({super.key, this.onClose});
  final VoidCallback? onClose;

  static const _kinds = [
    ('shahed', 'Шахед / БпЛА'),
    ('missile', 'Крилата ракета («ціль»)'),
    ('balist', 'Балістика'),
    ('kab', 'КАБ'),
    ('avia', 'Авіація'),
    ('other', 'Інше / тип невідомий'),
  ];

  @override
  Widget build(BuildContext context) {
    TextStyle t() => TextStyle(fontSize: 12.5, color: AppColors.textPrimary);
    Widget row(Widget icon, String label) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 3),
          child: Row(mainAxisSize: MainAxisSize.min, children: [
            SizedBox(width: 26, child: Center(child: icon)),
            const SizedBox(width: 8),
            Flexible(child: Text(label, style: t())),
          ]),
        );
    Widget fill(Color c) => Container(
          width: 18,
          height: 12,
          decoration: BoxDecoration(
            color: c.withValues(alpha: 0.6),
            borderRadius: BorderRadius.circular(3),
            border: Border.all(color: c),
          ),
        );
    Widget section(String s) => Padding(
          padding: const EdgeInsets.only(top: 8, bottom: 2),
          child: Text(s.toUpperCase(),
              style: TextStyle(
                  fontSize: 10, fontWeight: FontWeight.w800, letterSpacing: 1, color: AppColors.textMuted)),
        );

    return Glass(
      radius: 18,
      padding: const EdgeInsets.fromLTRB(14, 10, 10, 12),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 250),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(children: [
              Expanded(
                child: Text('Легенда',
                    style: TextStyle(fontSize: 14, fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
              ),
              if (onClose != null)
                InkResponse(
                  onTap: onClose,
                  radius: 16,
                  child: Icon(Icons.close_rounded, size: 18, color: AppColors.textSecondary),
                ),
            ]),
            section('Цілі'),
            for (final k in _kinds)
              row(ThreatGlyph(k.$1, size: 18, color: kThreatColors[k.$1]), k.$2),
            section('Тривоги'),
            row(fill(const Color(0xFFC0303F)), 'Повітряна тривога'),
            row(fill(const Color(0xFFD9B21F)), 'Часткова тривога / загроза'),
            section('Рух'),
            row(
                SizedBox(
                  width: 22,
                  child: CustomPaint(size: const Size(22, 2), painter: _DashPainter(AppColors.textSecondary)),
                ),
                'Курс / маршрут'),
            row(Icon(Icons.radio_button_unchecked_rounded, size: 16, color: kThreatColors['balist']),
                'Точка удару (балістика, КАБ)'),
            row(Icon(Icons.blur_circular_rounded, size: 16, color: kThreatColors['balist']),
                'Пуск: куди могла долетіти'),
            row(Icon(Icons.circle, size: 10, color: AppColors.primary), 'Ваше місце'),
            const SizedBox(height: 6),
            Text('«?» біля «з …» — район пуску оцінено, канал його не назвав.',
                style: TextStyle(fontSize: 11, height: 1.3, color: AppColors.textMuted)),
          ],
        ),
      ),
    );
  }
}

class _DashPainter extends CustomPainter {
  _DashPainter(this.color);
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final p = Paint()
      ..color = color
      ..strokeWidth = 2;
    for (var x = 0.0; x < size.width; x += 7) {
      canvas.drawLine(Offset(x, size.height / 2), Offset(x + 4, size.height / 2), p);
    }
  }

  @override
  bool shouldRepaint(_DashPainter old) => old.color != color;
}
