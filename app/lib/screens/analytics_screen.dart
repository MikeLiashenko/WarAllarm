import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/map_data_controller.dart';
import '../domain/analytics.dart';
import '../models/analytics_row.dart';
import '../theme/app_colors.dart';
import '../theme/app_theme.dart';
import '../widgets/charts.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import '../widgets/ui_kit.dart';
import 'home_shell.dart';

/// «Аналітика» — the alarm journal as a dashboard: KPIs with trends, daily
/// dynamics (alarms + impacts), threat mix, weekday×hour heatmap, the 24-hour
/// clock, regions, impacts, auto insights and the latest events.
class AnalyticsScreen extends StatefulWidget {
  const AnalyticsScreen({super.key});

  @override
  State<AnalyticsScreen> createState() => _AnalyticsScreenState();
}

class _AnalyticsScreenState extends State<AnalyticsScreen> {
  String _period = '30';
  final Set<String> _kinds = {};
  String? _zone;

  static const _periods = [('1', 'Доба'), ('7', '7 дн'), ('30', '30 дн'), ('90', '90 дн'), ('all', 'Усе')];

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final all = data.rows;
    final s = AnalyticsSummary.compute(all, _period, kinds: _kinds, zone: _zone);
    final base = AnalyticsSummary.compute(all, _period); // for filter chip counts

