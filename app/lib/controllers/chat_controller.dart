import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';

import '../models/chat_message.dart';
import '../services/chat_media.dart';
import '../services/local_store.dart';
import '../services/rtdb_service.dart';

/// Where the user is on the way into the chat.
enum ChatGate { needAge, tooYoung, needProfile, ready }

/// Community chat.
///
/// Nobody but the server can write the public chat: the app drops a request into
/// its own write-only inbox `chat/in/<uid>` (signed with a device key it keeps
/// locally) and the server relay checks it (13+ profile, key, rate limit, file
/// type, bans) and publishes `chat/msgs`, `chat/media`, `chat/profiles`, `chat/ava`.
/// If it rejects a request, the reason appears in `chat/res/<uid>/<id>`.
class ChatController extends ChangeNotifier {
  ChatController(this._rtdb, this._store, {String suggestedName = ''})
      : suggestedName = suggestedName == 'Guest' ? '' : suggestedName {
    _uid = _store.getString('chat_uid');
    _key = _store.getString('chat_key');
    if (_uid.length < 16 || _key.length < 24) {
      final r = Random.secure();
      String hex(int n) => List.generate(n, (_) => r.nextInt(16).toRadixString(16)).join();
      _uid = hex(28);
      _key = hex(48);
      _store.setString('chat_uid', _uid);
      _store.setString('chat_key', _key);
    }
    _dob = DateTime.tryParse(_store.getString('chat_dob'));
    _kidOk = _store.getBool('chat_kid_ok');
    _profileSaved = _store.getBool('chat_profile_saved');
    _blocked = _store.getString('chat_blocked').split(',').where((e) => e.isNotEmpty).toSet();
    _lastSeen = int.tryParse(_store.getString('chat_seen')) ?? 0;
    try {
      final p = jsonDecode(_store.getString('chat_profile', fallback: '{}'));
      if (p is Map) _draft = Map<String, dynamic>.from(p);
    } catch (_) {}
  }

  final RtdbService _rtdb;
  final LocalStore _store;
  final String suggestedName;

  late String _uid, _key;
  String get uid => _uid;

  // ── Age gate ───────────────────────────────────────────────────────────────
  DateTime? _dob;
  DateTime? get dob => _dob;
  bool _profileSaved = false;

  static int ageAt(DateTime dob, DateTime now) {
    var a = now.year - dob.year;
    if (now.month < dob.month || (now.month == dob.month && now.day < dob.day)) a--;
    return a;
  }

  ChatGate get gate {
    final d = _dob;
    if (d == null) return ChatGate.needAge;
    if (ageAt(d, DateTime.now()) < 13 && !_kidOk) return ChatGate.tooYoung;
    if (!_profileSaved) return ChatGate.needProfile;
    return ChatGate.ready;
  }

  // ── Parental permission (under 13) ─────────────────────────────────────────
  // A parent who is a chat admin opens the chat for THIS phone by its ID
  // (or the server owner lists it in chat_kids). The server publishes chat/kids/<uid>.
  bool _kidOk = false;
  bool get isUnder13 => _dob != null && ageAt(_dob!, DateTime.now()) < 13;
  StreamSubscription<dynamic>? _kidSub;

  void watchParentalPermission() {
    if (_kidSub != null || !isUnder13) return;
    _kidSub = _rtdb.stream('chat/kids/$_uid').listen((v) {
      final ok = v == true;
      if (ok == _kidOk) return;
      _kidOk = ok;
      _store.setBool('chat_kid_ok', ok);
      notifyListeners();
    }, onError: (_) {});
  }

  /// Admin: open (or close) the chat for a child's phone by its ID.
  Future<void> allowChild(String id, {bool on = true}) =>
      _simple('kid', {'uid': id.trim().toLowerCase(), 'on': on});

  /// The date of birth is asked once: a "too young" answer stays (no retry until reinstall).
  Future<void> setBirthDate(DateTime d) async {
    _dob = DateTime(d.year, d.month, d.day);
    await _store.setString('chat_dob', _dob!.toIso8601String());
    notifyListeners();
  }

