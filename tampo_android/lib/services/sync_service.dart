import 'dart:convert';
import 'package:http/http.dart' as http;
import '../models/word_models.dart';
import 'db_service.dart';
import 'fsrs_engine.dart';

class SyncService {
  static final SyncService instance = SyncService._internal();
  SyncService._internal();

  final DatabaseService _db = DatabaseService.instance;

  String formatServerUrl(String rawHost) {
    String host = rawHost.trim();
    if (host.isEmpty) return '';
    if (!host.startsWith('http://') && !host.startsWith('https://')) {
      host = 'http://$host';
    }
    if (!host.contains(':', 6)) {
      host = '$host:52080';
    }
    if (host.endsWith('/')) {
      host = host.substring(0, host.length - 1);
    }
    return host;
  }

  /// 测试与电脑端的连通性
  Future<bool> ping(String rawHost) async {
    try {
      final url = '${formatServerUrl(rawHost)}/api/ping';
      final res = await http.get(Uri.parse(url)).timeout(const Duration(seconds: 4));
      return res.statusCode == 200;
    } catch (_) {
      return false;
    }
  }

  /// 从电脑端拉取全部数据并合并到本地
  Future<SyncReport> pullFromPC(String rawHost, String pin) async {
    final serverUrl = formatServerUrl(rawHost);
    final url = '$serverUrl/api/sync/pull';

    final res = await http.post(
      Uri.parse(url),
      headers: {
        'Content-Type': 'application/json',
        'X-Sync-Pin': pin.trim(),
      },
    ).timeout(const Duration(seconds: 15));

    if (res.statusCode == 401) {
      throw Exception('PIN 码校验失败，请检查电脑端显示的 6 位配对码');
    }

    if (res.statusCode != 200) {
      throw Exception('拉取失败 (HTTP ${res.statusCode}): ${res.body}');
    }

    final data = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
    return await _mergeIncomingData(data);
  }

  /// 双向数据同步：先从电脑拉取合并，再将本地最新数据推送给电脑端
  Future<SyncReport> biDirectionalSync(String rawHost, String pin) async {
    // 1. 先拉取合并
    final pullReport = await pullFromPC(rawHost, pin);

    // 2. 打包本地全部数据推送至电脑
    final serverUrl = formatServerUrl(rawHost);
    final pushUrl = '$serverUrl/api/sync/push';

    final localWords = await _db.getAllWords();
    final localLists = await _db.getAllWordLists();
    final localLogs = await _db.getAllReviewLogs();
    final localTombstones = await _db.getAllTombstones();

    final payload = {
      'wordLists': localLists.map((l) => l.toMap()).toList(),
      'words': localWords.map((w) => w.toSyncMap()).toList(),
      'reviewLogs': localLogs.map((l) => l.toSyncMap()).toList(),
      'tombstones': localTombstones.map((t) => {
        'entityType': t['entityType'],
        'entityKey': t['entityKey'],
        'deletedAt': t['deletedAt'],
      }).toList(),
      'timestamp': DateTime.now().toIso8601String(),
      'deviceName': 'Tampo Android Mobile',
    };

    final pushRes = await http.post(
      Uri.parse(pushUrl),
      headers: {
        'Content-Type': 'application/json',
        'X-Sync-Pin': pin.trim(),
      },
      body: jsonEncode(payload),
    ).timeout(const Duration(seconds: 60));

    if (pushRes.statusCode != 200) {
      throw Exception('双向同步推送电脑失败 (HTTP ${pushRes.statusCode}): ${pushRes.body}');
    }

    return pullReport;
  }

  String _formatTimeKey(DateTime dt) {
    final d = dt.toLocal();
    return '${d.year}${d.month.toString().padLeft(2, '0')}${d.day.toString().padLeft(2, '0')}'
        '${d.hour.toString().padLeft(2, '0')}${d.minute.toString().padLeft(2, '0')}${d.second.toString().padLeft(2, '0')}';
  }

