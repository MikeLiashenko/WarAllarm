import 'package:flutter/material.dart';

import '../data/ukraine_geo.dart';
import '../domain/motion.dart';
import '../domain/situation.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import 'glass.dart';
import 'ui_kit.dart';

/// 8-point compass in Ukrainian («ПнСх») for a bearing in degrees.
String compassUa(double deg) {
  const names = ['Пн', 'ПнСх', 'Сх', 'ПдСх', 'Пд', 'ПдЗх', 'Зх', 'ПнЗх'];
  return names[(((deg % 360) + 22.5) ~/ 45) % 8];
}

/// Typical flight height of a target type — what to expect overhead.
String typicalAltitude(MapTarget t) {
  final l = t.label.toLowerCase();
  return switch (t.cat) {
    'shahed' when l.contains('реакт') => '2–8 км',
    'shahed' => '0,1–2 км',
    'missile' when l.contains('циркон') || l.contains('онікс') || l.contains('х-22') || l.contains('х-32') =>
      'висока, пікірує на ціль',
    'missile' => '30–200 м над землею',
    'balist' => 'апогей до ~50 км',
    'kab' => 'скид з 10–14 км, планує',
    'avia' => '5–12 км',
    'fpv' => 'до 0,5 км, низько',
    _ => 'невідомо',
  };
}

/// Everything known about one target + follow / show buttons. Shows a «gone»
/// state when the target left the feed (shot down, arrived or all-clear).
class TargetCard extends StatelessWidget {
  const TargetCard({
    super.key,
    required this.target,
    required this.place,
    required this.following,
    required this.onFollow,
    required this.onShow,
    required this.onClose,
    required this.stateTs,
  });

  final MapTarget? target;
  final MonitoredPlace place;
  final bool following;
  final ValueChanged<bool> onFollow;
  final VoidCallback onShow;
  final VoidCallback onClose;
  final int stateTs;

