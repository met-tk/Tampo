import 'dart:async';
import 'dart:math';
import 'package:path/path.dart';
import 'package:sqflite/sqflite.dart';
import '../models/word_models.dart';
import 'fsrs_engine.dart';

class DatabaseService {
  static final DatabaseService instance = DatabaseService._internal();
  DatabaseService._internal();

  Database? _db;

  Future<Database> get database async {
    if (_db != null) return _db!;
    _db = await _initDatabase();
    return _db!;
  }

  Future<Database> _initDatabase() async {
    final dbPath = await getDatabasesPath();
    final path = join(dbPath, 'tampo_vocab.db');

    return await openDatabase(
      path,
      version: 1,
      onCreate: (db, version) async {
        await db.execute('''
          CREATE TABLE words (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            text TEXT NOT NULL UNIQUE,
            createdAt TEXT NOT NULL,
            isInList INTEGER NOT NULL DEFAULT 0,
            wordListId INTEGER,
            wordListName TEXT,
            state INTEGER NOT NULL DEFAULT 0,
            stability REAL NOT NULL DEFAULT 0.0,
            difficulty REAL NOT NULL DEFAULT 0.0,
            reps INTEGER NOT NULL DEFAULT 0,
            lapses INTEGER NOT NULL DEFAULT 0,
            lastReviewDate TEXT,
            nextReviewDate TEXT,
            stateUpdatedAt TEXT,
            metaUpdatedAt TEXT
          )
        ''');

        await db.execute('''
          CREATE TABLE word_lists (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            createdAt TEXT NOT NULL,
            updatedAt TEXT,
            wordCount INTEGER NOT NULL DEFAULT 0
          )
        ''');

        await db.execute('''
          CREATE TABLE review_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            wordId INTEGER NOT NULL,
            reviewDate TEXT NOT NULL,
            rating INTEGER NOT NULL,
            state INTEGER NOT NULL DEFAULT 0,
            stability REAL NOT NULL DEFAULT 0.0,
            difficulty REAL NOT NULL DEFAULT 0.0,
            stabilityBefore REAL NOT NULL DEFAULT 0.0,
            difficultyBefore REAL NOT NULL DEFAULT 0.0,
            elapsedDays INTEGER NOT NULL DEFAULT 0,
            scheduledDays INTEGER NOT NULL DEFAULT 0
          )
        ''');

        await db.execute('''
          CREATE TABLE IF NOT EXISTS sync_tombstones (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            entityType TEXT NOT NULL,
            entityKey TEXT NOT NULL,
            deletedAt TEXT NOT NULL
          )
        ''');
      },
      onOpen: (db) async {
        await db.execute('''
          CREATE TABLE IF NOT EXISTS sync_tombstones (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            entityType TEXT NOT NULL,
            entityKey TEXT NOT NULL,
            deletedAt TEXT NOT NULL
          )
        ''');

        // 平滑迁移增列 (stateUpdatedAt, metaUpdatedAt, updatedAt, stabilityBefore, difficultyBefore)
        try {
          final wordCols = await db.rawQuery('PRAGMA table_info(words)');
          final wordColNames = wordCols.map((c) => c['name'].toString().toLowerCase()).toSet();
          if (!wordColNames.contains('stateupdatedat')) {
            await db.execute('ALTER TABLE words ADD COLUMN stateUpdatedAt TEXT');
            await db.execute("UPDATE words SET stateUpdatedAt = coalesce(lastReviewDate, createdAt, datetime('now', 'localtime')) WHERE stateUpdatedAt IS NULL");
          }
          if (!wordColNames.contains('metaupdatedat')) {
            await db.execute('ALTER TABLE words ADD COLUMN metaUpdatedAt TEXT');
            await db.execute("UPDATE words SET metaUpdatedAt = coalesce(createdAt, datetime('now', 'localtime')) WHERE metaUpdatedAt IS NULL");
          }

          final listCols = await db.rawQuery('PRAGMA table_info(word_lists)');
          final listColNames = listCols.map((c) => c['name'].toString().toLowerCase()).toSet();
          if (!listColNames.contains('updatedat')) {
            await db.execute('ALTER TABLE word_lists ADD COLUMN updatedAt TEXT');
            await db.execute("UPDATE word_lists SET updatedAt = coalesce(createdAt, datetime('now', 'localtime')) WHERE updatedAt IS NULL");
          }

          final logCols = await db.rawQuery('PRAGMA table_info(review_logs)');
          final logColNames = logCols.map((c) => c['name'].toString().toLowerCase()).toSet();
          if (!logColNames.contains('stabilitybefore')) {
            await db.execute('ALTER TABLE review_logs ADD COLUMN stabilityBefore REAL NOT NULL DEFAULT 0.0');
          }
          if (!logColNames.contains('difficultybefore')) {
            await db.execute('ALTER TABLE review_logs ADD COLUMN difficultyBefore REAL NOT NULL DEFAULT 0.0');
          }
        } catch (_) {}

        // 自动将历史版本中误设为 4 的已掌握状态平滑迁移为 3，并确保已掌握词汇脱离复习调度 (nextReviewDate = NULL)
        await db.execute('UPDATE words SET state = 3 WHERE state = 4');
        await db.execute('UPDATE words SET nextReviewDate = NULL WHERE state = 3');
        await db.execute('UPDATE review_logs SET state = 3 WHERE state = 4');

        // 自动清理超过 90 天的过期墓碑
        try {
          final cutoffIso = DateTime.now().subtract(const Duration(days: 90)).toIso8601String();
          await db.delete('sync_tombstones', where: 'deletedAt < ?', whereArgs: [cutoffIso]);
        } catch (_) {}
      },
    );
  }

