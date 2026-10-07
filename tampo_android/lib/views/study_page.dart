import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../models/word_models.dart';
import '../services/db_service.dart';
import '../services/event_service.dart';

import '../services/tts_service.dart';

class StudyPage extends StatefulWidget {
  const StudyPage({super.key});

  @override
  State<StudyPage> createState() => StudyPageState();
}

class StudyPageState extends State<StudyPage> {
  final DatabaseService _db = DatabaseService.instance;
  final ScrollController _scrollController = ScrollController();
  StreamSubscription? _eventSub;

  List<Word> _dueWords = [];
  List<WordList> _wordLists = [];
  bool _isLoading = true;
  bool _isSubmitting = false;

  int? _selectedListId;

  // 本地暂存的打卡状态：wordId -> rating (3:记得, 1:忘记, 4:掌握)
  // 未曾点击过的单词不在该 Map 中
  final Map<int, int> _pendingRatings = {};

  @override
  void initState() {
    super.initState();
    _loadData();
    _eventSub = DataEventService.instance.onDataChanged.listen((_) {
      if (mounted) {
        _loadData();
      }
    });
  }

  @override
  void dispose() {
    _eventSub?.cancel();
    _scrollController.dispose();
    super.dispose();
  }

  Future<void> _loadData() async {
    setState(() => _isLoading = true);
    final lists = await _db.getAllWordLists();
    final queue = await _db.getStudyQueue(listId: _selectedListId);

    if (mounted) {
      setState(() {
        _wordLists = lists;
        _dueWords = queue;
        _isLoading = false;
        // 清理已不在当前待学习队列中的暂存
        final currentIds = queue.map((w) => w.id).toSet();
        _pendingRatings.removeWhere((id, _) => !currentIds.contains(id));
      });
    }
  }

  /// 公开的刷新方法供父组件切换 Tab 时调用
  void refresh() {
    _loadData();
  }

  /// 首次点击未标记的单词卡任意区域：标记为【记得】
  /// 优化：点击列表中第1个单词时（index == 0）不触发下滑，点击其他（index > 0）正常平滑下滑
  void _onInitialCardTap(Word word, int index) {
    setState(() {
      _pendingRatings[word.id] = 3; // 默认为记得
    });

    HapticFeedback.lightImpact();
    // 首次点击标记时触发一次发音（受自动发音开关控制）
    if (TtsService.instance.isAutoEnabled) {
      TtsService.instance.speak(word.text);
    }

    // 仅当点击非首个单词时，自动平滑往下滚动一个卡片高度
    if (index > 0 && _scrollController.hasClients) {
      const cardHeight = 98.0;
      final maxScroll = _scrollController.position.maxScrollExtent;
      final target = (_scrollController.offset + cardHeight).clamp(0.0, maxScroll);
      _scrollController.animateTo(
        target,
        duration: const Duration(milliseconds: 260),
        curve: Curves.easeOutCubic,
      );
    }
  }

