import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:webview_flutter/webview_flutter.dart' as wf;
import 'package:webview_windows/webview_windows.dart' as ww;

import '../controllers/map_data_controller.dart';
import '../domain/motion.dart';
import '../models/map_target.dart';
import '../theme/app_colors.dart';
import '../widgets/glass.dart';

/// The 3D city map (Ukraine-3D, Mike's own OSM map of every building in
/// Ukraine) opened on a followed target: `go/` picks the oblast, the oblast page
/// draws the weapon's model over the real houses. Once a second the app pushes
/// the target's exact position from its own motion model ([motionOf]) into the
/// page (`window.WA.push`), so the 3D flight is the same as on the app's maps.
const String kCity3dBase = 'https://mikeliashenko.github.io/Ukraine-3D/go/';

/// The link for a target (the bot and the site build the same one).
Uri city3dUrl(MapTarget t, {double? lat, double? lon, double? heading}) => Uri.parse(kCity3dBase).replace(queryParameters: {
      'id': t.id,
      'lat': (lat ?? t.lat).toStringAsFixed(5),
      'lon': (lon ?? t.lon).toStringAsFixed(5),
      'cat': t.cat,
      if ((heading ?? t.heading) != null) 'hdg': (heading ?? t.heading)!.round().toString(),
      if (t.label.isNotEmpty) 'label': t.label,
      if (t.kmh > 0) 'kmh': t.kmh.round().toString(),
    });

/// Opens the 3D view: inside the app on Android and Windows, in the browser elsewhere.
Future<void> openTarget3d(BuildContext context, MapTarget t) async {
  final m = motionOf(t, DateTime.now().millisecondsSinceEpoch);
  final url = city3dUrl(t, lat: m.lat, lon: m.lon, heading: m.heading);
  final inApp = !kIsWeb &&
      (defaultTargetPlatform == TargetPlatform.android ||
          defaultTargetPlatform == TargetPlatform.iOS ||
          defaultTargetPlatform == TargetPlatform.windows);
  if (!inApp) {
    await launchUrl(url, mode: LaunchMode.externalApplication);
    return;
  }
  if (!context.mounted) return;
  await Navigator.of(context).push(MaterialPageRoute<void>(
    builder: (_) => Target3DScreen(targetId: t.id, url: url),
  ));
}

class Target3DScreen extends StatefulWidget {
  const Target3DScreen({super.key, required this.targetId, required this.url});

  final String targetId;
  final Uri url;

  @override
  State<Target3DScreen> createState() => _Target3DScreenState();
}

class _Target3DScreenState extends State<Target3DScreen> {
  wf.WebViewController? _android;
  ww.WebviewController? _win;
  bool _winReady = false;
  String? _error;
  Timer? _timer;
  bool _trailSent = false;
  MapTarget? _last;

  bool get _isWin => defaultTargetPlatform == TargetPlatform.windows;

