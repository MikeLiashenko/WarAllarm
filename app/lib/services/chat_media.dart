import 'package:flutter/foundation.dart';
import 'package:image/image.dart' as img;

/// A photo ready to send: JPEG bytes without EXIF (no GPS / camera data) + its size.
class PreparedImage {
  final Uint8List bytes;
  final int width, height;
  const PreparedImage(this.bytes, this.width, this.height);
}

/// Photo / avatar preparation, off the UI thread.
///
/// Every picture is decoded, turned upright, stripped of EXIF (a photo's GPS tag
/// would reveal where it was taken) and re-encoded as JPEG small enough for the
/// relay: chat photos ≤ ~340 KB, avatars a 256×256 square ≤ ~80 KB.
class ChatMedia {
  const ChatMedia._();

  static Future<PreparedImage?> photo(Uint8List raw) =>
      compute(_photo, raw);

  static Future<PreparedImage?> avatar(Uint8List raw) =>
      compute(_avatar, raw);

  static PreparedImage? _photo(Uint8List raw) {
    final src = _decode(raw);
    if (src == null) return null;
    var maxSide = 1600;
    var quality = 80;
    for (var i = 0; i < 8; i++) {
      final im = _fit(src, maxSide);
      final out = img.encodeJpg(im, quality: quality);
      if (out.length <= 340 * 1024) return PreparedImage(out, im.width, im.height);
      if (quality > 60) {
        quality -= 10;
      } else {
        maxSide = (maxSide * 0.8).round();
      }
    }
    final im = _fit(src, 800);
    return PreparedImage(img.encodeJpg(im, quality: 55), im.width, im.height);
  }

  static PreparedImage? _avatar(Uint8List raw) {
    final src = _decode(raw);
    if (src == null) return null;
    final side = src.width < src.height ? src.width : src.height;
    var sq = img.copyCrop(src,
        x: (src.width - side) ~/ 2, y: (src.height - side) ~/ 2, width: side, height: side);
    sq = img.copyResize(sq, width: 256, height: 256, interpolation: img.Interpolation.average);
    var q = 82;
    var out = img.encodeJpg(sq, quality: q);
    while (out.length > 80 * 1024 && q > 40) {
      q -= 10;
      out = img.encodeJpg(sq, quality: q);
    }
    return PreparedImage(out, 256, 256);
  }

  static img.Image? _decode(Uint8List raw) {
    try {
      final d = img.decodeImage(raw);
      if (d == null) return null;
      final up = img.bakeOrientation(d);
      up.exif = img.ExifData();
      return up;
    } catch (_) {
      return null;
    }
  }

  static img.Image _fit(img.Image src, int maxSide) {
    final big = src.width > src.height ? src.width : src.height;
    if (big <= maxSide) return src;
    return src.width >= src.height
        ? img.copyResize(src, width: maxSide, interpolation: img.Interpolation.average)
        : img.copyResize(src, height: maxSide, interpolation: img.Interpolation.average);
  }
}