  /// 批量结算提交
  Future<void> _handleSubmit() async {
    if (_pendingRatings.isEmpty) return;

    setState(() => _isSubmitting = true);

    try {
      final batchNow = DateTime.now();
      final entries = _pendingRatings.entries.toList();
      for (final entry in entries) {
        final wordId = entry.key;
        final rating = entry.value;

        final matches = _dueWords.where((w) => w.id == wordId);
        if (matches.isEmpty) continue;
        final word = matches.first;

        if (rating == 4) {
          // 标记为已掌握
          await _db.updateWordState(word.id, WordLearningState.mastered);
        } else {
          // 记得(3) 或 忘记(1) 进行 FSRS 复习计算（统一使用批次起始时间防止跨午夜分裂）
          await _db.reviewWordFSRS(word, rating, now: batchNow);
        }
      }

      final count = _pendingRatings.length;
      _pendingRatings.clear();
      DataEventService.instance.notify('review_submitted');
      await _loadData();

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('成功结算 $count 个单词的学习记录！'),
            duration: const Duration(seconds: 2),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('提交失败: $e'), backgroundColor: Colors.red),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _isSubmitting = false);
      }
    }
  }

  /// 长按仅复制单词
  void _copyWord(Word word) {
    Clipboard.setData(ClipboardData(text: word.text));
    HapticFeedback.mediumImpact();
    ScaffoldMessenger.of(context).clearSnackBars();
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('已复制「${word.text}」到剪贴板'),
        duration: const Duration(seconds: 1),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final totalDue = _dueWords.length;
    final pendingCount = _pendingRatings.length;

    return Scaffold(
      appBar: AppBar(
        title: const Text('学习'),
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
          // 当有待提交项时显示【取消】与【提交 (X)】按钮
          if (pendingCount > 0) ...[
            TextButton(
              onPressed: _isSubmitting
                  ? null
                  : () {
                      setState(() {
                        _pendingRatings.clear();
                      });
                      HapticFeedback.selectionClick();
                    },
              child: const Text('取消', style: TextStyle(color: Colors.grey)),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8, horizontal: 8),
              child: ElevatedButton.icon(
                icon: _isSubmitting
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                      )
                    : const Icon(Icons.check, size: 16),
                label: Text('提交 ($pendingCount)'),
                style: ElevatedButton.styleFrom(
                  backgroundColor: theme.colorScheme.primary,
                  foregroundColor: theme.colorScheme.onPrimary,
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                ),
                onPressed: _isSubmitting ? null : _handleSubmit,
              ),
            ),
          ],
        ],
      ),
      body: Column(
        children: [
          // 顶部状态指标卡与词单选择
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Container(
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                color: theme.cardTheme.color,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: theme.colorScheme.outline),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              crossAxisAlignment: CrossAxisAlignment.baseline,
                              textBaseline: TextBaseline.alphabetic,
                              children: [
                                Text(
                                  '$totalDue',
                                  style: const TextStyle(fontSize: 24, fontWeight: FontWeight.bold),
                                ),
                                const SizedBox(width: 6),
                                Text(
                                  '词待复习',
                                  style: TextStyle(fontSize: 12, color: theme.hintColor),
                                ),
                              ],
                            ),
                            const SizedBox(height: 2),
                            Text(
                              pendingCount > 0 ? '已暂存 $pendingCount 词，右上角提交' : '点击卡片发音，点击标记复习',
                              style: TextStyle(
                                fontSize: 11,
                                color: pendingCount > 0 ? theme.colorScheme.primary : theme.hintColor,
                                fontWeight: pendingCount > 0 ? FontWeight.w600 : FontWeight.normal,
                              ),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(width: 8),
                      // 词单切换下拉菜单
                      ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 130),
                        child: DropdownButtonHideUnderline(
                          child: DropdownButton<int?>(
                            isExpanded: true,
                            isDense: true,
                            value: _selectedListId,
                            hint: const Text('全部到期词汇', style: TextStyle(fontSize: 12), overflow: TextOverflow.ellipsis),
                            style: TextStyle(fontSize: 12, color: theme.colorScheme.onSurface),
                            items: [
                              const DropdownMenuItem<int?>(
                                value: null,
                                child: Text('全部到期词汇', overflow: TextOverflow.ellipsis),
                              ),
                              ..._wordLists.map((l) => DropdownMenuItem<int?>(
                                    value: l.id,
                                    child: Text('${l.name} (${l.wordCount})', overflow: TextOverflow.ellipsis),
                                  )),
                            ],
                            onChanged: (val) {
                              setState(() {
                                _selectedListId = val;
                                _pendingRatings.clear();
                              });
                              _loadData();
                            },
                          ),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),

          const SizedBox(height: 2),

          // 核心列表卡片流
          Expanded(
            child: _isLoading
                ? const Center(child: CircularProgressIndicator(strokeWidth: 2))
                : _dueWords.isEmpty
                    ? Center(
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(Icons.check_circle_outline, size: 52, color: theme.hintColor),
                            const SizedBox(height: 12),
                            const Text(
                              '太棒了！当前无待复习单词',
                              style: TextStyle(fontSize: 14, fontWeight: FontWeight.bold),
                            ),
                            const SizedBox(height: 4),
                            Text(
                              '可以导入新生词或稍后再来巩固',
                              style: TextStyle(color: theme.hintColor, fontSize: 12),
                            ),
                          ],
                        ),
                      )
                    : ListView.separated(
                        controller: _scrollController,
                        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                        itemCount: _dueWords.length,
                        separatorBuilder: (context, index) => const SizedBox(height: 8),
                        itemBuilder: (context, index) {
                          final word = _dueWords[index];
                          final currentRating = _pendingRatings[word.id];
                          return _buildStudyWordCard(word, currentRating, theme, index);
                        },
                      ),
          ),
        ],
      ),
    );
  }

  Widget _buildStudyWordCard(Word word, int? pendingRating, ThemeData theme, int index) {
    final bool isMarked = pendingRating != null;

    return InkWell(
      borderRadius: BorderRadius.circular(10),
      // 未标记时点击：标记为记得并触发发音；已标记时点击：触发发音
      onTap: () {
        if (!isMarked) {
          _onInitialCardTap(word, index);
        } else {
          if (TtsService.instance.isAutoEnabled) {
            TtsService.instance.speak(word.text);
          }
        }
      },
      onLongPress: () => _copyWord(word),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 200),
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        decoration: BoxDecoration(
          color: theme.cardTheme.color,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(
            color: isMarked ? theme.colorScheme.primary.withValues(alpha: 0.8) : theme.colorScheme.outline,
            width: isMarked ? 1.5 : 1.0,
          ),
        ),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    word.text,
                    style: const TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.bold,
                      letterSpacing: -0.3,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Wrap(
                    crossAxisAlignment: WrapCrossAlignment.center,
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(4),
                          border: Border.all(color: theme.colorScheme.outline),
                        ),
                        child: Text(
                          word.learningState.displayName,
                          style: TextStyle(fontSize: 10, color: theme.colorScheme.onSurface),
                        ),
                      ),
                      Text(
                        '已学 ${word.reps} 次',
                        style: TextStyle(fontSize: 11, color: theme.hintColor),
                      ),
                      if (word.wordListName != null && word.wordListName!.isNotEmpty)
                        Text(
                          word.wordListName!,
                          style: TextStyle(fontSize: 11, color: theme.hintColor),
                        ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),

            // 右侧标记组件：
            // 未曾点击时：完全不显示任何勾勾或评级状态
            // 标记之后：显示绿色的勾勾（或其他选定状态），并支持在其上方弹出 3 选菜单（掌握、忘记、记得）
            if (isMarked)
              _buildRatingActionMenu(word, pendingRating, theme)
            else
              const SizedBox(width: 0, height: 0),
          ],
        ),
      ),
    );
  }

  /// 绿色勾勾及其上方弹出的【掌握、忘记、记得】小菜单
  Widget _buildRatingActionMenu(Word word, int currentRating, ThemeData theme) {
    return Theme(
      data: theme.copyWith(
        popupMenuTheme: PopupMenuThemeData(
          color: theme.cardTheme.color,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8),
            side: BorderSide(color: theme.colorScheme.outline),
          ),
        ),
      ),
      child: PopupMenuButton<int>(
        tooltip: '更改状态',
        // 在绿色勾勾的上方优雅弹出
        offset: const Offset(0, -145),
        onSelected: (int selectedRating) {
          setState(() {
            _pendingRatings[word.id] = selectedRating;
          });
          HapticFeedback.selectionClick();
        },
        itemBuilder: (BuildContext context) => <PopupMenuEntry<int>>[
          PopupMenuItem<int>(
            value: 4,
            height: 40,
            child: Row(
              children: [
                Icon(
                  Icons.verified_outlined,
                  size: 18,
                  color: currentRating == 4 ? theme.colorScheme.primary : theme.colorScheme.onSurface,
                ),
                const SizedBox(width: 10),
                Text(
                  '掌握',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: currentRating == 4 ? FontWeight.bold : FontWeight.normal,
                    color: currentRating == 4 ? theme.colorScheme.primary : theme.colorScheme.onSurface,
                  ),
                ),
              ],
            ),
          ),
          PopupMenuItem<int>(
            value: 1,
            height: 40,
            child: Row(
              children: [
                Icon(
                  Icons.close_outlined,
                  size: 18,
                  color: currentRating == 1 ? Colors.orange : theme.colorScheme.onSurface,
                ),
                const SizedBox(width: 10),
                Text(
                  '忘记',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: currentRating == 1 ? FontWeight.bold : FontWeight.normal,
                    color: currentRating == 1 ? Colors.orange : theme.colorScheme.onSurface,
                  ),
                ),
              ],
            ),
          ),
          PopupMenuItem<int>(
            value: 3,
            height: 40,
            child: Row(
              children: [
                Icon(
                  Icons.check_circle_outline,
                  size: 18,
                  color: currentRating == 3 ? Colors.green : theme.colorScheme.onSurface,
                ),
                const SizedBox(width: 10),
                Text(
                  '记得',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: currentRating == 3 ? FontWeight.bold : FontWeight.normal,
                    color: currentRating == 3 ? Colors.green : theme.colorScheme.onSurface,
                  ),
                ),
              ],
            ),
          ),
        ],
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
          decoration: BoxDecoration(
            color: _getBadgeBackgroundColor(currentRating, theme),
            borderRadius: BorderRadius.circular(6),
            border: Border.all(color: _getBadgeBorderColor(currentRating, theme)),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              _getBadgeIcon(currentRating),
              const SizedBox(width: 4),
              Text(
                _getBadgeText(currentRating),
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.bold,
                  color: _getBadgeTextColor(currentRating, theme),
                ),
              ),
              const SizedBox(width: 2),
              Icon(
                Icons.arrow_drop_up,
                size: 14,
                color: _getBadgeTextColor(currentRating, theme),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _getBadgeIcon(int rating) {
    if (rating == 4) {
      return const Icon(Icons.verified, size: 16, color: Colors.blue);
    } else if (rating == 1) {
      return const Icon(Icons.cancel_outlined, size: 16, color: Colors.orange);
    }
    return const Icon(Icons.check_circle, size: 16, color: Colors.green);
  }

  String _getBadgeText(int rating) {
    if (rating == 4) return '掌握';
    if (rating == 1) return '忘记';
    return '记得';
  }

  Color _getBadgeBackgroundColor(int rating, ThemeData theme) {
    if (rating == 4) return Colors.blue.withValues(alpha: 0.1);
    if (rating == 1) return Colors.orange.withValues(alpha: 0.1);
    return Colors.green.withValues(alpha: 0.12);
  }

  Color _getBadgeBorderColor(int rating, ThemeData theme) {
    if (rating == 4) return Colors.blue.withValues(alpha: 0.4);
    if (rating == 1) return Colors.orange.withValues(alpha: 0.4);
    return Colors.green.withValues(alpha: 0.4);
  }

  Color _getBadgeTextColor(int rating, ThemeData theme) {
    if (rating == 4) return Colors.blue;
    if (rating == 1) return Colors.orange;
    return Colors.green;
  }
}
