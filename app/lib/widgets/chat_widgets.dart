import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';
import 'package:provider/provider.dart';
import 'package:record/record.dart';

import '../controllers/chat_controller.dart';
import '../models/chat_message.dart';
import '../theme/app_colors.dart';

/// A stable per-user colour for names and initials.
Color chatUserColor(String uid) {
  const palette = [
    Color(0xFF4F9DFF),
    Color(0xFFFF8A4C),
    Color(0xFF3BC98E),
    Color(0xFFB77CFF),
    Color(0xFFFF5C8A),
    Color(0xFF2EC4D6),
    Color(0xFFE8B730),
    Color(0xFF8A9BFF),
  ];
  var h = 0;
  for (final c in uid.codeUnits) {
    h = (h * 31 + c) & 0x7fffffff;
  }
  return palette[h % palette.length];
}

/// Round avatar: the user's photo, or a coloured initial until it loads / if none.
class ChatAvatar extends StatefulWidget {
  const ChatAvatar({super.key, required this.uid, required this.name, this.size = 34, this.preview});

  final String uid;
  final String name;
  final double size;

  /// Show these bytes instead (the profile editor's preview).
  final Uint8List? preview;

  @override
  State<ChatAvatar> createState() => _ChatAvatarState();
}

class _ChatAvatarState extends State<ChatAvatar> {
  Uint8List? _bytes;
  String _key = '';

  void _resolve() {
    final chat = context.read<ChatController>();
    final rev = chat.profile(widget.uid)?.avaRev ?? 0;
    final local = widget.uid == chat.uid ? (chat.myAvatar?.length ?? 0) : 0;
    final key = '${widget.uid}:$rev:$local';
    if (key == _key) return;
    _key = key;
    chat.avatar(widget.uid).then((b) {
      if (mounted && key == _key) setState(() => _bytes = b);
    });
  }

  @override
  Widget build(BuildContext context) {
    context.select<ChatController, int>((c) => c.profile(widget.uid)?.avaRev ?? 0);
    _resolve();
    final bytes = widget.preview ?? _bytes;
    final s = widget.size;
    if (bytes != null) {
      return ClipOval(
        child: Image.memory(bytes, width: s, height: s, fit: BoxFit.cover, gaplessPlayback: true),
      );
    }
    final color = chatUserColor(widget.uid);
    final initial = widget.name.trim().isEmpty ? '?' : widget.name.trim().characters.first.toUpperCase();
    return Container(
      width: s,
      height: s,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [color, Color.lerp(color, Colors.black, 0.35)!],
        ),
      ),
      child: Text(initial,
          style: TextStyle(color: Colors.white, fontWeight: FontWeight.w800, fontSize: s * 0.42)),
    );
  }
}

/// A chat photo: loads on demand, keeps its aspect ratio, opens full screen on tap.
class ChatImage extends StatefulWidget {
  const ChatImage({super.key, required this.message, this.maxWidth = 260});

  final ChatMessage message;
  final double maxWidth;

  @override
  State<ChatImage> createState() => _ChatImageState();
}

class _ChatImageState extends State<ChatImage> {
  late Future<Uint8List?> _future;
  String _src = '';

  void _load() {
    final m = widget.message;
    final src = m.localBytes != null ? 'local:${m.id}' : 'mid:${m.mid}';
    if (src == _src) return;
    _src = src;
    final chat = context.read<ChatController>();
    _future = m.localBytes != null
        ? Future.value(Uint8List.fromList(m.localBytes!))
        : (m.mid == null ? Future.value(null) : chat.media(m.mid!));
  }

  @override
  Widget build(BuildContext context) {
    _load();
    final message = widget.message;
    final ratio = (message.w > 0 && message.h > 0) ? (message.w / message.h).clamp(0.5, 2.0) : 1.3;
    return ConstrainedBox(
      constraints: BoxConstraints(maxWidth: widget.maxWidth, maxHeight: 340),
      child: AspectRatio(
        aspectRatio: ratio.toDouble(),
        child: FutureBuilder<Uint8List?>(
          future: _future,
          builder: (context, snap) {
            final b = snap.data;
            if (b == null) {
              return Container(
                color: AppColors.surface2,
                alignment: Alignment.center,
                child: snap.connectionState == ConnectionState.done
                    ? Icon(Icons.broken_image_outlined, color: AppColors.textMuted)
                    : const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2)),
              );
            }
            return GestureDetector(
              onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(
                builder: (_) => ChatImageViewer(bytes: b, title: message.name),
              )),
              child: Image.memory(b, fit: BoxFit.cover, gaplessPlayback: true),
            );
          },
        ),
      ),
    );
  }
}

