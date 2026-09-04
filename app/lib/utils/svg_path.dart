import 'package:flutter/widgets.dart';

/// Minimal SVG path-data parser → [Path].
///
/// Supports the command set used by the WarAlarm map: M/m, L/l, H/h, V/v,
/// C/c, S/s, Q/q, T/t, Z/z (absolute + relative). Enough for the Ukraine
/// outline (M/c/l/z) and the target silhouettes (M/L/C/Z).
Path parseSvgPath(String d) {
  final path = Path();
  final tokens = _tokenize(d);
  int i = 0;

  double cx = 0, cy = 0; // current point
  double sx = 0, sy = 0; // subpath start
  double lastC1x = 0, lastC1y = 0; // last control point (for S/T smoothing)
  String prevCmd = '';

  double num() => tokens[i++] as double;
  bool hasNum() => i < tokens.length && tokens[i] is double;

  while (i < tokens.length) {
    final t = tokens[i];
    String cmd;
    if (t is String) {
      cmd = t;
      i++;
    } else {
      // Implicit repeat of previous command (M→L, m→l).
      cmd = prevCmd == 'M' ? 'L' : (prevCmd == 'm' ? 'l' : prevCmd);
    }
    final rel = cmd == cmd.toLowerCase();

    switch (cmd.toUpperCase()) {
      case 'M':
        cx = rel ? cx + num() : num();
        cy = rel ? cy + num() : num();
        path.moveTo(cx, cy);
        sx = cx;
        sy = cy;
        break;
      case 'L':
        cx = rel ? cx + num() : num();
        cy = rel ? cy + num() : num();
        path.lineTo(cx, cy);
        break;
      case 'H':
        cx = rel ? cx + num() : num();
        path.lineTo(cx, cy);
        break;
      case 'V':
        cy = rel ? cy + num() : num();
        path.lineTo(cx, cy);
        break;
      case 'C':
        final x1 = rel ? cx + num() : num();
        final y1 = rel ? cy + num() : num();
        final x2 = rel ? cx + num() : num();
        final y2 = rel ? cy + num() : num();
        cx = rel ? cx + num() : num();
        cy = rel ? cy + num() : num();
        path.cubicTo(x1, y1, x2, y2, cx, cy);
        lastC1x = x2;
        lastC1y = y2;
        break;
      case 'S':
        final smooth = prevCmd.toUpperCase() == 'C' || prevCmd.toUpperCase() == 'S';
        final x1 = smooth ? 2 * cx - lastC1x : cx;
        final y1 = smooth ? 2 * cy - lastC1y : cy;
        final x2 = rel ? cx + num() : num();
        final y2 = rel ? cy + num() : num();
        cx = rel ? cx + num() : num();
        cy = rel ? cy + num() : num();
        path.cubicTo(x1, y1, x2, y2, cx, cy);
        lastC1x = x2;
        lastC1y = y2;
        break;
      case 'Q':
        final x1 = rel ? cx + num() : num();
        final y1 = rel ? cy + num() : num();
        cx = rel ? cx + num() : num();
        cy = rel ? cy + num() : num();
        path.quadraticBezierTo(x1, y1, cx, cy);
        lastC1x = x1;
        lastC1y = y1;
        break;
      case 'T':
        final smooth = prevCmd.toUpperCase() == 'Q' || prevCmd.toUpperCase() == 'T';
        final x1 = smooth ? 2 * cx - lastC1x : cx;
        final y1 = smooth ? 2 * cy - lastC1y : cy;
        cx = rel ? cx + num() : num();
        cy = rel ? cy + num() : num();
        path.quadraticBezierTo(x1, y1, cx, cy);
        lastC1x = x1;
        lastC1y = y1;
        break;
      case 'Z':
        path.close();
        cx = sx;
        cy = sy;
        break;
      default:
        // Unknown command — skip its number if any to avoid a stall.
        if (hasNum()) i++;
    }
    prevCmd = cmd;
  }
  return path;
}

/// Splits path data into command letters ([String]) and numbers ([double]).
List<Object> _tokenize(String d) {
  final out = <Object>[];
  final buf = StringBuffer();

  void flush() {
    if (buf.isNotEmpty) {
      final v = double.tryParse(buf.toString());
      if (v != null) out.add(v);
      buf.clear();
    }
  }

  for (var k = 0; k < d.length; k++) {
    final c = d[k];
    if ((c.codeUnitAt(0) >= 65 && c.codeUnitAt(0) <= 90) ||
        (c.codeUnitAt(0) >= 97 && c.codeUnitAt(0) <= 122)) {
      // Not part of a number exponent (e/E handled below).
      if (c == 'e' || c == 'E') {
        buf.write(c);
        continue;
      }
      flush();
      out.add(c);
    } else if (c == ' ' || c == ',' || c == '\n' || c == '\t' || c == '\r') {
      flush();
    } else if (c == '-' || c == '+') {
      // A sign starts a new number unless it follows an exponent marker.
      final prev = buf.isEmpty ? '' : buf.toString()[buf.length - 1];
      if (prev == 'e' || prev == 'E') {
        buf.write(c);
      } else {
        flush();
        buf.write(c);
      }
    } else if (c == '.') {
      // A second dot starts a new number (e.g. "1.2.3" → 1.2, .3).
      if (buf.toString().contains('.')) flush();
      buf.write(c);
    } else {
      buf.write(c);
    }
  }
  flush();
  return out;
}
