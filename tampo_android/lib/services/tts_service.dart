import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:http/http.dart' as http;
import 'db_service.dart';

class TtsApiItem {
  String id;
  String name;
  String urlTemplate; // 包含 {reading} 或 {text}
  bool isEnabled;
  bool isDefault;

  TtsApiItem({
    required this.id,
    required this.name,
    required this.urlTemplate,
    this.isEnabled = true,
    this.isDefault = false,
  });

  Map<String, dynamic> toMap() => {
        'id': id,
        'name': name,
        'urlTemplate': urlTemplate,
        'isEnabled': isEnabled,
        'isDefault': isDefault,
      };

  factory TtsApiItem.fromMap(Map<String, dynamic> map) => TtsApiItem(
        id: map['id'] ?? '',
        name: map['name'] ?? '',
        urlTemplate: map['urlTemplate'] ?? '',
        isEnabled: map['isEnabled'] ?? true,
        isDefault: map['isDefault'] ?? false,
      );
}

class TtsService {
  static final TtsService instance = TtsService._internal();
  TtsService._internal();

  static const MethodChannel _audioChannel = MethodChannel('com.taketo.tampo_android/audio');
  static final http.Client _httpClient = http.Client();
  final DatabaseService _db = DatabaseService.instance;

  bool _isAutoEnabled = true;
  bool get isAutoEnabled => _isAutoEnabled;
  final ValueNotifier<bool> autoEnabledNotifier = ValueNotifier<bool>(true);

  bool _autoClearOnExit = false;
  bool get autoClearOnExit => _autoClearOnExit;
  final ValueNotifier<bool> autoClearOnExitNotifier = ValueNotifier<bool>(false);

  List<TtsApiItem> _apis = [];
  List<TtsApiItem> get apis => List.unmodifiable(_apis);

  bool _initialized = false;
  Directory? _cacheDir;

  Future<Directory> _getCacheDir() async {
    if (_cacheDir != null) return _cacheDir!;
    final dir = Directory('${Directory.systemTemp.path}/tampo_audio_cache');
    if (!dir.existsSync()) {
      dir.createSync(recursive: true);
    }
    _cacheDir = dir;
    return dir;
  }

  String _toHexKey(String text) {
    final buffer = StringBuffer();
    for (final unit in text.codeUnits) {
      buffer.write(unit.toRadixString(16).padLeft(4, '0'));
    }
    return buffer.toString();
  }

  List<TtsApiItem> _getDefaultApis() {
    return [
      TtsApiItem(
        id: 'youdao',
        name: '有道词典日语 TTS',
        urlTemplate: 'https://dict.youdao.com/dictvoice?audio={reading}&le=jap',
        isEnabled: true,
        isDefault: true,
      ),
      TtsApiItem(
        id: 'google',
        name: '谷歌翻译日语 TTS',
        urlTemplate: 'https://translate.google.com/translate_tts?ie=UTF-8&tl=ja&client=tw-ob&q={reading}',
        isEnabled: true,
        isDefault: true,
      ),
      TtsApiItem(
        id: 'baidu',
        name: '百度翻译日语 TTS',
        urlTemplate: 'https://fanyi.baidu.com/gettts?lan=jp&text={reading}&spd=5&source=web',
        isEnabled: true,
        isDefault: true,
      ),
      TtsApiItem(
        id: 'japanesepod',
        name: 'JapanesePod101 真人发音',
        urlTemplate: 'https://assets.languagepod101.com/dictionary/japanese/audiomp3.php?kanji={kanji}&kana={kana}',
        isEnabled: true,
        isDefault: true,
      ),
    ];
  }

  Future<void> init() async {
    if (_initialized) return;

    // 读取自动发音开关（默认为 true 开启）
    final autoSetting = await _db.getSetting('tts_auto_enabled');
    _isAutoEnabled = autoSetting == null ? true : autoSetting == 'true';
    autoEnabledNotifier.value = _isAutoEnabled;

    // 读取退出自动清理音频缓存开关（默认为 false）
    final autoClearSetting = await _db.getSetting('auto_clear_audio_cache_on_exit');
    _autoClearOnExit = autoClearSetting == 'true';
    autoClearOnExitNotifier.value = _autoClearOnExit;
    if (_autoClearOnExit) {
      await clearCache();
    }

    // 读取 API 配置
    final apiSetting = await _db.getSetting('tts_api_list');
    if (apiSetting != null && apiSetting.isNotEmpty) {
      try {
        final List list = jsonDecode(apiSetting);
        _apis = list.map((e) => TtsApiItem.fromMap(e as Map<String, dynamic>)).toList();
        
        // 确保包含更新后的默认 API 项
        bool hasChanges = false;
        final podIndex = _apis.indexWhere((a) => a.id == 'japanesepod');
        if (podIndex >= 0) {
          // 修复旧模板中缺失双参数的问题
          if (!_apis[podIndex].urlTemplate.contains('kana=')) {
            _apis[podIndex].urlTemplate = 'https://assets.languagepod101.com/dictionary/japanese/audiomp3.php?kanji={kanji}&kana={kana}';
            hasChanges = true;
          }
        } else {
          _apis.add(
            TtsApiItem(
              id: 'japanesepod',
              name: 'JapanesePod101 真人发音',
              urlTemplate: 'https://assets.languagepod101.com/dictionary/japanese/audiomp3.php?kanji={kanji}&kana={kana}',
              isEnabled: true,
              isDefault: true,
            ),
          );
          hasChanges = true;
        }

        if (hasChanges) {
          await _saveApis();
        }
      } catch (_) {
        _apis = _getDefaultApis();
        await _saveApis();
      }
    } else {
      _apis = _getDefaultApis();
      await _saveApis();
    }

    _initialized = true;
  }

