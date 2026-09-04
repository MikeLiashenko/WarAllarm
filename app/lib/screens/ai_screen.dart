import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/ai_controller.dart';
import '../controllers/map_data_controller.dart';
import '../controllers/nav_controller.dart';
import '../controllers/settings_controller.dart';
import '../domain/analytics.dart';
import '../domain/situation.dart';
import '../models/ai_session.dart';
import '../models/map_target.dart';
import '../models/monitored_place.dart';
import '../theme/app_colors.dart';
import '../utils/formatting.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

/// AI tab — the Reader's AI functionality ported into the app's style:
/// a live LLM (Claude/Gemini) analysis + heuristic risk, trajectory forecast
/// and history patterns computed from the current situation.
class AiScreen extends StatefulWidget {
  const AiScreen({super.key});

  @override
  State<AiScreen> createState() => _AiScreenState();
}

class _AiScreenState extends State<AiScreen> {
  final _input = TextEditingController();

  @override
  void dispose() {
    _input.dispose();
    super.dispose();
  }

  String _situation(MapDataController d, List<MonitoredPlace> sel) {
    final b = StringBuffer();
    b.writeln('Дім: ${d.homeName} (${d.home.lat}, ${d.home.lon}).');
    final places = sel.isEmpty ? d.places : sel;
    for (final pl in places) {
      final arr = placeThreats(pl, d.targets);
      if (arr.isEmpty) {
        b.writeln('${pl.name}: загроз поруч немає.');
        continue;
      }
      b.writeln('${pl.name}:');
      for (final t in arr.take(4)) {
        b.writeln(
            '  - ${t.label}${t.amount > 1 ? ' ×${t.amount}' : ''}, ${t.pdist?.round() ?? '?'} км, '
            'ETA ${fmtEta(etaNow(t, d.stateTs))}, ${t.stageLabel}, напрям ${t.dir}, біля ${t.place}.');
      }
    }
    return b.toString();
  }

