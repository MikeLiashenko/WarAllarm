import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../firebase_config.dart';
import '../theme/app_colors.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

/// API · Дані — the app's data as plain JSON over public HTTP (no key needed).
/// The Reader publishes live state to Firebase RTDB; these endpoints return it.
class ApiKeysScreen extends StatefulWidget {
  const ApiKeysScreen({super.key});

  @override
  State<ApiKeysScreen> createState() => _ApiKeysScreenState();
}

class _ApiKeysScreenState extends State<ApiKeysScreen> {
  int _lang = 0; // 0=Python 1=JS 2=cURL
  static const _langs = ['Python', 'JavaScript', 'cURL'];

  String get _base => FirebaseConfig.databaseUrl;
  static const _alerts = 'https://vadimklimenko.com/map/statuses.json';

  @override
  Widget build(BuildContext context) {
    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionHeader(
            title: 'API · Дані',
            subtitle: 'Живі дані у JSON — цілі, тривоги, стан. Без ключа.',
            icon: Icons.data_object_rounded,
          ),
          const SizedBox(height: 16),

          _InfoBanner(),
          const SizedBox(height: 16),

          _sectionLabel('Ендпоінти (GET · JSON)'),
          const SizedBox(height: 10),
          _Endpoint(
            title: 'Стан і цілі',
            desc: 'Все одразу: цілі (threats[]), дім, налаштування, аналітика.',
            url: '$_base/state.json',
            onCopy: _copy,
          ),
          _Endpoint(
            title: 'Тільки цілі',
            desc: 'Масив цілей: label, cat, lat, lon, place, dir, stage, '
                'dist(км), eta(хв), n(×скільки). null = чисто.',
            url: '$_base/state/threats.json',
            onCopy: _copy,
          ),
          _Endpoint(
            title: 'Ймовірність удару 🎯',
            desc: 'risk: шанс удару (0–99%) по дому й місцях — percent, verdict '
                '(прилетить/навряд), cls, byKind{тип:%}, factors[]. Рахує Reader.',
            url: '$_base/state/risk.json',
            onCopy: _copy,
          ),
          _Endpoint(
            title: 'Офіційні тривоги по областях 🚨',
            desc: 'alerts: red[]/yellow[] (назви областей), redCount/yellowCount, '
                'byOblast{область:red|yellow}, ts. Оновлює Reader щохвилини.',
            url: '$_base/state/alerts.json',
            onCopy: _copy,
          ),
          _Endpoint(
            title: 'Аналітика (журнал тривог)',
            desc: 'rows[]: d(дата), h(година), k(тип), z(зона), g(рівень).',
            url: '$_base/state/rows.json',
            onCopy: _copy,
          ),
          _Endpoint(
            title: 'Офіційні тривоги по областях',
            desc: 'states{}: по кожній області — enabled, alert_level '
                '(red/yellow), districts{}. Джерело офіційної карти.',
            url: _alerts,
            onCopy: _copy,
          ),
          const SizedBox(height: 18),

          // Code snippet
          GlassContainer(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text('Приклад коду',
                        style: TextStyle(
                            fontSize: 15,
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary)),
                    const Spacer(),
                    _langTabs(),
                  ],
                ),
                const SizedBox(height: 14),
                _CodeBlock(
                  code: _snippet(_lang),
                  onCopy: () => _copy(_snippet(_lang), 'Код скопійовано'),
                ),
              ],
            ),
          ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

  Widget _sectionLabel(String t) => Text(
        t.toUpperCase(),
        style: TextStyle(
            fontSize: 12,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.6,
            color: AppColors.textMuted),
      );

  Widget _langTabs() {
    return Container(
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(
        color: AppColors.primarySoft.withValues(alpha: 0.6),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (var i = 0; i < _langs.length; i++)
            GestureDetector(
              onTap: () => setState(() => _lang = i),
              child: AnimatedContainer(
                duration: const Duration(milliseconds: 180),
                padding:
                    const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
                decoration: BoxDecoration(
                  color: _lang == i ? AppColors.primary : Colors.transparent,
                  borderRadius: BorderRadius.circular(9),
                ),
                child: Text(
                  _langs[i],
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w700,
                    color: _lang == i ? Colors.white : AppColors.textSecondary,
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }

  void _copy(String text, [String msg = 'Скопійовано']) {
    Clipboard.setData(ClipboardData(text: text));
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(msg)));
  }

  String _snippet(int lang) {
    switch (lang) {
      case 0:
        return '''import requests

BASE = "$_base"

# Живі цілі + стан (публічно, без ключа)
data = requests.get(f"{BASE}/state.json").json() or {}
for t in (data.get("threats") or []):
    print(t["label"], t.get("place",""), t.get("dist"), "км",
          t.get("stageLabel",""))

# Ймовірність удару по дому
risk = requests.get(f"{BASE}/state/risk.json").json() or {}
home = risk.get("home") or {}
print("Дім:", home.get("percent"), "% —", home.get("verdict"))''';
      case 1:
        return '''const BASE = "$_base";

// Живі цілі + стан (публічно, без ключа)
const data = await (await fetch(`\${BASE}/state.json`)).json() ?? {};
for (const t of data.threats ?? []) {
  console.log(t.label, t.place ?? "", t.dist, "км", t.stageLabel ?? "");
}

// Ймовірність удару по дому
const risk = await (await fetch(`\${BASE}/state/risk.json`)).json() ?? {};
console.log("Дім:", risk.home?.percent, "% —", risk.home?.verdict);''';
      default:
        return 'curl "$_base/state/risk.json"';
    }
  }
}

class _InfoBanner extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        color: AppColors.primarySoft,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.primaryTint),
      ),
      child: Row(
        children: [
          Icon(Icons.public_rounded, color: AppColors.primaryDark, size: 20),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              'Публічний доступ, ключ не потрібен. Дані оновлює Reader на ПК; '
              'офіційні тривоги — окремим джерелом.',
              style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w600,
                  color: AppColors.primaryDark),
            ),
          ),
        ],
      ),
    );
  }
}