  // ── My profile ─────────────────────────────────────────────────────────────
  Map<String, dynamic> _draft = {};
  String get myName => (_draft['name'] ?? '').toString();
  String get myBio => (_draft['bio'] ?? '').toString();
  String get myCity => (_draft['city'] ?? '').toString();
  Uint8List? get myAvatar {
    final a = _store.getString('chat_ava');
    if (a.isEmpty) return null;
    try {
      return base64Decode(a);
    } catch (_) {
      return null;
    }
  }

  String? _profileReq;
  String? profileError;
  bool savingProfile = false;

  /// Sends the profile. [avatar] — new JPEG (already prepared), [removeAvatar] — drop it.
  Future<bool> saveProfile({
    required String name,
    required String bio,
    required String city,
    Uint8List? avatar,
    bool removeAvatar = false,
  }) async {
    savingProfile = true;
    profileError = null;
    notifyListeners();
    try {
      final body = <String, dynamic>{
        'name': name.trim(),
        'bio': bio.trim(),
        'city': city.trim(),
        'age13': !isUnder13,
      };
      if (avatar != null) body['ava'] = base64Encode(avatar);
      if (removeAvatar) body['avaDel'] = true;
      _profileReq = await _request('profile', body);
      _draft = {'name': name.trim(), 'bio': bio.trim(), 'city': city.trim()};
      await _store.setString('chat_profile', jsonEncode(_draft));
      if (avatar != null) await _store.setString('chat_ava', base64Encode(avatar));
      if (removeAvatar) await _store.setString('chat_ava', '');
      _profileSaved = true;
      await _store.setBool('chat_profile_saved', true);
      start();
      return true;
    } catch (_) {
      profileError = 'Немає зв\'язку з сервером — спробуй ще раз';
      return false;
    } finally {
      savingProfile = false;
      notifyListeners();
    }
  }

  // ── Live data ──────────────────────────────────────────────────────────────
  final List<StreamSubscription<dynamic>> _subs = [];
  bool _started = false;
  bool connected = false;

  List<ChatMessage> _server = const [];
  final List<ChatMessage> _pending = [];
  final Map<String, ChatProfile> _profiles = {};
  ChatProfile? profile(String uid) => _profiles[uid];
  ChatProfile? get me => _profiles[_uid];
  bool get isAdmin => me?.adm == true;

  Set<String> _blocked = {};
  bool isBlocked(String uid) => _blocked.contains(uid);

  int _lastSeen = 0;

  List<ChatMessage> get messages {
    final out = <ChatMessage>[
      for (final m in _server)
        if (!_blocked.contains(m.uid) && (!m.hidden || isAdmin || m.uid == _uid)) m,
      ..._pending,
    ];
    return out;
  }

  /// New messages from others since the chat was last opened.
  int get unread => _server
      .where((m) => m.uid != _uid && !_blocked.contains(m.uid) && m.timestamp.millisecondsSinceEpoch > _lastSeen)
      .length;

  void markSeen() {
    if (_server.isEmpty) return;
    final last = _server.last.timestamp.millisecondsSinceEpoch;
    if (last <= _lastSeen) return;
    _lastSeen = last;
    _store.setString('chat_seen', '$last');
    // No notify: called from build.
  }

  void start() {
    if (_started) return;
    _started = true;
    _subs.add(_rtdb.stream('chat/msgs').listen(_onMsgs, onError: (_) {}));
    _subs.add(_rtdb.stream('chat/profiles').listen(_onProfiles, onError: (_) {}));
    _subs.add(_rtdb.stream('chat/res/$_uid').listen(_onRes, onError: (_) {}));
    watchParentalPermission();
  }

  void _onMsgs(dynamic data) {
    connected = true;
    final items = <ChatMessage>[];
    if (data is Map) {
      data.forEach((k, v) {
        if (v is Map) items.add(ChatMessage.fromMap(k.toString(), v));
      });
    }
    items.sort((a, b) {
      final c = a.timestamp.compareTo(b.timestamp);
      return c != 0 ? c : a.id.compareTo(b.id);
    });
    _server = items;
    final ids = items.map((m) => m.id).toSet();
    _pending.removeWhere((p) => ids.contains(p.id));
    notifyListeners();
  }

  void _onProfiles(dynamic data) {
    _profiles.clear();
    if (data is Map) {
      data.forEach((k, v) {
        if (v is Map) _profiles[k.toString()] = ChatProfile.fromMap(k.toString(), v);
      });
    }
    notifyListeners();
  }

