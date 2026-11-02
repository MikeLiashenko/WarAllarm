import 'package:flutter/material.dart';

/// The WarAlarm mark: the Ukrainian flag on a squircle, a black shield with a
/// white rim in the middle and the gold tryzub on it.
///
/// One painter for everything: the in-app [BrandMark], and the launcher /
/// Windows / web icons rendered by `tool/render_icon_test.dart`.
class BrandIconPainter extends CustomPainter {
  const BrandIconPainter({this.square = false});

  /// Full-bleed square tile with the shield inside the 80% safe zone
  /// (maskable web icons: the launcher cuts its own shape).
  final bool square;

  static const _blue1 = Color(0xFF2A7BE0);
  static const _blue2 = Color(0xFF0B4DA2);
  static const _yellow1 = Color(0xFFFFD94A);
  static const _yellow2 = Color(0xFFF6B600);
  static const _gold1 = Color(0xFFFFE566);
  static const _gold2 = Color(0xFFF0AE00);

  @override
  void paint(Canvas canvas, Size size) {
    final s = size.shortestSide;
    canvas.save();
    canvas.translate((size.width - s) / 2, (size.height - s) / 2);

    // Flag tile: blue over yellow.
    final full = Rect.fromLTWH(0, 0, s, s);
    final tile = square ? RRect.fromRectXY(full, 0, 0) : RRect.fromRectAndRadius(full, Radius.circular(s * 0.23));
    canvas.save();
    canvas.clipRRect(tile);
    final top = Rect.fromLTWH(0, 0, s, s / 2), bottom = Rect.fromLTWH(0, s / 2, s, s / 2);
    canvas.drawRect(top, Paint()..shader = const LinearGradient(
        begin: Alignment.topCenter, end: Alignment.bottomCenter, colors: [_blue1, _blue2]).createShader(top));
    canvas.drawRect(bottom, Paint()..shader = const LinearGradient(
        begin: Alignment.topCenter, end: Alignment.bottomCenter, colors: [_yellow1, _yellow2]).createShader(bottom));
    canvas.restore();

    // Emblem; in the maskable square it shrinks into the safe zone.
    if (square) {
      canvas.translate(s * 0.1, s * 0.1);
      canvas.scale(0.8);
    }
    final shield = _shield(s);
    canvas.drawShadow(shield, Colors.black, s * 0.02, false);
    canvas.drawPath(shield, Paint()..shader = const LinearGradient(
        begin: Alignment.topCenter, end: Alignment.bottomCenter,
        colors: [Color(0xFF1A1F29), Color(0xFF05070B)]).createShader(shield.getBounds()));
    canvas.drawPath(shield, Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = s * 0.024
      ..strokeJoin = StrokeJoin.round
      ..color = Colors.white);

    // Tryzub: half the tile high, centred on the shield.
    final h = s * 0.5, t0 = Offset(s * 0.5, s * 0.255);
    final tryzub = Path()..fillType = PathFillType.evenOdd;
    for (final loop in _tryzub) {
      tryzub.moveTo(t0.dx + loop[0] * h, t0.dy + loop[1] * h);
      for (var i = 2; i < loop.length; i += 2) {
        tryzub.lineTo(t0.dx + loop[i] * h, t0.dy + loop[i + 1] * h);
      }
      tryzub.close();
    }
    canvas.drawPath(tryzub, Paint()..shader = const LinearGradient(
        begin: Alignment.topCenter, end: Alignment.bottomCenter,
        colors: [_gold1, _gold2]).createShader(tryzub.getBounds()));
    canvas.restore();
  }

  // Shield: a peak on top with concave shoulders, straight sides, pointed bottom.
  Path _shield(double s) => Path()
    ..moveTo(s * 0.5, s * 0.13)
    ..cubicTo(s * 0.585, s * 0.212, s * 0.685, s * 0.243, s * 0.772, s * 0.25)
    ..quadraticBezierTo(s * 0.8, s * 0.253, s * 0.8, s * 0.282)
    ..lineTo(s * 0.8, s * 0.47)
    ..cubicTo(s * 0.8, s * 0.68, s * 0.67, s * 0.8, s * 0.5, s * 0.89)
    ..cubicTo(s * 0.33, s * 0.8, s * 0.2, s * 0.68, s * 0.2, s * 0.47)
    ..lineTo(s * 0.2, s * 0.282)
    ..quadraticBezierTo(s * 0.2, s * 0.253, s * 0.228, s * 0.25)
    ..cubicTo(s * 0.315, s * 0.243, s * 0.415, s * 0.212, s * 0.5, s * 0.13)
    ..close();

  @override
  bool shouldRepaint(BrandIconPainter old) => old.square != square;

