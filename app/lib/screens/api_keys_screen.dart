import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../controllers/api_keys_controller.dart';
import '../firebase_config.dart';
import '../models/api_key.dart';
import '../theme/app_colors.dart';
import '../utils/formatting.dart';
import '../widgets/glass_container.dart';
import '../widgets/section_header.dart';
import 'home_shell.dart';

class ApiKeysScreen extends StatefulWidget {
  const ApiKeysScreen({super.key});

  @override
  State<ApiKeysScreen> createState() => _ApiKeysScreenState();
}

class _ApiKeysScreenState extends State<ApiKeysScreen> {
  int _lang = 0; // 0=Python 1=JS 2=cURL
  static const _langs = ['Python', 'JavaScript', 'cURL'];

  @override
  Widget build(BuildContext context) {
    final controller = context.watch<ApiKeysController>();
    final keys = controller.keys;
    final sampleKey = keys.isNotEmpty ? keys.first.key : 'wa_live_your_api_key';

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            title: 'API Keys',
            subtitle: 'Generate keys and integrate the RTDB',
            icon: Icons.vpn_key_rounded,
            trailing: ElevatedButton.icon(
              onPressed: () => _showGenerate(context, controller),
              icon: const Icon(Icons.add, size: 18),
              label: const Text('Generate key'),
            ),
          ),
          const SizedBox(height: 18),

          // Keys list
          GlassContainer(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Active keys',
                    style: TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textPrimary)),
                const SizedBox(height: 12),
                if (keys.isEmpty)
                  const Padding(
                    padding: EdgeInsets.symmetric(vertical: 20),
                    child: Center(
                      child: Text('No keys yet — generate your first key.',
                          style: TextStyle(color: AppColors.textSecondary)),
                    ),
                  )
                else
                  ...keys.map((k) => _KeyRow(
                        apiKey: k,
                        onCopy: () => _copy(context, k.key, 'API key copied'),
                        onToggle: () => controller.revoke(k.id),
                        onDelete: () => controller.remove(k.id),
                      )),
              ],
            ),
          ),
          const SizedBox(height: 18),

          // Integration snippets
          GlassContainer(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const Text('Integration',
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
                  code: _snippet(_lang, sampleKey),
                  onCopy: () =>
                      _copy(context, _snippet(_lang, sampleKey), 'Snippet copied'),
                ),
              ],
            ),
          ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }

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

  void _copy(BuildContext context, String text, String msg) {
    Clipboard.setData(ClipboardData(text: text));
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
  }

  void _showGenerate(BuildContext context, ApiKeysController controller) {
    final labelCtl = TextEditingController();
    showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: AppColors.surface,
        shape:
            RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        title: const Text('Generate API key'),
        content: SizedBox(
          width: 340,
          child: TextField(
            controller: labelCtl,
            autofocus: true,
            decoration: const InputDecoration(
              hintText: 'Key label (e.g. Production)',
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          ElevatedButton(
            onPressed: () {
              controller.generate(labelCtl.text);
              Navigator.pop(context);
            },
            child: const Text('Generate'),
          ),
        ],
      ),
    );
  }

  String _snippet(int lang, String key) {
    final url = FirebaseConfig.databaseUrl;
    switch (lang) {
      case 0:
        return '''import requests

API_KEY = "$key"
BASE = "$url"

# Read the live threat status
res = requests.get(f"{BASE}/status.json", headers={"X-API-Key": API_KEY})
print(res.json())''';
      case 1:
        return '''const API_KEY = "$key";
const BASE = "$url";

// Read the live threat status
const res = await fetch(`\${BASE}/status.json`, {
  headers: { "X-API-Key": API_KEY },
});
console.log(await res.json());''';
      default:
        return '''curl "$url/status.json" \\
  -H "X-API-Key: $key"''';
    }
  }
}

class _KeyRow extends StatelessWidget {
  const _KeyRow({
    required this.apiKey,
    required this.onCopy,
    required this.onToggle,
    required this.onDelete,
  });

  final ApiKey apiKey;
  final VoidCallback onCopy;
  final VoidCallback onToggle;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final active = apiKey.active;
    return Container(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppColors.border),
      ),
      child: Row(
        children: [
          Container(
            width: 42,
            height: 42,
            decoration: BoxDecoration(
              color: active ? AppColors.primarySoft : AppColors.divider,
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(Icons.vpn_key_rounded,
                color: active ? AppColors.primary : AppColors.textMuted,
                size: 20),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        apiKey.label,
                        style: const TextStyle(
                            fontSize: 14.5,
                            fontWeight: FontWeight.w700,
                            color: AppColors.textPrimary),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 8, vertical: 2),
                      decoration: BoxDecoration(
                        color: active
                            ? AppColors.safeSoft
                            : AppColors.threatSoft,
                        borderRadius: BorderRadius.circular(20),
                      ),
                      child: Text(
                        active ? 'Active' : 'Revoked',
                        style: TextStyle(
                          fontSize: 10.5,
                          fontWeight: FontWeight.w700,
                          color:
                              active ? AppColors.safe : AppColors.threatDark,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 3),
                Text(
                  '${apiKey.masked}  ·  ${timeAgo(apiKey.createdAt)}',
                  style: const TextStyle(
                      fontSize: 12,
                      color: AppColors.textMuted,
                      fontFamily: 'monospace'),
                ),
              ],
            ),
          ),
          IconButton(
            tooltip: 'Copy',
            onPressed: onCopy,
            icon: const Icon(Icons.copy_rounded,
                size: 18, color: AppColors.textSecondary),
          ),
          IconButton(
            tooltip: active ? 'Revoke' : 'Re-activate',
            onPressed: onToggle,
            icon: Icon(
              active ? Icons.block_rounded : Icons.check_circle_rounded,
              size: 18,
              color: active ? AppColors.threat : AppColors.safe,
            ),
          ),
          IconButton(
            tooltip: 'Delete',
            onPressed: onDelete,
            icon: const Icon(Icons.delete_outline_rounded,
                size: 18, color: AppColors.textMuted),
          ),
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
        color: const Color(0xFF0F2035),
        borderRadius: BorderRadius.circular(16),
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
                  label: const Text('Copy',
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