  final Set<String> _seenRes = {};
  void _onRes(dynamic data) {
    if (data is! Map) return;
    var changed = false;
    data.forEach((k, v) {
      final id = k.toString();
      if (_seenRes.contains(id) || v is! Map) return;
      _seenRes.add(id);
      final why = (v['why'] ?? 'Не прийнято').toString();
      if (id == _profileReq) {
        profileError = why;
        _profileSaved = false;
        _store.setBool('chat_profile_saved', false);
        changed = true;
      }
      final i = _pending.indexWhere((p) => p.id == id);
      if (i >= 0) {
        _pending[i] = _pending[i].copyWith(pending: false, error: why);
        changed = true;
      } else if (id != _profileReq) {
        lastNotice = why;
        changed = true;
      }
    });
    if (changed) notifyListeners();
  }

  /// A one-off server answer to show as a snackbar (e.g. a rejected reaction).
  String? lastNotice;
  String? takeNotice() {
    final n = lastNotice;
    lastNotice = null;
    return n;
  }

  // ── Sending ────────────────────────────────────────────────────────────────
  Future<String> _request(String type, Map<String, dynamic> body) =>
      _rtdb.push('chat/in/$_uid', {
        'k': _key,
        't': type,
        'ts': {'.sv': 'timestamp'},
        ...body,
      });

  int _tmp = 0;

  Future<void> _sendMessage(ChatMessage local, Map<String, dynamic> body) async {
    _pending.add(local);
    notifyListeners();
    try {
      final id = await _request('msg', body);
      final i = _pending.indexWhere((p) => identical(p, local));
      if (i >= 0) {
        _pending[i] = ChatMessage(
          id: id,
          uid: local.uid,
          name: local.name,
          type: local.type,
          text: local.text,
          timestamp: local.timestamp,
          w: local.w,
          h: local.h,
          dur: local.dur,
          wave: local.wave,
          re: local.re,
          reName: local.reName,
          reText: local.reText,
          pending: true,
          localBytes: local.localBytes,
        );
        _retryBodies[id] = body;
      }
      // The server publishes the media under the same id — no need to download our own file.
      final lb = local.localBytes;
      if (lb != null) _media[id] = Future.value(Uint8List.fromList(lb));
      notifyListeners();
      // No echo from the server in 25 s → show it as not delivered.
      Timer(const Duration(seconds: 25), () {
        final j = _pending.indexWhere((p) => p.id == id && p.pending);
        if (j >= 0) {
          _pending[j] = _pending[j].copyWith(pending: false, error: 'Сервер не відповів — спробуй ще');
          notifyListeners();
        }
      });
    } catch (_) {
      final i = _pending.indexWhere((p) => identical(p, local));
      if (i >= 0) {
        _retryBodies[local.id] = body;
        _pending[i] = local.copyWith(pending: false, error: 'Немає зв\'язку');
        notifyListeners();
      }
    }
  }

  final Map<String, Map<String, dynamic>> _retryBodies = {};

  Future<void> retry(ChatMessage m) async {
    final body = _retryBodies.remove(m.id);
    _pending.removeWhere((p) => p.id == m.id);
    if (body == null) {
      notifyListeners();
      return;
    }
    await _sendMessage(m.copyWith(pending: true), body);
  }

  void discard(ChatMessage m) {
    _pending.removeWhere((p) => p.id == m.id);
    _retryBodies.remove(m.id);
    notifyListeners();
  }

  ChatMessage _local(String type, String text, ChatMessage? reply,
          {double w = 0, double h = 0, double dur = 0, List<int> wave = const [], List<int>? bytes}) =>
      ChatMessage(
        id: 'local-${_tmp++}',
        uid: _uid,
        name: myName,
        type: type,
        text: text,
        timestamp: DateTime.now(),
        w: w,
        h: h,
        dur: dur,
        wave: wave,
        re: reply?.id,
        reName: reply?.name,
        reText: reply?.preview,
        pending: true,
        localBytes: bytes,
      );

  Map<String, dynamic> _replyBody(ChatMessage? reply) => reply == null
      ? {}
      : {
          're': reply.id,
          'reName': reply.name,
          'reText': reply.preview.length > 120 ? reply.preview.substring(0, 120) : reply.preview,
        };