  // --- 墓碑 (Tombstones) 追踪 ---

  Future<void> cleanupExpiredTombstones({int retentionDays = 90}) async {
    try {
      final db = await database;
      final cutoffIso = DateTime.now().subtract(Duration(days: retentionDays)).toIso8601String();
      await db.delete('sync_tombstones', where: 'deletedAt < ?', whereArgs: [cutoffIso]);
    } catch (_) {}
  }

  static String _escapeLikePattern(String input) {
    return input.replaceAll(r'\', r'\\').replaceAll('%', r'\%').replaceAll('_', r'\_');
  }

  Future<void> addTombstone(String entityType, String entityKey, {DateTime? deletedAt}) async {
    final db = await database;
    final cleanKey = entityKey.trim().toLowerCase();
    final effectiveTime = (deletedAt ?? DateTime.now()).toLocal();
    final existing = await db.query(
      'sync_tombstones',
      where: 'entityType = ? AND entityKey = ?',
      whereArgs: [entityType, cleanKey],
    );
    if (existing.isEmpty) {
      await db.insert('sync_tombstones', {
        'entityType': entityType,
        'entityKey': cleanKey,
        'deletedAt': effectiveTime.toIso8601String(),
      });
    } else {
      final prevTime = DateTime.tryParse(existing.first['deletedAt']?.toString() ?? '')?.toLocal();
      if (prevTime == null || effectiveTime.isAfter(prevTime)) {
        await db.update(
          'sync_tombstones',
          {'deletedAt': effectiveTime.toIso8601String()},
          where: 'entityType = ? AND entityKey = ?',
          whereArgs: [entityType, cleanKey],
        );
      }
    }
  }

  Future<List<Map<String, dynamic>>> getAllTombstones() async {
    final db = await database;
    return await db.query('sync_tombstones');
  }

  Future<void> removeTombstone(String entityType, String entityKey) async {
    final db = await database;
    final cleanKey = entityKey.trim().toLowerCase();
    await db.delete(
      'sync_tombstones',
      where: 'entityType = ? AND entityKey = ?',
      whereArgs: [entityType, cleanKey],
    );
  }

  // --- 单词操作 ---

  Future<List<Word>> getAllWords() async {
    final db = await database;
    final List<Map<String, dynamic>> maps = await db.query('words', orderBy: 'createdAt DESC');
    return maps.map((m) => Word.fromMap(m)).toList();
  }

  Future<List<Word>> searchWords(
    String query, {
    int? listId,
    int? stateFilter,
    bool? archivedFilter,
  }) async {
    final db = await database;
    String where = '1=1';
    List<dynamic> args = [];

    if (query.trim().isNotEmpty) {
      where += r" AND text LIKE ? ESCAPE '\'";
      args.add('%${_escapeLikePattern(query.trim())}%');
    }

    if (listId != null) {
      where += ' AND wordListId = ?';
      args.add(listId);
    }

    if (stateFilter != null && stateFilter >= 0) {
      if (stateFilter == WordLearningState.mastered.value) {
        where += ' AND (state = ? OR state = 4)';
        args.add(stateFilter);
      } else {
        where += ' AND state = ?';
        args.add(stateFilter);
      }
    }

    if (archivedFilter != null) {
      if (archivedFilter) {
        where += ' AND (isInList = 1 OR wordListId IS NOT NULL)';
      } else {
        where += ' AND (isInList = 0 OR isInList IS NULL) AND wordListId IS NULL';
      }
    }

    final List<Map<String, dynamic>> maps = await db.query(
      'words',
      where: where,
      whereArgs: args,
      orderBy: 'createdAt DESC',
    );
    return maps.map((m) => Word.fromMap(m)).toList();
  }

  Future<List<Word>> getStudyQueue({int? listId}) async {
    final db = await database;
    final now = DateTime.now();
    final todayStartIso = DateTime(now.year, now.month, now.day).toIso8601String();
    final endOfTodayIso = DateTime(now.year, now.month, now.day, 23, 59, 59, 999).toIso8601String();
    
    // 到期条件：不是已掌握(3/4)，且排除今日已复习过的词汇 (lastReviewDate >= todayStart)，并且 (从未学过 reps == 0 或 nextReviewDate 为空 或 nextReviewDate <= 今日结束)
    String where = 'state != 3 AND state != 4 AND (lastReviewDate IS NULL OR lastReviewDate < ?) AND (reps = 0 OR nextReviewDate IS NULL OR nextReviewDate <= ?)';
    List<dynamic> args = [todayStartIso, endOfTodayIso];

    if (listId != null) {
      where += ' AND wordListId = ?';
      args.add(listId);
    }

    final maps = await db.query(
      'words',
      where: where,
      whereArgs: args,
      orderBy: 'CASE WHEN reps = 0 THEN 0 ELSE 1 END ASC, nextReviewDate ASC, createdAt ASC',
    );
    return maps.map((m) => Word.fromMap(m)).toList();
  }

  Future<int> insertWord(Word word) async {
    final db = await database;
    final cleanKey = word.text.trim().toLowerCase();
    await removeTombstone('word', cleanKey);
    return await db.insert('words', word.toMap(), conflictAlgorithm: ConflictAlgorithm.ignore);
  }

  Future<int> updateWord(Word word) async {
    final db = await database;
    return await db.update('words', word.toMap(), where: 'id = ?', whereArgs: [word.id]);
  }

  Future<int> deleteWord(int id) async {
    final db = await database;
    final words = await db.query('words', where: 'id = ?', whereArgs: [id]);
    if (words.isNotEmpty) {
      final text = words.first['text']?.toString() ?? '';
      if (text.isNotEmpty) {
        await addTombstone('word', text.trim().toLowerCase());
      }
    }
    await db.delete('review_logs', where: 'wordId = ?', whereArgs: [id]);
    return await db.delete('words', where: 'id = ?', whereArgs: [id]);
  }

  Future<void> deleteWordsBatch(List<int> ids) async {
    if (ids.isEmpty) return;
    final db = await database;
    await db.transaction((txn) async {
      for (final id in ids) {
        final words = await txn.query('words', where: 'id = ?', whereArgs: [id]);
        if (words.isNotEmpty) {
          final text = words.first['text']?.toString() ?? '';
          if (text.isNotEmpty) {
            final cleanKey = text.trim().toLowerCase();
            final existing = await txn.query(
              'sync_tombstones',
              where: 'entityType = ? AND entityKey = ?',
              whereArgs: ['word', cleanKey],
            );
            if (existing.isEmpty) {
              await txn.insert('sync_tombstones', {
                'entityType': 'word',
                'entityKey': cleanKey,
                'deletedAt': DateTime.now().toIso8601String(),
              });
            }
          }
        }
        await txn.delete('review_logs', where: 'wordId = ?', whereArgs: [id]);
        await txn.delete('words', where: 'id = ?', whereArgs: [id]);
      }
    });
  }

  Future<void> addWordsToList(List<int> wordIds, int listId, String listName) async {
    if (wordIds.isEmpty) return;
    final db = await database;
    final nowIso = DateTime.now().toIso8601String();
    await db.transaction((txn) async {
      for (final id in wordIds) {
        await txn.update(
          'words',
          {
            'isInList': 1,
            'wordListId': listId,
            'wordListName': listName,
            'metaUpdatedAt': nowIso,
          },
          where: 'id = ?',
          whereArgs: [id],
        );
      }
    });
  }

  Future<void> removeWordsFromList(List<int> wordIds) async {
    if (wordIds.isEmpty) return;
    final db = await database;
    final nowIso = DateTime.now().toIso8601String();
    await db.transaction((txn) async {
      for (final id in wordIds) {
        await txn.update(
          'words',
          {
            'isInList': 0,
            'wordListId': null,
            'wordListName': null,
            'metaUpdatedAt': nowIso,
          },
          where: 'id = ?',
          whereArgs: [id],
        );
      }
    });
  }

  /// 手动更改单词状态（严格遵循公理：手动标记单词状态仅限【未学习(newWord)】和【已掌握(mastered)】两种）
  Future<void> updateWordState(int id, WordLearningState newState) async {
    await updateWordsStateBatch([id], newState);
  }

  /// 批量手动更改单词状态（严格仅限【未学习】与【已掌握】；【已掌握】独立于 FSRS 算法，不伪造 Stability/LastReviewDate）
  Future<void> updateWordsStateBatch(List<int> ids, WordLearningState newState) async {
    if (ids.isEmpty) return;
    if (newState != WordLearningState.mastered) {
      // 任何非已掌握的手动状态标记统一归约为重置【未学习 (newWord)】
      await relearnWordsBatch(ids);
      return;
    }

    final db = await database;
    final nowIso = DateTime.now().toIso8601String();
    await db.transaction((txn) async {
      for (final id in ids) {
        await txn.update(
          'words',
          {
            'state': WordLearningState.mastered.value,
            'nextReviewDate': null,
            'stateUpdatedAt': nowIso,
          },
          where: 'id = ?',
          whereArgs: [id],
        );
      }
    });
  }

  /// 重置为【未学习 (newWord = 0)】并清空历史复习记录（写入同步墓碑）
  Future<void> relearnWordsBatch(List<int> ids) async {
    if (ids.isEmpty) return;
    final db = await database;
    final now = DateTime.now();
    final nowIso = now.toIso8601String();
    await db.transaction((txn) async {
      for (final id in ids) {
        final wordRows = await txn.query('words', columns: ['text'], where: 'id = ?', whereArgs: [id]);
        final wordText = wordRows.isNotEmpty ? (wordRows.first['text']?.toString() ?? '').trim().toLowerCase() : '';
        if (wordText.isNotEmpty) {
          final oldLogs = await txn.query('review_logs', where: 'wordId = ?', whereArgs: [id]);
          for (final lMap in oldLogs) {
            final l = ReviewLog.fromMap(lMap);
            final d = l.reviewDate.toLocal();
            final timeStr = '${d.year}${d.month.toString().padLeft(2, '0')}${d.day.toString().padLeft(2, '0')}'
                '${d.hour.toString().padLeft(2, '0')}${d.minute.toString().padLeft(2, '0')}${d.second.toString().padLeft(2, '0')}';
            final tombKey = '${wordText}_$timeStr';
            await txn.delete(
              'sync_tombstones',
              where: 'entityType = ? AND entityKey = ?',
              whereArgs: ['review_log', tombKey],
            );
            await txn.insert('sync_tombstones', {
              'entityType': 'review_log',
              'entityKey': tombKey,
              'deletedAt': nowIso,
            });
          }
        }
        await txn.delete('review_logs', where: 'wordId = ?', whereArgs: [id]);
        await txn.update(
          'words',
          {
            'state': WordLearningState.newWord.value,
            'stability': 0.0,
            'difficulty': 0.0,
            'reps': 0,
            'lapses': 0,
            'lastReviewDate': null,
            'nextReviewDate': null,
            'stateUpdatedAt': nowIso,
          },
          where: 'id = ?',
          whereArgs: [id],
        );
      }
    });
  }

  /// 获取指定词单/搜索条件下，各个分类胶囊的单词统计数
  Future<Map<String, int>> getCategoryCounts({int? listId, String query = ''}) async {
    try {
      final db = await database;
      String baseWhere = '1=1';
      List<dynamic> baseArgs = [];
      if (listId != null) {
        baseWhere += ' AND wordListId = ?';
        baseArgs.add(listId);
      }
      if (query.trim().isNotEmpty) {
        baseWhere += r" AND text LIKE ? ESCAPE '\'";
        baseArgs.add('%${_escapeLikePattern(query.trim())}%');
      }

      final total = Sqflite.firstIntValue(
        await db.rawQuery('SELECT COUNT(*) FROM words WHERE $baseWhere', baseArgs),
      ) ?? 0;

      final newWords = Sqflite.firstIntValue(
        await db.rawQuery('SELECT COUNT(*) FROM words WHERE $baseWhere AND state = 0', baseArgs),
      ) ?? 0;

      final learning = Sqflite.firstIntValue(
        await db.rawQuery('SELECT COUNT(*) FROM words WHERE $baseWhere AND state = 1', baseArgs),
      ) ?? 0;

      final review = Sqflite.firstIntValue(
        await db.rawQuery('SELECT COUNT(*) FROM words WHERE $baseWhere AND state = 2', baseArgs),
      ) ?? 0;

      final mastered = Sqflite.firstIntValue(
        await db.rawQuery('SELECT COUNT(*) FROM words WHERE $baseWhere AND (state = 3 OR state = 4)', baseArgs),
      ) ?? 0;

      final unarchived = Sqflite.firstIntValue(
        await db.rawQuery(
          'SELECT COUNT(*) FROM words WHERE $baseWhere AND (isInList = 0 OR isInList IS NULL) AND wordListId IS NULL',
          baseArgs,
        ),
      ) ?? 0;

      final archived = Sqflite.firstIntValue(
        await db.rawQuery(
          'SELECT COUNT(*) FROM words WHERE $baseWhere AND (isInList = 1 OR wordListId IS NOT NULL)',
          baseArgs,
        ),
      ) ?? 0;

      return {
        'all': total,
        'newWord': newWords,
        'learning': learning,
        'review': review,
        'mastered': mastered,
        'unarchived': unarchived,
        'archived': archived,
      };
    } catch (_) {
      return {
        'all': 0,
        'newWord': 0,
        'learning': 0,
        'review': 0,
        'mastered': 0,
        'unarchived': 0,
        'archived': 0,
      };
    }
  }

  // --- 批量导入生词 ---

  Future<int> importWords(List<String> rawWords, {int? listId, String? listName}) async {
    final db = await database;
    int count = 0;
    final now = DateTime.now();

    await db.transaction((txn) async {
      for (final raw in rawWords) {
        final text = raw.trim();
        if (text.isEmpty) continue;

        // 查重
        final existing = await txn.query('words', where: 'text = ?', whereArgs: [text]);
        if (existing.isEmpty) {
          final word = Word(
            text: text,
            createdAt: now,
            isInList: listId != null,
            wordListId: listId,
            wordListName: listName,
            state: WordLearningState.newWord.value,
          );
          await txn.insert('words', word.toMap());
          final cleanKey = text.toLowerCase();
          await txn.delete(
            'sync_tombstones',
            where: 'entityType = ? AND entityKey = ?',
            whereArgs: ['word', cleanKey],
          );
          count++;
        }
      }

      if (listId != null) {
        final currentCountResult = await txn.rawQuery(
          'SELECT COUNT(*) as c FROM words WHERE wordListId = ?',
          [listId],
        );
        int totalInList = Sqflite.firstIntValue(currentCountResult) ?? 0;
        await txn.update('word_lists', {'wordCount': totalInList}, where: 'id = ?', whereArgs: [listId]);
      }
    });

    return count;
  }

  // --- FSRS 复习打卡（同一自然日单次打卡幂等） ---

  Future<void> reviewWordFSRS(Word word, int rating, {DateTime? now}) async {
    final effectiveNow = (now ?? DateTime.now()).toLocal();
    final dayStart = DateTime(effectiveNow.year, effectiveNow.month, effectiveNow.day).toIso8601String();
    final dayEnd = DateTime(effectiveNow.year, effectiveNow.month, effectiveNow.day, 23, 59, 59, 999).toIso8601String();
    final db = await database;

    final sameDayMaps = await db.query(
      'review_logs',
      where: 'wordId = ? AND reviewDate >= ? AND reviewDate <= ?',
      whereArgs: [word.id, dayStart, dayEnd],
      orderBy: 'reviewDate DESC',
    );

    if (sameDayMaps.isNotEmpty) {
      final existingLog = ReviewLog.fromMap(sameDayMaps.first);
      final priorMaps = await db.query(
        'review_logs',
        where: 'wordId = ? AND reviewDate < ?',
        whereArgs: [word.id, dayStart],
        orderBy: 'reviewDate ASC',
      );
      final priorLogs = priorMaps.map((m) => ReviewLog.fromMap(m)).toList();
      final prevLog = priorLogs.isNotEmpty ? priorLogs.last : null;

      final int preReps = priorLogs.length;
      final int preLapses = priorLogs.where((l) => l.rating == 1).length;
      final double preStability = preReps > 0
          ? (existingLog.stabilityBefore > 0
              ? existingLog.stabilityBefore
              : (prevLog != null && prevLog.stability > 0 ? prevLog.stability : word.stability))
          : 0.0;
      final double preDifficulty = preReps > 0
          ? (existingLog.difficultyBefore > 0
              ? existingLog.difficultyBefore
              : (prevLog != null && prevLog.difficulty > 0 ? prevLog.difficulty : word.difficulty))
          : 0.0;
      final DateTime? preLastReviewDate = prevLog?.reviewDate;

      final preWord = Word(
        id: word.id,
        text: word.text,
        state: preReps == 0
            ? WordLearningState.newWord.value
            : ((preReps < 2 || (prevLog != null && prevLog.rating == 1))
                ? WordLearningState.learning.value
                : WordLearningState.review.value),
        stability: preStability,
        difficulty: preDifficulty,
        reps: preReps,
        lapses: preLapses,
        lastReviewDate: preLastReviewDate,
      );

      final result = FsrsEngine.review(preWord, rating, effectiveNow);
      word.stability = result.newStability;
      word.difficulty = result.newDifficulty;
      word.lastReviewDate = effectiveNow;
      word.reps = preReps + 1;
      word.lapses = preLapses + (rating == 1 ? 1 : 0);

      if (word.state == WordLearningState.mastered.value) {
        word.nextReviewDate = null;
      } else if (rating == 1) {
        word.state = WordLearningState.learning.value;
        word.nextReviewDate = result.nextReviewDate;
      } else {
        word.state = (word.reps < 2) ? WordLearningState.learning.value : WordLearningState.review.value;
        word.nextReviewDate = result.nextReviewDate;
      }

      word.stateUpdatedAt = effectiveNow;
      await db.update('words', word.toMap(), where: 'id = ?', whereArgs: [word.id]);

      final int elapsed = preLastReviewDate != null
          ? FsrsEngine.getCalendarElapsedDays(preLastReviewDate, effectiveNow).round()
          : 0;

      await db.update(
        'review_logs',
        {
          'reviewDate': effectiveNow.toIso8601String(),
          'rating': rating,
          'state': word.state,
          'stability': result.newStability,
          'difficulty': result.newDifficulty,
          'stabilityBefore': preStability,
          'difficultyBefore': preDifficulty,
          'elapsedDays': elapsed,
          'scheduledDays': result.intervalDays,
        },
        where: 'id = ?',
        whereArgs: [existingLog.id],
      );

      // 清理同一天可能存在的多余重复日志
      if (sameDayMaps.length > 1) {
        for (int i = 1; i < sameDayMaps.length; i++) {
          final dupId = sameDayMaps[i]['id'] as int?;
          if (dupId != null) {
            await db.delete('review_logs', where: 'id = ?', whereArgs: [dupId]);
          }
        }
      }
      return;
    }

    final double oldS = word.stability;
    final double oldD = word.difficulty;
    final int elapsedDays = (word.reps > 0 && word.lastReviewDate != null)
        ? FsrsEngine.getCalendarElapsedDays(word.lastReviewDate!, effectiveNow).round()
        : 0;
    final result = FsrsEngine.review(word, rating, effectiveNow);

    word.stability = result.newStability;
    word.difficulty = result.newDifficulty;
    word.lastReviewDate = effectiveNow;
    bool isFirst = word.reps == 0;
    word.reps += 1;

    if (rating == 1) {
      word.lapses += 1;
    }

    if (word.state == WordLearningState.mastered.value) {
      // 用户已手动指定为【已掌握】，保持独立分类且免除下次复习排期
      word.nextReviewDate = null;
    } else if (rating == 1 || isFirst || word.reps < 2) {
      word.state = WordLearningState.learning.value;
      word.nextReviewDate = result.nextReviewDate;
    } else {
      word.state = WordLearningState.review.value;
      word.nextReviewDate = result.nextReviewDate;
    }

    word.stateUpdatedAt = effectiveNow;
    await db.update('words', word.toMap(), where: 'id = ?', whereArgs: [word.id]);

    final log = ReviewLog(
      wordId: word.id,
      reviewDate: effectiveNow,
      rating: rating,
      state: word.state,
      stability: result.newStability,
      difficulty: result.newDifficulty,
      stabilityBefore: oldS,
      difficultyBefore: oldD,
      elapsedDays: elapsedDays,
      scheduledDays: result.intervalDays,
    );
    await db.insert('review_logs', log.toMap());
  }

  // --- 今日打卡记录与纠错 ---

  Future<List<Map<String, dynamic>>> getTodayReviewItems() async {
    final db = await database;
    final now = DateTime.now();
    final todayPrefix = '${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}';
    final startOfDay = DateTime(now.year, now.month, now.day).toIso8601String();
    final endOfDay = DateTime(now.year, now.month, now.day, 23, 59, 59, 999).toIso8601String();

    final sql = '''
      SELECT r.id as logId, r.wordId, w.text as wordText, r.rating, r.reviewDate,
             w.state, w.stability, w.difficulty, w.reps, w.lastReviewDate
      FROM review_logs r
      JOIN words w ON r.wordId = w.id
      WHERE (r.reviewDate LIKE ? OR (r.reviewDate >= ? AND r.reviewDate <= ?))
      ORDER BY r.reviewDate DESC
    ''';

    return await db.rawQuery(sql, ['$todayPrefix%', startOfDay, endOfDay]);
  }

  Future<void> changeTodayReviewRating(int logId, int newRating) async {
    final db = await database;
    final logMap = await db.query('review_logs', where: 'id = ?', whereArgs: [logId]);
    if (logMap.isEmpty) return;

    final log = ReviewLog.fromMap(logMap.first);
    final int oldRating = log.rating;
    if (oldRating == newRating) return;

    final wordMap = await db.query('words', where: 'id = ?', whereArgs: [log.wordId]);
    if (wordMap.isEmpty) return;

    final word = Word.fromMap(wordMap.first);
    final allWordLogMaps = await db.query(
      'review_logs',
      where: 'wordId = ? AND id != ?',
      whereArgs: [word.id, logId],
      orderBy: 'reviewDate ASC',
    );
    final allOtherLogs = allWordLogMaps.map((m) => ReviewLog.fromMap(m)).toList();
    final priorLogs = allOtherLogs.where((l) => !l.reviewDate.isAfter(log.reviewDate)).toList();
    final subsequentLogs = allOtherLogs.where((l) => l.reviewDate.isAfter(log.reviewDate)).toList();
    final ReviewLog? prevLog = priorLogs.isNotEmpty ? priorLogs.last : null;

    final int preReps = priorLogs.length;
    final int preLapses = priorLogs.where((l) => l.rating == 1).length;

    double preStability = 0.0;
    double preDifficulty = 0.0;
    DateTime? preLastReviewDate;

    if (preReps > 0) {
      preStability = log.stabilityBefore > 0
          ? log.stabilityBefore
          : (prevLog != null && prevLog.stability > 0 ? prevLog.stability : word.stability);
      preDifficulty = log.difficultyBefore > 0
          ? log.difficultyBefore
          : (prevLog != null && prevLog.difficulty > 0 ? prevLog.difficulty : word.difficulty);
      preLastReviewDate = prevLog != null
          ? prevLog.reviewDate
          : log.reviewDate.subtract(Duration(days: max(1, preStability.round())));
    }

    final simWord = Word(
      id: word.id,
      text: word.text,
      state: preReps == 0
          ? WordLearningState.newWord.value
          : ((preReps < 2 || (prevLog != null && prevLog.rating == 1))
              ? WordLearningState.learning.value
              : WordLearningState.review.value),
      stability: preStability,
      difficulty: preDifficulty,
      reps: preReps,
      lapses: preLapses,
      lastReviewDate: preLastReviewDate,
    );

    final int elapsedForTarget = preLastReviewDate != null
        ? FsrsEngine.getCalendarElapsedDays(preLastReviewDate, log.reviewDate).round()
        : 0;
    final result = FsrsEngine.review(simWord, newRating, log.reviewDate);

    simWord.stability = result.newStability;
    simWord.difficulty = result.newDifficulty;
    simWord.lastReviewDate = log.reviewDate;
    simWord.nextReviewDate = result.nextReviewDate;
    simWord.reps = preReps + 1;
    simWord.lapses = preLapses + (newRating == 1 ? 1 : 0);
    simWord.state = newRating == 1
        ? WordLearningState.learning.value
        : (simWord.reps < 2 ? WordLearningState.learning.value : WordLearningState.review.value);

    await db.update(
      'review_logs',
      {
        'rating': newRating,
        'state': simWord.state,
        'stability': result.newStability,
        'difficulty': result.newDifficulty,
        'stabilityBefore': preStability,
        'difficultyBefore': preDifficulty,
        'elapsedDays': elapsedForTarget,
        'scheduledDays': result.intervalDays,
      },
      where: 'id = ?',
      whereArgs: [logId],
    );

    // 若该日志之后还有后续日志，按时间轴重放修正
    for (final subLog in subsequentLogs) {
      final subPreS = simWord.stability;
      final subPreD = simWord.difficulty;
      final int subElapsed = simWord.lastReviewDate != null
          ? FsrsEngine.getCalendarElapsedDays(simWord.lastReviewDate!, subLog.reviewDate).round()
          : 0;
      final subRes = FsrsEngine.review(simWord, subLog.rating, subLog.reviewDate);

      simWord.stability = subRes.newStability;
      simWord.difficulty = subRes.newDifficulty;
      simWord.lastReviewDate = subLog.reviewDate;
      simWord.nextReviewDate = subRes.nextReviewDate;
      simWord.reps += 1;
      if (subLog.rating == 1) {
        simWord.lapses += 1;
        simWord.state = WordLearningState.learning.value;
      } else {
        simWord.state = (simWord.reps < 2) ? WordLearningState.learning.value : WordLearningState.review.value;
      }

      await db.update(
        'review_logs',
        {
          'state': simWord.state,
          'stability': subRes.newStability,
          'difficulty': subRes.newDifficulty,
          'stabilityBefore': subPreS,
          'difficultyBefore': subPreD,
          'elapsedDays': subElapsed,
          'scheduledDays': subRes.intervalDays,
        },
        where: 'id = ?',
        whereArgs: [subLog.id],
      );
    }

    word.stability = simWord.stability;
    word.difficulty = simWord.difficulty;
    word.lastReviewDate = simWord.lastReviewDate;
    word.reps = simWord.reps;
    word.lapses = simWord.lapses;
    if (word.state == WordLearningState.mastered.value) {
      word.nextReviewDate = null;
    } else {
      word.state = simWord.state;
      word.nextReviewDate = simWord.nextReviewDate;
    }

    word.stateUpdatedAt = DateTime.now();
    await db.update('words', word.toMap(), where: 'id = ?', whereArgs: [word.id]);
  }

  Future<void> revertTodayReview(int logId, {bool isSyncRevert = false}) async {
    final db = await database;
    final logMap = await db.query('review_logs', where: 'id = ?', whereArgs: [logId]);
    if (logMap.isEmpty) return;

    final log = ReviewLog.fromMap(logMap.first);
    final wordMap = await db.query('words', where: 'id = ?', whereArgs: [log.wordId]);

    await db.delete('review_logs', where: 'id = ?', whereArgs: [logId]);

    if (wordMap.isNotEmpty) {
      final word = Word.fromMap(wordMap.first);
      final remainingMaps = await db.query(
        'review_logs',
        where: 'wordId = ?',
        whereArgs: [word.id],
        orderBy: 'reviewDate ASC',
      );
      final remainingLogs = remainingMaps.map((m) => ReviewLog.fromMap(m)).toList();

      if (remainingLogs.isEmpty) {
        if (word.state != WordLearningState.mastered.value) {
          word.state = WordLearningState.newWord.value;
        }
        word.stability = 0.0;
        word.difficulty = 0.0;
        word.reps = 0;
        word.lapses = 0;
        word.lastReviewDate = null;
        word.nextReviewDate = null;
      } else {
        final simWord = Word(
          id: word.id,
          text: word.text,
          state: WordLearningState.newWord.value,
        );
        for (final remLog in remainingLogs) {
          final preS = simWord.stability;
          final preD = simWord.difficulty;
          final int elapsed = simWord.lastReviewDate != null
              ? FsrsEngine.getCalendarElapsedDays(simWord.lastReviewDate!, remLog.reviewDate).round()
              : 0;
          final res = FsrsEngine.review(simWord, remLog.rating, remLog.reviewDate);

          simWord.stability = res.newStability;
          simWord.difficulty = res.newDifficulty;
          simWord.lastReviewDate = remLog.reviewDate;
          simWord.nextReviewDate = res.nextReviewDate;
          simWord.reps += 1;
          if (remLog.rating == 1) {
            simWord.lapses += 1;
            simWord.state = WordLearningState.learning.value;
          } else {
            simWord.state = (simWord.reps < 2) ? WordLearningState.learning.value : WordLearningState.review.value;
          }

          await db.update(
            'review_logs',
            {
              'state': simWord.state,
              'stability': res.newStability,
              'difficulty': res.newDifficulty,
              'stabilityBefore': preS,
              'difficultyBefore': preD,
              'elapsedDays': elapsed,
              'scheduledDays': res.intervalDays,
            },
            where: 'id = ?',
            whereArgs: [remLog.id],
          );
        }

        word.stability = simWord.stability;
        word.difficulty = simWord.difficulty;
        word.reps = simWord.reps;
        word.lapses = simWord.lapses;
        word.lastReviewDate = simWord.lastReviewDate;

        if (word.state == WordLearningState.mastered.value) {
          word.nextReviewDate = null;
        } else {
          // 主动撤销今日打卡时，允许该词今日重新进入学习队列
          word.state = simWord.state;
          word.nextReviewDate = isSyncRevert ? simWord.nextReviewDate : DateTime.now();
        }
      }

      if (!isSyncRevert) {
        word.stateUpdatedAt = DateTime.now();
      } else if (!word.stateUpdatedAt.isAfter(log.reviewDate.add(const Duration(seconds: 5)))) {
        word.stateUpdatedAt = word.lastReviewDate ?? word.createdAt;
      }

      await db.update('words', word.toMap(), where: 'id = ?', whereArgs: [word.id]);

      if (!isSyncRevert) {
        final d = log.reviewDate.toLocal();
        final timeStr = '${d.year}${d.month.toString().padLeft(2, '0')}${d.day.toString().padLeft(2, '0')}'
            '${d.hour.toString().padLeft(2, '0')}${d.minute.toString().padLeft(2, '0')}${d.second.toString().padLeft(2, '0')}';
        await addTombstone('review_log', '${word.text.trim().toLowerCase()}_$timeStr');
      }
    }
  }

  // --- 词单操作 ---

  Future<List<WordList>> getAllWordLists() async {
    final db = await database;
    final List<Map<String, dynamic>> maps = await db.rawQuery('''
      SELECT l.id, l.name, l.createdAt, l.updatedAt,
             (SELECT COUNT(*) FROM words w WHERE w.wordListId = l.id) AS wordCount
      FROM word_lists l
      ORDER BY l.createdAt DESC
    ''');
    return maps.map((m) => WordList.fromMap(m)).toList();
  }

  Future<WordList> createWordList(String name) async {
    final db = await database;
    final cleanName = name.trim();

    // 创建词单时移除可能存在的历史删除墓碑
    await removeTombstone('word_list', cleanName);

    final existing = await db.query('word_lists', where: 'name = ?', whereArgs: [cleanName]);
    if (existing.isNotEmpty) {
      return WordList.fromMap(existing.first);
    }

    final now = DateTime.now();
    final list = WordList(name: cleanName, createdAt: now, updatedAt: now);
    final id = await db.insert('word_lists', list.toMap());
    list.id = id;
    return list;
  }

  Future<int> updateWordList(WordList list) async {
    final db = await database;
    return await db.update('word_lists', list.toMap(), where: 'id = ?', whereArgs: [list.id]);
  }

  Future<void> deleteWordList(int listId) async {
    final db = await database;
    final nowIso = DateTime.now().toIso8601String();
    await db.transaction((txn) async {
      final listMaps = await txn.query('word_lists', where: 'id = ?', whereArgs: [listId]);
      if (listMaps.isNotEmpty) {
        final name = listMaps.first['name']?.toString() ?? '';
        if (name.isNotEmpty) {
          final cleanKey = name.trim().toLowerCase();
          final existing = await txn.query(
            'sync_tombstones',
            where: 'entityType = ? AND entityKey = ?',
            whereArgs: ['word_list', cleanKey],
          );
          if (existing.isEmpty) {
            await txn.insert('sync_tombstones', {
              'entityType': 'word_list',
              'entityKey': cleanKey,
              'deletedAt': nowIso,
            });
          }
        }
      }

      await txn.update(
        'words',
        {
          'isInList': 0,
          'wordListId': null,
          'wordListName': null,
          'metaUpdatedAt': nowIso,
        },
        where: 'wordListId = ?',
        whereArgs: [listId],
      );
      await txn.delete('word_lists', where: 'id = ?', whereArgs: [listId]);
    });
  }

  // --- 全量同步支持 ---

  Future<List<ReviewLog>> getReviewLogs(int wordId) async {
    final db = await database;
    final maps = await db.query(
      'review_logs',
      where: 'wordId = ?',
      whereArgs: [wordId],
      orderBy: 'reviewDate ASC',
    );
    return maps.map((m) => ReviewLog.fromMap(m)).toList();
  }

  Future<List<ReviewLog>> getAllReviewLogs() async {
    final db = await database;
    final maps = await db.query('review_logs', orderBy: 'reviewDate ASC');
    return maps.map((m) => ReviewLog.fromMap(m)).toList();
  }

  Future<void> clearAllData() async {
    final db = await database;
    await db.transaction((txn) async {
      await txn.delete('words');
      await txn.delete('word_lists');
      await txn.delete('review_logs');
      await txn.delete('sync_tombstones');
    });
  }

  // --- 应用轻量配置持久化 (IP / PIN 等) ---

  Future<String?> getSetting(String key) async {
    final db = await database;
    await db.execute('''
      CREATE TABLE IF NOT EXISTS app_settings (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      )
    ''');
    final maps = await db.query('app_settings', where: 'key = ?', whereArgs: [key]);
    if (maps.isNotEmpty) {
      return maps.first['value']?.toString();
    }
    return null;
  }

  Future<void> setSetting(String key, String value) async {
    final db = await database;
    await db.execute('''
      CREATE TABLE IF NOT EXISTS app_settings (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      )
    ''');
    await db.insert(
      'app_settings',
      {'key': key, 'value': value},
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }
}