  Future<void> setAutoEnabled(bool val) async {
    _isAutoEnabled = val;
    autoEnabledNotifier.value = val;
    await _db.setSetting('tts_auto_enabled', val ? 'true' : 'false');
  }

  Future<void> toggleAutoEnabled() async {
    await setAutoEnabled(!_isAutoEnabled);
  }

  Future<void> setAutoClearOnExit(bool val) async {
    _autoClearOnExit = val;
    autoClearOnExitNotifier.value = val;
    await _db.setSetting('auto_clear_audio_cache_on_exit', val ? 'true' : 'false');
  }

  Future<void> toggleAutoClearOnExit() async {
    await setAutoClearOnExit(!_autoClearOnExit);
  }

  Future<int> getCacheSizeBytes() async {
    try {
      final dir = await _getCacheDir();
      if (!dir.existsSync()) return 0;
      int total = 0;
      for (final file in dir.listSync()) {
        if (file is File) {
          total += file.lengthSync();
        }
      }
      return total;
    } catch (_) {
      return 0;
    }
  }

  Future<String> getCacheSizeFormatted() async {
    final bytes = await getCacheSizeBytes();
    if (bytes <= 0) return '0 KB';
    if (bytes < 1024 * 1024) {
      return '${(bytes / 1024).toStringAsFixed(1)} KB';
    }
    return '${(bytes / (1024 * 1024)).toStringAsFixed(2)} MB';
  }

  Future<void> clearCache() async {
    try {
      await _audioChannel.invokeMethod('stop');
    } catch (_) {}
    try {
      final dir = await _getCacheDir();
      if (dir.existsSync()) {
        for (final file in dir.listSync()) {
          if (file is File) {
            try {
              file.deleteSync();
            } catch (_) {}
          }
        }
      }
    } catch (_) {}
  }

  Future<void> handleExitCleanup() async {
    if (_autoClearOnExit) {
      await clearCache();
    }
  }

  Future<void> _saveApis() async {
    final jsonStr = jsonEncode(_apis.map((a) => a.toMap()).toList());
    await _db.setSetting('tts_api_list', jsonStr);
  }

  Future<void> addApi(String name, String urlTemplate) async {
    final newItem = TtsApiItem(
      id: 'custom_${DateTime.now().millisecondsSinceEpoch}',
      name: name,
      urlTemplate: urlTemplate,
      isEnabled: true,
      isDefault: false,
    );
    _apis.add(newItem);
    await _saveApis();
  }

  Future<void> addCustomApi(String name, String urlTemplate) => addApi(name, urlTemplate);

  Future<void> updateApi(String id, String newName, String newUrlTemplate) async {
    final index = _apis.indexWhere((a) => a.id == id);
    if (index >= 0) {
      _apis[index].name = newName;
      _apis[index].urlTemplate = newUrlTemplate;
      await _saveApis();
    }
  }

  Future<void> removeApi(String id) async {
    _apis.removeWhere((a) => a.id == id && !a.isDefault);
    await _saveApis();
  }

  Future<void> deleteApi(String id) => removeApi(id);

  Future<void> toggleApiEnabled(String id, bool val) async {
    final item = _apis.firstWhere((a) => a.id == id, orElse: () => _apis.first);
    item.isEnabled = val;
    await _saveApis();
  }

  Future<void> movePriority(int oldIndex, int newIndex) async {
    if (oldIndex < 0 || oldIndex >= _apis.length || newIndex < 0 || newIndex >= _apis.length) return;
    final item = _apis.removeAt(oldIndex);
    _apis.insert(newIndex, item);
    await _saveApis();
  }

  Future<void> resetToDefaults() async {
    _apis = _getDefaultApis();
    await _saveApis();
  }

  String _buildUrl(String template, String text, {String? reading}) {
    final wordEncoded = Uri.encodeComponent(text);
    final readingEncoded = Uri.encodeComponent((reading != null && reading.isNotEmpty) ? reading : text);
    return template
        .replaceAll('{word}', wordEncoded)
        .replaceAll('{kanji}', wordEncoded)
        .replaceAll('{text}', wordEncoded)
        .replaceAll('{reading}', readingEncoded)
        .replaceAll('{kana}', readingEncoded);
  }

