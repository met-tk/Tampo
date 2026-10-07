import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/theme_service.dart';
import '../services/tts_service.dart';
import '../services/db_service.dart';
import '../services/event_service.dart';

class SettingsDialog extends StatefulWidget {
  const SettingsDialog({super.key});

  static Future<void> show(BuildContext context) {
    return showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (ctx) => const SettingsDialog(),
    );
  }

  @override
  State<SettingsDialog> createState() => _SettingsDialogState();
}

class _SettingsDialogState extends State<SettingsDialog> {
  final ThemeService _themeService = ThemeService.instance;
  final TtsService _ttsService = TtsService.instance;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final mediaQuery = MediaQuery.of(context);

    return Container(
      constraints: BoxConstraints(maxHeight: mediaQuery.size.height * 0.85),
      decoration: BoxDecoration(
        color: theme.scaffoldBackgroundColor,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
        border: Border.all(color: theme.colorScheme.outline),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // 顶部抓手与标题栏
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 12, 8),
              child: Row(
                children: [
                  const Icon(Icons.settings_outlined, size: 20),
                  const SizedBox(width: 8),
                  const Text(
                    '设置',
                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.bold),
                  ),
                  const Spacer(),
                  IconButton(
                    icon: const Icon(Icons.close, size: 20),
                    tooltip: '关闭',
                    onPressed: () => Navigator.pop(context),
                  ),
                ],
              ),
            ),
            const Divider(height: 1),

            Flexible(
              child: ListView(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                children: [
                  // --- 1. 主题模式设置 ---
                  _buildSectionHeader('主题外观', Icons.palette_outlined, theme),
                  const SizedBox(height: 8),
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
                        Text(
                          '黑白单色风格，明亮与深色模式均采用高反差排版，确保字迹清晰。',
                          style: TextStyle(fontSize: 12, color: theme.hintColor),
                        ),
                        const SizedBox(height: 12),
                        ValueListenableBuilder<ThemeMode>(
                          valueListenable: _themeService.themeModeNotifier,
                          builder: (context, currentMode, _) {
                            return Row(
                              children: [
                                Expanded(
                                  child: _buildThemeModeButton(
                                    '跟随系统',
                                    Icons.brightness_auto_outlined,
                                    ThemeMode.system,
                                    currentMode,
                                    theme,
                                  ),
                                ),
                                const SizedBox(width: 8),
                                Expanded(
                                  child: _buildThemeModeButton(
                                    '明亮模式',
                                    Icons.light_mode_outlined,
                                    ThemeMode.light,
                                    currentMode,
                                    theme,
                                  ),
                                ),
                                const SizedBox(width: 8),
                                Expanded(
                                  child: _buildThemeModeButton(
                                    '黑暗模式',
                                    Icons.dark_mode_outlined,
                                    ThemeMode.dark,
                                    currentMode,
                                    theme,
                                  ),
                                ),
                              ],
                            );
                          },
                        ),
                      ],
                    ),
                  ),

                  const SizedBox(height: 20),

                  // --- 2. 自动发音与 TTS API 设置 ---
                  _buildSectionHeader('自动发音与 TTS API', Icons.record_voice_over_outlined, theme),
                  const SizedBox(height: 8),

                  Container(
                    decoration: BoxDecoration(
                      color: theme.cardTheme.color,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: theme.colorScheme.outline),
                    ),
                    child: Column(
                      children: [
                        ValueListenableBuilder<bool>(
                          valueListenable: _ttsService.autoEnabledNotifier,
                          builder: (context, isAuto, _) {
                            return SwitchListTile(
                              title: const Text(
                                '点击自动发音',
                                style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
                              ),
                              subtitle: Text(
                                '点击卡片或标记时立即朗读发音',
                                style: TextStyle(fontSize: 11, color: theme.hintColor),
                              ),
                              value: isAuto,
                              onChanged: (val) {
                                _ttsService.setAutoEnabled(val);
                                HapticFeedback.selectionClick();
                              },
                            );
                          },
                        ),
                        const Divider(height: 1),
                        Padding(
                          padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                          child: Row(
                            children: [
                              Expanded(
                                child: Text(
                                  '发音 API 降级优先级',
                                  style: TextStyle(fontSize: 12, color: theme.hintColor, fontWeight: FontWeight.w500),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              TextButton(
                                onPressed: () async {
                                  await _ttsService.resetToDefaults();
                                  setState(() {});
                                },
                                style: TextButton.styleFrom(
                                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                                  visualDensity: VisualDensity.compact,
                                ),
                                child: const Text('恢复默认', style: TextStyle(fontSize: 11)),
                              ),
                              const SizedBox(width: 2),
                              TextButton.icon(
                                icon: const Icon(Icons.add, size: 14),
                                label: const Text('添加 API', style: TextStyle(fontSize: 11)),
                                style: TextButton.styleFrom(
                                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                                  visualDensity: VisualDensity.compact,
                                ),
                                onPressed: _showAddApiDialog,
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 4),
                        ...List.generate(_ttsService.apis.length, (index) {
                          final api = _ttsService.apis[index];
                          return _buildApiItemRow(api, index, theme);
                        }),
                        const SizedBox(height: 8),
                      ],
                    ),
                  ),
                  const SizedBox(height: 16),
                  _buildSectionHeader('音频缓存管理', Icons.cleaning_services_outlined, theme),
                  const SizedBox(height: 8),

                  Container(
                    decoration: BoxDecoration(
                      color: theme.cardTheme.color,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: theme.colorScheme.outline),
                    ),
                    child: Column(
                      children: [
                        ValueListenableBuilder<bool>(
                          valueListenable: _ttsService.autoClearOnExitNotifier,
                          builder: (context, autoClear, _) {
                            return SwitchListTile(
                              title: const Text(
                                '自动清理音频缓存',
                                style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
                              ),
                              subtitle: Text(
                                '退出应用或切至后台时自动清空临时音频',
                                style: TextStyle(fontSize: 11, color: theme.hintColor),
                              ),
                              value: autoClear,
                              onChanged: (val) {
                                _ttsService.setAutoClearOnExit(val);
                                HapticFeedback.selectionClick();
                              },
                            );
                          },
                        ),
                        const Divider(height: 1),
                        FutureBuilder<String>(
                          future: _ttsService.getCacheSizeFormatted(),
                          builder: (context, snapshot) {
                            final sizeStr = snapshot.data ?? '计算中...';
                            return ListTile(
                              title: const Text('当前缓存体积', style: TextStyle(fontSize: 14)),
                              subtitle: Text(sizeStr, style: TextStyle(fontSize: 12, color: theme.hintColor)),
                              trailing: OutlinedButton(
                                style: OutlinedButton.styleFrom(
                                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                                  visualDensity: VisualDensity.compact,
                                ),
                                onPressed: () async {
                                  await _ttsService.clearCache();
                                  HapticFeedback.mediumImpact();
                                  if (context.mounted) {
                                    ScaffoldMessenger.of(context).clearSnackBars();
                                    ScaffoldMessenger.of(context).showSnackBar(
                                      const SnackBar(
                                        content: Text('已清空音频缓存'),
                                        duration: Duration(seconds: 1),
                                        behavior: SnackBarBehavior.floating,
                                      ),
                                    );
                                  }
                                  setState(() {});
                                },
                                child: const Text('立即清空', style: TextStyle(fontSize: 12)),
                              ),
                            );
                          },
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 16),
                  _buildSectionHeader('数据管理', Icons.storage_outlined, theme),
                  const SizedBox(height: 8),

                  Container(
                    decoration: BoxDecoration(
                      color: theme.cardTheme.color,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: theme.colorScheme.outline),
                    ),
                    child: Column(
                      children: [
                        ListTile(
                          title: const Text(
                            '清除本机单词数据库',
                            style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: Colors.red),
                          ),
                          subtitle: Text(
                            '清空本机全部生词、复习记录与自建词单',
                            style: TextStyle(fontSize: 11, color: theme.hintColor),
                          ),
                          trailing: OutlinedButton(
                            style: OutlinedButton.styleFrom(
                              foregroundColor: Colors.red,
                              side: const BorderSide(color: Colors.red),
                              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                              visualDensity: VisualDensity.compact,
                            ),
                            onPressed: _showClearDatabaseDialog,
                            child: const Text('清除数据', style: TextStyle(fontSize: 12)),
                          ),
                        ),
                      ],
                    ),
                  ),

                  // 底部版本与制作人信息（隐藏构建号，仅保留版本号）
                  const SizedBox(height: 24),
                  Center(
                    child: Column(
                      children: [
                        Text(
                          'Tampo Android v1.2.6',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w500,
                            color: theme.hintColor.withValues(alpha: 0.8),
                            letterSpacing: 0.5,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'AI-driven by Taketo',
                          style: TextStyle(
                            fontSize: 10,
                            color: theme.hintColor.withValues(alpha: 0.5),
                            letterSpacing: 0.8,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildSectionHeader(String title, IconData icon, ThemeData theme) {
    return Row(
      children: [
        Icon(icon, size: 16, color: theme.colorScheme.primary),
        const SizedBox(width: 6),
        Text(
          title,
          style: const TextStyle(fontSize: 13, fontWeight: FontWeight.bold),
        ),
      ],
    );
  }

  Widget _buildThemeModeButton(
    String label,
    IconData icon,
    ThemeMode mode,
    ThemeMode current,
    ThemeData theme,
  ) {
    final isSelected = current == mode;
    return InkWell(
      borderRadius: BorderRadius.circular(8),
      onTap: () {
        _themeService.setThemeMode(mode);
        HapticFeedback.selectionClick();
      },
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 4),
        decoration: BoxDecoration(
          color: isSelected ? theme.colorScheme.primary : Colors.transparent,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(
            color: isSelected ? theme.colorScheme.primary : theme.colorScheme.outline,
          ),
        ),
        child: Column(
          children: [
            Icon(
              icon,
              size: 20,
              color: isSelected ? theme.colorScheme.onPrimary : theme.colorScheme.onSurface,
            ),
            const SizedBox(height: 4),
            Text(
              label,
              style: TextStyle(
                fontSize: 11,
                fontWeight: isSelected ? FontWeight.bold : FontWeight.normal,
                color: isSelected ? theme.colorScheme.onPrimary : theme.colorScheme.onSurface,
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildApiItemRow(TtsApiItem api, int index, ThemeData theme) {
    final isFirst = index == 0;
    final isLast = index == _ttsService.apis.length - 1;

    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: theme.scaffoldBackgroundColor,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: theme.colorScheme.outline),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color: theme.colorScheme.primary.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(4),
                ),
                child: Text(
                  '优先级 ${index + 1}',
                  style: TextStyle(fontSize: 10, fontWeight: FontWeight.bold, color: theme.colorScheme.primary),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  api.name,
                  style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              // 启用/禁用开关
              Switch(
                value: api.isEnabled,
                activeThumbColor: theme.colorScheme.primary,
                materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                onChanged: (val) async {
                  await _ttsService.toggleApiEnabled(api.id, val);
                  setState(() {});
                },
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            api.urlTemplate,
            style: TextStyle(
              fontSize: 10,
              color: theme.hintColor,
              fontFamily: 'monospace',
            ),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
          const SizedBox(height: 6),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              // 试听按钮
              TextButton.icon(
                icon: const Icon(Icons.volume_up_outlined, size: 14),
                label: const Text('试听', style: TextStyle(fontSize: 11)),
                style: TextButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                  visualDensity: VisualDensity.compact,
                ),
                onPressed: () {
                  _ttsService.playApi(api, '桜', reading: 'さくら');
                },
              ),
              const SizedBox(width: 4),
              // 编辑按钮
              IconButton(
                icon: const Icon(Icons.edit_outlined, size: 16),
                tooltip: '编辑此 API',
                constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                padding: EdgeInsets.zero,
                onPressed: () => _showEditApiDialog(api),
              ),
              const SizedBox(width: 2),
              // 上移优先级
              IconButton(
                icon: const Icon(Icons.arrow_upward, size: 16),
                tooltip: '提高优先级',
                constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                padding: EdgeInsets.zero,
                onPressed: isFirst
                    ? null
                    : () async {
                        await _ttsService.movePriority(index, index - 1);
                        setState(() {});
                      },
              ),
              // 下移优先级
              IconButton(
                icon: const Icon(Icons.arrow_downward, size: 16),
                tooltip: '降低优先级',
                constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                padding: EdgeInsets.zero,
                onPressed: isLast
                    ? null
                    : () async {
                        await _ttsService.movePriority(index, index + 1);
                        setState(() {});
                      },
              ),
              // 删除自定义 API
              if (!api.isDefault) ...[
                const SizedBox(width: 2),
                IconButton(
                  icon: const Icon(Icons.delete_outline, size: 16, color: Colors.red),
                  tooltip: '删除此 API',
                  constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                  padding: EdgeInsets.zero,
                  onPressed: () async {
                    await _ttsService.deleteApi(api.id);
                    setState(() {});
                  },
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }

  void _showEditApiDialog(TtsApiItem api) {
    final nameCtrl = TextEditingController(text: api.name);
    final urlCtrl = TextEditingController(text: api.urlTemplate);

    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('编辑 API: ${api.name}'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              '支持使用 {reading} 或 {text} 作为日语词汇占位符',
              style: TextStyle(fontSize: 12, color: Colors.grey),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: nameCtrl,
              decoration: const InputDecoration(
                labelText: 'API 名称',
                contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: urlCtrl,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: 'URL 请求模板',
                contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                border: OutlineInputBorder(),
              ),
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          ElevatedButton(
            onPressed: () async {
              final name = nameCtrl.text.trim();
              final url = urlCtrl.text.trim();
              if (name.isNotEmpty && url.isNotEmpty) {
                await _ttsService.updateApi(api.id, name, url);
                if (ctx.mounted) Navigator.pop(ctx);
                setState(() {});
              }
            },
            child: const Text('保存修改'),
          ),
        ],
      ),
    );
  }

  void _showAddApiDialog() {
    final nameCtrl = TextEditingController();
    final urlCtrl = TextEditingController(
      text: 'https://example.com/tts?lang=ja&text={reading}',
    );

    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('添加自定义 TTS API'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              '支持使用 {reading} 或 {text} 作为日语词汇占位符',
              style: TextStyle(fontSize: 12, color: Colors.grey),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: nameCtrl,
              decoration: const InputDecoration(
                labelText: 'API 名称 (如: 某某翻译 TTS)',
                contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: urlCtrl,
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: 'URL 请求模板',
                contentPadding: EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                border: OutlineInputBorder(),
              ),
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          ElevatedButton(
            onPressed: () async {
              final name = nameCtrl.text.trim();
              final url = urlCtrl.text.trim();
              if (name.isNotEmpty && url.isNotEmpty) {
                await _ttsService.addCustomApi(name, url);
                if (ctx.mounted) Navigator.pop(ctx);
                setState(() {});
              }
            },
            child: const Text('保存'),
          ),
        ],
      ),
    );
  }

  void _showClearDatabaseDialog() {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Row(
          children: [
            Icon(Icons.warning_amber_rounded, color: Colors.red, size: 24),
            SizedBox(width: 8),
            Text('清空本地词库数据'),
          ],
        ),
        content: const Text(
          '此操作将永久清空本机全部生词列表、打卡历史以及自建词单，且不可恢复。\n\n确定要继续吗？',
          style: TextStyle(fontSize: 13, height: 1.5),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.red,
              foregroundColor: Colors.white,
            ),
            onPressed: () async {
              Navigator.pop(ctx);
              await DatabaseService.instance.clearAllData();
              DataEventService.instance.notify('words_changed');
              HapticFeedback.heavyImpact();
              if (mounted) {
                ScaffoldMessenger.of(context).clearSnackBars();
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(
                    content: Text('已成功清空本机全部词库数据'),
                    duration: Duration(seconds: 2),
                    behavior: SnackBarBehavior.floating,
                  ),
                );
                setState(() {});
              }
            },
            child: const Text('确定清空'),
          ),
        ],
      ),
    );
  }
}
