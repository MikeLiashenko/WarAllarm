import 'package:flutter/foundation.dart';

/// Which target the user picked on a map (by the Reader's stable id) and
/// whether the camera follows it. Shared by «Зараз» (tap a target row) and
/// both maps (tap a marker).
class TargetFocusController extends ChangeNotifier {
  String? _id;
  bool _follow = false;

  String? get id => _id;
  bool get follow => _follow && _id != null;

  void select(String id, {bool follow = false}) {
    if (id.isEmpty) return;
    _id = id;
    _follow = follow;
    notifyListeners();
  }

  void setFollow(bool v) {
    if (_follow == v) return;
    _follow = v;
    notifyListeners();
  }

  void clear() {
    if (_id == null) return;
    _id = null;
    _follow = false;
    notifyListeners();
  }
}