  bool _isValidAudio(List<int> bytes) {
    if (bytes.length <= 512) return false;
    // 过滤 JapanesePod101 查无词条时返回的 52288 字节占位音频
    if (bytes.length == 52288) return false;
    // 过滤 HTML 报错返回文本
    if (bytes.length >= 6) {
      final prefix = String.fromCharCodes(bytes.take(20)).toLowerCase();
      if (prefix.contains('<html') || prefix.contains('<!doc') || prefix.contains('<?xml')) {
        return false;
      }
    }
    return true;
  }

  DateTime? _lastSpeakTime;
  String? _lastSpeakText;

  /// 单独试听指定 API（用于设置界面试听特定音源，即下即播并独立缓存）
  Future<bool> playApi(TtsApiItem api, String text, {String? reading}) async {
    final clean = text.trim();
    if (clean.isEmpty) return false;

    try {
      await _audioChannel.invokeMethod('stop');
    } catch (_) {}

    final cacheDir = await _getCacheDir();
    final hexKey = _toHexKey(clean);
    final testFile = File('${cacheDir.path}/test_${api.id}_$hexKey.mp3');

    // 智能兜底：若针对双参数真人音源(如 JapanesePod101)未显式指定假名，自动使用标准词对
    String actualText = clean;
    String actualReading = (reading != null && reading.isNotEmpty) ? reading : clean;
    if (api.id == 'japanesepod' && (reading == null || reading.isEmpty || reading == clean)) {
      actualText = '桜';
      actualReading = 'さくら';
    }

    final url = _buildUrl(api.urlTemplate, actualText, reading: actualReading);

    try {
      final reqUri = Uri.tryParse(url);
      if (reqUri == null) return false;

      final response = await _httpClient.get(
        reqUri,
        headers: {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
          'Accept': 'audio/*,*/*;q=0.9',
        },
      ).timeout(const Duration(milliseconds: 3000));

      if (response.statusCode == 200 && _isValidAudio(response.bodyBytes)) {
        await testFile.writeAsBytes(response.bodyBytes, flush: true);
        final res = await _audioChannel.invokeMethod<bool>('playUrl', {'url': testFile.path});
        return res == true;
      }
    } catch (_) {}
    return false;
  }

  /// 朗读单词
  /// 借鉴 Yomitan 方案：
  /// 1. 本地缓存优先（0 延迟直接播放本地文件，极为流畅）；
  /// 2. 远端带标准 UA/Accept 请求与数据完整性校验，首选失败立即自动降级顺延到备选音源；
  /// 3. 下载成功后持久化写入本地缓存，避免重复网络开销。
  Future<bool> speak(String text, {String? reading}) async {
    final clean = text.trim();
    if (clean.isEmpty) return false;

    final now = DateTime.now();
    if (_lastSpeakText == clean && _lastSpeakTime != null) {
      final diff = now.difference(_lastSpeakTime!).inMilliseconds;
      if (diff < 300) {
        return false; // 短时间重复触发防抖
      }
    }
    _lastSpeakTime = now;
    _lastSpeakText = clean;

    try {
      await _audioChannel.invokeMethod('stop');
    } catch (_) {}

    await init();
    final cacheDir = await _getCacheDir();
    final hexKey = _toHexKey(clean);

    // 1. 本地音频缓存优先（0 毫秒极速播放）
    final cachedFile = File('${cacheDir.path}/word_$hexKey.mp3');
    if (cachedFile.existsSync() && _isValidAudio(cachedFile.readAsBytesSync())) {
      try {
        final res = await _audioChannel.invokeMethod<bool>('playUrl', {'url': cachedFile.path});
        if (res == true) return true;
      } catch (_) {}
    }

    final enabledApis = _apis.where((a) => a.isEnabled).toList();
    if (enabledApis.isEmpty) return false;

    // 2. 按优先级遍历尝试各个音源
    for (final api in enabledApis) {
      try {
        final url = _buildUrl(api.urlTemplate, clean, reading: reading);
        final reqUri = Uri.tryParse(url);
        if (reqUri == null) continue;

        final response = await _httpClient.get(
          reqUri,
          headers: {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
            'Accept': 'audio/*,*/*;q=0.9',
          },
        ).timeout(const Duration(milliseconds: 2500));

        // 校验返回：200 且属于有效音频（排除空响应、52288占位音或 HTML 404 报错文本）
        if (response.statusCode == 200 && _isValidAudio(response.bodyBytes)) {
          await cachedFile.writeAsBytes(response.bodyBytes, flush: true);
          final res = await _audioChannel.invokeMethod<bool>('playUrl', {'url': cachedFile.path});
          if (res == true) {
            return true;
          }
        }
      } catch (_) {
        // 当前 API 异常或超时，顺延尝试下一个音源
        continue;
      }
    }
    return false;
  }
}
