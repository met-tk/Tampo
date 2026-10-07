/// 单词状态枚举 (与电脑端完全一致)
enum WordLearningState {
  newWord(0),
  learning(1),
  review(2),
  mastered(3);

  final int value;
  const WordLearningState(this.value);

  static WordLearningState fromInt(int val) {
    if (val == 4) return WordLearningState.mastered;
    return WordLearningState.values.firstWhere(
      (e) => e.value == val,
      orElse: () => WordLearningState.newWord,
    );
  }

  String get displayName {
    switch (this) {
      case WordLearningState.newWord:
        return '未学习';
      case WordLearningState.learning:
        return '学习中';
      case WordLearningState.review:
        return '复习中';
      case WordLearningState.mastered:
        return '已掌握';
    }
  }
}

/// 单词模型
class Word {
  int id;
  String text;
  DateTime createdAt;
  bool isInList;
  int? wordListId;
  String? wordListName;

  // FSRS 算法字段
  int state; // 0=New(未学习), 1=Learning(学习中), 2=Review(复习中), 3=Mastered(已掌握，独立于FSRS)
  double stability;
  double difficulty;
  int reps;
  int lapses;
  DateTime? lastReviewDate;
  DateTime? nextReviewDate;

  // 正交双向同步时间戳
  DateTime stateUpdatedAt;
  DateTime metaUpdatedAt;

  Word({
    this.id = 0,
    required this.text,
    DateTime? createdAt,
    this.isInList = false,
    this.wordListId,
    this.wordListName,
    this.state = 0,
    this.stability = 0.0,
    this.difficulty = 0.0,
    this.reps = 0,
    this.lapses = 0,
    this.lastReviewDate,
    this.nextReviewDate,
    DateTime? stateUpdatedAt,
    DateTime? metaUpdatedAt,
  })  : createdAt = createdAt ?? DateTime.now(),
        stateUpdatedAt = stateUpdatedAt ?? lastReviewDate ?? createdAt ?? DateTime.now(),
        metaUpdatedAt = metaUpdatedAt ?? createdAt ?? DateTime.now();

  WordLearningState get learningState => WordLearningState.fromInt(state);
  set learningState(WordLearningState s) => state = s.value;

  bool get isDue {
    if (state == WordLearningState.mastered.value || state == 4) return false;
    final now = DateTime.now();
    final todayStart = DateTime(now.year, now.month, now.day);
    final endOfToday = DateTime(now.year, now.month, now.day, 23, 59, 59, 999);
    if (lastReviewDate != null && !lastReviewDate!.toLocal().isBefore(todayStart)) {
      return false;
    }
    if (state == WordLearningState.newWord.value || reps <= 0 || nextReviewDate == null) {
      return true;
    }
    return !nextReviewDate!.toLocal().isAfter(endOfToday);
  }

  Map<String, dynamic> toMap() {
    return {
      if (id > 0) 'id': id,
      'text': text,
      'createdAt': createdAt.toIso8601String(),
      'isInList': isInList ? 1 : 0,
      'wordListId': wordListId,
      'wordListName': wordListName,
      'state': state,
      'stability': stability,
      'difficulty': difficulty,
      'reps': reps,
      'lapses': lapses,
      'lastReviewDate': lastReviewDate?.toIso8601String(),
      'nextReviewDate': nextReviewDate?.toIso8601String(),
      'stateUpdatedAt': stateUpdatedAt.toIso8601String(),
      'metaUpdatedAt': metaUpdatedAt.toIso8601String(),
    };
  }

  Map<String, dynamic> toSyncMap() {
    return {
      if (id > 0) 'id': id,
      'text': text,
      'createdAt': createdAt.toIso8601String(),
      'isInList': isInList,
      'wordListId': wordListId,
      'wordListName': wordListName,
      'state': state,
      'stability': stability,
      'difficulty': difficulty,
      'reps': reps,
      'lapses': lapses,
      'lastReviewDate': lastReviewDate?.toIso8601String(),
      'nextReviewDate': nextReviewDate?.toIso8601String(),
      'stateUpdatedAt': stateUpdatedAt.toIso8601String(),
      'metaUpdatedAt': metaUpdatedAt.toIso8601String(),
    };
  }

