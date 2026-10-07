import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/db_service.dart';
import '../services/event_service.dart';
import '../services/sync_service.dart';
import '../services/preset_service.dart';
import '../services/tts_service.dart';
import 'settings_dialog.dart';

class ImportSyncPage extends StatefulWidget {
  const ImportSyncPage({super.key});

  @override
  State<ImportSyncPage> createState() => _ImportSyncPageState();
}

class _ImportSyncPageState extends State<ImportSyncPage> with SingleTickerProviderStateMixin {
  late TabController _tabController;
  final DatabaseService _db = DatabaseService.instance;
  final SyncService _sync = SyncService.instance;

  // 原始文本输入
  final TextEditingController _importTextCtrl = TextEditingController();

  // 预设列表与自定义正则表达式
  List<CleansingPreset> _presets = PresetService.builtInPresets;
  CleansingPreset _selectedPreset = PresetService.builtInPresets.first;
  late TextEditingController _patternCtrl;
  int _captureGroupIndex = 1;
  bool _isInverseFilter = false;
  bool _matchWholeLine = false;
  bool _isCustomRuleExpanded = false;

  // 候选词清洗预览列表 (支持单项删除与定位)
  List<String> _cleanedWords = [];
  bool _isImporting = false;
  Timer? _debounceTimer;