    return PageBody(
      child: LayoutBuilder(builder: (context, c) {
        final wide = c.maxWidth >= 900;
        final mid = c.maxWidth >= 600;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SectionHeader(
              title: 'Аналітика',
              subtitle: '${all.length} подій у журналі'
                  '${data.isDemo ? ' · демо-дані' : ''} · тривоги, прильоти, патерни',
              icon: Icons.insights_rounded,
              iconColor: AppColors.primary,
              trailing: mid ? _periodPicker() : null,
            ),
            if (!mid) ...[
              const SizedBox(height: 14),
              SingleChildScrollView(scrollDirection: Axis.horizontal, child: _periodPicker()),
            ],
            const SizedBox(height: 16),
            _filters(base),
            const SizedBox(height: 18),
            if (all.isEmpty)
              _empty()
            else ...[
              _kpis(s, c.maxWidth, data.airborne),
              const SizedBox(height: 16),
              _row(wide, [
                (2, _dynamics(s)),
                (1, _kindsCard(s)),
              ]),
              const SizedBox(height: 16),
              _row(wide, [
                (2, _heatCard(s)),
                (1, _clockCard(s)),
              ]),
              const SizedBox(height: 16),
              _row(c.maxWidth >= 760, [
                (1, _weekdayCard(s)),
                (1, _zonesCard(s)),
                if (c.maxWidth >= 1100) (1, _impactsCard(s)),
              ]),
              if (c.maxWidth < 1100) ...[const SizedBox(height: 16), _impactsCard(s)],
              const SizedBox(height: 16),
              _insights(s, c.maxWidth),
              const SizedBox(height: 16),
              _journal(s),
              const SizedBox(height: 8),
            ],
          ],
        );
      }),
    );
  }

  // ─── Layout helpers ─────────────────────────────────────────────────────────

  Widget _row(bool wide, List<(int, Widget)> items) {
    if (!wide) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < items.length; i++) ...[
            if (i > 0) const SizedBox(height: 16),
            items[i].$2,
          ],
        ],
      );
    }
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < items.length; i++) ...[
            if (i > 0) const SizedBox(width: 16),
            Expanded(flex: items[i].$1, child: items[i].$2),
          ],
        ],
      ),
    );
  }

  Widget _periodPicker() => Segmented<String>(
        value: _period,
        items: _periods,
        onChanged: (p) => setState(() => _period = p),
      );

  Widget _filters(AnalyticsSummary base) {
    final zones = base.zones.map((z) => z.$1).where((z) => z != '—').toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: Row(
            children: [
              FilterPill(
                label: 'Усі загрози',
                icon: Icons.all_inclusive_rounded,
                selected: _kinds.isEmpty,
                onTap: () => setState(_kinds.clear),
              ),
              for (final k in base.kinds)
                FilterPill(
                  label: kindName(k.$1),
                  count: k.$2,
                  color: kindColor(k.$1),
                  icon: kindIcon(k.$1),
                  selected: _kinds.contains(k.$1),
                  onTap: () => setState(() {
                    _kinds.contains(k.$1) ? _kinds.remove(k.$1) : _kinds.add(k.$1);
                  }),
                ),
            ],
          ),
        ),
        if (zones.length > 1) ...[
          const SizedBox(height: 8),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                FilterPill(
                  label: 'Уся Україна',
                  icon: Icons.public_rounded,
                  selected: _zone == null,
                  onTap: () => setState(() => _zone = null),
                ),
                for (final z in zones)
                  FilterPill(
                    label: zoneName(z),
                    icon: Icons.location_on_outlined,
                    selected: _zone == z,
                    onTap: () => setState(() => _zone = _zone == z ? null : z),
                  ),
              ],
            ),
          ),
        ],
      ],
    );
  }

  Widget _empty() => GlassContainer(
        padding: const EdgeInsets.symmetric(vertical: 48, horizontal: 24),
        child: Column(
          children: [
            Icon(Icons.query_stats_rounded, size: 44, color: AppColors.textMuted),
            const SizedBox(height: 12),
            Text('Журнал ще порожній',
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: AppColors.textPrimary)),
            const SizedBox(height: 6),
            Text('Графіки зʼявляться після перших тривог — монітор записує кожну.',
                textAlign: TextAlign.center, style: TextStyle(color: AppColors.textSecondary)),
          ],
        ),
      );

  // ─── KPIs ───────────────────────────────────────────────────────────────────

  Widget _kpis(AnalyticsSummary s, double width, int airborne) {
    final cols = width >= 1000 ? 4 : (width >= 560 ? 3 : 2);
    final busiest = s.busiestDay;
    final tiles = <Widget>[
      KpiTile(
        icon: Icons.notifications_active_rounded,
        color: AppColors.warn,
        label: 'Тривоги',
        value: '${s.alarms}',
        delta: AnalyticsSummary.delta(s.alarms, s.prevAlarms),
        footer: Sparkline(s.daily.map((d) => d.$2).toList(), color: AppColors.warn),
      ),
      KpiTile(
        icon: Icons.local_fire_department_rounded,
        color: AppColors.threat,
        label: 'Прильоти',
        value: '${s.impacts}',
        delta: AnalyticsSummary.delta(s.impacts, s.prevImpacts),
        footer: Sparkline(s.daily.map((d) => d.$3).toList(), color: AppColors.threat),
      ),
      KpiTile(
        icon: Icons.speed_rounded,
        color: AppColors.primary,
        label: 'За добу в середньому',
        value: s.avgPerDay >= 10 ? s.avgPerDay.round().toString() : s.avgPerDay.toStringAsFixed(1),
        note: busiest == null ? 'пік — немає' : 'пік ${busiest.$2} · ${_dm(busiest.$1)}',
      ),
      KpiTile(
        icon: Icons.nightlight_round,
        color: AppColors.night,
        label: 'Вночі (22–06)',
        value: '${(s.nightShare * 100).round()}',
        unit: '%',
        footer: ProgressLine(s.nightShare, color: AppColors.night),
      ),
      KpiTile(
        icon: Icons.calendar_month_rounded,
        color: AppColors.accent,
        label: 'Днів із тривогами',
        value: '${s.daysWithAlarms}',
        unit: '/ ${s.days}',
        footer: ProgressLine(s.days == 0 ? 0 : s.daysWithAlarms / s.days, color: AppColors.accent),
      ),
      KpiTile(
        icon: Icons.spa_rounded,
        color: AppColors.safe,
        label: 'Тихих днів',
        value: '${s.quietDays}',
        note: 'найдовше затишшя ${s.longestQuiet} дн',
      ),
      KpiTile(
        icon: Icons.timeline_rounded,
        color: const Color(0xFFFF7A3C),
        label: 'Серія тривожних днів',
        value: '${s.currentStreak}',
        unit: 'дн',
        note: 'рекорд за період ${s.longestStreak} дн',
      ),
      KpiTile(
        icon: Icons.radar_rounded,
        color: AppColors.highlight,
        label: 'У повітрі зараз',
        value: '$airborne',
        note: s.alarms == 0
            ? 'живі дані монітора'
            : '${(s.impactRate * 100).round()} прильотів на 100 тривог',
      ),
    ];
    return LayoutBuilder(builder: (context, c) {
      const gap = 12.0;
      final w = (c.maxWidth - gap * (cols - 1)) / cols;
      return Wrap(
        spacing: gap,
        runSpacing: gap,
        children: [for (final t in tiles) SizedBox(width: w, child: t)],
      );
    });
  }

  static String _dm(String date) =>
      date.length >= 10 ? '${date.substring(8, 10)}.${date.substring(5, 7)}' : date;

  // ─── Cards ──────────────────────────────────────────────────────────────────

  Widget _dynamics(AnalyticsSummary s) {
    final n = s.daily.length;
    final hourly = _period == '1';
    final labels = <(int, String)>[];
    if (hourly) {
      labels.addAll([(0, '00'), (6, '06'), (12, '12'), (18, '18'), (23, '23')]);
    } else if (n > 0) {
      final step = (n / 5).ceil().clamp(1, 1000);
      for (var i = 0; i < n; i += step) {
        labels.add((i, _dm(s.daily[i].$1)));
      }
      if (labels.last.$1 != n - 1) {
        if (n - 1 - labels.last.$1 < step / 2) labels.removeLast();
        labels.add((n - 1, 'сьогодні'));
      }
    }
    return ChartCard(
      title: hourly ? 'Доба по годинах' : 'Динаміка по днях',
      subtitle: 'Тривоги й прильоти · торкнись стовпчика',
      icon: Icons.bar_chart_rounded,
      child: StackedBars(
        height: 190,
        series: hourly
            ? [
                BarSeries('Тривоги', AppColors.warn, s.hourly),
                BarSeries('Прильоти', AppColors.threat, s.hourlyImpacts),
              ]
            : [
                BarSeries('Тривоги', AppColors.warn, s.daily.map((d) => d.$2).toList()),
                BarSeries('Прильоти', AppColors.threat, s.daily.map((d) => d.$3).toList()),
              ],
        labelOf: (i) => hourly ? '${i.toString().padLeft(2, '0')}:00' : _dm(s.daily[i].$1),
        axisLabels: labels,
        highlight: hourly ? DateTime.now().hour : n - 1,
      ),
    );
  }

  Widget _kindsCard(AnalyticsSummary s) {
    final total = s.alarms;
    final top = s.kinds.take(6).toList();
    final rest = s.kinds.skip(6).fold<int>(0, (a, b) => a + b.$2);
    return ChartCard(
      title: 'Типи загроз',
      subtitle: 'Частка кожного типу',
      icon: Icons.donut_large_rounded,
      child: Column(
        children: [
          const SizedBox(height: 4),
          DonutChart(
            size: 156,
            thickness: 20,
            parts: [
              for (final k in top) (kindColor(k.$1), k.$2),
              if (rest > 0) (AppColors.textMuted, rest),
            ],
            center: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text('$total', style: AppText.number(28)),
                const SizedBox(height: 2),
                Text('тривог', style: TextStyle(fontSize: 11.5, color: AppColors.textMuted)),
              ],
            ),
          ),
          const SizedBox(height: 16),
          if (top.isEmpty)
            Text('немає даних за період', style: TextStyle(color: AppColors.textMuted))
          else
            for (final k in top)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 3.5),
                child: Row(
                  children: [
                    Container(
                      width: 10,
                      height: 10,
                      decoration: BoxDecoration(
                          color: kindColor(k.$1), borderRadius: BorderRadius.circular(3)),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(kindName(k.$1),
                          style: TextStyle(fontSize: 13, color: AppColors.textSecondary)),
                    ),
                    Text('${k.$2}',
                        style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w800,
                            color: AppColors.textPrimary,
                            fontFeatures: AppText.tabular)),
                    SizedBox(
                      width: 46,
                      child: Text('${total == 0 ? 0 : (k.$2 * 100 / total).round()}%',
                          textAlign: TextAlign.right,
                          style: TextStyle(
                              fontSize: 12, color: AppColors.textMuted, fontFeatures: AppText.tabular)),
                    ),
                  ],
                ),
              ),
        ],
      ),
    );
  }

  Widget _heatCard(AnalyticsSummary s) => ChartCard(
        title: 'Коли найчастіше',
        subtitle: 'День тижня × година доби',
        icon: Icons.grid_on_rounded,
        child: HeatmapGrid(data: s.heat, rowLabels: kWeekdays, color: AppColors.warn),
      );

  Widget _clockCard(AnalyticsSummary s) {
    final q = s.quietestWindow;
    String hh(int h) => '${(h % 24).toString().padLeft(2, '0')}:00';
    return ChartCard(
      title: 'Циферблат доби',
      subtitle: 'Тривоги за годинами · ніч підсвічено',
      icon: Icons.schedule_rounded,
      child: Column(
        children: [
          DayClock(
            hours: s.hourly,
            color: AppColors.warn,
            size: 196,
            center: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(hh(s.peakHour), style: AppText.number(20)),
                Text('пік', style: TextStyle(fontSize: 11, color: AppColors.textMuted)),
              ],
            ),
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: MiniStat(
                  icon: Icons.wb_sunny_rounded,
                  color: AppColors.warn,
                  value: '${s.alarms - s.nightAlarms}',
                  label: 'вдень',
                ),
              ),
              Expanded(
                child: MiniStat(
                  icon: Icons.nightlight_round,
                  color: AppColors.night,
                  value: '${s.nightAlarms}',
                  label: 'вночі',
                ),
              ),
              Expanded(
                child: MiniStat(
                  icon: Icons.self_improvement_rounded,
                  color: AppColors.safe,
                  value: '${hh(q)}–${hh(q + 3)}',
                  label: 'найтихіше',
                  small: true,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _weekdayCard(AnalyticsSummary s) {
    final peak = s.peakWeekday;
    return ChartCard(
      title: 'Дні тижня',
      subtitle: s.alarms == 0 ? 'немає даних' : 'найчастіше — ${_weekdayFull[peak]}',
      icon: Icons.view_week_rounded,
      child: StackedBars(
        height: 150,
        series: [BarSeries('Тривоги', AppColors.primary, s.weekday)],
        labelOf: (i) => _weekdayFull[i],
        axisLabels: [for (var i = 0; i < 7; i++) (i, kWeekdays[i])],
        highlight: DateTime.now().weekday - 1,
      ),
    );
  }

  static const _weekdayFull = ['понеділок', 'вівторок', 'середа', 'четвер', 'пʼятниця', 'субота', 'неділя'];

  Widget _zonesCard(AnalyticsSummary s) {
    final top = s.zones.take(7).toList();
    final max = top.isEmpty ? 1 : top.map((z) => z.$2 + z.$3).reduce((a, b) => a > b ? a : b);
    return ChartCard(
      title: 'Регіони',
      subtitle: 'Тривоги + прильоти',
      icon: Icons.map_rounded,
      trailing: Wrap(spacing: 10, children: [
        LegendDot(AppColors.primary, 'тр.'),
        LegendDot(AppColors.threat, 'пр.'),
      ]),
      child: top.isEmpty
          ? Text('немає даних за період', style: TextStyle(color: AppColors.textMuted))
          : Column(
              children: [
                for (final z in top)
                  RankBar(
                    label: zoneName(z.$1),
                    value: z.$2,
                    second: z.$3,
                    max: max,
                    color: AppColors.primary,
                    secondColor: AppColors.threat,
                    trailing: z.$3 > 0 ? '${z.$2} · ${z.$3}🔥' : '${z.$2}',
                  ),
              ],
            ),
    );
  }

  Widget _impactsCard(AnalyticsSummary s) {
    final zones = s.impactZones.take(5).toList();
    final kinds = s.impactKinds.take(4).toList();
    final maxZ = zones.isEmpty ? 1 : zones.first.$2;
    return ChartCard(
      title: 'Прильоти',
      subtitle: s.impacts == 0 ? 'за період не зафіксовано 🙏' : 'Де і чим били',
      icon: Icons.local_fire_department_rounded,
      accent: AppColors.threat,
      child: s.impacts == 0
          ? Padding(
              padding: const EdgeInsets.symmetric(vertical: 18),
              child: Center(
                child: Icon(Icons.verified_rounded, color: AppColors.safe, size: 40),
              ),
            )
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final k in kinds)
                      Tag('${kindName(k.$1)} · ${k.$2}', color: kindColor(k.$1), icon: kindIcon(k.$1)),
                  ],
                ),
                const SizedBox(height: 10),
                for (final z in zones)
                  RankBar(label: zoneName(z.$1), value: z.$2, max: maxZ, color: AppColors.threat),
              ],
            ),
    );
  }

  // ─── Insights ───────────────────────────────────────────────────────────────

  Widget _insights(AnalyticsSummary s, double width) {
    String hh(int h) => '${(h % 24).toString().padLeft(2, '0')}:00';
    final d = AnalyticsSummary.delta(s.alarms, s.prevAlarms);
    final items = <(IconData, Color, String, String)>[
      if (s.alarms > 0)
        (Icons.access_time_filled_rounded, AppColors.warn, 'Найнебезпечніша година',
            '${hh(s.peakHour)}–${hh(s.peakHour + 1)}: ${s.hourly[s.peakHour]} тривог'),
      if (s.kinds.isNotEmpty)
        (kindIcon(s.kinds.first.$1), kindColor(s.kinds.first.$1), 'Головна загроза',
            '${kindName(s.kinds.first.$1)} — ${(s.kinds.first.$2 * 100 / (s.alarms == 0 ? 1 : s.alarms)).round()}% усіх тривог'),
      if (d != null)
        (d > 0 ? Icons.trending_up_rounded : Icons.trending_down_rounded,
            d > 0 ? AppColors.threat : AppColors.safe,
            d > 0 ? 'Стало гучніше' : (d < 0 ? 'Стало тихіше' : 'Без змін'),
            '${d > 0 ? '+' : ''}$d% тривог проти попередніх ${_periodWord()}'),
      if (s.impactZones.isNotEmpty)
        (Icons.place_rounded, AppColors.threat, 'Найбільше прильотів',
            '${zoneName(s.impactZones.first.$1)} — ${s.impactZones.first.$2}'),
      if (s.alarms > 0)
        (Icons.event_busy_rounded, AppColors.primary, 'Найважчий день тижня',
            '${_weekdayFull[s.peakWeekday]} — ${s.weekday[s.peakWeekday]} тривог'),
      (Icons.self_improvement_rounded, AppColors.safe, 'Найтихіший час',
          '${hh(s.quietestWindow)}–${hh(s.quietestWindow + 3)} — плануй справи на цей час'),
    ];
    final cols = width >= 1000 ? 3 : (width >= 600 ? 2 : 1);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionLabel('Що варто знати'),
        LayoutBuilder(builder: (context, c) {
          const gap = 12.0;
          final w = (c.maxWidth - gap * (cols - 1)) / cols;
          return Wrap(
            spacing: gap,
            runSpacing: gap,
            children: [
              for (final it in items)
                SizedBox(
                  width: w,
                  child: GlassContainer(
                    padding: const EdgeInsets.all(14),
                    child: Row(
                      children: [
                        IconTile(it.$1, color: it.$2, size: 42),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(it.$3,
                                  style: TextStyle(
                                      fontSize: 12,
                                      fontWeight: FontWeight.w700,
                                      color: AppColors.textMuted)),
                              const SizedBox(height: 3),
                              Text(it.$4,
                                  style: TextStyle(
                                      fontSize: 14,
                                      height: 1.3,
                                      fontWeight: FontWeight.w700,
                                      color: AppColors.textPrimary)),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
            ],
          );
        }),
      ],
    );
  }

  String _periodWord() => switch (_period) {
        '1' => 'доби',
        '7' => '7 днів',
        '30' => '30 днів',
        _ => '90 днів',
      };

  // ─── Journal ────────────────────────────────────────────────────────────────

  Widget _journal(AnalyticsSummary s) {
    final list = s.recent.take(12).toList();
    return ChartCard(
      title: 'Останні події',
      subtitle: 'Журнал монітора',
      icon: Icons.receipt_long_rounded,
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 8),
      child: list.isEmpty
          ? Text('немає подій за період', style: TextStyle(color: AppColors.textMuted))
          : Column(
              children: [
                for (var i = 0; i < list.length; i++) ...[
                  if (i > 0) Divider(height: 1, color: AppColors.divider),
                  _event(list[i]),
                ],
              ],
            ),
    );
  }

  Widget _event(AnalyticsRow r) {
    final impact = r.isImpact;
    final c = impact ? AppColors.threat : kindColor(r.kind);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Row(
        children: [
          IconTile(impact ? Icons.local_fire_department_rounded : kindIcon(r.kind),
              color: c, size: 36),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(kindName(r.kind),
                    style: TextStyle(
                        fontSize: 14, fontWeight: FontWeight.w700, color: AppColors.textPrimary)),
                Text(zoneName(r.zone),
                    style: TextStyle(fontSize: 12.5, color: AppColors.textSecondary)),
              ],
            ),
          ),
          Tag(impact ? 'ПРИЛІТ' : 'ТРИВОГА',
              color: impact ? AppColors.threat : AppColors.warn),
          const SizedBox(width: 12),
          SizedBox(
            width: 64,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text('${r.hour.toString().padLeft(2, '0')}:00',
                    style: AppText.number(14, weight: FontWeight.w700)),
                const SizedBox(height: 2),
                Text(_dm(r.date),
                    style: TextStyle(fontSize: 11.5, color: AppColors.textMuted)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

// ─── Building blocks ──────────────────────────────────────────────────────────

/// A titled panel for one chart.
class ChartCard extends StatelessWidget {
  const ChartCard({
    super.key,
    required this.title,
    required this.child,
    this.subtitle,
    this.icon,
    this.trailing,
    this.accent,
    this.padding = const EdgeInsets.all(18),
  });

  final String title;
  final String? subtitle;
  final IconData? icon;
  final Widget? trailing;
  final Widget child;
  final Color? accent;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return GlassContainer(
      padding: padding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              if (icon != null) ...[
                Icon(icon, size: 18, color: accent ?? AppColors.textSecondary),
                const SizedBox(width: 8),
              ],
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title,
                        style: TextStyle(
                            fontSize: 15.5,
                            fontWeight: FontWeight.w800,
                            letterSpacing: -0.2,
                            color: AppColors.textPrimary)),
                    if (subtitle != null)
                      Text(subtitle!,
                          style: TextStyle(fontSize: 12, color: AppColors.textMuted)),
                  ],
                ),
              ),
              ?trailing,
            ],
          ),
          const SizedBox(height: 14),
          child,
        ],
      ),
    );
  }
}