  Future<void> sendText(String text, {ChatMessage? replyTo}) async {
    final t = text.trim();
    if (t.isEmpty) return;
    await _sendMessage(_local('text', t, replyTo), {'text': t, ..._replyBody(replyTo)});
  }

  /// [raw] — any picked picture; it is resized and stripped of EXIF first.
  Future<bool> sendImage(Uint8List raw, {String caption = '', ChatMessage? replyTo}) async {
    final p = await ChatMedia.photo(raw);
    if (p == null) return false;
    final c = caption.trim();
    await _sendMessage(
      _local('img', c, replyTo, w: p.width.toDouble(), h: p.height.toDouble(), bytes: p.bytes),
      {
        'img': base64Encode(p.bytes),
        'w': p.width,
        'h': p.height,
        if (c.isNotEmpty) 'text': c,
        ..._replyBody(replyTo),
      },
    );
    return true;
  }

  Future<void> sendVoice(Uint8List bytes, double seconds, List<int> wave, {ChatMessage? replyTo}) async {
    await _sendMessage(
      _local('voice', '', replyTo, dur: seconds, wave: wave, bytes: bytes),
      {
        'voice': base64Encode(bytes),
        'dur': double.parse(seconds.toStringAsFixed(1)),
        'wave': wave,
        ..._replyBody(replyTo),
      },
    );
  }

  Future<void> _simple(String type, Map<String, dynamic> body) async {
    try {
      await _request(type, body);
    } catch (_) {
      lastNotice = 'Немає зв\'язку з сервером';
      notifyListeners();
    }
  }

  Future<void> delete(ChatMessage m) => _simple('del', {'id': m.id});
  Future<void> edit(ChatMessage m, String text) => _simple('edit', {'id': m.id, 'text': text.trim()});
  Future<void> react(ChatMessage m, String code) => _simple('react', {'id': m.id, 'e': code});
  Future<void> report(ChatMessage m, String why) => _simple('report', {'id': m.id, 'why': why});
  Future<void> ban(String uid) => _simple('ban', {'uid': uid});

  Future<void> setBlocked(String uid, bool on) async {
    if (on) {
      _blocked.add(uid);
    } else {
      _blocked.remove(uid);
    }
    await _store.setString('chat_blocked', _blocked.join(','));
    notifyListeners();
  }

  // ── Media (fetched on demand, cached) ──────────────────────────────────────
  final Map<String, Future<Uint8List?>> _media = {};
  final Map<String, Future<Uint8List?>> _avas = {};

  Future<Uint8List?> media(String mid) => _media.putIfAbsent(mid, () async {
        try {
          final v = await _rtdb.getOnce('chat/media/$mid');
          if (v is Map && v['data'] is String) return base64Decode(v['data'] as String);
        } catch (_) {
          Future<void>.delayed(const Duration(seconds: 10), () => _media.remove(mid));
        }
        return null;
      });

  Future<Uint8List?> avatar(String uid) {
    if (uid == _uid && myAvatar != null && (me == null || me!.avaRev > 0)) {
      return Future.value(myAvatar);
    }
    final rev = _profiles[uid]?.avaRev ?? 0;
    if (rev == 0) return Future.value(null);
    return _avas.putIfAbsent('$uid:$rev', () async {
      try {
        final v = await _rtdb.getOnce('chat/ava/$uid');
        if (v is String) return base64Decode(v);
      } catch (_) {
        Future<void>.delayed(const Duration(seconds: 10), () => _avas.remove('$uid:$rev'));
      }
      return null;
    });
  }

  /// A voice message as a local file (players want a path).
  Future<String?> voiceFile(ChatMessage m) async {
    final bytes = m.localBytes != null ? Uint8List.fromList(m.localBytes!) : (m.mid == null ? null : await media(m.mid!));
    if (bytes == null) return null;
    final dir = await getTemporaryDirectory();
    final f = File('${dir.path}/chat_voice_${m.id}.m4a');
    if (!await f.exists() || await f.length() != bytes.length) await f.writeAsBytes(bytes, flush: true);
    return f.path;
  }

  @override
  void dispose() {
    for (final s in _subs) {
      s.cancel();
    }
    _kidSub?.cancel();
    super.dispose();
  }
}