  @override
  Widget build(BuildContext context) {
    final t = target;
    if (t == null) {
      return Glass(
        radius: 22,
        padding: const EdgeInsets.fromLTRB(16, 14, 8, 14),
        child: Row(children: [
          Icon(Icons.radar_rounded, color: AppColors.textMuted),
          const SizedBox(width: 10),
          Expanded(
            child: Text('Ціль зникла з радарів — збита, долетіла або відбій.',
                style: TextStyle(fontSize: 13.5, color: AppColors.textPrimary)),
          ),
          IconButton(onPressed: onClose, icon: const Icon(Icons.close_rounded)),
        ]),
      );
    }

    final now = DateTime.now().millisecondsSinceEpoch;
    final m = motionOf(t, now);
    final color = kThreatColors[t.cat] ?? kThreatColors['other']!;
    final km = kmBetween(m.lat, m.lon, place.lat, place.lon);
    final kmh = speedKmPerMin(t) * 60;
    final threat = (kRank[t.stage] ?? 0) >= 3;
    final seen = DateTime.fromMillisecondsSinceEpoch(t.seenMs > 0 ? t.seenMs : stateTs);
    final ageMin = DateTime.now().difference(seen).inMinutes;
    String two(int v) => v.toString().padLeft(2, '0');

    // Arrival: flights — time left to impact; else straight-line at its speed.
    String eta;
    if (m.isFlight) {
      final left = (m.flightMin! * (1 - m.progress!));
      eta = m.landed ? 'долетіла' : (left < 1 ? '<1 хв до удару' : '~${left.round()} хв до удару');
    } else if (m.launch) {
      eta = 'ціль невідома';
    } else {
      final e = km / kmh * 60;
      eta = e < 1 ? '<1 хв' : '~${e.round()} хв';
    }

    final rows = <(IconData, String, String)>[
      (Icons.place_outlined, m.quietMin >= 3 ? 'Востаннє бачили' : 'Де зараз',
          m.launch ? 'пуск: ${t.fromName}' : (t.place.isEmpty ? '—' : t.place)),
      if (m.uncertKm > 0)
        (Icons.blur_circular_rounded, 'Тиша', '${m.quietMin.round()} хв без даних — може бути в радіусі ~${m.uncertKm.round()} км'),
      (Icons.straighten_rounded, 'До «${place.name}»', '${km.round()} км'),
      (Icons.timer_outlined, 'Підліт', eta),
      (Icons.speed_rounded, 'Швидкість', '${kmh.round()} км/год'),
      if (!m.launch && (t.heading != null || m.isFlight))
        (Icons.explore_outlined, 'Курс',
            t.headingKnown || m.isFlight
                ? '${compassUa(m.heading)} · ${m.heading.round()}°'
                : 'невідомий — бачили лише в одній точці'),
      if (t.fromName.isNotEmpty && !m.launch)
        (Icons.call_made_rounded, 'Звідки', '${t.fromName}${t.fromGuess ? ' (оцінка)' : ''}'),
      (Icons.height_rounded, 'Висота',
          altitudeShort(t).isNotEmpty ? '${altitudeShort(t)} (з каналу)' : '${typicalAltitude(t)} (типово)'),
      if (t.sources > 0)
        (Icons.podcasts_rounded, 'Джерела (${t.sources})', t.srcs.map((s) => '@$s').join(', ')),
      (Icons.update_rounded, 'Оновлено',
          '${two(seen.hour)}:${two(seen.minute)}:${two(seen.second)}${ageMin >= 1 ? ' · $ageMin хв тому' : ''}'),
    ];

    return Glass(
      radius: 24,
      padding: const EdgeInsets.fromLTRB(16, 14, 10, 14),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Container(
                width: 42,
                height: 42,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: const Color(0xF0101419),
                  shape: BoxShape.circle,
                  border: Border.all(color: threat ? AppColors.threat : color, width: 2),
                ),
                child: ThreatGlyph(t.cat, size: 24, heading: m.heading),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${t.label}${t.amount > 1 ? '  ×${t.amount}' : ''}',
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w800,
                            color: threat ? AppColors.threatDark : AppColors.textPrimary)),
                    const SizedBox(height: 4),
                    Wrap(spacing: 6, runSpacing: 4, children: [
                      if (t.stageLabel.isNotEmpty)
                        Tag(t.stageLabel, color: threat ? AppColors.threat : AppColors.textSecondary),
                      if (t.sources > 1) Tag('✓ ${t.sources} джерела', color: AppColors.safe),
                      if (following) Tag('👁 стежу', color: AppColors.highlight),
                      if (t.down) Tag(t.altNote.isEmpty ? 'падає' : t.altNote, color: AppColors.threat),
                    ]),
                  ],
                ),
              ),
              IconButton(
                tooltip: 'Закрити',
                onPressed: onClose,
                icon: Icon(Icons.close_rounded, color: AppColors.textSecondary),
              ),
            ],
          ),
          if (m.isFlight) ...[
            const SizedBox(height: 10),
            ClipRRect(
              borderRadius: BorderRadius.circular(4),
              child: LinearProgressIndicator(
                value: m.progress,
                minHeight: 6,
                color: color,
                backgroundColor: AppColors.textMuted.withValues(alpha: 0.2),
              ),
            ),
            const SizedBox(height: 4),
            Text('політ ${(m.progress! * 100).round()}% · ${m.flightMin!.toStringAsFixed(1)} хв загалом',
                style: TextStyle(fontSize: 11.5, color: AppColors.textMuted, fontFeatures: AppText.tabular)),
          ],
          const SizedBox(height: 10),
          for (final r in rows)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 3),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(r.$1, size: 16, color: AppColors.textMuted),
                  const SizedBox(width: 8),
                  SizedBox(
                    width: 112,
                    child: Text(r.$2,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12.5, color: AppColors.textSecondary)),
                  ),
                  Expanded(
                    child: Text(r.$3,
                        style: TextStyle(
                            fontSize: 12.5,
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary,
                            fontFeatures: AppText.tabular)),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: FilledButton.icon(
                  onPressed: () => onFollow(!following),
                  style: FilledButton.styleFrom(
                    backgroundColor: following ? AppColors.highlight : AppColors.primary,
                    foregroundColor: following ? Colors.black : Colors.white,
                    padding: const EdgeInsets.symmetric(vertical: 12),
                  ),
                  icon: Icon(following ? Icons.visibility_off_outlined : Icons.visibility_outlined, size: 18),
                  label: Text(following ? 'Не стежити' : 'Стежити'),
                ),
              ),
              const SizedBox(width: 8),
              OutlinedButton.icon(
                onPressed: onShow,
                style: OutlinedButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 14)),
                icon: const Icon(Icons.center_focus_strong_outlined, size: 18),
                label: const Text('Показати'),
              ),
              const SizedBox(width: 6),
            ],
          ),
        ],
      ),
    );
  }
}