/// KPI tile: icon, label, big number (+unit), ↑/↓ delta, sparkline or note.
class KpiTile extends StatelessWidget {
  const KpiTile({
    super.key,
    required this.icon,
    required this.color,
    required this.label,
    required this.value,
    this.unit,
    this.delta,
    this.note,
    this.footer,
  });

  final IconData icon;
  final Color color;
  final String label, value;
  final String? unit, note;
  final int? delta;
  final Widget? footer;

  @override
  Widget build(BuildContext context) {
    // For alarms/impacts «less» is good: ↓ green, ↑ red.
    final dc = delta == null || delta == 0
        ? AppColors.textMuted
        : (delta! > 0 ? AppColors.threat : AppColors.safe);
    return GlassContainer(
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
      child: SizedBox(
        height: 112,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                IconTile(icon, color: color, size: 30),
                const SizedBox(width: 9),
                Expanded(
                  child: Text(label,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontSize: 12,
                          height: 1.2,
                          fontWeight: FontWeight.w600,
                          color: AppColors.textSecondary)),
                ),
              ],
            ),
            const Spacer(),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Flexible(
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    alignment: Alignment.centerLeft,
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.baseline,
                      textBaseline: TextBaseline.alphabetic,
                      children: [
                        Text(value, style: AppText.number(28)),
                        if (unit != null) ...[
                          const SizedBox(width: 4),
                          Text(unit!,
                              style: TextStyle(
                                  fontSize: 14,
                                  fontWeight: FontWeight.w700,
                                  color: AppColors.textMuted)),
                        ],
                      ],
                    ),
                  ),
                ),
                if (delta != null) ...[
                  const SizedBox(width: 6),
                  Padding(
                    padding: const EdgeInsets.only(bottom: 3),
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                      decoration: BoxDecoration(
                        color: dc.withValues(alpha: 0.14),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Text(
                        '${delta! > 0 ? '▲' : (delta! < 0 ? '▼' : '•')} ${delta!.abs()}%',
                        style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w800,
                            color: dc,
                            fontFeatures: AppText.tabular),
                      ),
                    ),
                  ),
                ],
              ],
            ),
            const SizedBox(height: 8),
            SizedBox(
              height: 22,
              child: footer ??
                  Align(
                    alignment: Alignment.bottomLeft,
                    child: Text(note ?? '',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 11.5, color: AppColors.textMuted)),
                  ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Thin rounded progress line.
class ProgressLine extends StatelessWidget {
  const ProgressLine(this.value, {super.key, required this.color});
  final double value;
  final Color color;

  @override
  Widget build(BuildContext context) => Align(
        alignment: Alignment.bottomLeft,
        child: Container(
          height: 7,
          margin: const EdgeInsets.only(bottom: 4),
          decoration: BoxDecoration(
            color: AppColors.textMuted.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(4),
          ),
          alignment: Alignment.centerLeft,
          child: FractionallySizedBox(
            widthFactor: value.clamp(0.0, 1.0),
            child: Container(
              decoration: BoxDecoration(
                gradient: LinearGradient(colors: [color.withValues(alpha: 0.6), color]),
                borderRadius: BorderRadius.circular(4),
              ),
            ),
          ),
        ),
      );
}

class MiniStat extends StatelessWidget {
  const MiniStat({
    super.key,
    required this.icon,
    required this.color,
    required this.value,
    required this.label,
    this.small = false,
  });

  final IconData icon;
  final Color color;
  final String value, label;
  final bool small;

  @override
  Widget build(BuildContext context) => Column(
        children: [
          Icon(icon, size: 17, color: color),
          const SizedBox(height: 4),
          FittedBox(child: Text(value, style: AppText.number(small ? 13 : 17))),
          Text(label, style: TextStyle(fontSize: 11, color: AppColors.textMuted)),
        ],
      );
}

/// Toggleable pill for filters.
class FilterPill extends StatelessWidget {
  const FilterPill({
    super.key,
    required this.label,
    required this.selected,
    required this.onTap,
    this.color,
    this.icon,
    this.count,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;
  final Color? color;
  final IconData? icon;
  final int? count;

  @override
  Widget build(BuildContext context) {
    final c = color ?? AppColors.primary;
    return Padding(
      padding: const EdgeInsets.only(right: 8),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(20),
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 160),
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              color: selected ? c.withValues(alpha: AppColors.isDark ? 0.2 : 0.14) : AppColors.surface,
              borderRadius: BorderRadius.circular(20),
              border: Border.all(color: selected ? c.withValues(alpha: 0.7) : AppColors.border),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (icon != null) ...[
                  Icon(icon, size: 15, color: selected ? c : AppColors.textMuted),
                  const SizedBox(width: 6),
                ],
                Text(label,
                    style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                        color: selected ? AppColors.textPrimary : AppColors.textSecondary)),
                if (count != null) ...[
                  const SizedBox(width: 6),
                  Text('$count',
                      style: TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w800,
                          color: selected ? c : AppColors.textMuted,
                          fontFeatures: AppText.tabular)),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
