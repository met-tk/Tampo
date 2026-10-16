import 'package:flutter_test/flutter_test.dart';
import 'package:tampo_android/models/word_models.dart';
import 'package:tampo_android/services/sync_service.dart';

void main() {
  group('SyncService formatServerUrl tests', () {
    final sync = SyncService.instance;

    test('formatServerUrl with empty string returns empty', () {
      expect(sync.formatServerUrl(''), '');
      expect(sync.formatServerUrl('   '), '');
    });

    test('formatServerUrl adds http prefix and default port 52080', () {
      expect(sync.formatServerUrl('192.168.1.100'), 'http://192.168.1.100:52080');
      expect(sync.formatServerUrl('  192.168.1.50  '), 'http://192.168.1.50:52080');
    });

    test('formatServerUrl preserves existing port and protocol', () {
      expect(sync.formatServerUrl('http://192.168.1.100:8080'), 'http://192.168.1.100:8080');
      expect(sync.formatServerUrl('http://192.168.1.100:8080/'), 'http://192.168.1.100:8080');
      expect(sync.formatServerUrl('https://myserver.lan:52080'), 'https://myserver.lan:52080');
    });

    test('formatServerUrl corrects UDP discovery port 52081 to HTTP port 52080', () {
      expect(sync.formatServerUrl('192.168.1.3:52081'), 'http://192.168.1.3:52080');
      expect(sync.formatServerUrl('http://192.168.1.3:52081/'), 'http://192.168.1.3:52080');
    });
  });

  group('SyncReport tests', () {
    test('SyncReport formats summary string accurately', () {
      final report = SyncReport(
        syncedLists: 3,
        insertedWords: 15,
        updatedWords: 7,
        syncedLogs: 22,
      );
      expect(report.syncedLists, 3);
      expect(report.insertedWords, 15);
      expect(report.updatedWords, 7);
      expect(report.syncedLogs, 22);
      expect(report.summary, contains('词单: 3 个'));
      expect(report.summary, contains('新词: 15 个'));
      expect(report.summary, contains('更新进度: 7 个'));
      expect(report.summary, contains('打卡日志: 22 条'));
    });
  });

  group('SyncLogItem tests', () {
    test('SyncLogItem records timestamp and message', () {
      final item = SyncLogItem('测试同步日志', isError: false);
      expect(item.message, '测试同步日志');
      expect(item.isError, false);
      expect(item.timeFormatted, matches(r'^\d{2}:\d{2}:\d{2}$'));

      final errItem = SyncLogItem('错误日志', isError: true);
      expect(errItem.isError, true);
    });
  });

  group('Word timestamp fallback tests', () {
    test('Word.fromMap with missing stateUpdatedAt falls back to lastReviewDate or epochZero, never DateTime.now()', () {
      final mapWithNoTimestamps = {
        'id': 1,
        'text': 'テスト',
        'state': 0,
        'reps': 0,
      };
      final word = Word.fromMap(mapWithNoTimestamps);
      expect(word.stateUpdatedAt.year, lessThan(2000));
      expect(word.metaUpdatedAt.year, lessThan(2000));

      final mapWithReviewDate = {
        'id': 2,
        'text': 'テスト2',
        'lastReviewDate': '2026-05-01T12:00:00.000',
        'createdAt': '2026-04-01T10:00:00.000',
        'state': 1,
        'reps': 1,
      };
      final word2 = Word.fromMap(mapWithReviewDate);
      expect(word2.stateUpdatedAt.year, 2026);
      expect(word2.stateUpdatedAt.month, 5);
      expect(word2.metaUpdatedAt.month, 4);
    });
  });

  group('SyncService realtime state tests', () {
    final sync = SyncService.instance;

    test('isSseConnectedNotifier defaults to false', () {
      expect(sync.isSseConnectedNotifier.value, isFalse);
    });

    test('addLog inserts log and trims beyond 60 items', () {
      for (int i = 0; i < 70; i++) {
        sync.addLog('Log $i');
      }
      expect(sync.syncLogsNotifier.value.length, lessThanOrEqualTo(60));
      expect(sync.syncLogsNotifier.value.first.message, 'Log 69');
    });
  });
}
