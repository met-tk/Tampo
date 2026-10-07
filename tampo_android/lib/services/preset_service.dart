import 'dart:convert';
import 'db_service.dart';

class CleansingPreset {
  final String id;
  final String name;
  final String pattern;
  final int captureGroupIndex;
  final bool isInverseFilter;
  final bool matchWholeLine;
  final String description;
  final bool isCustom;

  const CleansingPreset({
    required this.id,
    required this.name,
    required this.pattern,
    this.captureGroupIndex = 0,
    this.isInverseFilter = false,
    this.matchWholeLine = false,
    required this.description,
    this.isCustom = false,
  });

  Map<String, dynamic> toMap() => {
        'id': id,
        'name': name,
        'pattern': pattern,
        'captureGroupIndex': captureGroupIndex,
        'isInverseFilter': isInverseFilter,
        'matchWholeLine': matchWholeLine,
        'description': description,
        'isCustom': isCustom,
      };

  factory CleansingPreset.fromMap(Map<String, dynamic> map) => CleansingPreset(
        id: map['id'] ?? '',
        name: map['name'] ?? '',
        pattern: map['pattern'] ?? '',
        captureGroupIndex: map['captureGroupIndex'] ?? 0,
        isInverseFilter: map['isInverseFilter'] ?? false,
        matchWholeLine: map['matchWholeLine'] ?? false,
        description: map['description'] ?? '',
        isCustom: map['isCustom'] ?? true,
      );
}

class PresetService {
  static final List<CleansingPreset> builtInPresets = [
    const CleansingPreset(
      id: 'builtin-lines',
      name: '纯文本/换行清洗',
      pattern: r'^\s*([^\r\n]+?)\s*$',
      captureGroupIndex: 1,
      description: '按行匹配所有非空纯文本行',
      isCustom: false,
    ),
    const CleansingPreset(
      id: 'builtin-ebook',
      name: '电子书标记清洗',
      pattern: r'^[▪•\-\*\s]+([ぁ-んァ-ヶー\u4e00-\u9fa5a-zA-Z0-9]+)\s*$',
      captureGroupIndex: 1,
      description: '自动剔除行首电子书特殊符号、章节标记并提取单词',
      isCustom: false,
    ),
    const CleansingPreset(
      id: 'builtin-japanese',
      name: '纯日文/汉字提取',
      pattern: r'([ぁ-んァ-ヶー\u4e00-\u9fa5]+)',
      captureGroupIndex: 1,
      description: '忽略所有西文标点与排版符号，精准抽取日文假名及汉字词块',
      isCustom: false,
    ),
    const CleansingPreset(
      id: 'builtin-exclude-noise',
      name: '排除注释与数字行',
      pattern: r'^\s*(#|//|\d+|http[s]?:)',
      captureGroupIndex: 0,
      isInverseFilter: true,
      matchWholeLine: true,
      description: '反向过滤+匹配整行：命中注释、数字序号或链接时直接将整行删掉',
      isCustom: false,
    ),
    const CleansingPreset(
      id: 'builtin-strip-brackets',
      name: '剔除括号与注音',
      pattern: r'[（\(［\[\{【].*?[）\)］\]\}】]',
      captureGroupIndex: 0,
      isInverseFilter: true,
      matchWholeLine: false,
      description: '反向过滤：匹配各种括号与注音假名并删除，保留该行剩余干净词汇',
      isCustom: false,
    ),
  ];

  /// 获取全部预设（内置预设 + 用户自定义预设）
  static Future<List<CleansingPreset>> getAllPresets() async {
    final customList = await getCustomPresets();
    return [...builtInPresets, ...customList];
  }

  /// 获取用户自定义预设
  static Future<List<CleansingPreset>> getCustomPresets() async {
    final saved = await DatabaseService.instance.getSetting('custom_cleansing_presets');
    if (saved == null || saved.isEmpty) return [];
    try {
      final List list = jsonDecode(saved);
      return list.map((e) => CleansingPreset.fromMap(e as Map<String, dynamic>)).toList();
    } catch (_) {
      return [];
    }
  }

