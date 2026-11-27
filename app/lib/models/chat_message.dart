/// One chat message as published by the server relay at `chat/msgs/<id>`.
class ChatMessage {
  final String id;
  final String uid;
  final String name;

  /// `text`, `img` or `voice`.
  final String type;
  final String text;
  final DateTime timestamp;

  /// Media id at `chat/media/<mid>` (photo / voice), if any.
  final String? mid;
  final double w, h;
  final double dur;
  final List<int> wave;

  /// Reply: id of the quoted message + a short preview of it.
  final String? re;
  final String? reName;
  final String? reText;

  final bool adm;
  final bool hidden;
  final bool edited;

  /// Reactions: code (`like`, `heart`…) → set of uids.
  final Map<String, Set<String>> rx;

  /// Local-only: still on its way to the server / rejected (with reason).
  final bool pending;
  final String? error;

  /// Local-only: a picked photo / recorded voice before the server published it.
  final List<int>? localBytes;

  const ChatMessage({
    required this.id,
    required this.uid,
    required this.name,
    required this.type,
    required this.text,
    required this.timestamp,
    this.mid,
    this.w = 0,
    this.h = 0,
    this.dur = 0,
    this.wave = const [],
    this.re,
    this.reName,
    this.reText,
    this.adm = false,
    this.hidden = false,
    this.edited = false,
    this.rx = const {},
    this.pending = false,
    this.error,
    this.localBytes,
  });

  bool get isImage => type == 'img';
  bool get isVoice => type == 'voice';

  /// Short one-line preview (for replies and notifications).
  String get preview {
    if (isImage) return text.isEmpty ? '📷 Фото' : '📷 $text';
    if (isVoice) return '🎤 Голосове ${fmtDur(dur)}';
    return text;
  }

  ChatMessage copyWith({bool? pending, String? error}) => ChatMessage(
        id: id,
        uid: uid,
        name: name,
        type: type,
        text: text,
        timestamp: timestamp,
        mid: mid,
        w: w,
        h: h,
        dur: dur,
        wave: wave,
        re: re,
        reName: reName,
        reText: reText,
        adm: adm,
        hidden: hidden,
        edited: edited,
        rx: rx,
        pending: pending ?? this.pending,
        error: error,
        localBytes: localBytes,
      );

  factory ChatMessage.fromMap(String id, Map<dynamic, dynamic> m) {
    final rx = <String, Set<String>>{};
    final r = m['rx'];
    if (r is Map) {
      r.forEach((k, v) {
        if (v is Map && v.isNotEmpty) {
          rx[k.toString()] = v.keys.map((e) => e.toString()).toSet();
        }
      });
    }
    final wave = <int>[];
    final wv = m['wave'];
    if (wv is List) {
      for (final x in wv) {
        if (x is num) wave.add(x.round().clamp(0, 100));
      }
    }
    return ChatMessage(
      id: id,
      uid: (m['uid'] ?? '').toString(),
      name: (m['name'] ?? 'Гість').toString(),
      type: (m['t'] ?? 'text').toString(),
      text: (m['text'] ?? '').toString(),
      timestamp: _asTime(m['ts']),
      mid: m['mid']?.toString(),
      w: _num(m['w']),
      h: _num(m['h']),
      dur: _num(m['dur']),
      wave: wave,
      re: m['re']?.toString(),
      reName: m['reName']?.toString(),
      reText: m['reText']?.toString(),
      adm: m['adm'] == true,
      hidden: m['hidden'] == true,
      edited: m['edited'] != null,
      rx: rx,
    );
  }

  static double _num(dynamic v) => v is num ? v.toDouble() : 0;

  static DateTime _asTime(dynamic v) {
    if (v is num) {
      final i = v.toInt();
      return DateTime.fromMillisecondsSinceEpoch(i < 100000000000 ? i * 1000 : i);
    }
    return DateTime.now();
  }
}

/// A public chat profile at `chat/profiles/<uid>` (avatar itself lives at `chat/ava/<uid>`).
class ChatProfile {
  final String uid;
  final String name;
  final String bio;
  final String city;
  final DateTime? since;
  final int avaRev;
  final bool adm;

  const ChatProfile({
    required this.uid,
    required this.name,
    this.bio = '',
    this.city = '',
    this.since,
    this.avaRev = 0,
    this.adm = false,
  });

  factory ChatProfile.fromMap(String uid, Map<dynamic, dynamic> m) => ChatProfile(
        uid: uid,
        name: (m['name'] ?? 'Гість').toString(),
        bio: (m['bio'] ?? '').toString(),
        city: (m['city'] ?? '').toString(),
        since: m['since'] is num
            ? DateTime.fromMillisecondsSinceEpoch((m['since'] as num).toInt())
            : null,
        avaRev: m['avaRev'] is num ? (m['avaRev'] as num).toInt() : 0,
        adm: m['adm'] == true,
      );
}

/// Reaction codes the server accepts, with their emoji.
const chatReactions = <String, String>{
  'like': '👍',
  'heart': '❤️',
  'lol': '😂',
  'wow': '😮',
  'sad': '😢',
  'pray': '🙏',
  'fire': '🔥',
  'ua': '🇺🇦',
};

String fmtDur(double s) {
  final t = s.round();
  return '${t ~/ 60}:${(t % 60).toString().padLeft(2, '0')}';
}
