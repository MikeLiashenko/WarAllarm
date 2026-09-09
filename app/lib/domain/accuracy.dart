import 'dart:math' as math;

/// Forecast-accuracy metrics. We periodically record the probability we
/// predicted for home and, 30 minutes later, whether a real alarm actually
/// materialised. These functions turn that log into honest, readable numbers:
/// a hit rate, a calibration chart (did "70%" happen ~70% of the time?) and a
/// Brier error.

/// One resolved prediction: predicted percent [p] (0-100) and whether it came
/// true ([hit]).
class AccSample {
  final int p;
  final bool hit;
  const AccSample(this.p, this.hit);
}

class CalBucket {
  final int lo, hi; // range, e.g. 60..80
  final int n; // samples in this bucket
  final double predictedAvg; // 0-100
  final double actualRate; // 0-100
  const CalBucket(this.lo, this.hi, this.n, this.predictedAvg, this.actualRate);
}

class AccuracyStats {
  final int samples;
  final int correctPct; // % of yes/no calls that were right (threshold 50%)
  final double brier; // 0..1, lower is better
  final int hits; // how many predictions actually materialised
  final List<CalBucket> buckets;
  const AccuracyStats({
    required this.samples,
    required this.correctPct,
    required this.brier,
    required this.hits,
    required this.buckets,
  });

  bool get enough => samples >= 8;
}

AccuracyStats computeAccuracy(List<AccSample> s) {
  if (s.isEmpty) {
    return const AccuracyStats(
        samples: 0, correctPct: 0, brier: 0, hits: 0, buckets: []);
  }
  var correct = 0, hits = 0;
  var brierSum = 0.0;
  for (final x in s) {
    final pred = x.p / 100.0;
    final out = x.hit ? 1.0 : 0.0;
    brierSum += (pred - out) * (pred - out);
    if ((x.p >= 50) == x.hit) correct++;
    if (x.hit) hits++;
  }
  // Five calibration buckets of 20% each.
  const edges = [0, 20, 40, 60, 80, 100];
  final buckets = <CalBucket>[];
  for (var i = 0; i < 5; i++) {
    final lo = edges[i], hi = edges[i + 1];
    final inB = s.where((x) => x.p >= lo && (i == 4 ? x.p <= hi : x.p < hi));
    final n = inB.length;
    if (n == 0) {
      buckets.add(CalBucket(lo, hi, 0, (lo + hi) / 2, 0));
      continue;
    }
    final predAvg = inB.map((x) => x.p).reduce((a, b) => a + b) / n;
    final rate = inB.where((x) => x.hit).length / n * 100;
    buckets.add(CalBucket(lo, hi, n, predAvg, rate));
  }
  return AccuracyStats(
    samples: s.length,
    correctPct: (correct / s.length * 100).round(),
    brier: brierSum / s.length,
    hits: hits,
    buckets: buckets,
  );
}

/// A friendly 0-100 "how good is the model" score derived from the Brier error
/// (0 error → 100, 0.25 baseline-random → 50).
int qualityScore(double brier) =>
    (100 - math.min(100, brier / 0.25 * 50)).round().clamp(0, 100);