  String _formatDayKey(DateTime dt) {
    final d = dt.toLocal();
    return '${d.year}${d.month.toString().padLeft(2, '0')}${d.day.toString().padLeft(2, '0')}';
  }

  Future<SyncReport> _mergeIncomingData(Map<String, dynamic> data) async {
    int updatedWords = 0;
    int insertedWords = 0;
    int syncedLists = 0;
    int syncedLogs = 0;

    // 0. 处理墓碑 (Tombstones)：优先同步并应用删除与撤销
    final localTombstones = await _db.getAllTombstones();
    final deletedWordTimes = <String, DateTime?>{};
    final deletedListTimes = <String, DateTime?>{};
    final deletedLogKeys = <String>{};

    for (var t in localTombstones) {
      final type = t['entityType']?.toString() ?? '';
      final key = (t['entityKey']?.toString() ?? '').trim().toLowerCase();
      if (key.isEmpty) continue;
      final delTime = DateTime.tryParse(t['deletedAt']?.toString() ?? '')?.toLocal();
      if (type == 'word') {
        final prev = deletedWordTimes[key];
        if (prev == null || (delTime != null && delTime.isAfter(prev))) {
          deletedWordTimes[key] = delTime;
        }
      } else if (type == 'word_list') {
        final prev = deletedListTimes[key];
        if (prev == null || (delTime != null && delTime.isAfter(prev))) {
          deletedListTimes[key] = delTime;
        }
      } else if (type == 'review_log') {
        deletedLogKeys.add(key);
      }
    }

    final incomingTombstones = data['tombstones'] as List? ?? [];
    for (var item in incomingTombstones) {
      if (item is Map<String, dynamic>) {
        final entityType = (item['entityType'] ?? item['EntityType'])?.toString() ?? '';
        final entityKey = ((item['entityKey'] ?? item['EntityKey'])?.toString() ?? '').trim().toLowerCase();
        if (entityKey.isEmpty) continue;
        final tombDeletedAt = DateTime.tryParse((item['deletedAt'] ?? item['DeletedAt'])?.toString() ?? '')?.toLocal();

        if (entityType == 'word') {
          final localWords = await _db.getAllWords();
          final match = localWords.where((w) => w.text.trim().toLowerCase() == entityKey).toList();
          final matchTime = match.isNotEmpty
              ? (match.first.metaUpdatedAt.isAfter(match.first.createdAt) ? match.first.metaUpdatedAt : match.first.createdAt)
              : null;

          if (matchTime == null || tombDeletedAt == null || !tombDeletedAt.isBefore(matchTime.subtract(const Duration(seconds: 1)))) {
            deletedWordTimes[entityKey] = tombDeletedAt;
            await _db.addTombstone('word', entityKey, deletedAt: tombDeletedAt);
            for (var w in match) {
              await _db.deleteWord(w.id);
            }
          } else {
            deletedWordTimes.remove(entityKey);
            await _db.removeTombstone('word', entityKey);
          }
        } else if (entityType == 'word_list') {
          final localLists = await _db.getAllWordLists();
          final match = localLists.where((l) => l.name.trim().toLowerCase() == entityKey).toList();
          final matchTime = match.isNotEmpty
              ? (match.first.updatedAt.isAfter(match.first.createdAt) ? match.first.updatedAt : match.first.createdAt)
              : null;

          if (matchTime == null || tombDeletedAt == null || !tombDeletedAt.isBefore(matchTime.subtract(const Duration(seconds: 1)))) {
            deletedListTimes[entityKey] = tombDeletedAt;
            await _db.addTombstone('word_list', entityKey, deletedAt: tombDeletedAt);
            for (var l in match) {
              await _db.deleteWordList(l.id);
            }
          } else {
            deletedListTimes.remove(entityKey);
            await _db.removeTombstone('word_list', entityKey);
          }
        } else if (entityType == 'review_log') {
          deletedLogKeys.add(entityKey);
          await _db.addTombstone('review_log', entityKey, deletedAt: tombDeletedAt);
          // cleanKey 格式为 wordText_yyyyMMddHHmmss
          final lastUnderscore = entityKey.lastIndexOf('_');
          if (lastUnderscore > 0) {
            final wText = entityKey.substring(0, lastUnderscore);
            final timePart = entityKey.substring(lastUnderscore + 1);
            final localWords = await _db.getAllWords();
            final wMatch = localWords.where((w) => w.text.trim().toLowerCase() == wText).toList();
            if (wMatch.isNotEmpty) {
              final word = wMatch.first;
              final logs = await _db.getReviewLogs(word.id);
              for (var l in logs) {
                final lTime = _formatTimeKey(l.reviewDate);
                if (lTime == timePart) {
                  await _db.revertTodayReview(l.id, isSyncRevert: true);
                  break;
                }
              }
            }
          }
        }
      }
    }

    // 1. 同步词单 (WordLists)
    final incomingLists = (data['wordLists'] as List? ?? [])
        .map((item) => WordList.fromMap(item as Map<String, dynamic>))
        .toList();

    final localLists = await _db.getAllWordLists();
    final localListMap = {for (var l in localLists) l.name.trim().toLowerCase(): l};

    for (var inList in incomingLists) {
      final key = inList.name.trim().toLowerCase();
      if (key.isEmpty) continue;

      if (deletedListTimes.containsKey(key)) {
        final delTime = deletedListTimes[key];
        final inListTime = inList.updatedAt.isAfter(inList.createdAt) ? inList.updatedAt : inList.createdAt;
        if (delTime == null || !delTime.isBefore(inListTime.subtract(const Duration(seconds: 1)))) {
          continue;
        }
        deletedListTimes.remove(key);
        await _db.removeTombstone('word_list', key);
      }

      if (!localListMap.containsKey(key)) {
        final created = await _db.createWordList(inList.name);
        created.updatedAt = inList.updatedAt;
        await _db.updateWordList(created);
        localListMap[key] = created;
        syncedLists++;
      } else {
        final local = localListMap[key]!;
        if (inList.updatedAt.isAfter(local.updatedAt.add(const Duration(seconds: 1)))) {
          local.updatedAt = inList.updatedAt;
          await _db.updateWordList(local);
        }
      }
    }

    // 2. 双向正交合并单词 (Words)
    final incomingWords = (data['words'] as List? ?? [])
        .map((item) => Word.fromMap(item as Map<String, dynamic>))
        .toList();

    final localWords = await _db.getAllWords();
    final localWordMap = {for (var w in localWords) w.text.trim().toLowerCase(): w};

    for (var inWord in incomingWords) {
      final key = inWord.text.trim().toLowerCase();
      if (key.isEmpty) continue;

      if (deletedWordTimes.containsKey(key)) {
        final delTime = deletedWordTimes[key];
        final inWordTime = inWord.metaUpdatedAt.isAfter(inWord.createdAt) ? inWord.metaUpdatedAt : inWord.createdAt;
        if (delTime == null || !delTime.isBefore(inWordTime.subtract(const Duration(seconds: 1)))) {
          continue;
        }
        deletedWordTimes.remove(key);
        await _db.removeTombstone('word', key);
      }

      if (localWordMap.containsKey(key)) {
        final local = localWordMap[key]!;
        bool needUpdate = false;
        final incomingState = inWord.state == 4 ? WordLearningState.mastered.value : inWord.state;

        // 【维度 1：学习算法与复习进度状态，由 stateUpdatedAt 裁决】
        if (inWord.stateUpdatedAt.isAfter(local.stateUpdatedAt.add(const Duration(seconds: 1)))) {
          local.state = incomingState;
          if (incomingState == WordLearningState.mastered.value) {
            // 已掌握是独立于 FSRS 的手动分类：豁免复习调度，不伪造 FSRS 参数
            local.nextReviewDate = null;
            if (inWord.reps > local.reps) {
              local.stability = inWord.stability;
              local.difficulty = inWord.difficulty;
              local.reps = inWord.reps;
              local.lapses = inWord.lapses;
              local.lastReviewDate = inWord.lastReviewDate;
            }
          } else {
            local.stability = inWord.stability;
            local.difficulty = inWord.difficulty;
            local.reps = inWord.reps;
            local.lapses = inWord.lapses;
            local.lastReviewDate = inWord.lastReviewDate;
            local.nextReviewDate = inWord.nextReviewDate;
          }
          local.stateUpdatedAt = inWord.stateUpdatedAt;
          needUpdate = true;
        } else if (incomingState == WordLearningState.mastered.value &&
            local.state != WordLearningState.mastered.value &&
            !local.stateUpdatedAt.isAfter(inWord.stateUpdatedAt.add(const Duration(seconds: 1)))) {
          // 显式已掌握属性安全保底（仅当传入端状态不落后于本地最新状态修改时生效）
          local.state = WordLearningState.mastered.value;
          local.nextReviewDate = null;
          local.stateUpdatedAt = inWord.stateUpdatedAt.isAfter(local.stateUpdatedAt) ? inWord.stateUpdatedAt : local.stateUpdatedAt;
          needUpdate = true;
        }

        // 【维度 2：词单归属与元数据，由 metaUpdatedAt 独立裁决】
        if (inWord.metaUpdatedAt.isAfter(local.metaUpdatedAt.add(const Duration(seconds: 1)))) {
          int? targetListId;
          String? targetListName;
          if (inWord.wordListName != null &&
              localListMap.containsKey(inWord.wordListName!.trim().toLowerCase())) {
            final mappedList = localListMap[inWord.wordListName!.trim().toLowerCase()]!;
            targetListId = mappedList.id;
            targetListName = mappedList.name;
          }

          local.wordListId = targetListId;
          local.wordListName = targetListName;
          local.isInList = targetListId != null;
          local.metaUpdatedAt = inWord.metaUpdatedAt;
          needUpdate = true;
        }

        if (needUpdate) {
          await _db.updateWord(local);
          updatedWords++;
        }
      } else {
        // 本地没有的新单词，直接导入
        int? targetListId;
        String? targetListName;
        if (inWord.wordListName != null &&
            localListMap.containsKey(inWord.wordListName!.trim().toLowerCase())) {
          final mappedList = localListMap[inWord.wordListName!.trim().toLowerCase()]!;
          targetListId = mappedList.id;
          targetListName = mappedList.name;
        }
        inWord.id = 0; // 重置主键让数据库自增
        inWord.state = inWord.state == 4 ? WordLearningState.mastered.value : inWord.state;
        if (inWord.state == WordLearningState.mastered.value) {
          inWord.nextReviewDate = null;
        }
        inWord.wordListId = targetListId;
        inWord.wordListName = targetListName;
        inWord.isInList = targetListId != null;
        await _db.insertWord(inWord);
        insertedWords++;
      }
    }

    // 3. 增量合并或原地更新复习日志（按 wordId + 自然日 yyyyMMdd 唯一归并）
    final incomingLogs = (data['reviewLogs'] as List? ?? [])
        .map((item) => ReviewLog.fromMap(item as Map<String, dynamic>))
        .toList();

    if (incomingLogs.isNotEmpty) {
      final refreshedWords = await _db.getAllWords();
      final wordIdMap = {for (var w in refreshedWords) w.text.trim().toLowerCase(): w.id};
      final inWordMap = {for (var w in incomingWords) w.id: w.text.trim().toLowerCase()};

      final localLogs = await _db.getAllReviewLogs();
      final existingLogsByDayKey = <String, ReviewLog>{};
      final db = await _db.database;

      // 先对本地可能存在的同日多条日志做去重清洗
      for (var l in localLogs) {
        final dayKey = '${l.wordId}_${_formatDayKey(l.reviewDate)}';
        final prev = existingLogsByDayKey[dayKey];
        if (prev == null) {
          existingLogsByDayKey[dayKey] = l;
        } else {
          if (l.reviewDate.isAfter(prev.reviewDate)) {
            await db.delete('review_logs', where: 'id = ?', whereArgs: [prev.id]);
            existingLogsByDayKey[dayKey] = l;
          } else {
            await db.delete('review_logs', where: 'id = ?', whereArgs: [l.id]);
          }
        }
      }

      final pendingLogTombstones = <String>[];
      await db.transaction((txn) async {
        for (var log in incomingLogs) {
          final wordTextKey = inWordMap[log.wordId];
          if (wordTextKey != null) {
            if (deletedWordTimes.containsKey(wordTextKey)) continue;

            final localLogDate = log.reviewDate.toLocal();
            log.reviewDate = localLogDate;
            final timeStr = _formatTimeKey(localLogDate);
            final dayStr = _formatDayKey(localLogDate);
            final logTombKey = '${wordTextKey}_$timeStr';
            // 如果该 log 已被撤销删除，忽略！
            if (deletedLogKeys.contains(logTombKey)) continue;

            if (wordIdMap.containsKey(wordTextKey)) {
              final localWordId = wordIdMap[wordTextKey]!;
              final dayKey = '${localWordId}_$dayStr';
              final existingLog = existingLogsByDayKey[dayKey];
              if (existingLog == null) {
                log.wordId = localWordId;
                log.id = 0;
                final insertedId = await txn.insert('review_logs', log.toMap());
                log.id = insertedId;
                existingLogsByDayKey[dayKey] = log;
                syncedLogs++;
              } else {
                // 同一自然日已存在打卡记录：以较新时间戳的打卡为准，并将被覆盖的旧时间戳记入墓碑防止复活
                final existingTimeStr = _formatTimeKey(existingLog.reviewDate);
                if (localLogDate.isAfter(existingLog.reviewDate) ||
                    (localLogDate.isAtSameMomentAs(existingLog.reviewDate) &&
                        (existingLog.rating != log.rating ||
                            (log.stability > 0 && (existingLog.stability - log.stability).abs() > 0.0001) ||
                            (log.difficulty > 0 && (existingLog.difficulty - log.difficulty).abs() > 0.0001)))) {
                  if (existingTimeStr != timeStr) {
                    final oldTombKey = '${wordTextKey}_$existingTimeStr';
                    deletedLogKeys.add(oldTombKey);
                    pendingLogTombstones.add(oldTombKey);
                  }
                  await txn.update(
                    'review_logs',
                    {
                      'reviewDate': localLogDate.toIso8601String(),
                      'rating': log.rating,
                      if (log.state > 0) 'state': log.state,
                      if (log.stability > 0) 'stability': log.stability,
                      if (log.difficulty > 0) 'difficulty': log.difficulty,
                      'stabilityBefore': log.stabilityBefore,
                      'difficultyBefore': log.difficultyBefore,
                      'elapsedDays': log.elapsedDays,
                      if (log.scheduledDays > 0) 'scheduledDays': log.scheduledDays,
                    },
                    where: 'id = ?',
                    whereArgs: [existingLog.id],
                  );
                  existingLog.reviewDate = localLogDate;
                  existingLog.rating = log.rating;
                  if (log.state > 0) existingLog.state = log.state;
                  if (log.stability > 0) existingLog.stability = log.stability;
                  if (log.difficulty > 0) existingLog.difficulty = log.difficulty;
                  existingLog.stabilityBefore = log.stabilityBefore;
                  existingLog.difficultyBefore = log.difficultyBefore;
                  existingLog.elapsedDays = log.elapsedDays;
                  if (log.scheduledDays > 0) existingLog.scheduledDays = log.scheduledDays;
                } else if (existingTimeStr != timeStr) {
                  deletedLogKeys.add(logTombKey);
                  pendingLogTombstones.add(logTombKey);
                }
              }
            }
          }
        }
      });

      for (final tombKey in pendingLogTombstones) {
        await _db.addTombstone('review_log', tombKey);
      }
    }

    // 4. 复习日志与 FSRS 状态重放自愈：
    // 若双端离线跨日打卡合并导致单词 Reps 与有效日志条数不符，或快照链断裂，按时间轴严格重放 FSRS
    final allLogs = await _db.getAllReviewLogs();
    final logsByWordId = <int, List<ReviewLog>>{};
    for (var l in allLogs) {
      logsByWordId.putIfAbsent(l.wordId, () => []).add(l);
    }

    final wordsToCheck = await _db.getAllWords();
    final db = await _db.database;

    for (var w in wordsToCheck) {
      final wordLogs = logsByWordId[w.id];
      if (wordLogs == null || wordLogs.isEmpty) continue;

      wordLogs.sort((a, b) => a.reviewDate.compareTo(b.reviewDate));

      // 按自然日再次确保唯一
      final dedupedLogs = <ReviewLog>[];
      for (final l in wordLogs) {
        if (dedupedLogs.isNotEmpty && _formatDayKey(dedupedLogs.last.reviewDate) == _formatDayKey(l.reviewDate)) {
          await db.delete('review_logs', where: 'id = ?', whereArgs: [dedupedLogs.last.id]);
          dedupedLogs[dedupedLogs.length - 1] = l;
        } else {
          dedupedLogs.add(l);
        }
      }

      if (w.state == WordLearningState.newWord.value && w.reps == 0) continue;

      bool needsReplay = w.reps != dedupedLogs.length;
      if (!needsReplay && dedupedLogs.length > 1) {
        for (int i = 1; i < dedupedLogs.length; i++) {
          if ((dedupedLogs[i].stabilityBefore - dedupedLogs[i - 1].stability).abs() > 0.01) {
            needsReplay = true;
            break;
          }
        }
      }
      if (!needsReplay && dedupedLogs.isNotEmpty) {
        final lastLog = dedupedLogs.last;
        if ((w.stability - lastLog.stability).abs() > 0.01 ||
            (w.difficulty - lastLog.difficulty).abs() > 0.01) {
          needsReplay = true;
        }
      }

      if (needsReplay) {
        final simWord = Word(
          id: w.id,
          text: w.text,
          state: WordLearningState.newWord.value,
        );
        for (final rl in dedupedLogs) {
          final preS = simWord.stability;
          final preD = simWord.difficulty;
          final int elapsed = simWord.lastReviewDate != null
              ? FsrsEngine.getCalendarElapsedDays(simWord.lastReviewDate!, rl.reviewDate).round()
              : 0;
          final res = FsrsEngine.review(simWord, rl.rating, rl.reviewDate);

          simWord.stability = res.newStability;
          simWord.difficulty = res.newDifficulty;
          simWord.lastReviewDate = rl.reviewDate;
          simWord.nextReviewDate = res.nextReviewDate;
          simWord.reps += 1;
          if (rl.rating == 1) {
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
            whereArgs: [rl.id],
          );
        }

        w.stability = simWord.stability;
        w.difficulty = simWord.difficulty;
        w.reps = simWord.reps;
        w.lapses = simWord.lapses;
        w.lastReviewDate = simWord.lastReviewDate;
        if (w.state == WordLearningState.mastered.value) {
          w.nextReviewDate = null;
        } else {
          w.state = simWord.state;
          w.nextReviewDate = simWord.nextReviewDate;
        }
        await _db.updateWord(w);
      } else if (w.state == WordLearningState.mastered.value && w.nextReviewDate != null) {
        w.nextReviewDate = null;
        await _db.updateWord(w);
      }
    }

    // 5. 清理超过 90 天的过期墓碑
    await _db.cleanupExpiredTombstones(retentionDays: 90);

    return SyncReport(
      syncedLists: syncedLists,
      insertedWords: insertedWords,
      updatedWords: updatedWords,
      syncedLogs: syncedLogs,
    );
  }
}

class SyncReport {
  final int syncedLists;
  final int insertedWords;
  final int updatedWords;
  final int syncedLogs;

  SyncReport({
    required this.syncedLists,
    required this.insertedWords,
    required this.updatedWords,
    required this.syncedLogs,
  });

  String get summary =>
      '同步完成！词单: $syncedLists 个，新词: $insertedWords 个，更新进度: $updatedWords 个，打卡日志: $syncedLogs 条';
}