  factory Word.fromMap(Map<String, dynamic> map) {
    DateTime? cDate = map['createdAt'] != null
        ? DateTime.tryParse(map['createdAt'].toString())?.toLocal()
        : null;
    DateTime? lrDate = map['lastReviewDate'] != null
        ? DateTime.tryParse(map['lastReviewDate'].toString())?.toLocal()
        : null;
    DateTime now = DateTime.now();

    return Word(
      id: map['id'] is int ? map['id'] : int.tryParse(map['id'].toString()) ?? 0,
      text: map['text']?.toString() ?? '',
      createdAt: cDate ?? now,
      isInList: map['isInList'] == 1 || map['isInList'] == true,
      wordListId: map['wordListId'] is int
          ? map['wordListId']
          : int.tryParse(map['wordListId']?.toString() ?? ''),
      wordListName: map['wordListName']?.toString(),
      state: (() {
        final raw = map['state'] is int ? map['state'] as int : int.tryParse(map['state']?.toString() ?? '') ?? 0;
        return raw == 4 ? WordLearningState.mastered.value : raw;
      })(),
      stability: (map['stability'] is num ? (map['stability'] as num).toDouble() : double.tryParse(map['stability']?.toString() ?? '')) ?? 0.0,
      difficulty: (map['difficulty'] is num ? (map['difficulty'] as num).toDouble() : double.tryParse(map['difficulty']?.toString() ?? '')) ?? 0.0,
      reps: map['reps'] is int ? map['reps'] : int.tryParse(map['reps']?.toString() ?? '') ?? 0,
      lapses: map['lapses'] is int ? map['lapses'] : int.tryParse(map['lapses']?.toString() ?? '') ?? 0,
      lastReviewDate: lrDate,
      nextReviewDate: map['nextReviewDate'] != null
          ? DateTime.tryParse(map['nextReviewDate'].toString())?.toLocal()
          : null,
      stateUpdatedAt: map['stateUpdatedAt'] != null
          ? DateTime.tryParse(map['stateUpdatedAt'].toString())?.toLocal() ?? lrDate ?? cDate ?? now
          : lrDate ?? cDate ?? now,
      metaUpdatedAt: map['metaUpdatedAt'] != null
          ? DateTime.tryParse(map['metaUpdatedAt'].toString())?.toLocal() ?? cDate ?? now
          : cDate ?? now,
    );
  }

  Map<String, dynamic> toJson() => toMap();
  factory Word.fromJson(Map<String, dynamic> json) => Word.fromMap(json);
}

/// 词单模型
class WordList {
  int id;
  String name;
  DateTime createdAt;
  DateTime updatedAt;
  int wordCount;

  WordList({
    this.id = 0,
    required this.name,
    DateTime? createdAt,
    DateTime? updatedAt,
    this.wordCount = 0,
  })  : createdAt = createdAt ?? DateTime.now(),
        updatedAt = updatedAt ?? createdAt ?? DateTime.now();

  Map<String, dynamic> toMap() {
    return {
      if (id > 0) 'id': id,
      'name': name,
      'createdAt': createdAt.toIso8601String(),
      'updatedAt': updatedAt.toIso8601String(),
      'wordCount': wordCount,
    };
  }

  factory WordList.fromMap(Map<String, dynamic> map) {
    DateTime? cDate = map['createdAt'] != null
        ? DateTime.tryParse(map['createdAt'].toString())?.toLocal()
        : null;
    return WordList(
      id: map['id'] is int ? map['id'] : int.tryParse(map['id'].toString()) ?? 0,
      name: map['name']?.toString() ?? '',
      createdAt: cDate ?? DateTime.now(),
      updatedAt: map['updatedAt'] != null
          ? DateTime.tryParse(map['updatedAt'].toString())?.toLocal() ?? cDate ?? DateTime.now()
          : cDate ?? DateTime.now(),
      wordCount: map['wordCount'] is int ? map['wordCount'] : int.tryParse(map['wordCount']?.toString() ?? '') ?? 0,
    );
  }