  Future<void> _ask(BuildContext context) async {
    final text = _input.text.trim();
    if (text.isEmpty) return;
    _input.clear();
    final settings = context.read<SettingsController>();
    final data = context.read<MapDataController>();
    await context.read<AiController>().sendMessage(
          text,
          apiKey: settings.aiApiKey,
          model: settings.aiModel,
          situation: _situation(data, data.selectedPlaces),
        );
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<MapDataController>();
    final settings = context.watch<SettingsController>();
    final ai = context.watch<AiController>();

    final activePlaces =
        data.places.where((p) => !p.paused).toList();
    final risks = activePlaces.map((p) => riskOf(p, data.targets)).toList()
      ..sort((a, b) => b.score.compareTo(a.score));
    final worst = risks.isEmpty ? null : risks.first;
    final patterns = computePatterns(data.rows);

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionHeader(
            title: 'ШІ · аналіз загроз',
            subtitle: 'Розумний висновок, ризик, прогноз курсу та патерни',
            icon: Icons.auto_awesome_rounded,
          ),
          const SizedBox(height: 18),

          if (data.isDemo) ...[
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
              decoration: BoxDecoration(
                color: const Color(0xFFFFF2D6),
                borderRadius: BorderRadius.circular(14),
                border: Border.all(color: const Color(0xFFE9B949)),
              ),
              child: const Row(
                children: [
                  Icon(Icons.science_rounded, color: Color(0xFFB8860B), size: 20),
                  SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Аналіз нижче — на ДЕМО-даних (приклад). Реальний ІІ-аналіз '
                      'зʼявиться, коли монітор підключено.',
                      style: TextStyle(
                          fontSize: 12.5,
                          fontWeight: FontWeight.w600,
                          color: Color(0xFF6B540A)),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),
          ],

          // 1 — Live LLM assistant (Claude / Gemini)
          _llmCard(context, data, settings, ai),
          const SizedBox(height: 22),

          // 2 — Smart verdict now
          _heading('Розумний висновок · зараз'),
          _verdictCard(worst, data),
          const SizedBox(height: 22),

          // 3 — Risk per place
          _heading('Оцінка ризику по місцях'),
          ...risks.map(_riskCard),
          const SizedBox(height: 22),

          // 4 — Trajectory forecast
          _heading('Прогноз траєкторії'),
          _forecast(activePlaces, data),
          const SizedBox(height: 22),

          // 5 — Patterns from history
          _heading('Патерни з історії'),
          _patternGrid(patterns),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

  Widget _heading(String text) => Padding(
        padding: const EdgeInsets.only(bottom: 12, left: 4),
        child: Text(
          text.toUpperCase(),
          style: const TextStyle(
            fontSize: 12,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.6,
            color: AppColors.textMuted,
          ),
        ),
      );

  Widget _llmCard(BuildContext context, MapDataController data,
      SettingsController settings, AiController ai) {
    final configured = settings.aiConfigured;
    final msgs = ai.active?.messages ?? const <AiMessage>[];
    return GlassContainer(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  gradient: const LinearGradient(
                      colors: [AppColors.accent, AppColors.primary]),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: const Icon(Icons.auto_awesome_rounded,
                    color: Colors.white, size: 20),
              ),
              const SizedBox(width: 12),
              const Expanded(
                child: Text('Асистент (Claude · Gemini)',
                    style: TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textPrimary)),
              ),
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                decoration: BoxDecoration(
                  color: configured ? AppColors.safeSoft : AppColors.primarySoft,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Text(
                  configured
                      ? (settings.keyFromSecrets ? 'ключ з secrets' : 'ключ підключено')
                      : 'ключ не задано',
                  style: TextStyle(
                    fontSize: 11.5,
                    fontWeight: FontWeight.w700,
                    color: configured ? AppColors.safe : AppColors.primaryDark,
                  ),
                ),
              ),
            ],
          ),
          if (!configured)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppColors.primarySoft.withValues(alpha: 0.5),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Row(
                  children: [
                    const Expanded(
                      child: Text(
                        'Додай ключ Anthropic (sk-ant-…) або Google (AIza…/AQ…) '
                        'у Налаштуваннях, щоб увімкнути справжній ІІ.',
                        style: TextStyle(
                            fontSize: 13, color: AppColors.textSecondary),
                      ),
                    ),
                    TextButton(
                      onPressed: () => context
                          .read<NavController>()
                          .select(AppTab.settings),
                      child: const Text('Налаштування'),
                    ),
                  ],
                ),
              ),
            ),
          if (data.aiText.isNotEmpty) ...[
            const SizedBox(height: 12),
            _bubble('🤖 ${data.aiText}',
                sub: data.aiAt == null ? null : clock(data.aiAt!)),
          ],
          for (final m in msgs)
            Padding(
              padding: const EdgeInsets.only(top: 10),
              child: _bubble(m.content, mine: m.isUser),
            ),
          if (ai.thinking)
            const Padding(
              padding: EdgeInsets.only(top: 10),
              child: Text('Аналізую…',
                  style: TextStyle(
                      color: AppColors.textSecondary,
                      fontStyle: FontStyle.italic)),
            ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _input,
                  textInputAction: TextInputAction.send,
                  onSubmitted: (_) => _ask(context),
                  decoration: InputDecoration(
                    hintText: configured
                        ? 'Запитати ІІ про обстановку…'
                        : 'Запитати (локальна відповідь без ключа)…',
                  ),
                ),
              ),
              const SizedBox(width: 10),
              SizedBox(
                width: 50,
                height: 50,
                child: ElevatedButton(
                  onPressed: ai.thinking ? null : () => _ask(context),
                  style: ElevatedButton.styleFrom(
                    padding: EdgeInsets.zero,
                    shape: const CircleBorder(),
                  ),
                  child: const Icon(Icons.arrow_upward_rounded, size: 20),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _bubble(String text, {bool mine = false, String? sub}) {
    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        constraints: const BoxConstraints(maxWidth: 620),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
        decoration: BoxDecoration(
          color: mine ? AppColors.primary : AppColors.primarySoft,
          borderRadius: BorderRadius.circular(14),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(text,
                style: TextStyle(
                    fontSize: 14,
                    height: 1.45,
                    color: mine ? Colors.white : AppColors.textPrimary)),
            if (sub != null)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(sub,
                    style: const TextStyle(
                        fontSize: 11, color: AppColors.textMuted)),
              ),
          ],
        ),
      ),
    );
  }

  Widget _verdictCard(RiskResult? worst, MapDataController data) {
    if (worst != null && worst.score >= 55) {
      final t = worst.top!;
      return _panel(
        AppColors.threat,
        AppColors.threatSoft,
        '🔴 Зараз небезпечно — ${worst.place.name}',
        '${t.label} · ${t.pdist?.round() ?? '?'} км · ${fmtEta(etaNow(t, data.stateTs))} · напрям ${t.dir}\n➤ ${worst.advice}',
      );
    }
    if (worst != null && worst.score >= 35) {
      final t = worst.top!;
      return _panel(
        AppColors.primary,
        AppColors.primarySoft,
        '🟡 Підвищена увага — ${worst.place.name}',
        '${t.label} · ${t.pdist?.round() ?? '?'} км · ${fmtEta(etaNow(t, data.stateTs))}\n${worst.advice}',
      );
    }
    final near = data.targets.where((t) => t.distKm != null).toList()
      ..sort((a, b) => a.distKm!.compareTo(b.distKm!));
    final tail = data.targets.isEmpty
        ? 'Активних загроз немає.'
        : 'По країні цілей: ${data.targets.length} (далеко). Найближча — '
            '${near.isEmpty ? '—' : '${near.first.label} ${near.first.distKm!.round()} км'}.';
    return _panel(AppColors.safe, AppColors.safeSoft, '🟢 Зараз безпечно',
        'Активних загроз поруч немає. $tail');
  }

  Widget _panel(Color accent, Color bg, String title, String body) {
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: bg.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: accent.withValues(alpha: 0.4)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title,
              style: TextStyle(
                  fontSize: 15.5,
                  fontWeight: FontWeight.w700,
                  color: accent == AppColors.safe
                      ? AppColors.textPrimary
                      : accent)),
          const SizedBox(height: 6),
          Text(body,
              style: const TextStyle(
                  fontSize: 13.5, height: 1.5, color: AppColors.textSecondary)),
        ],
      ),
    );
  }

  Widget _riskCard(RiskResult r) {
    final color = r.cls == 'alarm'
        ? AppColors.threat
        : r.cls == 'warn'
            ? const Color(0xFFF4B740)
            : AppColors.safe;
    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text('${r.place.icon} ${r.place.name}',
                  style: const TextStyle(
                      fontSize: 14.5,
                      fontWeight: FontWeight.w700,
                      color: AppColors.textPrimary)),
              const Spacer(),
              Text('${r.level} · ${r.score}/100',
                  style: TextStyle(
                      fontSize: 12.5,
                      fontWeight: FontWeight.w700,
                      color: color)),
            ],
          ),
          const SizedBox(height: 9),
          ClipRRect(
            borderRadius: BorderRadius.circular(6),
            child: LinearProgressIndicator(
              value: r.score / 100,
              minHeight: 9,
              backgroundColor: AppColors.divider,
              valueColor: AlwaysStoppedAnimation(color),
            ),
          ),
          const SizedBox(height: 8),
          Text(r.advice,
              style: const TextStyle(
                  fontSize: 13, color: AppColors.textSecondary)),
        ],
      ),
    );
  }

  Widget _forecast(List<MonitoredPlace> places, MapDataController data) {
    final cards = <Widget>[];
    for (final pl in places) {
      MapTarget? bestT;
      ClosestApproach? bestCa;
      for (final t in data.targets) {
        if (t.heading == null) continue;
        final ca = closest(t, pl);
        if (bestCa == null || ca.minKm < bestCa.minKm) {
          bestCa = ca;
          bestT = t;
        }
      }
      if (bestT == null || bestCa == null) continue;
      final d = kmTo(bestT, pl);
      final verdict = bestCa.approaching
          ? 'при збереженні курсу пройде за ~${bestCa.minKm.round()} км від «${pl.name}»${bestCa.minKm <= 12 ? ' — БЛИЗЬКО ⚠' : ''}'
          : 'віддаляється від «${pl.name}»';
      cards.add(Container(
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppColors.surface,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: AppColors.border),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('${bestT.label} → ${pl.name}',
                style: const TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textPrimary)),
            const SizedBox(height: 4),
            Text('зараз ${d.round()} км, курс ${bestT.dir}. $verdict.',
                style: const TextStyle(
                    fontSize: 13, color: AppColors.textSecondary)),
          ],
        ),
      ));
    }
    if (cards.isEmpty) {
      return _note('Немає активних цілей з відомим курсом.');
    }
    return Column(children: cards);
  }

  Widget _patternGrid(Patterns? p) {
    if (p == null) {
      return _note('Ще мало даних для патернів — накопичиться з тривогами.');
    }
    final tiles = [
      ('🕐', 'Найнебезпечніша година',
          '${p.peakHour.toString().padLeft(2, '0')}:00', 'найбільше тривог'),
      ('🎯', 'Найчастіша загроза',
          kKindMeta[p.topKind]?.name ?? p.topKind, 'за весь період'),
      ('📈', 'Тренд за тиждень', p.weekTrend, 'vs попередній тиждень'),
      ('💥', 'Найбільше прильотів', p.topImpactZone, 'за твоїми зонами'),
    ];
    return LayoutBuilder(builder: (context, c) {
      final cols = c.maxWidth > 640 ? 2 : 1;
      return GridView.count(
        crossAxisCount: cols,
        shrinkWrap: true,
        physics: const NeverScrollableScrollPhysics(),
        mainAxisSpacing: 12,
        crossAxisSpacing: 12,
        childAspectRatio: 2.6,
        children: [
          for (final t in tiles)
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: AppColors.surface,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: AppColors.border),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Text('${t.$1}  ${t.$2}',
                      style: const TextStyle(
                          fontSize: 12.5,
                          fontWeight: FontWeight.w600,
                          color: AppColors.textSecondary)),
                  const SizedBox(height: 6),
                  Text(t.$3,
                      style: const TextStyle(
                          fontSize: 22,
                          fontWeight: FontWeight.w800,
                          color: AppColors.textPrimary)),
                  Text(t.$4,
                      style: const TextStyle(
                          fontSize: 11.5, color: AppColors.textMuted)),
                ],
              ),
            ),
        ],
      );
    });
  }

  Widget _note(String text) => Container(
        width: double.infinity,
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppColors.primarySoft.withValues(alpha: 0.4),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: AppColors.border),
        ),
        child: Text(text,
            style:
                const TextStyle(fontSize: 13, color: AppColors.textSecondary)),
      );
}
