import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import '../models/word_models.dart';
import '../services/db_service.dart';
import '../services/event_service.dart';
import '../services/fsrs_engine.dart';
import '../services/tts_service.dart';
import '../widgets/word_progress_bg.dart';

class TodayPage extends StatefulWidget {
  const TodayPage({super.key});

  @override
  State<TodayPage> createState() => _TodayPageState();
}

class _TodayPageState extends State<TodayPage> {
  final DatabaseService _db = DatabaseService.instance;
  StreamSubscription? _eventSub;

  List<Map<String, dynamic>> _todayLogs = [];
  bool _isLoading = true;

  // 0: 全部, 1: 记得 (Rating >= 3), 2: 遗忘 (Rating < 3)
  int _filterRatingSegment = 0;

  int _calculateRetention(Map<String, dynamic> item) {
    final state = item['state'] as int? ?? 0;
    if (state == WordLearningState.mastered.value || state == 4) return 100;
    final reps = item['reps'] as int? ?? 0;
    if (state == WordLearningState.newWord.value || reps == 0) return 0;
    final stability = (item['stability'] as num?)?.toDouble() ?? 0.0;
    if (stability <= 0) return 0;

    final lastDateStr = item['lastReviewDate'] as String?;
    final lastDate = lastDateStr != null ? DateTime.tryParse(lastDateStr)?.toLocal() ?? DateTime.now() : DateTime.now();
    final double r = FsrsEngine.calculateRetrievability(stability, lastDate, DateTime.now());
    return (r * 100).round().clamp(0, 100);
  }

  @override
  void initState() {
    super.initState();
    _loadTodayData();
    _eventSub = DataEventService.instance.onDataChanged.listen((_) {
      if (mounted) _loadTodayData();
    });
  }

  @override
  void dispose() {
    _eventSub?.cancel();
    super.dispose();
  }

  Future<void> _loadTodayData() async {
    setState(() => _isLoading = true);
    final logs = await _db.getTodayReviewItems();
    setState(() {
      _todayLogs = logs;
      _isLoading = false;
    });
  }

