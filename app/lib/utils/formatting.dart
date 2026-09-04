import 'package:intl/intl.dart';

/// Human-friendly relative time, e.g. "3m ago", "2h ago".
String timeAgo(DateTime time) {
  final diff = DateTime.now().difference(time);
  if (diff.inSeconds < 60) return 'just now';
  if (diff.inMinutes < 60) return '${diff.inMinutes}m ago';
  if (diff.inHours < 24) return '${diff.inHours}h ago';
  if (diff.inDays < 7) return '${diff.inDays}d ago';
  return DateFormat('d MMM').format(time);
}

/// Clock time, e.g. "14:05".
String clock(DateTime time) => DateFormat('HH:mm').format(time);