  /// 保存自定义预设
  static Future<CleansingPreset> saveCustomPreset({
    required String name,
    required String pattern,
    int captureGroupIndex = 0,
    bool isInverseFilter = false,
    bool matchWholeLine = false,
    String? description,
  }) async {
    final list = await getCustomPresets();
    final newPreset = CleansingPreset(
      id: 'custom_${DateTime.now().millisecondsSinceEpoch}',
      name: name.trim(),
      pattern: pattern,
      captureGroupIndex: captureGroupIndex,
      isInverseFilter: isInverseFilter,
      matchWholeLine: matchWholeLine,
      description: description?.trim().isNotEmpty == true
          ? description!.trim()
          : '自定义规则：$pattern',
      isCustom: true,
    );

    list.add(newPreset);
    final jsonStr = jsonEncode(list.map((e) => e.toMap()).toList());
    await DatabaseService.instance.setSetting('custom_cleansing_presets', jsonStr);
    return newPreset;
  }

  /// 删除自定义预设
  static Future<void> deleteCustomPreset(String id) async {
    final list = await getCustomPresets();
    list.removeWhere((p) => p.id == id);
    final jsonStr = jsonEncode(list.map((e) => e.toMap()).toList());
    await DatabaseService.instance.setSetting('custom_cleansing_presets', jsonStr);
  }

  static List<String> executeCleaning(String sourceText, CleansingPreset preset) {
    return executeCleaningCustom(
      sourceText: sourceText,
      pattern: preset.pattern,
      captureGroupIndex: preset.captureGroupIndex,
      isInverseFilter: preset.isInverseFilter,
      matchWholeLine: preset.matchWholeLine,
    );
  }

  static List<String> executeCleaningCustom({
    required String sourceText,
    required String pattern,
    int captureGroupIndex = 0,
    bool isInverseFilter = false,
    bool matchWholeLine = false,
  }) {
    if (sourceText.trim().isEmpty || pattern.trim().isEmpty) return [];

    final results = <String>[];
    final seen = <String>{};

    try {
      final regex = RegExp(pattern, multiLine: true);

      if (isInverseFilter) {
        // 反向过滤
        final lines = sourceText.split(RegExp(r'\r\n|\r|\n'));
        for (final rawLine in lines) {
          if (rawLine.trim().isEmpty) continue;

          if (matchWholeLine) {
            // 反向过滤 + 匹配整行：命中任意字符，整行直接删掉
            if (regex.hasMatch(rawLine)) {
              continue;
            }
            final line = rawLine.trim();
            if (line.isNotEmpty && !seen.contains(line)) {
              seen.add(line);
              results.add(line);
            }
          } else {
            // 反向过滤 (未勾选匹配整行)：单纯匹配字符并删除，保留该行剩余文本
            final cleanedLine = rawLine.replaceAll(regex, '').trim();
            if (cleanedLine.isNotEmpty && !seen.contains(cleanedLine)) {
              seen.add(cleanedLine);
              results.add(cleanedLine);
            }
          }
        }
      } else {
        // 正向过滤
        if (matchWholeLine) {
          // 正向过滤 + 匹配整行：只要行内命中任意字符，就将整行输出
          final lines = sourceText.split(RegExp(r'\r\n|\r|\n'));
          for (final rawLine in lines) {
            if (rawLine.trim().isEmpty) continue;

            if (regex.hasMatch(rawLine)) {
              final line = rawLine.trim();
              if (line.isNotEmpty && !seen.contains(line)) {
                seen.add(line);
                results.add(line);
              }
            }
          }
        } else {
          // 正向过滤 (未勾选匹配整行)：提取匹配到的片段或捕获组
          final matches = regex.allMatches(sourceText);
          for (final match in matches) {
            String extracted = '';
            if (captureGroupIndex > 0 && match.groupCount >= captureGroupIndex) {
              extracted = match.group(captureGroupIndex) ?? '';
            } else {
              extracted = match.group(0) ?? '';
            }

            final clean = extracted.trim();
            if (clean.isNotEmpty && !seen.contains(clean)) {
              seen.add(clean);
              results.add(clean);
            }
          }
        }
      }
    } catch (_) {
      // 正则异常兜底按换行分割
      final lines = sourceText.split(RegExp(r'\r\n|\r|\n'));
      for (final line in lines) {
        final clean = line.trim();
        if (clean.isNotEmpty && !seen.contains(clean)) {
          seen.add(clean);
          results.add(clean);
        }
      }
    }

    return results;
  }
}
