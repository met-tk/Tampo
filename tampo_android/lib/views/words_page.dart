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

class WordsPage extends StatefulWidget {
  const WordsPage({super.key});

  @override
  State<WordsPage> createState() => _WordsPageState();
}

class _WordsPageState extends State<WordsPage> {
  final DatabaseService _db = DatabaseService.instance;
  final TextEditingController _searchCtrl = TextEditingController();
  StreamSubscription? _eventSub;

  List<Word> _words = [];
  List<WordList> _wordLists = [];
  bool _isLoading = true;

  // 分类筛选：'all', 'newWord', 'learning', 'review', 'mastered', 'unarchived', 'archived'
  String _selectedCategory = 'all';
  int? _selectedListId;
  Map<String, int> _categoryCounts = {};

  // 多选管理状态
  bool _isSelectionMode = false;
  final Set<int> _selectedWordIds = {};

  @override
  void initState() {
    super.initState();
    _loadData();
    _eventSub = DataEventService.instance.onDataChanged.listen((_) {
      if (mounted) _loadData();
    });
  }

  @override
  void dispose() {
    _eventSub?.cancel();
    _searchCtrl.dispose();
    super.dispose();
  }

  Future<void> _loadData() async {
    setState(() => _isLoading = true);
    try {
      final lists = await _db.getAllWordLists();

      int? stateFilter;
      bool? archivedFilter;

      if (_selectedCategory == 'newWord') stateFilter = 0;
      if (_selectedCategory == 'learning') stateFilter = 1;
      if (_selectedCategory == 'review') stateFilter = 2;
      if (_selectedCategory == 'mastered') stateFilter = WordLearningState.mastered.value;
      if (_selectedCategory == 'unarchived') archivedFilter = false;
      if (_selectedCategory == 'archived') archivedFilter = true;

      final words = await _db.searchWords(
        _searchCtrl.text,
        listId: _selectedListId,
        stateFilter: stateFilter,
        archivedFilter: archivedFilter,
      );

      final counts = await _db.getCategoryCounts(
        listId: _selectedListId,
        query: _searchCtrl.text,
      );

      if (mounted) {
        setState(() {
          _wordLists = lists;
          _words = words;
          _categoryCounts = counts;
          _isLoading = false;
          // 清除不存在的已选 ID
          final currentIds = words.map((w) => w.id).toSet();
          _selectedWordIds.removeWhere((id) => !currentIds.contains(id));
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() => _isLoading = false);
      }
    }
  }

  int _calculateRetentionPercent(Word word) {
    if (word.state == WordLearningState.mastered.value || word.state == 4) return 100;
    if (word.state == WordLearningState.newWord.value || word.reps == 0) return 0;
    if (word.stability <= 0) return 0;

    final now = DateTime.now();
    final lastDate = word.lastReviewDate ?? word.createdAt;
    final double r = FsrsEngine.calculateRetrievability(word.stability, lastDate, now);
    return (r * 100).round().clamp(0, 100);
  }

  void _toggleSelectionMode() {
    setState(() {
      _isSelectionMode = !_isSelectionMode;
      _selectedWordIds.clear();
    });
  }

  void _toggleSelectAll() {
    setState(() {
      if (_selectedWordIds.length == _words.length) {
        _selectedWordIds.clear();
      } else {
        _selectedWordIds.addAll(_words.map((w) => w.id));
      }
    });
  }

  // --- 批量标记为已掌握 ---
  Future<void> _handleBatchMaster() async {
    if (_selectedWordIds.isEmpty) return;
    final count = _selectedWordIds.length;
    await _db.updateWordsStateBatch(_selectedWordIds.toList(), WordLearningState.mastered);
    DataEventService.instance.notify('words_changed');
    setState(() {
      _isSelectionMode = false;
      _selectedWordIds.clear();
    });
    _loadData();
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('已批量将 $count 个单词标记为已掌握'),
          behavior: SnackBarBehavior.floating,
        ),
      );
    }
  }

  // --- 批量重学 ---
  Future<void> _handleBatchRelearn() async {
    if (_selectedWordIds.isEmpty) return;
    final count = _selectedWordIds.length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('批量重学确认'),
        content: Text('确定将已勾选的 $count 个单词重置为未学习吗？\n所有记忆与复习进度将被重置。'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
          ElevatedButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('确认重学'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      await _db.relearnWordsBatch(_selectedWordIds.toList());
      DataEventService.instance.notify('words_changed');
      setState(() {
        _isSelectionMode = false;
        _selectedWordIds.clear();
      });
      _loadData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已批量将 $count 个单词重置为未学习 (重学)'),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  Future<void> _handleWordMenuAction(Word word, String action) async {
    if (action == 'mastered') {
      await _db.updateWordState(word.id, WordLearningState.mastered);
      DataEventService.instance.notify('words_changed');
      _loadData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已将「${word.text}」标记为已掌握'),
            duration: const Duration(seconds: 1),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    } else if (action == 'relearn') {
      await _db.relearnWordsBatch([word.id]);
      DataEventService.instance.notify('words_changed');
      _loadData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已将「${word.text}」重置为未学习 (重学)'),
            duration: const Duration(seconds: 1),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    } else if (action == 'delete') {
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (dCtx) => AlertDialog(
          title: const Text('确认删除'),
          content: Text('确定从词库中彻底删除「${word.text}」及其复习记录吗？'),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dCtx, false),
              child: const Text('取消'),
            ),
            ElevatedButton(
              style: ElevatedButton.styleFrom(
                backgroundColor: Colors.red,
                foregroundColor: Colors.white,
              ),
              onPressed: () => Navigator.pop(dCtx, true),
              child: const Text('删除'),
            ),
          ],
        ),
      );
      if (confirmed == true) {
        await _db.deleteWord(word.id);
        DataEventService.instance.notify('words_changed');
        _loadData();
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('已从词库删除「${word.text}」'),
              duration: const Duration(seconds: 1),
              behavior: SnackBarBehavior.floating,
            ),
          );
        }
      }
    }
  }

  // --- 批量加入词单或新建词单 ---
  Future<void> _handleBatchAddToList() async {
    if (_selectedWordIds.isEmpty) return;

    final targetList = await showModalBottomSheet<Map<String, dynamic>>(
      context: context,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (ctx) {
        return SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 16),
                child: Text('选择或新建目标词单', style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
              ),
              const Divider(height: 1),
              ListTile(
                leading: const Icon(Icons.add_circle_outline, color: Colors.blue),
                title: const Text('新建词单并添加', style: TextStyle(color: Colors.blue, fontWeight: FontWeight.w600)),
                onTap: () async {
                  final nameCtrl = TextEditingController();
                  final newName = await showDialog<String>(
                    context: ctx,
                    builder: (dCtx) => AlertDialog(
                      title: const Text('新建词单'),
                      content: TextField(
                        controller: nameCtrl,
                        autofocus: true,
                        decoration: const InputDecoration(hintText: '输入词单名称 (如: N1考前冲刺)'),
                      ),
                      actions: [
                        TextButton(onPressed: () => Navigator.pop(dCtx), child: const Text('取消')),
                        ElevatedButton(
                          onPressed: () => Navigator.pop(dCtx, nameCtrl.text.trim()),
                          child: const Text('创建'),
                        ),
                      ],
                    ),
                  );
                  if (newName != null && newName.isNotEmpty) {
                    final created = await _db.createWordList(newName);
                    if (ctx.mounted) {
                      Navigator.pop(ctx, {'id': created.id, 'name': created.name});
                    }
                  }
                },
              ),
              if (_wordLists.isNotEmpty) ...[
                const Divider(height: 1),
                Flexible(
                  child: ListView.builder(
                    shrinkWrap: true,
                    itemCount: _wordLists.length,
                    itemBuilder: (context, idx) {
                      final l = _wordLists[idx];
                      return ListTile(
                        leading: const Icon(Icons.folder_outlined),
                        title: Text(l.name),
                        trailing: Text('${l.wordCount} 词', style: const TextStyle(fontSize: 12, color: Colors.grey)),
                        onTap: () => Navigator.pop(ctx, {'id': l.id, 'name': l.name}),
                      );
                    },
                  ),
                ),
              ],
            ],
          ),
        );
      },
    );

    if (targetList != null) {
      final listId = targetList['id'] as int;
      final listName = targetList['name'] as String;
      final addedCount = _selectedWordIds.length;
      await _db.addWordsToList(_selectedWordIds.toList(), listId, listName);
      DataEventService.instance.notify('words_changed');
      setState(() {
        _isSelectionMode = false;
        _selectedWordIds.clear();
      });
      _loadData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已成功将 $addedCount 个单词加入词单「$listName」'),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  // --- 批量移出词单 ---
  Future<void> _handleBatchRemoveFromList() async {
    if (_selectedWordIds.isEmpty) return;
    await _db.removeWordsFromList(_selectedWordIds.toList());
    DataEventService.instance.notify('words_changed');
    setState(() {
      _isSelectionMode = false;
      _selectedWordIds.clear();
    });
    _loadData();
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('已从词单中移出选中的单词（保留在全部词库中）'),
          behavior: SnackBarBehavior.floating,
        ),
      );
    }
  }

  // --- 批量彻底删除 ---
  Future<void> _handleBatchDelete() async {
    if (_selectedWordIds.isEmpty) return;

    final count = _selectedWordIds.length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('批量彻底删除'),
        content: Text('确定从词库中彻底删除已勾选的 $count 个单词及其所有复习记录吗？\n此操作将同步至电脑端并无法撤销。'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
          ElevatedButton(
            style: ElevatedButton.styleFrom(backgroundColor: Colors.red, foregroundColor: Colors.white),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('彻底删除'),
          ),
        ],
      ),
    );

    if (confirmed == true) {
      await _db.deleteWordsBatch(_selectedWordIds.toList());
      DataEventService.instance.notify('words_changed');
      setState(() {
        _isSelectionMode = false;
        _selectedWordIds.clear();
      });
      _loadData();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已成功删除 $count 个单词'),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        leading: _isSelectionMode
            ? IconButton(
                icon: const Icon(Icons.close),
                tooltip: '退出多选',
                onPressed: _toggleSelectionMode,
              )
            : null,
        title: Text(_isSelectionMode ? '已选择 ${_selectedWordIds.length} 项' : '词库'),
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
                        TtsService.instance.isAutoEnabled ? '已开启自动发音' : '已关闭自动发音',
                      ),
                      duration: const Duration(seconds: 1),
                      behavior: SnackBarBehavior.floating,
                    ),
                  );
                },
              );
            },
          ),
          if (_isSelectionMode) ...[
            TextButton(
              onPressed: _toggleSelectAll,
              child: Text(
                _selectedWordIds.length == _words.length && _words.isNotEmpty ? '取消全选' : '全选',
                style: TextStyle(
                  color: theme.colorScheme.primary,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ] else ...[
            IconButton(
              icon: const Icon(Icons.checklist_outlined),
              tooltip: '多选模式',
              onPressed: _toggleSelectionMode,
            ),
          ],
        ],
      ),
      body: Column(
        children: [
          // 顶部：缩短后的搜索框 + 紧凑词单筛选框
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
            child: Row(
              children: [
                // 搜索框（适当缩短宽度）
                Expanded(
                  flex: 6,
                  child: SizedBox(
                    height: 40,
                    child: TextField(
                      controller: _searchCtrl,
                      onChanged: (_) => _loadData(),
                      style: const TextStyle(fontSize: 13),
                      decoration: InputDecoration(
                        hintText: '搜索词汇...',
                        prefixIcon: const Icon(Icons.search, size: 18),
                        suffixIcon: _searchCtrl.text.isNotEmpty
                            ? IconButton(
                                icon: const Icon(Icons.clear, size: 16),
                                onPressed: () {
                                  _searchCtrl.clear();
                                  _loadData();
                                },
                              )
                            : null,
                        contentPadding: const EdgeInsets.symmetric(vertical: 0, horizontal: 8),
                        filled: true,
                        fillColor: theme.cardTheme.color,
                        border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(8),
                          borderSide: BorderSide(color: theme.colorScheme.outline),
                        ),
                        enabledBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(8),
                          borderSide: BorderSide(color: theme.colorScheme.outline),
                        ),
                      ),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                // 词单筛选下拉框（放置在右上侧）
                Expanded(
                  flex: 4,
                  child: Container(
                    height: 40,
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    decoration: BoxDecoration(
                      color: theme.cardTheme.color,
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: theme.colorScheme.outline),
                    ),
                    child: DropdownButtonHideUnderline(
                      child: DropdownButton<int?>(
                        value: _selectedListId,
                        isExpanded: true,
                        icon: const Icon(Icons.arrow_drop_down, size: 18),
                        style: TextStyle(fontSize: 12, color: theme.colorScheme.onSurface),
                        items: [
                          const DropdownMenuItem<int?>(
                            value: null,
                            child: Text('全部词单', style: TextStyle(fontSize: 12), overflow: TextOverflow.ellipsis),
                          ),
                          ..._wordLists.map((l) => DropdownMenuItem<int?>(
                                value: l.id,
                                child: Text('${l.name} (${l.wordCount})', style: const TextStyle(fontSize: 12), overflow: TextOverflow.ellipsis),
                              )),
                        ],
                        onChanged: (val) {
                          setState(() => _selectedListId = val);
                          _loadData();
                        },
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),

          // 7 态胶囊筛选栏（横向滑动）：全部、未学习、学习中、复习中、已掌握、未归档、已归档
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            child: Row(
              children: [
                _buildCategoryChip('全部', 'all'),
                const SizedBox(width: 8),
                _buildCategoryChip('未学习', 'newWord'),
                const SizedBox(width: 8),
                _buildCategoryChip('学习', 'learning'),
                const SizedBox(width: 8),
                _buildCategoryChip('复习', 'review'),
                const SizedBox(width: 8),
                _buildCategoryChip('掌握', 'mastered'),
                const SizedBox(width: 8),
                _buildCategoryChip('未归档', 'unarchived'),
                const SizedBox(width: 8),
                _buildCategoryChip('已归档', 'archived'),
              ],
            ),
          ),

          const SizedBox(height: 4),

          // 单词列表
          Expanded(
            child: _isLoading
                ? const Center(child: CircularProgressIndicator(strokeWidth: 2))
                : _words.isEmpty
                    ? Center(
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(Icons.inventory_2_outlined, size: 48, color: theme.hintColor),
                            const SizedBox(height: 12),
                            Text(
                              '暂无符合条件的单词',
                              style: TextStyle(color: theme.hintColor, fontSize: 13),
                            ),
                          ],
                        ),
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                        itemCount: _words.length,
                        separatorBuilder: (context, index) => const SizedBox(height: 8),
                        itemBuilder: (context, index) {
                          final word = _words[index];
                          return _buildWordCard(word);
                        },
                      ),
          ),

          // 多选模式底部操作栏
          if (_isSelectionMode) _buildSelectionBottomBar(theme),
        ],
      ),
    );
  }

  Widget _buildCategoryChip(String label, String categoryKey) {
    final isSelected = _selectedCategory == categoryKey;
    final theme = Theme.of(context);
    final count = _categoryCounts[categoryKey] ?? 0;
    final displayLabel = '$label ($count)';

    return InkWell(
      borderRadius: BorderRadius.circular(20),
      onTap: () {
        setState(() => _selectedCategory = categoryKey);
        _loadData();
      },
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 5),
        decoration: BoxDecoration(
          color: isSelected ? theme.colorScheme.primary : Colors.transparent,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(
            color: isSelected ? theme.colorScheme.primary : theme.colorScheme.outline,
          ),
        ),
        child: Text(
          displayLabel,
          style: TextStyle(
            fontSize: 12,
            fontWeight: isSelected ? FontWeight.w600 : FontWeight.normal,
            color: isSelected ? theme.colorScheme.onPrimary : theme.colorScheme.onSurface,
          ),
        ),
      ),
    );
  }

  Widget _buildWordCard(Word word) {
    final theme = Theme.of(context);
    final dateStr = DateFormat('yyyy-MM-dd').format(word.createdAt);
    final isChecked = _selectedWordIds.contains(word.id);
    final retention = _calculateRetentionPercent(word);

    return InkWell(
      borderRadius: BorderRadius.circular(10),
      onTap: () {
        if (_isSelectionMode) {
          setState(() {
            if (isChecked) {
              _selectedWordIds.remove(word.id);
            } else {
              _selectedWordIds.add(word.id);
            }
          });
        } else {
          // 点击卡片空白区域触发一次读音（受自动发音开关控制）
          if (TtsService.instance.isAutoEnabled) {
            TtsService.instance.speak(word.text);
          }
        }
      },
      onLongPress: () {
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
      },
      child: Container(
        decoration: BoxDecoration(
          color: isChecked ? theme.colorScheme.primary.withValues(alpha: 0.08) : theme.cardTheme.color,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(
            color: isChecked ? theme.colorScheme.primary : theme.colorScheme.outline,
          ),
        ),
        child: Stack(
          children: [
            // 动态背景记忆程度进度条与 Leading Edge 百分比指示
            WordCardProgressBackground(retentionPercent: retention),

            // 卡片内容
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
              child: Row(
                children: [
                  if (_isSelectionMode) ...[
                    Checkbox(
                      value: isChecked,
                      activeColor: theme.colorScheme.primary,
                      visualDensity: VisualDensity.compact,
                      onChanged: (val) {
                        setState(() {
                          if (val == true) {
                            _selectedWordIds.add(word.id);
                          } else {
                            _selectedWordIds.remove(word.id);
                          }
                        });
                      },
                    ),
                    const SizedBox(width: 4),
                  ],
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          word.text,
                          style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600),
                        ),
                        const SizedBox(height: 4),
                        Row(
                          children: [
                            Text(
                              '导入于 $dateStr',
                              style: TextStyle(fontSize: 11, color: theme.hintColor),
                            ),
                            const SizedBox(width: 8),
                            Text(
                              '复习 ${word.reps} 次',
                              style: TextStyle(fontSize: 11, color: theme.hintColor),
                            ),
                            if (word.wordListName != null && word.wordListName!.isNotEmpty) ...[
                              const SizedBox(width: 8),
                              Flexible(
                                child: Container(
                                  padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                                  decoration: BoxDecoration(
                                    color: theme.colorScheme.outline.withValues(alpha: 0.2),
                                    borderRadius: BorderRadius.circular(4),
                                  ),
                                  child: Text(
                                    word.wordListName!,
                                    style: TextStyle(fontSize: 10, color: theme.hintColor),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  // 右侧状态标签：点击在标签旁边弹出【掌握】【重学】【删除】选择菜单
                  Theme(
                    data: theme.copyWith(
                      popupMenuTheme: PopupMenuThemeData(
                        color: theme.cardTheme.color,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(8),
                          side: BorderSide(color: theme.colorScheme.outline),
                        ),
                      ),
                    ),
                    child: PopupMenuButton<String>(
                      tooltip: '更改状态或删除',
                      offset: const Offset(0, 32),
                      onSelected: (action) => _handleWordMenuAction(word, action),
                      itemBuilder: (context) => [
                        PopupMenuItem<String>(
                          value: 'mastered',
                          height: 38,
                          child: Row(
                            children: [
                              Icon(Icons.verified_outlined, size: 16, color: theme.colorScheme.primary),
                              const SizedBox(width: 8),
                              const Text('掌握', style: TextStyle(fontSize: 12)),
                            ],
                          ),
                        ),
                        PopupMenuItem<String>(
                          value: 'relearn',
                          height: 38,
                          child: Row(
                            children: [
                              Icon(Icons.replay_outlined, size: 16, color: theme.colorScheme.onSurface),
                              const SizedBox(width: 8),
                              const Text('重学', style: TextStyle(fontSize: 12)),
                            ],
                          ),
                        ),
                        const PopupMenuItem<String>(
                          value: 'delete',
                          height: 38,
                          child: Row(
                            children: [
                              Icon(Icons.delete_outline, size: 16, color: Colors.red),
                              SizedBox(width: 8),
                              Text('删除', style: TextStyle(fontSize: 12, color: Colors.red)),
                            ],
                          ),
                        ),
                      ],
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                        decoration: BoxDecoration(
                          color: theme.cardTheme.color,
                          borderRadius: BorderRadius.circular(4),
                          border: Border.all(color: theme.colorScheme.outline),
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(
                              word.learningState.displayName,
                              style: TextStyle(
                                fontSize: 11,
                                color: theme.colorScheme.onSurface,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            const SizedBox(width: 2),
                            Icon(Icons.arrow_drop_down, size: 14, color: theme.hintColor),
                          ],
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  // 多选管理底栏
  Widget _buildSelectionBottomBar(ThemeData theme) {
    final count = _selectedWordIds.length;
    final hasSelection = count > 0;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      decoration: BoxDecoration(
        color: theme.cardTheme.color,
        border: Border(top: BorderSide(color: theme.colorScheme.outline)),
      ),
      child: SafeArea(
        child: Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                icon: const Icon(Icons.playlist_add, size: 18),
                label: const Text('添加进词单', style: TextStyle(fontSize: 13)),
                style: OutlinedButton.styleFrom(
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                  padding: const EdgeInsets.symmetric(vertical: 10),
                ),
                onPressed: hasSelection ? _handleBatchAddToList : null,
              ),
            ),
            const SizedBox(width: 8),
            OutlinedButton(
              style: OutlinedButton.styleFrom(
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 12),
              ),
              onPressed: hasSelection ? _handleBatchRemoveFromList : null,
              child: const Text('移出词单', style: TextStyle(fontSize: 13)),
            ),
            const SizedBox(width: 8),
            // 批量标记按钮：包含[掌握][重学][删除]
            PopupMenuButton<String>(
              enabled: hasSelection,
              tooltip: '批量标记',
              offset: const Offset(0, -120),
              onSelected: (action) {
                if (action == 'master') _handleBatchMaster();
                if (action == 'relearn') _handleBatchRelearn();
                if (action == 'delete') _handleBatchDelete();
              },
              itemBuilder: (context) => [
                PopupMenuItem(
                  value: 'master',
                  height: 38,
                  child: Row(
                    children: [
                      Icon(Icons.verified_outlined, size: 16, color: theme.colorScheme.primary),
                      const SizedBox(width: 8),
                      const Text('批量掌握', style: TextStyle(fontSize: 12)),
                    ],
                  ),
                ),
                PopupMenuItem(
                  value: 'relearn',
                  height: 38,
                  child: Row(
                    children: [
                      Icon(Icons.refresh, size: 16, color: theme.colorScheme.primary),
                      const SizedBox(width: 8),
                      const Text('批量重学', style: TextStyle(fontSize: 12)),
                    ],
                  ),
                ),
                const PopupMenuItem(
                  value: 'delete',
                  height: 38,
                  child: Row(
                    children: [
                      Icon(Icons.delete_outline, size: 16, color: Colors.red),
                      SizedBox(width: 8),
                      Text('批量删除', style: TextStyle(fontSize: 12, color: Colors.red)),
                    ],
                  ),
                ),
              ],
              child: Container(
                padding: const EdgeInsets.symmetric(vertical: 9, horizontal: 12),
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(8),
                  border: Border.all(
                    color: hasSelection ? theme.colorScheme.primary : theme.disabledColor,
                  ),
                  color: hasSelection
                      ? theme.colorScheme.primary.withValues(alpha: 0.08)
                      : Colors.transparent,
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      Icons.bookmarks_outlined,
                      size: 16,
                      color: hasSelection ? theme.colorScheme.primary : theme.disabledColor,
                    ),
                    const SizedBox(width: 4),
                    Text(
                      '批量标记',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: hasSelection ? theme.colorScheme.primary : theme.disabledColor,
                      ),
                    ),
                    const SizedBox(width: 2),
                    Icon(
                      Icons.arrow_drop_down,
                      size: 16,
                      color: hasSelection ? theme.colorScheme.primary : theme.disabledColor,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