class ChatImageViewer extends StatelessWidget {
  const ChatImageViewer({super.key, required this.bytes, required this.title});
  final Uint8List bytes;
  final String title;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(title, style: const TextStyle(fontSize: 16)),
      ),
      body: Center(
        child: InteractiveViewer(
          maxScale: 6,
          child: Image.memory(bytes, fit: BoxFit.contain),
        ),
      ),
    );
  }
}

/// Voice message player with a waveform.
class VoicePlayer extends StatefulWidget {
  const VoicePlayer({super.key, required this.message, required this.mine});
  final ChatMessage message;
  final bool mine;

  @override
  State<VoicePlayer> createState() => _VoicePlayerState();
}

class _VoicePlayerState extends State<VoicePlayer> {
  static _VoicePlayerState? _playing;
  AudioPlayer? _player;
  final List<StreamSubscription<dynamic>> _subs = [];
  bool _loading = false, _on = false;
  Duration _pos = Duration.zero;

  Future<void> _toggle() async {
    final chat = context.read<ChatController>();
    final messenger = ScaffoldMessenger.of(context);
    if (_on) {
      await _player?.pause();
      if (mounted) setState(() => _on = false);
      return;
    }
    if (_playing != null && _playing != this) await _playing!._stop();
    _playing = this;
    if (_player == null) {
      if (!mounted) return;
      setState(() => _loading = true);
      final path = await chat.voiceFile(widget.message);
      if (!mounted) return;
      if (path == null) {
        setState(() => _loading = false);
        messenger.showSnackBar(const SnackBar(content: Text('Не вдалося завантажити голосове')));
        return;
      }
      final p = AudioPlayer();
      _player = p;
      _subs.add(p.onPositionChanged.listen((d) {
        if (mounted) setState(() => _pos = d);
      }));
      _subs.add(p.onPlayerComplete.listen((_) {
        if (mounted) {
          setState(() {
            _on = false;
            _pos = Duration.zero;
          });
        }
      }));
      await p.setSource(DeviceFileSource(path));
      if (!mounted) return;
      setState(() => _loading = false);
    }
    await _player!.resume();
    if (mounted) setState(() => _on = true);
  }

  Future<void> _stop() async {
    await _player?.pause();
    if (mounted) setState(() => _on = false);
  }

  @override
  void dispose() {
    if (_playing == this) _playing = null;
    for (final s in _subs) {
      s.cancel();
    }
    _player?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final m = widget.message;
    final fg = widget.mine ? Colors.white : AppColors.primary;
    final dim = widget.mine ? Colors.white.withValues(alpha: 0.45) : AppColors.textMuted.withValues(alpha: 0.5);
    final total = m.dur <= 0 ? 1.0 : m.dur;
    final prog = (_pos.inMilliseconds / 1000 / total).clamp(0.0, 1.0);
    final wave = m.wave.isNotEmpty ? m.wave : List<int>.filled(32, 30);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        InkResponse(
          onTap: _loading ? null : _toggle,
          radius: 24,
          child: Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: widget.mine ? Colors.white.withValues(alpha: 0.22) : AppColors.primary.withValues(alpha: 0.15),
            ),
            child: _loading
                ? Padding(
                    padding: const EdgeInsets.all(11),
                    child: CircularProgressIndicator(strokeWidth: 2, color: fg),
                  )
                : Icon(_on ? Icons.pause_rounded : Icons.play_arrow_rounded, color: fg, size: 26),
          ),
        ),
        const SizedBox(width: 10),
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            SizedBox(
              width: 150,
              height: 26,
              child: CustomPaint(painter: _WavePainter(wave, prog, fg, dim)),
            ),
            const SizedBox(height: 2),
            Text(
              _on || _pos > Duration.zero ? fmtDur(_pos.inMilliseconds / 1000) : fmtDur(m.dur),
              style: TextStyle(fontSize: 11, color: widget.mine ? Colors.white70 : AppColors.textSecondary),
            ),
          ],
        ),
      ],
    );
  }
}