  @override
  void initState() {
    super.initState();
    if (_isWin) {
      _initWindows();
    } else {
      _android = wf.WebViewController()
        ..setJavaScriptMode(wf.JavaScriptMode.unrestricted)
        ..setBackgroundColor(AppColors.mapSea)
        ..setNavigationDelegate(wf.NavigationDelegate(
          // A new oblast page (the target crossed a border): send the trail again.
          onPageFinished: (_) => _trailSent = false,
          onWebResourceError: (e) {
            if (e.isForMainFrame == true && mounted) setState(() => _error = e.description);
          },
        ))
        ..loadRequest(widget.url);
    }
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => _push());
  }

  Future<void> _initWindows() async {
    try {
      final c = ww.WebviewController();
      await c.initialize();
      await c.setBackgroundColor(AppColors.mapSea);
      c.loadingState.listen((s) {
        if (s == ww.LoadingState.navigationCompleted) _trailSent = false;
      });
      await c.loadUrl(widget.url.toString());
      if (!mounted) return;
      setState(() {
        _win = c;
        _winReady = true;
      });
    } catch (e) {
      // No WebView2 runtime: open the page in the browser instead.
      if (!mounted) return;
      setState(() => _error = 'WebView2 недоступний: $e');
      await launchUrl(widget.url, mode: LaunchMode.externalApplication);
    }
  }

  MapTarget? _find(MapDataController d) {
    for (final list in [d.targets, d.launches, d.aviation]) {
      for (final t in list) {
        if (t.id == widget.targetId) return t;
      }
    }
    return null;
  }

  /// The target's state for the page — the same flight as on the app's maps.
  void _push() {
    if (!mounted) return;
    final d = context.read<MapDataController>();
    final t = _find(d);
    final Map<String, dynamic> msg;
    if (t == null) {
      if (_last == null) return;
      msg = {'gone': true};
    } else {
      _last = t;
      final now = DateTime.now().millisecondsSinceEpoch;
      final m = motionOf(t, now), m2 = motionOf(t, now + 2000);
      // Effective speed of the shown flight (route at 85 %, creep at 70 %, stopped = 0).
      final vKmh = kmBetween(m.lat, m.lon, m2.lat, m2.lon) * 1800;
      final route = t.route;
      final ahead = m.isFlight && t.to != null
          ? [t.to!]
          : (route.length >= 2 && !m.orbiting ? routeAhead(route, m.lat, m.lon).skip(1).toList() : const <List<double>>[]);
      msg = {
        'id': t.id,
        'cat': t.cat,
        'label': t.label,
        'place': t.place,
        'n': t.amount,
        'stageLabel': d.stale ? 'дані застаріли' : t.stageLabel,
        'lat': m.lat,
        'lon': m.lon,
        'hdg': m.heading,
        'kmh': t.kmh,
        'v': vKmh,
        'moving': vKmh > 1,
        if (t.altM != null) 'alt': t.altM,
        'altNote': t.altNote,
        'down': t.down,
        if (m.progress != null) 'progress': m.progress,
        if (m.isFlight && t.from != null && t.to != null) 'distKm': kmBetween(t.from![0], t.from![1], t.to![0], t.to![1]),
        'uncertKm': m.uncertKm,
        'quiet': m.quietMin,
        'orbit': m.orbiting,
        'ahead': ahead,
        if (!_trailSent) 'trail': t.trail,
      };
    }
    final js = 'window.WA && window.WA.push(${jsonEncode(msg)}) || 0';
    try {
      if (_android != null) {
        _android!.runJavaScript(js).then((_) {}, onError: (_) {});
        if (msg.containsKey('trail')) _trailSent = true;
      } else if (_win != null && _winReady) {
        _win!.executeScript(js).then((_) {}, onError: (_) {});
        if (msg.containsKey('trail')) _trailSent = true;
      }
    } catch (_) {/* page still loading */}
  }

  @override
  void dispose() {
    _timer?.cancel();
    _win?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final pad = MediaQuery.paddingOf(context);
    Widget view;
    if (_error != null) {
      view = Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Text('Не вдалося відкрити 3D-карту', style: TextStyle(color: AppColors.textPrimary, fontSize: 16, fontWeight: FontWeight.w700)),
            const SizedBox(height: 8),
            Text(_error!, textAlign: TextAlign.center, style: TextStyle(color: AppColors.textSecondary, fontSize: 13)),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () => launchUrl(widget.url, mode: LaunchMode.externalApplication),
              child: const Text('Відкрити в браузері'),
            ),
          ]),
        ),
      );
    } else if (_android != null) {
      view = wf.WebViewWidget(controller: _android!);
    } else if (_win != null && _winReady) {
      view = ww.Webview(_win!);
    } else {
      view = const Center(child: CircularProgressIndicator());
    }
    return Scaffold(
      backgroundColor: AppColors.mapSea,
      body: Stack(children: [
        Positioned.fill(child: view),
        // bottom-right: the page's own target panel sits at the top
        Positioned(
          right: 16 + pad.right,
          bottom: 16 + pad.bottom,
          child: GlassIconButton(
            icon: Icons.close_rounded,
            tooltip: 'Закрити 3D',
            onTap: () => Navigator.of(context).maybePop(),
          ),
        ),
      ]),
    );
  }
}
