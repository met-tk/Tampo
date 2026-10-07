// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'dart:math';
import 'package:flutter_test/flutter_test.dart';
import 'package:tampo_android/models/word_models.dart';
import 'package:tampo_android/services/fsrs_engine.dart';

void main() {
  test('FsrsEngine FSRS-4.5 official formula parity with Windows C#', () {
    expect(FsrsEngine.w.length, 17);

    final now = DateTime(2026, 10, 6, 12, 0, 0);
    final wordGood = Word(text: '猫', createdAt: now, reps: 0);
    final resGood = FsrsEngine.review(wordGood, 3, now);
    expect(resGood.newStability, closeTo(3.7145, 1e-4));
    expect(resGood.newDifficulty, closeTo(5.1618, 1e-4));

    final wordAgain = Word(text: '犬', createdAt: now, reps: 0);
    final resAgain = FsrsEngine.review(wordAgain, 1, now);
    expect(resAgain.newStability, closeTo(0.4872, 1e-4));
    expect(resAgain.newDifficulty, closeTo(7.6214, 1e-4));

    // Post-lapse stability upper bound S'_f <= S and fixed 1-day interval
    final wordReview = Word(
      text: '桜',
      createdAt: now.subtract(const Duration(days: 5)),
      lastReviewDate: now.subtract(const Duration(days: 1)),
      reps: 2,
      stability: 0.5,
      difficulty: 2.0,
    );
    final resLapse = FsrsEngine.review(wordReview, 1, now);
    expect(resLapse.newStability, lessThanOrEqualTo(wordReview.stability));
    expect(resLapse.intervalDays, 1);
    expect(resLapse.nextReviewDate, DateTime(2026, 10, 7));
  });

  test('Calendar day elapsedDays and power-law retrievability accuracy', () {
    final yesterdayLate = DateTime(2026, 10, 5, 23, 50, 0);
    final todayEarly = DateTime(2026, 10, 6, 0, 10, 0);
    expect(FsrsEngine.getCalendarElapsedDays(yesterdayLate, todayEarly), 1.0);

    final todayMorning = DateTime(2026, 10, 6, 8, 0, 0);
    final todayNight = DateTime(2026, 10, 6, 22, 0, 0);
    expect(FsrsEngine.getCalendarElapsedDays(todayMorning, todayNight), 0.0);

    // Clock skew protection
    expect(FsrsEngine.getCalendarElapsedDays(todayNight, yesterdayLate), 0.0);

    // Same calendar day retrievability is 1.0
    expect(
      FsrsEngine.calculateRetrievability(10.0, todayMorning, todayNight),
      closeTo(1.0, 1e-6),
    );

    // Cross calendar day (1.0 day) retrievability matches power-law curve
    final expectedDay1 = pow(1.0 + (19.0 / 81.0) * (1.0 / 10.0), -0.5).toDouble();
    expect(
      FsrsEngine.calculateRetrievability(10.0, yesterdayLate, todayEarly),
      closeTo(expectedDay1, 1e-6),
    );

    // Invalid stability fallback
    expect(FsrsEngine.calculateRetrievability(0.0, yesterdayLate, todayEarly), 0.0);
    expect(FsrsEngine.calculateRetrievability(-5.0, yesterdayLate, todayEarly), 0.0);
    expect(FsrsEngine.calculateRetrievability(double.nan, yesterdayLate, todayEarly), 0.0);
    expect(FsrsEngine.calculateRetrievability(double.infinity, yesterdayLate, todayEarly), 0.0);
  });

  test('Binary rating (Again=1 / Good=3) difficulty balance and same-day stability protection', () {
    final day0 = DateTime(2026, 10, 1, 10, 0, 0);
    final word = Word(text: '均衡', createdAt: day0, reps: 0);

    final res1 = FsrsEngine.review(word, 3, day0);
    expect(res1.newDifficulty, closeTo(5.1618, 1e-4));

    word.reps = 1;
    word.stability = res1.newStability;
    word.difficulty = res1.newDifficulty;
    word.lastReviewDate = day0;

    // Consecutive Good(3) reviews decrease difficulty rather than getting stuck at D0(3)
    final day4 = day0.add(const Duration(days: 4));
    final res2 = FsrsEngine.review(word, 3, day4);
    expect(res2.newDifficulty, lessThan(res1.newDifficulty));

    word.reps = 2;
    word.stability = res2.newStability;
    word.difficulty = res2.newDifficulty;
    word.lastReviewDate = day4;

    final day15 = day4.add(const Duration(days: 11));
    final res3 = FsrsEngine.review(word, 3, day15);
    expect(res3.newDifficulty, lessThan(res2.newDifficulty));

    // Again(1) increases difficulty
    word.reps = 3;
    word.stability = res3.newStability;
    word.difficulty = res3.newDifficulty;
    word.lastReviewDate = day15;
    final resAgain = FsrsEngine.review(word, 1, day15.add(const Duration(days: 20)));
    expect(resAgain.newDifficulty, greaterThan(res3.newDifficulty));

    // Same calendar day (elapsedDays == 0) Good(3) monotonic stability protection
    final sameDayWord = Word(
      text: '同日',
      createdAt: day0,
      lastReviewDate: DateTime(2026, 10, 6, 9, 0, 0),
      reps: 2,
      stability: 8.5,
      difficulty: 5.0,
    );
    final resSameDay = FsrsEngine.review(sameDayWord, 3, DateTime(2026, 10, 6, 21, 0, 0));
    expect(resSameDay.newStability, greaterThanOrEqualTo(sameDayWord.stability));
  });

  test('Dirty data defense for NaN, Infinity, negative values, and out-of-bounds ratings', () {
    final now = DateTime(2026, 10, 6, 12, 0, 0);
    final dirtyWord = Word(
      text: '髒數據',
      createdAt: now.subtract(const Duration(days: 3)),
      lastReviewDate: now.subtract(const Duration(days: 3)),
      reps: 5,
      stability: double.nan,
      difficulty: double.infinity,
    );

    final res1 = FsrsEngine.review(dirtyWord, 99, now);
    expect(res1.newStability.isNaN || res1.newStability.isInfinite, isFalse);
    expect(res1.newDifficulty.isNaN || res1.newDifficulty.isInfinite, isFalse);
    expect(res1.newStability, inInclusiveRange(0.1, 36500.0));
    expect(res1.newDifficulty, inInclusiveRange(1.0, 10.0));

    final negWord = Word(
      text: '負數',
      createdAt: now.subtract(const Duration(days: 2)),
      lastReviewDate: now.subtract(const Duration(days: 2)),
      reps: 3,
      stability: -50.0,
      difficulty: -3.0,
    );
    final res2 = FsrsEngine.review(negWord, 0, now);
    expect(res2.newStability, inInclusiveRange(0.1, 36500.0));
    expect(res2.newDifficulty, inInclusiveRange(1.0, 10.0));

    expect(FsrsEngine.nextIntervalDays(double.nan), 1);
    expect(FsrsEngine.nextIntervalDays(double.infinity), 1);
    expect(FsrsEngine.nextIntervalDays(-10.0), 1);
    expect(FsrsEngine.nextDifficulty(double.nan, 3), inInclusiveRange(1.0, 10.0));
    expect(
      FsrsEngine.nextRecallStability(double.nan, double.infinity, double.nan, 3),
      inInclusiveRange(0.1, 36500.0),
    );
    expect(
      FsrsEngine.nextForgetStability(double.negativeInfinity, -1.0, double.nan),
      inInclusiveRange(0.1, 36500.0),
    );
  });

  test('Word domain axioms: Mastered decoupling and natural day isDue rules', () {
    final now = DateTime.now();
    final yesterday = now.subtract(const Duration(days: 1));
    final todayLate = DateTime(now.year, now.month, now.day, 23, 30, 0);

    // Mastered (state=3) is decoupled from FSRS and never due
    final masteredWord = Word(
      text: '掌握',
      state: WordLearningState.mastered.value,
      reps: 1,
      stability: 3.7145,
      lastReviewDate: yesterday,
      nextReviewDate: yesterday,
    );
    expect(masteredWord.isDue, isFalse);

    // Legacy state=4 normalizes to Mastered(3) without fabricating stability=25.0
    final mappedMastered = Word.fromMap({
      'text': '旧版掌握',
      'state': 4,
      'stability': 3.7145,
      'reps': 1,
    });
    expect(mappedMastered.state, WordLearningState.mastered.value);
    expect(mappedMastered.stability, closeTo(3.7145, 1e-4));
    expect(mappedMastered.isDue, isFalse);

    // Word due later tonight (23:30) is included in today's queue by natural day rule
    final dueTonightWord = Word(
      text: '今夜到期',
      state: WordLearningState.learning.value,
      reps: 1,
      lastReviewDate: yesterday,
      nextReviewDate: todayLate,
    );
    expect(dueTonightWord.isDue, isTrue);

    // Word already reviewed today is strictly excluded from today's queue
    final reviewedTodayWord = Word(
      text: '今日已学',
      state: WordLearningState.learning.value,
      reps: 1,
      lastReviewDate: now,
      nextReviewDate: now.subtract(const Duration(hours: 1)),
    );
    expect(reviewedTodayWord.isDue, isFalse);
  });

  test('ReviewLog cross-platform JSON compatibility and timezone normalization', () {
    final mapFromWindows = <String, dynamic>{
      'wordId': 42,
      'wordText': '言葉',
      'rating': 3,
      'reviewDate': '2026-10-06T04:00:00Z',
      'stabilityBefore': 0.4872,
      'difficultyBefore': 7.6214,
      'stabilityAfter': 2.5,
      'difficultyAfter': 6.8,
      'elapsedDays': 1.0,
      'scheduledDays': 3.0,
    };

    final log = ReviewLog.fromMap(mapFromWindows);
    expect(log.stabilityBefore, closeTo(0.4872, 1e-4));
    expect(log.difficultyBefore, closeTo(7.6214, 1e-4));
    expect(log.stabilityAfter, closeTo(2.5, 1e-4));
    expect(log.difficultyAfter, closeTo(6.8, 1e-4));
    expect(log.reviewDate.isUtc, isFalse);

    final syncMap = log.toSyncMap();
    expect(syncMap['stabilityAfter'], closeTo(2.5, 1e-4));
    expect(syncMap['stability'], closeTo(2.5, 1e-4));
    expect(syncMap['stabilityBefore'], closeTo(0.4872, 1e-4));
  });
}