  // 同步状态与自动保存
  final TextEditingController _hostCtrl = TextEditingController(text: '192.168.1.');
  final TextEditingController _pinCtrl = TextEditingController();
  bool _isSyncing = false;
  String _syncStatusMessage = '请输入电脑端设置页面显示的局域网地址与 PIN 码';

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 2, vsync: this);

    _patternCtrl = TextEditingController(text: _selectedPreset.pattern);
    _captureGroupIndex = _selectedPreset.captureGroupIndex;
    _isInverseFilter = _selectedPreset.isInverseFilter;
    _matchWholeLine = _selectedPreset.matchWholeLine;

    _importTextCtrl.addListener(_onTextChanged);
    _loadSavedSyncSettings();
    _loadAllPresets();
  }

  Future<void> _loadAllPresets() async {
    final list = await PresetService.getAllPresets();
    if (mounted) {
      setState(() {
        _presets = list;
        // 如果当前选中的预设不在列表中，则重置为第一个
        if (!list.any((p) => p.id == _selectedPreset.id)) {
          _selectedPreset = list.first;
        }
      });
    }
  }

  @override
  void dispose() {
    _debounceTimer?.cancel();
    _importTextCtrl.removeListener(_onTextChanged);
    _importTextCtrl.dispose();
    _patternCtrl.dispose();
    _hostCtrl.dispose();
    _pinCtrl.dispose();
    _tabController.dispose();
    super.dispose();
  }

  // --- 自动恢复与持久化保存局域网 IP 与 PIN ---
  Future<void> _loadSavedSyncSettings() async {
    final savedHost = await _db.getSetting('last_sync_host');
    final savedPin = await _db.getSetting('last_sync_pin');
    if (mounted) {
      setState(() {
        if (savedHost != null && savedHost.isNotEmpty) {
          _hostCtrl.text = savedHost;
        }
        if (savedPin != null && savedPin.isNotEmpty) {
          _pinCtrl.text = savedPin;
        }
      });
    }
  }

  Future<void> _saveSyncSettings(String host, String pin) async {
    await _db.setSetting('last_sync_host', host.trim());
    await _db.setSetting('last_sync_pin', pin.trim());
  }

  void _onTextChanged() {
    _debounceTimer?.cancel();
    _debounceTimer = Timer(const Duration(milliseconds: 160), () {
      _runCleansing();
    });
  }

  void _runCleansing() {
    final text = _importTextCtrl.text;
    final pattern = _patternCtrl.text;

    if (text.trim().isEmpty || pattern.trim().isEmpty) {
      if (_cleanedWords.isNotEmpty) {
        setState(() => _cleanedWords = []);
      }
      return;
    }

    final results = PresetService.executeCleaningCustom(
      sourceText: text,
      pattern: pattern,
      captureGroupIndex: _captureGroupIndex,
      isInverseFilter: _isInverseFilter,
      matchWholeLine: _matchWholeLine,
    );

    setState(() {
      _cleanedWords = results;
    });
  }

  void _onSelectPreset(CleansingPreset p) {
    setState(() {
      _selectedPreset = p;
      _patternCtrl.text = p.pattern;
      _captureGroupIndex = p.captureGroupIndex;
      _isInverseFilter = p.isInverseFilter;
      _matchWholeLine = p.matchWholeLine;
    });
    _runCleansing();
  }


  // 从候选词列表中单项剔除
  void _removeWordCandidate(int index) {
    if (index >= 0 && index < _cleanedWords.length) {
      final removed = _cleanedWords[index];
      setState(() {
        _cleanedWords.removeAt(index);
      });
      ScaffoldMessenger.of(context).clearSnackBars();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('已从待导入候选词中移除「$removed」'),
          duration: const Duration(seconds: 1),
          behavior: SnackBarBehavior.floating,
        ),
      );
    }
  }

  void _handleImportWords() async {
    if (_cleanedWords.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('当前候选词列表为空，请先输入文本或调整正则规则')),
      );
      return;
    }

    setState(() => _isImporting = true);
    final countToImport = _cleanedWords.length;
    final inserted = await _db.importWords(_cleanedWords);

    setState(() {
      _isImporting = false;
      _cleanedWords = [];
    });
    _importTextCtrl.clear();
    DataEventService.instance.notify('import_finished');

    if (mounted) {
      showDialog(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text('导入完成'),
          content: Text('成功导入 $inserted 个新单词到词库！(已自动跳过 ${countToImport - inserted} 个重复词)'),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: const Text('确定'),
            ),
          ],
        ),
      );
    }
  }

  void _testConnection() async {
    final host = _hostCtrl.text.trim();
    if (host.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('请输入电脑端局域网 IP 地址')),
      );
      return;
    }

    _saveSyncSettings(host, _pinCtrl.text);

    setState(() => _syncStatusMessage = '正在测试连接电脑端服务...');
    final ok = await _sync.ping(host);
    setState(() {
      _syncStatusMessage = ok ? '连接成功！电脑端同步服务正在运行。' : '连接失败，请确认手机与电脑在同一 WiFi，且电脑端已开启同步服务。';
    });
  }

  void _startSync(bool biDirectional) async {
    final host = _hostCtrl.text.trim();
    final pin = _pinCtrl.text.trim();

    if (host.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('请输入电脑端局域网 IP 地址')),
      );
      return;
    }
    if (pin.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('请输入电脑端显示的 6 位配对 PIN 码')),
      );
      return;
    }

    // 自动保存输入的 IP 和 PIN 码
    _saveSyncSettings(host, pin);

    setState(() {
      _isSyncing = true;
      _syncStatusMessage = biDirectional ? '正在执行双向合并同步...' : '正在从电脑端拉取全部数据...';
    });

    try {
      final report = biDirectional
          ? await _sync.biDirectionalSync(host, pin)
          : await _sync.pullFromPC(host, pin);

      setState(() {
        _isSyncing = false;
        _syncStatusMessage = report.summary;
      });
      DataEventService.instance.notify('sync_finished');

      if (mounted) {
        showDialog(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('局域网同步成功'),
            content: Text(report.summary),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: const Text('好的'),
              ),
            ],
          ),
        );
      }
    } catch (e) {
      setState(() {
        _isSyncing = false;
        _syncStatusMessage = '同步失败: $e';
      });
      if (mounted) {
        showDialog(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('同步失败'),
            content: Text(e.toString()),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: const Text('关闭'),
              ),
            ],
          ),
        );
      }
    }
  }

  // 保存自定义规则为预设
  Future<void> _showSavePresetDialog() async {
    final nameCtrl = TextEditingController();
    final descCtrl = TextEditingController();

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('保存为清洗预设'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('将当前自定义正则表达式与过滤规则保存到本地预设库：', style: TextStyle(fontSize: 12, color: Colors.grey)),
            const SizedBox(height: 12),
            TextField(
              controller: nameCtrl,
              autofocus: true,
              decoration: const InputDecoration(
                labelText: '预设名称',
                hintText: '如: 我的自定义提取规则',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: descCtrl,
              decoration: const InputDecoration(
                labelText: '规则说明 (可选)',
                hintText: '如: 提取特定括号内的词汇',
                border: OutlineInputBorder(),
              ),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
          ElevatedButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('保存预设'),
          ),
        ],
      ),
    );

    if (confirmed == true && nameCtrl.text.trim().isNotEmpty) {
      final saved = await PresetService.saveCustomPreset(
        name: nameCtrl.text.trim(),
        pattern: _patternCtrl.text,
        captureGroupIndex: _captureGroupIndex,
        isInverseFilter: _isInverseFilter,
        matchWholeLine: _matchWholeLine,
        description: descCtrl.text.trim(),
      );

      await _loadAllPresets();
      setState(() {
        _selectedPreset = saved;
      });

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('已成功保存预设「${saved.name}」'),
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  // 删除当前自定义预设
  Future<void> _deleteCurrentCustomPreset() async {
    if (!_selectedPreset.isCustom) return;

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('删除自定义预设'),
        content: Text('确定删除预设「${_selectedPreset.name}」吗？'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
          ElevatedButton(
            style: ElevatedButton.styleFrom(backgroundColor: Colors.red, foregroundColor: Colors.white),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('删除'),
          ),
        ],
      ),
    );

    if (confirmed == true) {
      await PresetService.deleteCustomPreset(_selectedPreset.id);
      await _loadAllPresets();
      setState(() {
        _selectedPreset = _presets.first;
      });
      _onSelectPreset(_selectedPreset);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('已删除自定义预设'),
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
        title: const Text('导入与同步'),
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
          // 设置按钮
          IconButton(
            icon: const Icon(Icons.settings_outlined, size: 20),
            tooltip: '设置',
            onPressed: () => SettingsDialog.show(context),
          ),
        ],
        bottom: TabBar(
          controller: _tabController,
          labelColor: theme.colorScheme.primary,
          unselectedLabelColor: theme.hintColor,
          indicatorColor: theme.colorScheme.primary,
          tabs: const [
            Tab(text: '生词清洗'),
            Tab(text: '局域网同步'),
          ],
        ),
      ),
      body: TabBarView(
        controller: _tabController,
        children: [
          _buildImportView(theme),
          _buildSyncView(theme),
        ],
      ),
    );
  }

  // --- 生词文本批量导入与正则清洗（深度移植电脑端体验） ---
  Widget _buildImportView(ThemeData theme) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        // 预设选择器
        DropdownButtonFormField<CleansingPreset>(
          initialValue: _presets.any((p) => p.id == _selectedPreset.id)
              ? _presets.firstWhere((p) => p.id == _selectedPreset.id)
              : _presets.first,
          decoration: InputDecoration(
            labelText: '清洗预设规则',
            prefixIcon: const Icon(Icons.tune_outlined, size: 20),
            contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
          ),
          items: _presets.map((p) {
            return DropdownMenuItem<CleansingPreset>(
              value: p,
              child: Row(
                children: [
                  if (p.isCustom) ...[
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
                      margin: const EdgeInsets.only(right: 6),
                      decoration: BoxDecoration(
                        color: theme.colorScheme.primary.withValues(alpha: 0.1),
                        borderRadius: BorderRadius.circular(3),
                      ),
                      child: Text(
                        '自定义',
                        style: TextStyle(fontSize: 10, color: theme.colorScheme.primary, fontWeight: FontWeight.bold),
                      ),
                    ),
                  ],
                  Text(p.name, style: const TextStyle(fontSize: 14)),
                ],
              ),
            );
          }).toList(),
          onChanged: (val) {
            if (val != null) {
              _onSelectPreset(val);
            }
          },
        ),

        const SizedBox(height: 8),

        // 自定义规则展开/收起折叠面板
        Container(
          decoration: BoxDecoration(
            color: theme.cardTheme.color,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(color: theme.colorScheme.outline),
          ),
          child: ExpansionTile(
            initiallyExpanded: _isCustomRuleExpanded,
            onExpansionChanged: (val) => setState(() => _isCustomRuleExpanded = val),
            title: Row(
              children: [
                const Icon(Icons.code, size: 18),
                const SizedBox(width: 8),
                Text(
                  '自定义正则表达式与参数',
                  style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: theme.colorScheme.onSurface),
                ),
              ],
            ),
            subtitle: Text(
              _selectedPreset.description,
              style: TextStyle(fontSize: 11, color: theme.hintColor),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '正则表达式 (Pattern)',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        color: theme.colorScheme.onSurface,
                      ),
                    ),
                    const SizedBox(height: 6),
                    TextField(
                      controller: _patternCtrl,
                      onChanged: (_) => _runCleansing(),
                      decoration: InputDecoration(
                        hintText: '如: ([ぁ-んァ-ヶー\\u4e00-\\u9fa5]+)',
                        contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
                      ),
                      style: const TextStyle(fontFamily: 'monospace', fontSize: 13),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      '捕获组索引',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        color: theme.colorScheme.onSurface,
                      ),
                    ),
                    const SizedBox(height: 6),
                    DropdownButtonFormField<int>(
                      initialValue: _captureGroupIndex,
                      decoration: InputDecoration(
                        contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
                      ),
                      items: const [
                        DropdownMenuItem(value: 0, child: Text('0 (整项匹配)')),
                        DropdownMenuItem(value: 1, child: Text('1 (第1捕获组)')),
                        DropdownMenuItem(value: 2, child: Text('2 (第2捕获组)')),
                      ],
                      onChanged: (val) {
                        if (val != null) {
                          setState(() => _captureGroupIndex = val);
                          _runCleansing();
                        }
                      },
                    ),
                    const SizedBox(height: 8),
                    SwitchListTile(
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      title: const Text('反向过滤 (去除匹配项)', style: TextStyle(fontSize: 13)),
                      value: _isInverseFilter,
                      onChanged: (val) {
                        setState(() => _isInverseFilter = val);
                        _runCleansing();
                      },
                    ),
                    SwitchListTile(
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      title: const Text('匹配整行 (命中时整行处理)', style: TextStyle(fontSize: 13)),
                      value: _matchWholeLine,
                      onChanged: (val) {
                        setState(() => _matchWholeLine = val);
                        _runCleansing();
                      },
                    ),
                    const SizedBox(height: 10),
                    // 预设操作按钮栏：【保存为新预设】以及如果是自定义的则展示【删除该预设】
                    Row(
                      children: [
                        OutlinedButton.icon(
                          icon: const Icon(Icons.bookmark_add_outlined, size: 16),
                          label: const Text('保存为新预设', style: TextStyle(fontSize: 12)),
                          style: OutlinedButton.styleFrom(
                            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                          ),
                          onPressed: _showSavePresetDialog,
                        ),
                        if (_selectedPreset.isCustom) ...[
                          const SizedBox(width: 8),
                          TextButton.icon(
                            icon: const Icon(Icons.delete_outline, size: 16, color: Colors.red),
                            label: const Text('删除此预设', style: TextStyle(fontSize: 12, color: Colors.red)),
                            style: TextButton.styleFrom(
                              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
                            ),
                            onPressed: _deleteCurrentCustomPreset,
                          ),
                        ],
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),

        const SizedBox(height: 12),

        // 原始文本输入框
        TextField(
          controller: _importTextCtrl,
          maxLines: 8,
          decoration: InputDecoration(
            labelText: '原始文本',
            hintText: '在此粘贴待清洗文本或生词列表...',
            filled: true,
            fillColor: theme.cardTheme.color,
            border: OutlineInputBorder(
              borderRadius: BorderRadius.circular(10),
              borderSide: BorderSide(color: theme.colorScheme.outline),
            ),
          ),
        ),

        const SizedBox(height: 14),

        // 最终清洗结果大预览列表（带定位和删除功能）
        Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: theme.cardTheme.color,
            borderRadius: BorderRadius.circular(10),
            border: Border.all(color: theme.colorScheme.outline),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.format_list_bulleted,
                    size: 18,
                    color: _cleanedWords.isNotEmpty ? Colors.green : theme.hintColor,
                  ),
                  const SizedBox(width: 8),
                  Text(
                    '候选词清洗预览 (共 ${_cleanedWords.length} 项)',
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.bold,
                      color: _cleanedWords.isNotEmpty ? Colors.green : theme.hintColor,
                    ),
                  ),
                  const Spacer(),
                  if (_cleanedWords.isNotEmpty)
                    Text(
                      '点击右侧×进行剔除',
                      style: TextStyle(fontSize: 11, color: theme.hintColor),
                    ),
                ],
              ),
              const SizedBox(height: 8),
              if (_cleanedWords.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 20),
                  child: Center(
                    child: Text(
                      '在上方粘贴文本后将通过正则在此生成待导入词表',
                      style: TextStyle(fontSize: 12, color: theme.hintColor),
                    ),
                  ),
                )
              else
                Container(
                  constraints: const BoxConstraints(maxHeight: 280),
                  decoration: BoxDecoration(
                    color: theme.scaffoldBackgroundColor,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: theme.colorScheme.outline),
                  ),
                  child: ListView.separated(
                    shrinkWrap: true,
                    padding: const EdgeInsets.symmetric(vertical: 4),
                    itemCount: _cleanedWords.length,
                    separatorBuilder: (context, index) => const Divider(height: 1),
                    itemBuilder: (context, index) {
                      final word = _cleanedWords[index];
                      return ListTile(
                        dense: true,
                        visualDensity: VisualDensity.compact,
                        leading: CircleAvatar(
                          radius: 11,
                          backgroundColor: theme.colorScheme.primary.withValues(alpha: 0.1),
                          child: Text(
                            '${index + 1}',
                            style: TextStyle(fontSize: 10, color: theme.colorScheme.primary, fontWeight: FontWeight.bold),
                          ),
                        ),
                        title: Text(word, style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w500)),
                        trailing: IconButton(
                          icon: const Icon(Icons.close, size: 16, color: Colors.grey),
                          tooltip: '从候选词中排除',
                          onPressed: () => _removeWordCandidate(index),
                        ),
                      );
                    },
                  ),
                ),
            ],
          ),
        ),

        const SizedBox(height: 16),

        // 导入执行大按钮
        SizedBox(
          width: double.infinity,
          height: 48,
          child: ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: theme.colorScheme.primary,
              foregroundColor: theme.colorScheme.onPrimary,
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
            ),
            onPressed: _isImporting || _cleanedWords.isEmpty ? null : _handleImportWords,
            child: _isImporting
                ? const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                  )
                : Text(
                    _cleanedWords.isNotEmpty ? '导入词库 (${_cleanedWords.length})' : '暂无有效候选词',
                    style: const TextStyle(fontWeight: FontWeight.bold),
                  ),
          ),
        ),
      ],
    );
  }

  // --- 局域网数据同步 (支持自动保存上次输入的 IP 与 PIN 码) ---
  Widget _buildSyncView(ThemeData theme) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: theme.cardTheme.color,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: theme.colorScheme.outline),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Row(
                children: [
                  Icon(Icons.sync_alt_outlined, size: 20),
                  SizedBox(width: 8),
                  Text('局域网数据同步', style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold)),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                '确保手机与电脑连接同一路由器/WiFi。\n在电脑端「设置 -> 局域网多端数据同步」开启服务，输入对应的 IP 与 PIN 码即可同步。',
                style: TextStyle(fontSize: 12, color: theme.hintColor, height: 1.4),
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        TextField(
          controller: _hostCtrl,
          decoration: InputDecoration(
            labelText: '电脑端 IP 地址',
            hintText: '如 192.168.1.100',
            prefixIcon: const Icon(Icons.computer_outlined, size: 20),
            suffixIcon: TextButton(
              onPressed: _testConnection,
              child: const Text('测试连接'),
            ),
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
          ),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _pinCtrl,
          keyboardType: TextInputType.number,
          maxLength: 6,
          decoration: InputDecoration(
            labelText: '配对 PIN 码',
            hintText: '6 位数字',
            prefixIcon: const Icon(Icons.password_outlined, size: 20),
            counterText: '',
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
          ),
        ),
        const SizedBox(height: 16),
        Row(
          children: [
            Expanded(
              child: OutlinedButton(
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 8),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                ),
                onPressed: _isSyncing ? null : () => _startSync(false),
                child: const Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text('从电脑拉取全部', style: TextStyle(fontWeight: FontWeight.w600)),
                    SizedBox(height: 2),
                    Text('以电脑端数据为准', style: TextStyle(fontSize: 10, color: Colors.grey)),
                  ],
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: ElevatedButton(
                style: ElevatedButton.styleFrom(
                  backgroundColor: theme.colorScheme.primary,
                  foregroundColor: theme.colorScheme.onPrimary,
                  padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 8),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                ),
                onPressed: _isSyncing ? null : () => _startSync(true),
                child: const Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text('双向合并同步', style: TextStyle(fontWeight: FontWeight.bold)),
                    SizedBox(height: 2),
                    Text('互通两端最新进度 (推荐)', style: TextStyle(fontSize: 10, color: Colors.white70)),
                  ],
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 16),
        Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: theme.scaffoldBackgroundColor,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(color: theme.colorScheme.outline),
          ),
          child: Row(
            children: [
              if (_isSyncing) ...[
                const SizedBox(
                  width: 14,
                  height: 14,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
                const SizedBox(width: 8),
              ],
              Expanded(
                child: Text(
                  _syncStatusMessage,
                  style: TextStyle(fontSize: 12, color: theme.hintColor),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
      ],
    );
  }
}