class _WavePainter extends CustomPainter {
  _WavePainter(this.wave, this.progress, this.on, this.off);
  final List<int> wave;
  final double progress;
  final Color on, off;

  @override
  void paint(Canvas canvas, Size size) {
    final n = wave.length;
    if (n == 0) return;
    final step = size.width / n;
    final bw = math.max(1.5, step * 0.55);
    final p = Paint()..strokeCap = StrokeCap.round..strokeWidth = bw;
    for (var i = 0; i < n; i++) {
      final v = (wave[i] / 100).clamp(0.08, 1.0);
      final x = i * step + step / 2;
      final h = size.height * v;
      p.color = (i + 0.5) / n <= progress ? on : off;
      canvas.drawLine(Offset(x, (size.height - h) / 2), Offset(x, (size.height + h) / 2), p);
    }
  }

  @override
  bool shouldRepaint(_WavePainter o) => o.progress != progress || o.wave != wave || o.on != on;
}

/// Recording a voice message: start → (cancel | send). Max ~2 minutes.
class VoiceRecorder {
  final AudioRecorder _rec = AudioRecorder();
  StreamSubscription<Amplitude>? _amp;
  final List<double> _levels = [];
  final Stopwatch _clock = Stopwatch();
  String? _path;

  Duration get elapsed => _clock.elapsed;
  List<double> get levels => _levels;
  static const maxLength = Duration(seconds: 115);

  /// null on success, otherwise a reason to show.
  Future<String?> start() async {
    try {
      if (!await _rec.hasPermission()) return 'Дай застосунку доступ до мікрофона';
      if (!await _rec.isEncoderSupported(AudioEncoder.aacLc)) return 'Запис голосу не підтримується на цьому пристрої';
      final dir = await getTemporaryDirectory();
      _path = '${dir.path}/chat_rec_${DateTime.now().millisecondsSinceEpoch}.m4a';
      _levels.clear();
      await _rec.start(
        const RecordConfig(encoder: AudioEncoder.aacLc, bitRate: 24000, sampleRate: 16000, numChannels: 1),
        path: _path!,
      );
      _clock
        ..reset()
        ..start();
      _amp = _rec.onAmplitudeChanged(const Duration(milliseconds: 100)).listen((a) {
        // dBFS: -50 (тиша) … 0 (максимум) → 0…100.
        _levels.add(((a.current + 50) / 50 * 100).clamp(0, 100).toDouble());
      });
      return null;
    } catch (e) {
      return 'Не вдалося почати запис';
    }
  }

  Future<void> cancel() async {
    await _amp?.cancel();
    _clock.stop();
    try {
      await _rec.cancel();
    } catch (_) {}
  }

  /// Bytes, seconds and a 40-bar waveform — or null when too short / failed.
  Future<(Uint8List, double, List<int>)?> finish() async {
    await _amp?.cancel();
    _clock.stop();
    final secs = _clock.elapsedMilliseconds / 1000;
    String? path;
    try {
      path = await _rec.stop();
    } catch (_) {}
    path ??= _path;
    if (path == null || secs < 0.7) return null;
    final f = File(path);
    if (!await f.exists()) return null;
    final bytes = await f.readAsBytes();
    unawaited(f.delete().catchError((_) => f));
    const bars = 40;
    final wave = <int>[];
    if (_levels.isEmpty) {
      wave.addAll(List<int>.filled(bars, 30));
    } else {
      for (var i = 0; i < bars; i++) {
        final a = (i * _levels.length / bars).floor();
        final b = math.max(a + 1, ((i + 1) * _levels.length / bars).floor());
        final slice = _levels.sublist(a, math.min(b, _levels.length));
        wave.add(slice.isEmpty ? 0 : slice.reduce(math.max).round());
      }
    }
    return (bytes, secs, wave);
  }

  void dispose() {
    _amp?.cancel();
    _rec.dispose();
  }
}