  Map<String, dynamic> toJson() => toMap();
  factory WordList.fromJson(Map<String, dynamic> json) => WordList.fromMap(json);
}

/// 打卡复习日志
class ReviewLog {
  int id;
  int wordId;
  DateTime reviewDate;
  int rating; // 1=Again, 2=Hard, 3=Good, 4=Easy
  int state;
  double stability;
  double difficulty;
  double stabilityBefore;
  double difficultyBefore;
  int elapsedDays;
  int scheduledDays;

  ReviewLog({
    this.id = 0,
    required this.wordId,
    DateTime? reviewDate,
    required this.rating,
    this.state = 0,
    this.stability = 0.0,
    this.difficulty = 0.0,
    this.stabilityBefore = 0.0,
    this.difficultyBefore = 0.0,
    this.elapsedDays = 0,
    this.scheduledDays = 0,
  }) : reviewDate = reviewDate ?? DateTime.now();

  double get stabilityAfter => stability;
  set stabilityAfter(double v) => stability = v;

  double get difficultyAfter => difficulty;
  set difficultyAfter(double v) => difficulty = v;

  Map<String, dynamic> toMap() {
    return {
      if (id > 0) 'id': id,
      'wordId': wordId,
      'reviewDate': reviewDate.toIso8601String(),
      'rating': rating,
      'state': state,
      'stability': stability,
      'difficulty': difficulty,
      'stabilityBefore': stabilityBefore,
      'difficultyBefore': difficultyBefore,
      'elapsedDays': elapsedDays,
      'scheduledDays': scheduledDays,
    };
  }

  Map<String, dynamic> toSyncMap() {
    return {
      ...toMap(),
      'stabilityAfter': stability,
      'difficultyAfter': difficulty,
    };
  }

  factory ReviewLog.fromMap(Map<String, dynamic> map) {
    double parseDouble(dynamic val) {
      if (val is num) return val.toDouble();
      if (val != null) return double.tryParse(val.toString()) ?? 0.0;
      return 0.0;
    }

    final parsedAfterS = parseDouble(map['stabilityAfter']);
    final parsedS = parseDouble(map['stability']);
    final parsedAfterD = parseDouble(map['difficultyAfter']);
    final parsedD = parseDouble(map['difficulty']);

    return ReviewLog(
      id: map['id'] is int ? map['id'] : int.tryParse(map['id'].toString()) ?? 0,
      wordId: map['wordId'] is int ? map['wordId'] : int.tryParse(map['wordId']?.toString() ?? '') ?? 0,
      reviewDate: map['reviewDate'] != null
          ? DateTime.tryParse(map['reviewDate'].toString())?.toLocal() ?? DateTime.now()
          : DateTime.now(),
      rating: map['rating'] is int ? map['rating'] : int.tryParse(map['rating']?.toString() ?? '') ?? 3,
      state: (() {
        final raw = map['state'] is int ? map['state'] as int : int.tryParse(map['state']?.toString() ?? '') ?? 0;
        return raw == 4 ? WordLearningState.mastered.value : raw;
      })(),
      stability: parsedAfterS > 0 ? parsedAfterS : parsedS,
      difficulty: parsedAfterD > 0 ? parsedAfterD : parsedD,
      stabilityBefore: parseDouble(map['stabilityBefore']),
      difficultyBefore: parseDouble(map['difficultyBefore']),
      elapsedDays: map['elapsedDays'] is int ? map['elapsedDays'] : int.tryParse(map['elapsedDays']?.toString() ?? '') ?? 0,
      scheduledDays: map['scheduledDays'] is int ? map['scheduledDays'] : int.tryParse(map['scheduledDays']?.toString() ?? '') ?? 0,
    );
  }

  Map<String, dynamic> toJson() => toSyncMap();
  factory ReviewLog.fromJson(Map<String, dynamic> json) => ReviewLog.fromMap(json);
}