class _Endpoint extends StatelessWidget {
  const _Endpoint({
    required this.title,
    required this.desc,
    required this.url,
    required this.onCopy,
  });
  final String title, desc, url;
  final void Function(String, [String]) onCopy;

  @override
  Widget build(BuildContext context) {
    return Container(
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
          Row(
            children: [
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: AppColors.safeSoft,
                  borderRadius: BorderRadius.circular(6),
                ),
                child: Text('GET',
                    style: TextStyle(
                        fontSize: 10.5,
                        fontWeight: FontWeight.w800,
                        color: AppColors.safe)),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(title,
                    style: TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textPrimary)),
              ),
              IconButton(
                tooltip: 'Копіювати адресу',
                onPressed: () => onCopy(url, 'Адресу скопійовано'),
                icon: Icon(Icons.copy_rounded,
                    size: 18, color: AppColors.textSecondary),
              ),
            ],
          ),
          const SizedBox(height: 4),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: SelectableText(
              url,
              style: TextStyle(
                  fontFamily: 'monospace',
                  fontSize: 12,
                  color: AppColors.primaryDark),
            ),
          ),
          const SizedBox(height: 6),
          Text(desc,
              style: TextStyle(
                  fontSize: 12.5, color: AppColors.textSecondary, height: 1.35)),
        ],
      ),
    );
  }
}

class _CodeBlock extends StatelessWidget {
  const _CodeBlock({required this.code, required this.onCopy});
  final String code;
  final VoidCallback onCopy;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        color: AppColors.isDark ? const Color(0xFF07090D) : const Color(0xFF111821),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 8, 4),
            child: Row(
              children: [
                _dot(const Color(0xFFFF5F57)),
                _dot(const Color(0xFFFEBC2E)),
                _dot(const Color(0xFF28C840)),
                const Spacer(),
                TextButton.icon(
                  onPressed: onCopy,
                  icon: const Icon(Icons.copy_rounded,
                      size: 15, color: Colors.white70),
                  label: const Text('Копіювати',
                      style: TextStyle(color: Colors.white70, fontSize: 12.5)),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(18, 4, 18, 18),
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: SelectableText(
                code,
                style: const TextStyle(
                  fontFamily: 'monospace',
                  fontSize: 13,
                  height: 1.5,
                  color: Color(0xFFCFE7FF),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _dot(Color color) => Container(
        width: 11,
        height: 11,
        margin: const EdgeInsets.only(right: 7),
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      );
}