  // Tryzub outline traced from WarAlarm.png and made symmetric: closed loops of
  // x, y pairs in units of its height (x from the centre line, y from the top);
  // the inner loops are holes (even-odd fill).
  static const _tryzub = <List<double>>[
    [-0.0021, 0.0014, 0.0007, 0.0003, 0.0067, 0.0070, 0.0164, 0.0211, 0.0232, 0.0341, 0.0285, 0.0465, 0.0331,
      0.0614, 0.0359, 0.0751, 0.0368, 0.0845, 0.0375, 0.1000, 0.0360, 0.1479, 0.0271, 0.2845, 0.0249, 0.3493,
      0.0249, 0.3648, 0.0267, 0.3944, 0.0320, 0.4324, 0.0368, 0.4549, 0.0442, 0.4831, 0.0538, 0.5141, 0.0632,
      0.5408, 0.0754, 0.5722, 0.1049, 0.6290, 0.1335, 0.6239, 0.1430, 0.6213, 0.1516, 0.6169, 0.1613, 0.6091,
      0.1669, 0.6021, 0.1719, 0.5930, 0.1744, 0.5845, 0.1752, 0.5732, 0.1741, 0.5620, 0.1711, 0.5537, 0.1641,
      0.5423, 0.1570, 0.5352, 0.1444, 0.5277, 0.1359, 0.5250, 0.1261, 0.5241, 0.1176, 0.5248, 0.1147, 0.5239,
      0.1409, 0.3042, 0.1458, 0.2777, 0.1556, 0.2439, 0.1654, 0.2197, 0.1799, 0.1915, 0.1993, 0.1621, 0.2162,
      0.1414, 0.2387, 0.1183, 0.2627, 0.0984, 0.2826, 0.0845, 0.2993, 0.0748, 0.3002, 0.0789, 0.3003, 0.7972,
      0.2993, 0.7991, 0.2979, 0.7993, 0.1345, 0.7992, 0.1334, 0.8000, 0.1317, 0.8043, 0.1303, 0.8127, 0.1232,
      0.8372, 0.1134, 0.8623, 0.1035, 0.8820, 0.0916, 0.9014, 0.0768, 0.9218, 0.0600, 0.9408, 0.0500, 0.9508,
      0.0176, 0.9778, 0.0076, 0.9887, 0.0022, 0.9972, 0.0007, 0.9986, -0.0007, 0.9986, -0.0021, 0.9974,
      -0.0076, 0.9887, -0.0176, 0.9778, -0.0387, 0.9606, -0.0573, 0.9437, -0.0768, 0.9218, -0.0866, 0.9088,
      -0.1035, 0.8820, -0.1134, 0.8623, -0.1232, 0.8372, -0.1303, 0.8127, -0.1317, 0.8043, -0.1334, 0.8000,
      -0.1345, 0.7992, -0.2979, 0.7993, -0.2993, 0.7991, -0.3003, 0.7972, -0.3000, 0.0761, -0.2993, 0.0748,
      -0.2908, 0.0795, -0.2625, 0.0986, -0.2387, 0.1183, -0.2162, 0.1414, -0.1993, 0.1621, -0.1799, 0.1915,
      -0.1654, 0.2197, -0.1556, 0.2439, -0.1458, 0.2777, -0.1409, 0.3042, -0.1147, 0.5239, -0.1176, 0.5248,
      -0.1261, 0.5241, -0.1359, 0.5250, -0.1444, 0.5277, -0.1570, 0.5352, -0.1641, 0.5423, -0.1711, 0.5537,
      -0.1741, 0.5620, -0.1752, 0.5732, -0.1744, 0.5845, -0.1719, 0.5930, -0.1669, 0.6021, -0.1613, 0.6091,
      -0.1516, 0.6169, -0.1430, 0.6213, -0.1335, 0.6239, -0.1049, 0.6290, -0.0754, 0.5722, -0.0561, 0.5211,
      -0.0466, 0.4915, -0.0392, 0.4648, -0.0342, 0.4437, -0.0294, 0.4169, -0.0267, 0.3944, -0.0249, 0.3648,
      -0.0249, 0.3479, -0.0271, 0.2845, -0.0360, 0.1479, -0.0375, 0.1000, -0.0368, 0.0845, -0.0359, 0.0751,
      -0.0332, 0.0620, -0.0261, 0.0403, -0.0164, 0.0211],
    [-0.2502, 0.1817, -0.2486, 0.1806, -0.2472, 0.1817, -0.2331, 0.2007, -0.2181, 0.2254, -0.2111, 0.2394,
      -0.2036, 0.2577, -0.1967, 0.2789, -0.1918, 0.3000, -0.1893, 0.3155, -0.1696, 0.4831, -0.1711, 0.4858,
      -0.1782, 0.4895, -0.1880, 0.4965, -0.1979, 0.5059, -0.2036, 0.5127, -0.2080, 0.5183, -0.2148, 0.5299,
      -0.2218, 0.5484, -0.2246, 0.5493, -0.2472, 0.5493, -0.2504, 0.5479],
    [0.2472, 0.1817, 0.2486, 0.1806, 0.2500, 0.1813, 0.2504, 0.1831, 0.2504, 0.5479, 0.2472, 0.5493, 0.2246,
      0.5493, 0.2218, 0.5484, 0.2148, 0.5299, 0.2063, 0.5165, 0.2028, 0.5113, 0.1935, 0.5014, 0.1813, 0.4915,
      0.1711, 0.4858, 0.1696, 0.4831, 0.1893, 0.3155, 0.1918, 0.3000, 0.1967, 0.2789, 0.2086, 0.2451, 0.2181,
      0.2254, 0.2279, 0.2085],
    [-0.0013, 0.5211, 0.0007, 0.5206, 0.0021, 0.5225, 0.0106, 0.5488, 0.0246, 0.5835, 0.0344, 0.6042, 0.0539,
      0.6408, 0.0528, 0.6429, 0.0373, 0.6490, 0.0247, 0.6563, 0.0123, 0.6662, 0.0016, 0.6775, -0.0016, 0.6775,
      -0.0123, 0.6662, -0.0247, 0.6563, -0.0373, 0.6490, -0.0528, 0.6429, -0.0539, 0.6408, -0.0344, 0.6042,
      -0.0246, 0.5835, -0.0106, 0.5488],
    [-0.2494, 0.6000, -0.2472, 0.5992, -0.2246, 0.5992, -0.2218, 0.6003, -0.2149, 0.6183, -0.2077, 0.6306,
      -0.1979, 0.6428, -0.1894, 0.6507, -0.1768, 0.6599, -0.1614, 0.6676, -0.1287, 0.6761, -0.1284, 0.6803,
      -0.1305, 0.6873, -0.1350, 0.7113, -0.1375, 0.7408, -0.1379, 0.7479, -0.1387, 0.7496, -0.1401, 0.7500,
      -0.2472, 0.7500, -0.2494, 0.7493, -0.2504, 0.7465, -0.2504, 0.6028],
    [0.2224, 0.6000, 0.2246, 0.5992, 0.2472, 0.5992, 0.2486, 0.5994, 0.2504, 0.6014, 0.2501, 0.7479, 0.2494,
      0.7493, 0.2472, 0.7500, 0.1585, 0.7499, 0.1401, 0.7500, 0.1384, 0.7493, 0.1352, 0.7127, 0.1305, 0.6873,
      0.1284, 0.6803, 0.1287, 0.6761, 0.1614, 0.6676, 0.1768, 0.6599, 0.1894, 0.6507, 0.1979, 0.6428, 0.2077,
      0.6306, 0.2149, 0.6183, 0.2210, 0.6014],
    [-0.0790, 0.6887, -0.0754, 0.6881, -0.0683, 0.6899, -0.0570, 0.6948, -0.0460, 0.7028, -0.0368, 0.7127,
      -0.0292, 0.7268, -0.0271, 0.7324, -0.0250, 0.7451, -0.0251, 0.7479, -0.0261, 0.7494, -0.0331, 0.7499,
      -0.0852, 0.7500, -0.0869, 0.7493, -0.0875, 0.7465, -0.0866, 0.7269, -0.0842, 0.7099],
    [0.0736, 0.6887, 0.0768, 0.6879, 0.0790, 0.6887, 0.0822, 0.7000, 0.0869, 0.7296, 0.0875, 0.7465, 0.0869,
      0.7493, 0.0852, 0.7500, 0.0401, 0.7499, 0.0275, 0.7498, 0.0258, 0.7493, 0.0251, 0.7479, 0.0271, 0.7324,
      0.0317, 0.7211, 0.0391, 0.7099, 0.0492, 0.7000, 0.0613, 0.6926],
    [-0.0812, 0.8000, -0.0782, 0.7993, -0.0289, 0.7993, -0.0261, 0.7998, -0.0249, 0.8014, -0.0245, 0.9014,
      -0.0261, 0.9033, -0.0275, 0.9024, -0.0359, 0.8931, -0.0458, 0.8804, -0.0600, 0.8577, -0.0672, 0.8437,
      -0.0746, 0.8254, -0.0796, 0.8099],
    [0.0257, 0.8000, 0.0289, 0.7993, 0.0782, 0.7993, 0.0812, 0.8000, 0.0813, 0.8028, 0.0770, 0.8183, 0.0697,
      0.8378, 0.0600, 0.8577, 0.0512, 0.8718, 0.0405, 0.8873, 0.0289, 0.9011, 0.0261, 0.9033, 0.0246, 0.9020,
      0.0247, 0.8028],  ];
}

/// The app mark as a widget (navigation rail, menu).
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 34});
  final double size;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(size * 0.23),
        boxShadow: [
          BoxShadow(
              color: const Color(0xFF2A7BE0).withValues(alpha: 0.25),
              blurRadius: 14,
              spreadRadius: -4),
        ],
      ),
      child: SizedBox(
        width: size,
        height: size,
        child: const CustomPaint(painter: BrandIconPainter()),
      ),
    );
  }
}