  Future<void> _handleTodayAction(Map<String, dynamic> item, String action) async {
    final int logId = item['logId'] as int;
    final int wordId = item['wordId'] as int;
    final String wordText = item['wordText']?.toString() ?? '';
    final int currentRating = item['rating'] as int;

    if (action == 'remember') {
      if (currentRating < 3) {
        await _db.changeTodayReviewRating(logId, 3);
        DataEventService.instance.notify('correction');
        _loadTodayData();
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('已将「$wordText」更正为：记得'),
              duration: const Duration(seconds: 1),
              behavior: SnackBarBehavior.floating,
            ),
          );
        }
      }
    } else if (action == 'forget') {
      if (currentRating >= 3) {
        await _db.changeTodayReviewRating(logId, 1);
        DataEventService.instance.notify('correction');
        _loadTodayData();
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('已将「$wordText」更正为：遗忘'),
              duration: const Duration(seconds: 1),
              behavior: SnackBarBehavior.floating,
            ),
          );
        }
      }
    } else if (action == 'master') {
      await _db.updateWordState(wordId, WordLearningState.mastered);
      DataEventService.instance.notify('words_changed');
      _loadTodayData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已将「$wordText」标记为已掌握'),
            duration: const Duration(seconds: 1),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  Future<void> _confirmRevert(Map<String, dynamic> item) async {
    final int logId = item['logId'] as int;
    final String wordText = item['wordText']?.toString() ?? '';

    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('撤销今日记录'),
        content: Text('确定撤销「$wordText」的今日学习打卡记录吗？\n该记录将被移除，单词的复习状态将回退。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('取消'),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.orange,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('确认撤销'),
          ),
        ],
      ),
    );

    if (confirm == true) {
      await _db.revertTodayReview(logId);
      DataEventService.instance.notify('review_reverted');
      _loadTodayData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已撤销「$wordText」的今日打卡记录'),
            duration: const Duration(seconds: 2),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // 统计计算
    final totalCount = _todayLogs.length;
    final rememberCount = _todayLogs.where((l) => (l['rating'] as int) >= 3).length;
    final forgetCount = totalCount - rememberCount;
    final rememberRate = totalCount == 0 ? 0 : ((rememberCount / totalCount) * 100).round();

    // 筛选
    final filteredLogs = _todayLogs.where((l) {
      final r = l['rating'] as int;
      if (_filterRatingSegment == 1) return r >= 3;
      if (_filterRatingSegment == 2) return r < 3;
      return true;
    }).toList();

    return Scaffold(
      appBar: AppBar(
        title: const Text('今日'),
        actions: [
          // 自动发音切换按钮
          ValueListenableBuilder<bool>(
            valueListenable: TtsService.instance.autoEnabledNotifier,
            builder: (context, isAuto, _) {
              return IconButton(
                icon: Icon(
                  isAuto ? Icons.volume_up : Icons.volume_off_outlined,
                  size: 20,
                ),
                tooltip: isAuto ? '自动发音：已开启' : '自动发音：已关闭',
                onPressed: () {
                  TtsService.instance.toggleAutoEnabled();
                  HapticFeedback.lightImpact();
                  ScaffoldMessenger.of(context).clearSnackBars();
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text(
                        TtsService.instance.isAutoEnabled ? '已开启点击自动发音' : '已关闭点击自动发音',
                      ),
                      duration: const Duration(seconds: 1),
                      behavior: SnackBarBehavior.floating,
                    ),
                  );
                },
              );
            },
          ),
        ],
      ),
      body: _isLoading
          ? const Center(child: CircularProgressIndicator(strokeWidth: 2))
          : Column(
              children: [
                // 顶部今日复习统计面板
                Padding(
                  padding: const EdgeInsets.all(16),
                  child: Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: theme.cardTheme.color,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: theme.colorScheme.outline),
                    ),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceAround,
                      children: [
                        _buildStatItem('今日已学', '$totalCount', theme),
                        _buildStatDivider(theme),
                        _buildStatItem('记得率', '$rememberRate%', theme),
                        _buildStatDivider(theme),
                        _buildStatItem('记得', '$rememberCount', theme),
                        _buildStatDivider(theme),
                        _buildStatItem('遗忘', '$forgetCount', theme),
                      ],
                    ),
                  ),
                ),

                // 分段切换胶囊
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Row(
                    children: [
                      _buildChip('全部 ($totalCount)', 0, theme),
                      const SizedBox(width: 8),
                      _buildChip('记得 ($rememberCount)', 1, theme),
                      const SizedBox(width: 8),
                      _buildChip('遗忘 ($forgetCount)', 2, theme),
                    ],
                  ),
                ),

                const SizedBox(height: 10),

                // 今日打卡记录列表
                Expanded(
                  child: filteredLogs.isEmpty
                      ? Center(
                          child: Text(
                            '今日暂无相关打卡记录',
                            style: TextStyle(color: theme.hintColor, fontSize: 13),
                          ),
                        )
                      : ListView.separated(
                          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                          itemCount: filteredLogs.length,
                          separatorBuilder: (context, index) => const SizedBox(height: 8),
                          itemBuilder: (context, index) {
                            final item = filteredLogs[index];
                            final wordText = item['wordText']?.toString() ?? '';
                            final rating = item['rating'] as int;
                            final isRemembered = rating >= 3;
                            final dateRaw = item['reviewDate']?.toString() ?? '';
                            final dt = DateTime.tryParse(dateRaw) ?? DateTime.now();
                            final timeStr = DateFormat('HH:mm:ss').format(dt);
                            final retention = _calculateRetention(item);

                            return InkWell(
                              borderRadius: BorderRadius.circular(10),
                              onTap: () {
                                if (TtsService.instance.isAutoEnabled) {
                                  TtsService.instance.speak(wordText);
                                }
                              },
                              onLongPress: () {
                                Clipboard.setData(ClipboardData(text: wordText));
                                HapticFeedback.mediumImpact();
                                ScaffoldMessenger.of(context).clearSnackBars();
                                ScaffoldMessenger.of(context).showSnackBar(
                                  SnackBar(
                                    content: Text('已复制「$wordText」到剪贴板'),
                                    duration: const Duration(seconds: 1),
                                    behavior: SnackBarBehavior.floating,
                                  ),
                                );
                              },
                              child: Container(
                                decoration: BoxDecoration(
                                  color: theme.cardTheme.color,
                                  borderRadius: BorderRadius.circular(10),
                                  border: Border.all(color: theme.colorScheme.outline),
                                ),
                                child: Stack(
                                  children: [
                                    // 动态背景记忆程度进度条与 Leading Edge 百分比指示
                                    WordCardProgressBackground(retentionPercent: retention),

                                    // 卡片内容
                                    Padding(
                                      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                                      child: Row(
                                        children: [
                                          Expanded(
                                            child: Column(
                                              crossAxisAlignment: CrossAxisAlignment.start,
                                              children: [
                                                Text(
                                                  wordText,
                                                  style: const TextStyle(
                                                      fontSize: 16, fontWeight: FontWeight.w600),
                                                ),
                                                const SizedBox(height: 2),
                                                Text(
                                                  '打卡时间 $timeStr',
                                                  style:
                                                      TextStyle(fontSize: 11, color: theme.hintColor),
                                                ),
                                              ],
                                            ),
                                          ),
                                          PopupMenuButton<String>(
                                            tooltip: '修改记录',
                                            padding: EdgeInsets.zero,
                                            onSelected: (action) => _handleTodayAction(item, action),
                                            itemBuilder: (context) => [
                                              const PopupMenuItem(
                                                value: 'remember',
                                                height: 38,
                                                child: Text('记得', style: TextStyle(fontSize: 13)),
                                              ),
                                              const PopupMenuItem(
                                                value: 'forget',
                                                height: 38,
                                                child: Text('遗忘', style: TextStyle(fontSize: 13)),
                                              ),
                                              const PopupMenuItem(
                                                value: 'master',
                                                height: 38,
                                                child: Text('掌握', style: TextStyle(fontSize: 13)),
                                              ),
                                            ],
                                            child: Container(
                                              padding: const EdgeInsets.symmetric(
                                                  horizontal: 8, vertical: 4),
                                              decoration: BoxDecoration(
                                                borderRadius: BorderRadius.circular(4),
                                                border: Border.all(color: theme.colorScheme.outline),
                                              ),
                                              child: Row(
                                                mainAxisSize: MainAxisSize.min,
                                                children: [
                                                  Text(
                                                    isRemembered ? '记得' : '遗忘',
                                                    style: TextStyle(
                                                      fontSize: 11,
                                                      fontWeight: FontWeight.bold,
                                                      color: theme.colorScheme.onSurface,
                                                    ),
                                                  ),
                                                  const SizedBox(width: 2),
                                                  Icon(Icons.arrow_drop_down, size: 14, color: theme.hintColor),
                                                ],
                                              ),
                                            ),
                                          ),
                                          const SizedBox(width: 8),
                                          IconButton(
                                            icon: const Icon(Icons.undo_outlined, size: 18),
                                            tooltip: '撤销此单词的今日记录',
                                            padding: EdgeInsets.zero,
                                            constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                                            color: theme.hintColor,
                                            onPressed: () => _confirmRevert(item),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            );
                          },
                        ),
                ),
              ],
            ),
    );
  }

  Widget _buildStatItem(String label, String value, ThemeData theme) {
    return Column(
      children: [
        Text(
          value,
          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
        ),
        const SizedBox(height: 2),
        Text(
          label,
          style: TextStyle(fontSize: 11, color: theme.hintColor),
        ),
      ],
    );
  }

  Widget _buildStatDivider(ThemeData theme) {
    return SizedBox(
      height: 24,
      child: VerticalDivider(color: theme.colorScheme.outline, width: 1),
    );
  }

  Widget _buildChip(String label, int value, ThemeData theme) {
    final isSelected = _filterRatingSegment == value;
    return InkWell(
      borderRadius: BorderRadius.circular(20),
      onTap: () => setState(() => _filterRatingSegment = value),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
        decoration: BoxDecoration(
          color: isSelected ? theme.colorScheme.primary : Colors.transparent,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(
            color: isSelected ? theme.colorScheme.primary : theme.colorScheme.outline,
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 12,
            fontWeight: isSelected ? FontWeight.w600 : FontWeight.normal,
            color: isSelected ? theme.colorScheme.onPrimary : theme.colorScheme.onSurface,
          ),
        ),
      ),
    );
  }
}
