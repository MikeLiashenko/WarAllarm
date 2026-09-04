import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../controllers/settings_controller.dart';
import '../controllers/threat_controller.dart';
import '../models/alert_event.dart';
import '../theme/app_colors.dart';
import '../utils/formatting.dart';
import '../widgets/analytics_charts.dart';
import '../widgets/glass_container.dart';
import '../widgets/responsive.dart';
import '../widgets/threat_circle.dart';
import 'home_shell.dart';

class NowScreen extends StatelessWidget {
  const NowScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final threat = context.watch<ThreatController>();
    final compactFeed = context.watch<SettingsController>().compactFeed;
    final visibleFeed = context.select<SettingsController, bool>(
            (s) => s.threatOnly)
        ? threat.feed.where((e) => e.isThreat).toList()
        : threat.feed;

    return PageBody(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Status hero
          GlassContainer(
            padding: const EdgeInsets.symmetric(vertical: 30, horizontal: 24),
            child: Column(
              children: [
                ThreatCircle(
                  active: threat.threatActive,
                  label: threat.threatActive ? 'THREAT' : 'CALM',
                  sublabel: threat.region,
                  size: Responsive.isCompact(context) ? 210 : 250,
                ),
                const SizedBox(height: 20),
                Text(
                  threat.statusLabel,
                  style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: threat.threatActive
                        ? AppColors.threatDark
                        : AppColors.textPrimary,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  threat.threatActive
                      ? 'Take shelter and stay updated.'
                      : 'No active threats in your region.',
                  style: const TextStyle(
                      fontSize: 13.5, color: AppColors.textSecondary),
                ),
              ],
            ),
          ),
          const SizedBox(height: 18),

          // Analytics above the feed
          AnalyticsSection(
            hourly: threat.hourlyBuckets,
            eventsToday: threat.eventsToday,
            threatsToday: threat.threatsToday,
            connected: threat.connected,
          ),
          const SizedBox(height: 22),

          // Feed heading
          Row(
            children: [
              const Text(
                'Live feed',
                style: TextStyle(
                  fontSize: 17,
                  fontWeight: FontWeight.w700,
                  color: AppColors.textPrimary,
                ),
              ),
              const SizedBox(width: 8),
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: AppColors.primarySoft,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Text(
                  '${visibleFeed.length}',
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                    color: AppColors.primaryDark,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          if (visibleFeed.isEmpty)
            const _EmptyFeed()
          else
            ...visibleFeed.map(
              (e) => Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: _FeedTile(event: e, compact: compactFeed),
              ),
            ),
          const SizedBox(height: 20),
        ],
      ),
    );
  }
}

class _FeedTile extends StatelessWidget {
  const _FeedTile({required this.event, required this.compact});
  final AlertEvent event;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final threat = event.isThreat;
    final accent = threat ? AppColors.threat : AppColors.primary;
    return Container(
      padding: EdgeInsets.all(compact ? 12 : 16),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(
          color: threat ? AppColors.threatSoft : AppColors.border,
        ),
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
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 42,
            height: 42,
            decoration: BoxDecoration(
              color: threat ? AppColors.threatSoft : AppColors.primarySoft,
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(
              threat ? Icons.warning_amber_rounded : Icons.check_circle_rounded,
              color: accent,
              size: 22,
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        event.title,
                        style: TextStyle(
                          fontSize: 15,
                          fontWeight: FontWeight.w700,
                          color: threat
                              ? AppColors.threatDark
                              : AppColors.textPrimary,
                        ),
                      ),
                    ),
                    Text(
                      timeAgo(event.timestamp),
                      style: const TextStyle(
                          fontSize: 12, color: AppColors.textMuted),
                    ),
                  ],
                ),
                if (!compact && event.message.isNotEmpty) ...[
                  const SizedBox(height: 4),
                  Text(
                    event.message,
                    style: const TextStyle(
                        fontSize: 13.5, color: AppColors.textSecondary),
                  ),
                ],
                const SizedBox(height: 6),
                Row(
                  children: [
                    const Icon(Icons.place_rounded,
                        size: 13, color: AppColors.textMuted),
                    const SizedBox(width: 3),
                    Text(
                      event.region,
                      style: const TextStyle(
                          fontSize: 12, color: AppColors.textMuted),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _EmptyFeed extends StatelessWidget {
  const _EmptyFeed();

  @override
  Widget build(BuildContext context) {
    return GlassContainer(
      padding: const EdgeInsets.symmetric(vertical: 40),
      child: Column(
        children: const [
          Icon(Icons.inbox_rounded, size: 40, color: AppColors.textMuted),
          SizedBox(height: 10),
          Text('No events to show',
              style: TextStyle(color: AppColors.textSecondary)),
        ],
      ),
    );
  }
}
