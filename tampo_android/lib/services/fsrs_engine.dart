import 'dart:math';
import '../models/word_models.dart';

/// FSRS 4.5 记忆调度算法引擎 (与电脑端完全一致的数学模型)
class FsrsEngine {
  // FSRS-4.5 官方标准默认权重矩阵 (17个参数)
  static const List<double> w = [
    0.4872, 1.4003, 3.7145, 13.8206,
    5.1618, 1.2298, 0.8975, 0.031,
    1.6474, 0.1367, 1.0461, 2.1072,
    0.0793, 0.3246, 1.587, 0.2272,
    2.8755,
  ];

  static const double decay = -0.5;
  static const double factor = 19.0 / 81.0;
  static const double defaultDesiredRetention = 0.90;

  /// 按本地自然日（Calendar Day）计算经过的天数（整数天差，严禁亚日级小时/分钟折算）
  static double getCalendarElapsedDays(DateTime lastReview, DateTime now) {
    final l = lastReview.toLocal();
    final n = now.toLocal();
    final lastDay = DateTime(l.year, l.month, l.day);
    final nowDay = DateTime(n.year, n.month, n.day);
    return max(0.0, nowDay.difference(lastDay).inDays.toDouble());
  }

  /// 计算指定经过自然日与稳定性下的记忆留存概率 R(t, S)
  static double calculateRetrievability(double stability, DateTime lastReview, DateTime now) {
    if (stability.isNaN || stability.isInfinite || stability <= 0.0001) return 0.0;
    double elapsedDays = getCalendarElapsedDays(lastReview, now);
    double r = pow(1.0 + factor * (elapsedDays / stability), decay).toDouble();
    if (r.isNaN || r.isInfinite) return 0.0;
    return r.clamp(0.0, 1.0);
  }

  /// 根据稳定性与期望保留率计算下一次推荐复习间隔（天数）
  static int nextIntervalDays(double stability, [double desiredRetention = defaultDesiredRetention]) {
    if (stability.isNaN || stability.isInfinite || stability <= 0.0001) return 1;
    if (desiredRetention.isNaN || desiredRetention.isInfinite) {
      desiredRetention = defaultDesiredRetention;
    }
    desiredRetention = desiredRetention.clamp(0.70, 0.99);
    double interval = (stability / factor) * (pow(desiredRetention, 1.0 / decay) - 1.0);
    if (interval.isNaN || interval.isInfinite || interval <= 1.0) return 1;
    return max(1, interval.round().clamp(1, 36500));
  }

  /// 执行 FSRS 算法迭代（严格以自然日 Day 为时间单位）
  static FsrsReviewResult review(Word word, int rating, DateTime now) {
    double s = word.stability;
    double d = word.difficulty;
    int g = rating.clamp(1, 4); // 1=Again, 2=Hard, 3=Good, 4=Easy

    // 脏数据兜底与安全域 Clamp（含 NaN / Infinity 防御）
    if (d.isNaN || d.isInfinite || d <= 0.0001) {
      d = w[4];
    } else {
      d = d.clamp(1.0, 10.0);
    }

    double newS;
    double newD;

    if (word.reps <= 0 || s.isNaN || s.isInfinite || s <= 0.0001) {
      newS = initStability(g);
      newD = initDifficulty(g);
    } else {
      s = s.clamp(0.1, 36500.0);
      DateTime lastDate = word.lastReviewDate ?? word.createdAt;
      double elapsedDays = getCalendarElapsedDays(lastDate, now);
      double retrievability = pow(1.0 + factor * (elapsedDays / s), decay).toDouble();
      if (retrievability.isNaN || retrievability.isInfinite) {
        retrievability = 0.0;
      } else {
        retrievability = retrievability.clamp(0.0, 1.0);
      }

      newD = nextDifficulty(d, g);

      if (g == 1) {
        newS = nextForgetStability(d, s, retrievability);
      } else {
        newS = nextRecallStability(d, s, retrievability, g);
        if (elapsedDays <= 0.0001) {
          newS = max(s, newS);
        }
      }
    }

    if (newS.isNaN || newS.isInfinite) newS = initStability(g);
    if (newD.isNaN || newD.isInfinite) newD = w[4];
    newS = newS.clamp(0.1, 36500.0);
    newD = newD.clamp(1.0, 10.0);

    int intervalDays = (g == 1) ? 1 : nextIntervalDays(newS, defaultDesiredRetention);
    DateTime nextReview = DateTime(now.year, now.month, now.day).add(Duration(days: intervalDays));
    return FsrsReviewResult(newS, newD, nextReview, intervalDays);
  }

  static double initStability(int g) {
    int idx = (g - 1).clamp(0, 3);
    return w[idx];
  }

  static double initDifficulty(int g) {
    return (w[4] - w[5] * (g - 3)).clamp(1.0, 10.0);
  }

  /// 二元评级（1=Again, 3=Good）适配：Good(3) 赋予微幅负向难度冲量 (2/9)，使 90% 留存率下难度期望保持守恒
  static double nextDifficulty(double d, int g) {
    if (d.isNaN || d.isInfinite || d <= 0.0001) d = w[4];
    d = d.clamp(1.0, 10.0);
    double d0Target = w[4]; // D0(3) = w[4]
    double deltaG = (g == 3) ? (2.0 / 9.0) : (g - 3.0);
    double nextD = w[7] * d0Target + (1.0 - w[7]) * (d - w[6] * deltaG);
    if (nextD.isNaN || nextD.isInfinite) return d;
    return nextD.clamp(1.0, 10.0);
  }

  static double nextRecallStability(double d, double s, double r, int g) {
    if (d.isNaN || d.isInfinite || d <= 0.0001) d = w[4];
    if (s.isNaN || s.isInfinite || s <= 0.0001) s = 0.1;
    if (r.isNaN || r.isInfinite) r = 0.9;
    d = d.clamp(1.0, 10.0);
    s = s.clamp(0.1, 36500.0);
    r = r.clamp(0.0, 1.0);
    double hardPenalty = (g == 2) ? w[15] : 1.0;
    double easyBonus = (g == 4) ? w[16] : 1.0;
    double growth = exp(w[8]) *
        (11.0 - d) *
        pow(s, -w[9]) *
        (exp(w[10] * (1.0 - r)) - 1.0) *
        hardPenalty *
        easyBonus;
    if (growth.isNaN || growth.isInfinite) return s;
    return (s * (1.0 + growth)).clamp(0.1, 36500.0);
  }

  static double nextForgetStability(double d, double s, double r) {
    if (d.isNaN || d.isInfinite || d <= 0.0001) d = w[4];
    if (s.isNaN || s.isInfinite || s <= 0.0001) s = 0.1;
    if (r.isNaN || r.isInfinite) r = 0.0;
    d = d.clamp(1.0, 10.0);
    s = s.clamp(0.1, 36500.0);
    r = r.clamp(0.0, 1.0);
    double sf = w[11] *
        pow(d, -w[12]) *
        (pow(s + 1.0, w[13]) - 1.0) *
        exp(w[14] * (1.0 - r));
    if (sf.isNaN || sf.isInfinite) return max(0.1, s * 0.5);
    return min(s, sf).clamp(0.1, 36500.0);
  }

}

class FsrsReviewResult {
  final double newStability;
  final double newDifficulty;
  final DateTime nextReviewDate;
  final int intervalDays;

  FsrsReviewResult(this.newStability, this.newDifficulty, this.nextReviewDate, this.intervalDays);
}
