import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import '../models/word_models.dart';
import 'db_service.dart';
import 'event_service.dart';
import 'fsrs_engine.dart';

enum SyncStatus {
  idle,
  discovering,
  syncing,
  success,
  error,
}

class SyncLogItem {
  final DateTime timestamp;
  final String message;
  final bool isError;

  SyncLogItem(this.message, {this.isError = false}) : timestamp = DateTime.now();

  String get timeFormatted {
    final h = timestamp.hour.toString().padLeft(2, '0');
    final m = timestamp.minute.toString().padLeft(2, '0');
    final s = timestamp.second.toString().padLeft(2, '0');
    return '$h:$m:$s';
  }
}

class SyncService {
  static final SyncService instance = SyncService._internal();
  SyncService._internal();

  final DatabaseService _db = DatabaseService.instance;
  final http.Client _httpClient = http.Client();

  final ValueNotifier<SyncStatus> statusNotifier = ValueNotifier(SyncStatus.idle);
  final ValueNotifier<bool> isPairedNotifier = ValueNotifier(false);
  final ValueNotifier<String?> pairedHostNotifier = ValueNotifier(null);
  final ValueNotifier<String?> pairedPcNameNotifier = ValueNotifier(null);
  final ValueNotifier<DateTime?> lastSyncTimeNotifier = ValueNotifier(null);
  final ValueNotifier<List<SyncLogItem>> syncLogsNotifier = ValueNotifier([]);
  final ValueNotifier<bool> isAutoSyncEnabledNotifier = ValueNotifier(true);
  final ValueNotifier<int> totalRealtimeSyncCountNotifier = ValueNotifier(0);
  final ValueNotifier<bool> isSseConnectedNotifier = ValueNotifier(false);

  String? _pairedToken;
  Timer? _debounceTimer;
  StreamSubscription<String>? _dataEventSub;
  bool _initialized = false;
  bool _isPerformingSync = false;

  // 双端时钟偏差 (Server UTC - Client UTC in ms)
  int _clockSkewMs = 0;

  // SSE 长连接下行广播通道
  HttpClient? _sseHttpClient;
  StreamSubscription? _sseSub;
  Timer? _sseReconnectTimer;
  int _sseRetryBackoffSec = 2;

  /// 初始化服务并加载已持久化的绑定状态与配置
  Future<void> init() async {
    if (_initialized) return;
    _initialized = true;

    _pairedToken = await _db.getSetting('paired_token');
    final host = await _db.getSetting('paired_host');
    final pcName = await _db.getSetting('paired_pc_name');
    final autoSync = await _db.getBoolSetting('auto_sync_enabled', defaultValue: true);

    pairedHostNotifier.value = host;
    pairedPcNameNotifier.value = pcName;
    isPairedNotifier.value = _pairedToken != null && _pairedToken!.isNotEmpty;
    isAutoSyncEnabledNotifier.value = autoSync;

    // 监听全局数据变动，实现打卡/生词操作防抖自动推送
    _dataEventSub = DataEventService.instance.onDataChanged.listen((reason) {
      if (reason != 'sync_finished' && isAutoSyncEnabledNotifier.value && isPairedNotifier.value) {
        triggerAutoSyncDebounced(reason);
      }
    });

    // 若已绑定，立即建立局域网 SSE 待命互联通道（保持在线状态与电脑端识别）
    if (isPairedNotifier.value) {
      _connectSseStream();
    }

    addLog('同步服务已就绪${isPairedNotifier.value ? '（已绑定主机: ${pcName ?? host}）' : '（尚未绑定电脑主机）'}');
  }

  void addLog(String message, {bool isError = false}) {
    final current = List<SyncLogItem>.from(syncLogsNotifier.value);
    current.insert(0, SyncLogItem(message, isError: isError));
    if (current.length > 60) {
      current.removeLast();
    }
    syncLogsNotifier.value = current;
  }

  String formatServerUrl(String rawHost) {
    String host = rawHost.trim();
    if (host.isEmpty) return '';
    if (!host.startsWith('http://') && !host.startsWith('https://')) {
      host = 'http://$host';
    }
    // 智能容错：52081 为 UDP 自动发现专属端口，若误输入为 HTTP 端口，自动纠正为数据同步端口 52080
    if (host.endsWith(':52081')) {
      host = '${host.substring(0, host.length - 6)}:52080';
    } else if (host.contains(':52081/')) {
      host = host.replaceFirst(':52081/', ':52080/');
    }
    if (!host.contains(':', 6)) {
      host = '$host:52080';
    }
    if (host.endsWith('/')) {
      host = host.substring(0, host.length - 1);
    }
    return host;
  }

  /// 局域网自发现电脑端 TampoSyncService (支持全网卡 UDP 广播 + 子网定向广播 + 历史主机 HTTP Ping 竞速)
  Future<Map<String, dynamic>?> discoverHost({Duration timeout = const Duration(milliseconds: 3500)}) async {
    statusNotifier.value = SyncStatus.discovering;
    addLog('正在局域网全网段探测电脑端同步服务...');
    RawDatagramSocket? socket;
    try {
      final completer = Completer<Map<String, dynamic>?>();

      // 1. 尝试并行对已有历史主机发起极速 HTTP Ping 直连竞速
      final candidateHost = pairedHostNotifier.value ?? await _db.getSetting('paired_host');
      if (candidateHost != null && candidateHost.trim().isNotEmpty) {
        final candidateUrl = formatServerUrl(candidateHost.trim());
        _httpClient.get(Uri.parse('$candidateUrl/api/ping')).timeout(const Duration(milliseconds: 1000)).then((res) {
          if (res.statusCode == 200 && !completer.isCompleted) {
            try {
              final pingData = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
              final name = (pingData['device'] ?? pingData['name'] ?? 'PC-HOST').toString();
              final hostParts = candidateUrl.replaceFirst(RegExp(r'^https?:\/\/'), '');
              final info = {
                'ip': hostParts.split(':').first,
                'port': 52080,
                'host': hostParts,
                'name': name,
                'version': (pingData['version'] ?? '1.4.7').toString(),
              };
              completer.complete(info);
            } catch (_) {}
          }
        }).catchError((_) {});
      }

      // 2. 收集局域网广播目标：255.255.255.255 及所有活动 IPv4 接口的子网定向广播
      final broadcastTargets = <InternetAddress>{
        InternetAddress('255.255.255.255'),
      };
      final localSubnets = <String>{};
      try {
        final interfaces = await NetworkInterface.list(type: InternetAddressType.IPv4);
        for (var iface in interfaces) {
          for (var addr in iface.addresses) {
            final ipParts = addr.address.split('.');
            if (ipParts.length == 4 && !addr.isLoopback) {
              localSubnets.add('${ipParts[0]}.${ipParts[1]}.${ipParts[2]}');
              final subnetBcast = '${ipParts[0]}.${ipParts[1]}.${ipParts[2]}.255';
              try {
                broadcastTargets.add(InternetAddress(subnetBcast));
              } catch (_) {}
            }
          }
        }
      } catch (_) {}

      // 3. UDP Socket 初始化与监听
      socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
      socket.broadcastEnabled = true;

      final payload = utf8.encode('{"cmd":"tampo_discover"}');

      void sendBroadcasts() {
        for (var target in broadcastTargets) {
          try {
            socket?.send(payload, target, 52081);
          } catch (_) {}
        }
      }

      // 多轮快速发包防止 WiFi 链路丢包
      sendBroadcasts();
      Timer(const Duration(milliseconds: 250), sendBroadcasts);
      Timer(const Duration(milliseconds: 550), sendBroadcasts);

      socket.listen((event) {
        if (event == RawSocketEvent.read) {
          final dg = socket?.receive();
          if (dg != null) {
            try {
              final str = utf8.decode(dg.data);
              final json = jsonDecode(str) as Map<String, dynamic>;
              if (json['cmd'] == 'tampo_beacon') {
                final ip = dg.address.address;
                final port = json['port'] ?? 52080;
                final pcName = json['name']?.toString() ?? 'TAKETO-PC';
                final info = {
                  'ip': ip,
                  'port': port,
                  'host': '$ip:$port',
                  'name': pcName,
                  'version': json['version']?.toString() ?? '1.4.7',
                };
                if (!completer.isCompleted) {
                  completer.complete(info);
                }
              }
            } catch (_) {}
          }
        }
      });

      // 4. 关键穿透：若 300ms 后 UDP 广播仍未收到响应（如路由器对网线与 WiFi 隔离丢弃 UDP），自动启动子网并发 HTTP 扫描
      Timer(const Duration(milliseconds: 300), () async {
        if (completer.isCompleted) return;
        try {
          for (final subnet in localSubnets) {
            if (completer.isCompleted) break;
            const batchSize = 30;
            for (int i = 1; i <= 254; i += batchSize) {
              if (completer.isCompleted) break;
              final end = min(i + batchSize, 255);
              final batchFutures = <Future>[];
              for (int hostNum = i; hostNum < end; hostNum++) {
                final targetIp = '$subnet.$hostNum';
                final targetUri = Uri.parse('http://$targetIp:52080/api/ping');
                batchFutures.add(
                  _httpClient.get(targetUri).timeout(const Duration(milliseconds: 400)).then((res) {
                    if (res.statusCode == 200 && !completer.isCompleted) {
                      try {
                        final pingData = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
                        if (pingData['service'] == 'TampoSyncService' || pingData['status'] == 'ok') {
                          final name = (pingData['device'] ?? pingData['name'] ?? 'PC-HOST').toString();
                          final info = {
                            'ip': targetIp,
                            'port': 52080,
                            'host': '$targetIp:52080',
                            'name': name,
                            'version': (pingData['version'] ?? '1.4.7').toString(),
                          };
                          completer.complete(info);
                        }
                      } catch (_) {}
                    }
                  }).catchError((_) {}),
                );
              }
              await Future.wait(batchFutures);
            }
          }
        } catch (_) {}
      });

      Timer(timeout, () {
        if (!completer.isCompleted) {
          completer.complete(null);
        }
      });

      final res = await completer.future;
      if (res != null) {
        pairedHostNotifier.value = res['host'];
        pairedPcNameNotifier.value = res['name'];
        await _db.setSetting('paired_host', res['host']);
        await _db.setSetting('paired_pc_name', res['name']);
        addLog('成功连接电脑主机: ${res['name']} (${res['host']})');
        statusNotifier.value = SyncStatus.idle;
        if (isPairedNotifier.value) {
          ensureSseConnected();
        }
        return res;
      } else {
        addLog('未探测到新主机响应，使用当前配置');
        statusNotifier.value = SyncStatus.idle;
        return null;
      }
    } catch (e) {
      addLog('局域网探测异常: $e', isError: true);
      statusNotifier.value = SyncStatus.idle;
      return null;
    } finally {
      socket?.close();
    }
  }

  Future<String> _getOrCreateDeviceId() async {
    var devId = await _db.getSetting('device_id');
    if (devId == null || devId.trim().isEmpty) {
      devId = 'android_${DateTime.now().millisecondsSinceEpoch}_${Random().nextInt(9999)}';
      await _db.setSetting('device_id', devId);
    }
    return devId;
  }

  String _getDeviceModel() {
    try {
      final osVer = Platform.operatingSystemVersion;
      return 'Android ($osVer)';
    } catch (_) {
      return 'Android Device';
    }
  }

  /// 首次绑定配对：输入 6 位临时配对码，换取长效 Token
  Future<bool> pairDevice(String rawHost, String pairingCode, {String? deviceName}) async {
    final host = formatServerUrl(rawHost);
    statusNotifier.value = SyncStatus.syncing;
    addLog('正在向电脑端 ($host) 发起设备配对...');

    try {
      final devId = await _getOrCreateDeviceId();
      final devModel = _getDeviceModel();
      final url = '$host/api/pair';
      final payload = {
        'code': pairingCode.trim(),
        'deviceName': deviceName ?? 'Tampo Android Mobile',
        'deviceId': devId,
        'deviceModel': devModel,
      };

      final res = await _httpClient.post(
        Uri.parse(url),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode(payload),
      ).timeout(const Duration(seconds: 8));

      if (res.statusCode == 200) {
        final data = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
        final token = data['token']?.toString() ?? '';
        if (token.isNotEmpty) {
          _pairedToken = token;
          await _db.setSetting('paired_token', token);
          await _db.setSetting('paired_host', rawHost.trim());
          pairedHostNotifier.value = rawHost.trim();
          isPairedNotifier.value = true;
          statusNotifier.value = SyncStatus.success;
          ensureSseConnected();
          addLog('配对成功！已获得安全信任长效 Token 并建立实时下行通道');
          return true;
        }
      } else if (res.statusCode == 403) {
        addLog('配对失败：配对码无效或已过期 (5分钟有效)', isError: true);
        statusNotifier.value = SyncStatus.error;
        throw Exception('配对码无效或已过期，请在电脑端设置页重新生成');
      } else {
        addLog('配对失败 (HTTP ${res.statusCode})', isError: true);
        statusNotifier.value = SyncStatus.error;
        throw Exception('配对失败: HTTP ${res.statusCode}');
      }
      return false;
    } catch (e) {
      statusNotifier.value = SyncStatus.error;
      addLog('配对发生异常: $e', isError: true);
      rethrow;
    } finally {
      if (statusNotifier.value == SyncStatus.syncing) {
        statusNotifier.value = SyncStatus.idle;
      }
    }
  }

  /// 解除绑定
  Future<void> unpairDevice() async {
    _pairedToken = null;
    await _db.deleteSetting('paired_token');
    isPairedNotifier.value = false;
    _disconnectSse(keepTimer: false);
    addLog('已解除与电脑端的设备信任绑定');
  }

  /// 设置是否开启学习打卡后自动同步
  Future<void> setAutoSyncEnabled(bool enabled) async {
    isAutoSyncEnabledNotifier.value = enabled;
    await _db.setBoolSetting('auto_sync_enabled', enabled);
    ensureSseConnected();
    addLog(enabled ? '已开启学习打卡后自动同步' : '已关闭打卡后自动同步（仍保持局域网待命互联）');
  }

  /// 校准双端时钟偏差
  void _calibrateClockSkew(dynamic serverTimeRaw, dynamic timestampRaw) {
    int? serverMs;
    if (timestampRaw is num) {
      serverMs = timestampRaw.toInt();
    } else if (timestampRaw != null) {
      serverMs = int.tryParse(timestampRaw.toString());
      if (serverMs == null) {
        final dt = DateTime.tryParse(timestampRaw.toString());
        if (dt != null) {
          serverMs = dt.toUtc().millisecondsSinceEpoch;
        }
      }
    }
    if (serverMs == null && serverTimeRaw != null) {
      final dt = DateTime.tryParse(serverTimeRaw.toString());
      if (dt != null) {
        serverMs = dt.toUtc().millisecondsSinceEpoch;
      }
    }
    if (serverMs != null && serverMs > 0) {
      final localNowMs = DateTime.now().toUtc().millisecondsSinceEpoch;
      _clockSkewMs = serverMs - localNowMs;
    }
  }

  /// 确保 SSE 实时待命信道处于连接状态（只要已配对即可待命，确保电脑随时可识别手机）
  void ensureSseConnected() {
    if (!isPairedNotifier.value) return;
    if (isSseConnectedNotifier.value) return;
    _connectSseStream();
  }

  /// 建立到电脑端 TampoSyncService 的轻量级 SSE 广播长连接通道
  Future<void> _connectSseStream() async {
    _disconnectSse(keepTimer: false);

    if (!isPairedNotifier.value) return;
    final host = pairedHostNotifier.value ?? await _db.getSetting('paired_host');
    if (host == null || host.trim().isEmpty || _pairedToken == null || _pairedToken!.isEmpty) {
      return;
    }

    final serverUrl = formatServerUrl(host);
    final devId = await _getOrCreateDeviceId();
    final devModel = Uri.encodeComponent(_getDeviceModel());
    final devName = Uri.encodeComponent('Tampo Android Mobile');
    final uri = Uri.parse('$serverUrl/api/sync/events?deviceId=$devId&deviceName=$devName&deviceModel=$devModel');

    try {
      final client = HttpClient();
      client.connectionTimeout = const Duration(seconds: 10);
      _sseHttpClient = client;

      final request = await client.getUrl(uri);
      request.headers.set('Authorization', 'Bearer $_pairedToken');
      request.headers.set('X-Sync-Token', _pairedToken!);
      request.headers.set('Accept', 'text/event-stream');
      request.headers.set('Cache-Control', 'no-cache');

      final response = await request.close();
      if (response.statusCode != 200) {
        client.close(force: true);
        _scheduleSseReconnect();
        return;
      }

      isSseConnectedNotifier.value = true;
      _sseRetryBackoffSec = 2;

      String currentEvent = '';
      final buffer = StringBuffer();

      _sseSub = response
          .transform(utf8.decoder)
          .transform(const LineSplitter())
          .listen(
        (line) {
          final trimmed = line.trim();
          if (trimmed.isEmpty) {
            if (currentEvent.isNotEmpty && buffer.isNotEmpty) {
              _handleSseEvent(currentEvent, buffer.toString());
            }
            currentEvent = '';
            buffer.clear();
          } else if (trimmed.startsWith(':')) {
            // 心跳保活 (: keep-alive)
          } else if (trimmed.startsWith('event:')) {
            currentEvent = trimmed.substring(6).trim();
          } else if (trimmed.startsWith('data:')) {
            final dataContent = trimmed.substring(5).trim();
            if (buffer.isNotEmpty) buffer.write('\n');
            buffer.write(dataContent);
          }
        },
        onError: (err) {
          isSseConnectedNotifier.value = false;
          _scheduleSseReconnect();
        },
        onDone: () {
          isSseConnectedNotifier.value = false;
          _scheduleSseReconnect();
        },
        cancelOnError: true,
      );
    } catch (e) {
      isSseConnectedNotifier.value = false;
      _scheduleSseReconnect();
    }
  }

  Future<void> _handleSseEvent(String event, String dataStr) async {
    try {
      final data = jsonDecode(dataStr) as Map<String, dynamic>;
      _calibrateClockSkew(data['serverTime'] ?? data['server_time'], data['timestamp']);

      if (data.containsKey('syncEpoch') || data.containsKey('sync_epoch')) {
        final incomingEpoch = (data['syncEpoch'] ?? data['sync_epoch'])?.toString();
        await _checkAndHandleSyncEpoch(incomingEpoch);
      }

      if (event == 'connected') {
        addLog('实时下行信道 (SSE) 已联通，双端进入实时互联状态');
      } else if (event == 'data_changed') {
        addLog('感知到电脑端数据实时变动 (版本: ${data['version'] ?? 0})');
        triggerAutoSyncDebounced('sse_data_changed', 300);
      } else if (event == 'request_push') {
        final devId = await _getOrCreateDeviceId();
        final targetDevId = data['targetDeviceId']?.toString();
        if (targetDevId != null && targetDevId.isNotEmpty && targetDevId != devId) {
          // 定向推送目标不是本机，忽略
          return;
        }
        addLog('收到电脑端主动请求全量数据同步信号，正在全量推送数据至电脑端...');
        performSync(isAuto: false, forceFull: true, triggerReason: 'requested_by_pc');
      }
    } catch (_) {}
  }

  /// 权威纪元检查与冲突重置
  Future<void> _checkAndHandleSyncEpoch(String? incomingEpoch) async {
    if (incomingEpoch == null || incomingEpoch.trim().isEmpty) return;
    final storedEpoch = await _db.getSetting('sync_epoch');
    if (storedEpoch != null && storedEpoch.isNotEmpty && storedEpoch != incomingEpoch) {
      addLog('检测到电脑端执行了数据维护/恢复操作 (纪元重置: $storedEpoch -> $incomingEpoch)，执行权威全量覆盖同步以消除冲突');
      // 1. 废弃本地增量基准
      await _db.setSetting('last_sync_time', '0');
      lastSyncTimeNotifier.value = null;
      // 2. 清空本地墓碑，防止旧删除指令反向杀死刚恢复的单词
      await _db.clearTombstones();
      // 3. 记录新纪元
      await _db.setSetting('sync_epoch', incomingEpoch);
      // 4. 执行权威向下覆盖拉取（不向服务端推脏数据）
      await performAuthoritativeDownwardSync();
      return;
    }
    if (storedEpoch == null || storedEpoch.isEmpty) {
      await _db.setSetting('sync_epoch', incomingEpoch);
      await performAuthoritativeDownwardSync();
    }
  }

  /// 权威向下覆盖同步：只从电脑端拉取全量覆盖本地，绝不向电脑端推回任何本地旧状态
  Future<SyncReport> performAuthoritativeDownwardSync() async {
    final host = pairedHostNotifier.value ?? await _db.getSetting('paired_host');
    if (host == null || host.trim().isEmpty) {
      return SyncReport(syncedLists: 0, insertedWords: 0, updatedWords: 0, syncedLogs: 0);
    }
    final serverUrl = formatServerUrl(host);
    final headers = <String, String>{
      'Content-Type': 'application/json',
    };
    if (_pairedToken != null && _pairedToken!.isNotEmpty) {
      headers['Authorization'] = 'Bearer $_pairedToken';
      headers['X-Sync-Token'] = _pairedToken!;
    }

    final pullUrl = '$serverUrl/api/sync/pull';
    final pullRes = await _httpClient.post(Uri.parse(pullUrl), headers: headers).timeout(const Duration(seconds: 30));
    if (pullRes.statusCode != 200) {
      throw Exception('权威拉取数据失败 (HTTP ${pullRes.statusCode})');
    }

    final pullData = jsonDecode(utf8.decode(pullRes.bodyBytes)) as Map<String, dynamic>;
    final report = await _mergeIncomingData(pullData, forceFull: true, isAuthoritativeEpoch: true);
    lastSyncTimeNotifier.value = DateTime.now();
    totalRealtimeSyncCountNotifier.value += 1;
    statusNotifier.value = SyncStatus.success;
    addLog('权威数据对齐完成！(${report.summary})');
    DataEventService.instance.notify('sync_finished');
    return report;
  }

  void _scheduleSseReconnect() {
    _sseReconnectTimer?.cancel();
    if (!isPairedNotifier.value) return;

    final delaySec = _sseRetryBackoffSec;
    _sseRetryBackoffSec = min(_sseRetryBackoffSec * 2, 30);
    _sseReconnectTimer = Timer(Duration(seconds: delaySec), () {
      _connectSseStream();
    });
  }

  void _disconnectSse({bool keepTimer = false}) {
    if (!keepTimer) {
      _sseReconnectTimer?.cancel();
      _sseReconnectTimer = null;
    }
    _sseSub?.cancel();
    _sseSub = null;
    try {
      _sseHttpClient?.close(force: true);
    } catch (_) {}
    _sseHttpClient = null;
    isSseConnectedNotifier.value = false;
  }

  /// 测试连通性
  Future<bool> ping(String rawHost) async {
    try {
      final url = '${formatServerUrl(rawHost)}/api/ping';
      final res = await _httpClient.get(Uri.parse(url)).timeout(const Duration(seconds: 4));
      if (res.statusCode == 200) {
        try {
          final pingData = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
          _calibrateClockSkew(pingData['server_time'] ?? pingData['serverTime'], pingData['timestamp']);
        } catch (_) {}
        return true;
      }
      return false;
    } catch (_) {
      return false;
    }
  }

  /// 触发防抖自动同步 (默认 1.5 秒防抖，SSE 广播可传更低延迟)
  void triggerAutoSyncDebounced([String reason = 'data_changed', int debounceMs = 1500]) {
    _debounceTimer?.cancel();
    _debounceTimer = Timer(Duration(milliseconds: debounceMs), () {
      performSync(isAuto: true, triggerReason: reason);
    });
  }

  /// 应用启动或唤醒时的静默心跳同步
  Future<void> checkAndPerformAutoSync() async {
    if (!isAutoSyncEnabledNotifier.value || !isPairedNotifier.value) return;
    ensureSseConnected();
    final lastTime = lastSyncTimeNotifier.value;
    if (lastTime != null && DateTime.now().difference(lastTime).inSeconds < 15) {
      return; // 15秒冷却，避免前台切换过于频繁
    }
    await performSync(isAuto: true, triggerReason: 'app_active');
  }

  /// 核心双向同步方法 (统一支持自动静默与手动模式，支持增量与全量)
  Future<SyncReport> performSync({
    bool isAuto = false,
    String? host,
    String? pin,
    String? triggerReason,
    bool forceFull = false,
  }) async {
    if (_isPerformingSync) {
      if (isAuto) {
        return SyncReport(syncedLists: 0, insertedWords: 0, updatedWords: 0, syncedLogs: 0);
      }
      throw Exception('当前同步任务正在进行中，请稍候');
    }

    _isPerformingSync = true;
    statusNotifier.value = SyncStatus.syncing;

    try {
      // 确定目标主机
      String effectiveHost = (host != null && host.trim().isNotEmpty)
          ? host.trim()
          : (pairedHostNotifier.value ?? '');

      if (effectiveHost.isEmpty) {
        // 尝试自动 UDP 发现
        final discovered = await discoverHost();
        if (discovered != null) {
          effectiveHost = discovered['host'];
        }
      }

      if (effectiveHost.isEmpty) {
        throw Exception('未指定电脑端 IP 且局域网探测未发现主机');
      }

      final serverUrl = formatServerUrl(effectiveHost);
      addLog(isAuto ? '触发实时自动同步 ($triggerReason)...' : '正在执行双向数据同步...');

      // 构建请求头（优先使用长效 Token，兼容 PIN 码）
      final headers = <String, String>{
        'Content-Type': 'application/json',
      };
      if (_pairedToken != null && _pairedToken!.isNotEmpty) {
        headers['Authorization'] = 'Bearer $_pairedToken';
        headers['X-Sync-Token'] = _pairedToken!;
      } else if (pin != null && pin.trim().isNotEmpty) {
        headers['X-Sync-Pin'] = pin.trim();
      }

      bool hasExecutedAuthoritativeEpoch = false;

      Future<void> pushLocalData() async {
        if (hasExecutedAuthoritativeEpoch) {
          return;
        }

        final pushUrl = '$serverUrl/api/sync/push';
        final lastSync = lastSyncTimeNotifier.value;
        final bool isIncremental = !forceFull && lastSync != null;
        final DateTime? sinceTime = isIncremental
            ? lastSync.subtract(const Duration(seconds: 5))
            : null;

        final localWords = sinceTime != null
            ? await _db.getWordsModifiedSince(sinceTime)
            : await _db.getAllWords();
        final localLists = sinceTime != null
            ? await _db.getWordListsModifiedSince(sinceTime)
            : await _db.getAllWordLists();
        final localLogs = sinceTime != null
            ? await _db.getReviewLogsModifiedSince(sinceTime)
            : await _db.getAllReviewLogs();
        final localTombstones = sinceTime != null
            ? await _db.getTombstonesModifiedSince(sinceTime)
            : await _db.getAllTombstones();

        // 增量优化：如果本地没有任何变动的脏数据，直接跳过推送，极大减轻网络和序列化开销
        if (isIncremental &&
            localWords.isEmpty &&
            localLists.isEmpty &&
            localLogs.isEmpty &&
            localTombstones.isEmpty) {
          return;
        }

        final currentEpoch = await _db.getSetting('sync_epoch');
        final pushPayload = {
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
          'syncEpoch': currentEpoch,
        };

        final pushRes = await _httpClient.post(
          Uri.parse(pushUrl),
          headers: headers,
          body: jsonEncode(pushPayload),
        ).timeout(const Duration(seconds: 60));

        if (pushRes.statusCode == 503) {
          addLog('电脑端正在进行数据维护与备份还原，同步锁定中');
          return;
        }
        if (pushRes.statusCode == 409) {
          final errData = jsonDecode(utf8.decode(pushRes.bodyBytes)) as Map<String, dynamic>;
          final newEpoch = (errData['syncEpoch'] ?? errData['sync_epoch'])?.toString();
          addLog('电脑端已进入全新纪元，拒绝旧数据推送，准备执行权威向下同步对齐');
          hasExecutedAuthoritativeEpoch = true;
          await _checkAndHandleSyncEpoch(newEpoch);
          return;
        }
        if (pushRes.statusCode != 200) {
          throw Exception('推送数据失败 (HTTP ${pushRes.statusCode})');
        }
      }

      Future<SyncReport> pullRemoteData() async {
        final lastSync = lastSyncTimeNotifier.value;
        final bool isIncremental = !forceFull && lastSync != null;
        String pullUrl = '$serverUrl/api/sync/pull';
        if (isIncremental) {
          final sinceMs = lastSync.subtract(const Duration(seconds: 5)).millisecondsSinceEpoch;
          pullUrl = '$serverUrl/api/sync/pull?since=$sinceMs';
        }

        final pullRes = await _httpClient.post(
          Uri.parse(pullUrl),
          headers: headers,
        ).timeout(const Duration(seconds: 15));

        if (pullRes.statusCode == 503) {
          addLog('电脑端正在进行数据维护与备份还原，同步锁定中');
          return SyncReport(syncedLists: 0, insertedWords: 0, updatedWords: 0, syncedLogs: 0);
        }
        if (pullRes.statusCode == 401 || pullRes.statusCode == 403) {
          if (_pairedToken != null) {
            await unpairDevice();
          }
          throw Exception('认证未通过，电脑端已解除绑定或配对码无效');
        }
        if (pullRes.statusCode != 200) {
          throw Exception('拉取数据失败 (HTTP ${pullRes.statusCode})');
        }

        final pullData = jsonDecode(utf8.decode(pullRes.bodyBytes)) as Map<String, dynamic>;
        bool isAuthoritativeEpoch = false;
        if (pullData.containsKey('syncEpoch') || pullData.containsKey('sync_epoch')) {
          final epoch = (pullData['syncEpoch'] ?? pullData['sync_epoch'])?.toString();
          if (epoch != null && epoch.isNotEmpty) {
            final storedEpoch = await _db.getSetting('sync_epoch');
            if (storedEpoch != null && storedEpoch.isNotEmpty && storedEpoch != epoch) {
              addLog('检测到电脑端恢复了历史快照 (纪元更新: $epoch)，执行权威全量覆盖');
              await _db.clearTombstones();
              await _db.setSetting('sync_epoch', epoch);
              await _db.setSetting('last_sync_time', '0');
              lastSyncTimeNotifier.value = null;
              isAuthoritativeEpoch = true;
              hasExecutedAuthoritativeEpoch = true;
            } else if (storedEpoch == null || storedEpoch.isEmpty) {
              await _db.setSetting('sync_epoch', epoch);
            }
          }
        }
        if (pullData.containsKey('timestamp')) {
          _calibrateClockSkew(pullData['serverTime'], pullData['timestamp']);
        }
        return await _mergeIncomingData(pullData, forceFull: forceFull || isAuthoritativeEpoch, isAuthoritativeEpoch: isAuthoritativeEpoch);
      }

      SyncReport report;
      if (triggerReason == 'sse_data_changed' || triggerReason == 'sse_event') {
        // 接收到电脑端 SSE 下行广播通知：单向拉取合并电脑端变动，严禁反向推回电脑，彻底切断乒乓无限回音
        report = await pullRemoteData();
      } else {
        final bool pushFirst = (triggerReason != null &&
            triggerReason != 'app_active' &&
            triggerReason != 'timer');
        if (pushFirst) {
          // 本地数据刚刚产生变动（打卡复习、标记状态等）：先将最新修改推送至电脑端落盘合并，再拉取对齐
          await pushLocalData();
          if (hasExecutedAuthoritativeEpoch) {
            report = SyncReport(syncedLists: 0, insertedWords: 0, updatedWords: 0, syncedLogs: 0);
          } else {
            report = await pullRemoteData();
          }
        } else {
          // 常规心跳或手动同步：先拉取远端更新合并，再将本地最新合并结果推回电脑
          report = await pullRemoteData();
          if (!hasExecutedAuthoritativeEpoch) {
            await pushLocalData();
          }
        }
      }

      lastSyncTimeNotifier.value = DateTime.now();
      totalRealtimeSyncCountNotifier.value += 1;
      statusNotifier.value = SyncStatus.success;

      addLog('实时同步成功！(${report.summary})');
      DataEventService.instance.notify('sync_finished');

      return report;
    } catch (e) {
      statusNotifier.value = SyncStatus.error;
      addLog('同步失败: $e', isError: true);
      if (!isAuto) {
        rethrow;
      }
      return SyncReport(syncedLists: 0, insertedWords: 0, updatedWords: 0, syncedLogs: 0);
    } finally {
      _isPerformingSync = false;
      Future.delayed(const Duration(seconds: 3), () {
        if (statusNotifier.value == SyncStatus.success || statusNotifier.value == SyncStatus.error) {
          statusNotifier.value = SyncStatus.idle;
        }
      });
    }
  }

  /// 兼容旧版调用方法：拉取全部数据
  Future<SyncReport> pullFromPC(String rawHost, String pin) async {
    final serverUrl = formatServerUrl(rawHost);
    final url = '$serverUrl/api/sync/pull';

    final headers = <String, String>{
      'Content-Type': 'application/json',
    };
    if (_pairedToken != null && _pairedToken!.isNotEmpty) {
      headers['Authorization'] = 'Bearer $_pairedToken';
      headers['X-Sync-Token'] = _pairedToken!;
    } else {
      headers['X-Sync-Pin'] = pin.trim();
    }

    final res = await _httpClient.post(
      Uri.parse(url),
      headers: headers,
    ).timeout(const Duration(seconds: 15));

    if (res.statusCode == 401 || res.statusCode == 403) {
      throw Exception('PIN 码或凭据校验失败，请检查电脑端显示的配对码');
    }

    if (res.statusCode != 200) {
      throw Exception('拉取失败 (HTTP ${res.statusCode}): ${res.body}');
    }

    final data = jsonDecode(utf8.decode(res.bodyBytes)) as Map<String, dynamic>;
    final report = await _mergeIncomingData(data, forceFull: true);
    lastSyncTimeNotifier.value = DateTime.now();
    statusNotifier.value = SyncStatus.success;
    addLog('手动拉取完成 (${report.summary})');
    DataEventService.instance.notify('sync_finished');
    return report;
  }

  /// 兼容旧版调用方法：双向数据同步
  Future<SyncReport> biDirectionalSync(String rawHost, String pin) async {
    return await performSync(isAuto: false, host: rawHost, pin: pin);
  }

  String _formatTimeKey(DateTime dt) {
    final d = dt.toLocal();
    return '${d.year}${d.month.toString().padLeft(2, '0')}${d.day.toString().padLeft(2, '0')}'
        '${d.hour.toString().padLeft(2, '0')}${d.minute.toString().padLeft(2, '0')}${d.second.toString().padLeft(2, '0')}';
  }

  String _formatDayKey(DateTime dt) {
    final d = FsrsEngine.getLogicalDate(dt);
    return '${d.year}${d.month.toString().padLeft(2, '0')}${d.day.toString().padLeft(2, '0')}';
  }

  /// 双向合并接收到的数据 (支持基于 affectedWordIds 的针对性 FSRS 快速重放与权威纪元恢复)
  Future<SyncReport> _mergeIncomingData(Map<String, dynamic> data, {bool forceFull = false, bool isAuthoritativeEpoch = false}) async {
    int updatedWords = 0;
    int insertedWords = 0;
    int syncedLists = 0;
    int syncedLogs = 0;
    final affectedWordIds = <int>{};

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
    if (incomingTombstones.isNotEmpty) {
      final tombWords = await _db.getAllWords();
      final tombWordsByKey = <String, List<Word>>{};
      for (final w in tombWords) {
        tombWordsByKey.putIfAbsent(w.text.trim().toLowerCase(), () => []).add(w);
      }

      final tombLists = await _db.getAllWordLists();
      final tombListsByKey = <String, List<WordList>>{};
      for (final l in tombLists) {
        tombListsByKey.putIfAbsent(l.name.trim().toLowerCase(), () => []).add(l);
      }

      for (var item in incomingTombstones) {
        if (item is Map<String, dynamic>) {
          final entityType = (item['entityType'] ?? item['EntityType'])?.toString() ?? '';
          final entityKey = ((item['entityKey'] ?? item['EntityKey'])?.toString() ?? '').trim().toLowerCase();
          if (entityKey.isEmpty) continue;
          final tombDeletedAt = DateTime.tryParse((item['deletedAt'] ?? item['DeletedAt'])?.toString() ?? '')?.toLocal();

          if (entityType == 'word') {
            final match = tombWordsByKey[entityKey] ?? const <Word>[];
            DateTime? matchTime;
            if (match.isNotEmpty) {
              final w = match.first;
              matchTime = w.stateUpdatedAt.isAfter(w.metaUpdatedAt)
                  ? (w.stateUpdatedAt.isAfter(w.createdAt) ? w.stateUpdatedAt : w.createdAt)
                  : (w.metaUpdatedAt.isAfter(w.createdAt) ? w.metaUpdatedAt : w.createdAt);
            }

            if (matchTime == null || tombDeletedAt == null || !tombDeletedAt.isBefore(matchTime.subtract(const Duration(seconds: 1)))) {
              deletedWordTimes[entityKey] = tombDeletedAt;
              await _db.addTombstone('word', entityKey, deletedAt: tombDeletedAt);
              for (var w in match) {
                await _db.deleteWord(w.id);
              }
              tombWordsByKey.remove(entityKey);
            } else {
              deletedWordTimes.remove(entityKey);
              await _db.removeTombstone('word', entityKey);
            }
          } else if (entityType == 'word_list') {
            final match = tombListsByKey[entityKey] ?? const <WordList>[];
            final matchTime = match.isNotEmpty
                ? (match.first.updatedAt.isAfter(match.first.createdAt) ? match.first.updatedAt : match.first.createdAt)
                : null;

            if (matchTime == null || tombDeletedAt == null || !tombDeletedAt.isBefore(matchTime.subtract(const Duration(seconds: 1)))) {
              deletedListTimes[entityKey] = tombDeletedAt;
              await _db.addTombstone('word_list', entityKey, deletedAt: tombDeletedAt);
              for (var l in match) {
                await _db.deleteWordList(l.id);
              }
              tombListsByKey.remove(entityKey);
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
              final wMatch = tombWordsByKey[wText] ?? const <Word>[];
              if (wMatch.isNotEmpty) {
                final word = wMatch.first;
                final logs = await _db.getReviewLogs(word.id);
                for (var l in logs) {
                  final lTime = _formatTimeKey(l.reviewDate);
                  if (lTime == timePart) {
                    await _db.revertTodayReview(l.id, isSyncRevert: true);
                    affectedWordIds.add(word.id);
                    break;
                  }
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

    if (isAuthoritativeEpoch) {
      final incomingListKeys = incomingLists.map((l) => l.name.trim().toLowerCase()).toSet();
      final db = await _db.database;
      for (var local in localLists) {
        final key = local.name.trim().toLowerCase();
        if (!incomingListKeys.contains(key)) {
          if (local.id > 0) {
            await db.delete('word_lists', where: 'id = ?', whereArgs: [local.id]);
          }
          localListMap.remove(key);
        }
      }
    }

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

    if (isAuthoritativeEpoch) {
      final incomingWordKeys = incomingWords.map((w) => w.text.trim().toLowerCase()).toSet();
      final db = await _db.database;
      for (var local in localWords) {
        final key = local.text.trim().toLowerCase();
        if (!incomingWordKeys.contains(key)) {
          if (local.id > 0) {
            await db.delete('words', where: 'id = ?', whereArgs: [local.id]);
            await db.delete('review_logs', where: 'wordId = ?', whereArgs: [local.id]);
          }
          localWordMap.remove(key);
        }
      }
    }

    for (var inWord in incomingWords) {
      final key = inWord.text.trim().toLowerCase();
      if (key.isEmpty) continue;

      if (deletedWordTimes.containsKey(key)) {
        final delTime = deletedWordTimes[key];
        final inWordMaxTime = inWord.stateUpdatedAt.isAfter(inWord.metaUpdatedAt)
            ? (inWord.stateUpdatedAt.isAfter(inWord.createdAt) ? inWord.stateUpdatedAt : inWord.createdAt)
            : (inWord.metaUpdatedAt.isAfter(inWord.createdAt) ? inWord.metaUpdatedAt : inWord.createdAt);
        if (delTime == null || !delTime.isBefore(inWordMaxTime.subtract(const Duration(seconds: 1)))) {
          continue;
        }
        deletedWordTimes.remove(key);
        await _db.removeTombstone('word', key);
      }

      if (localWordMap.containsKey(key)) {
        final local = localWordMap[key]!;
        bool needUpdate = false;
        final incomingState = inWord.state == 4 ? WordLearningState.mastered.value : inWord.state;

        // 【维度 1：学习算法与复习进度状态】
        // 防御铁律：本地已有学习记录（reps > 0 或不是新词），绝对禁止被对端尚未学习的初始新词状态（reps == 0 && state == New）覆盖！
        final inStateUtc = inWord.stateUpdatedAt.toUtc().subtract(Duration(milliseconds: _clockSkewMs));
        final localStateUtc = local.stateUpdatedAt.toUtc();
        final localHasProgress = local.reps > 0 || local.state != WordLearningState.newWord.value;
        final inHasProgress = inWord.reps > 0 || incomingState != WordLearningState.newWord.value;

        if (isAuthoritativeEpoch) {
          // 权威纪元恢复：服务端快照处于绝对权威地位，无条件采纳服务端状态
          local.state = incomingState;
          local.stability = inWord.stability;
          local.difficulty = inWord.difficulty;
          local.reps = inWord.reps;
          local.lapses = inWord.lapses;
          local.lastReviewDate = inWord.lastReviewDate;
          local.nextReviewDate = inWord.nextReviewDate;
          local.stateUpdatedAt = inWord.stateUpdatedAt;
          needUpdate = true;
        } else if (incomingState == WordLearningState.mastered.value && local.state != WordLearningState.mastered.value) {
          local.state = WordLearningState.mastered.value;
          local.nextReviewDate = null;
          if (inWord.reps > local.reps) {
            local.stability = inWord.stability;
            local.difficulty = inWord.difficulty;
            local.reps = inWord.reps;
            local.lapses = inWord.lapses;
            local.lastReviewDate = inWord.lastReviewDate;
          }
          local.stateUpdatedAt = inWord.stateUpdatedAt;
          needUpdate = true;
        } else if (inHasProgress && !localHasProgress) {
          // 本地无学习进度，对端有进度 -> 全量采纳对端学习进度
          local.state = incomingState;
          local.stability = inWord.stability;
          local.difficulty = inWord.difficulty;
          local.reps = inWord.reps;
          local.lapses = inWord.lapses;
          local.lastReviewDate = inWord.lastReviewDate;
          local.nextReviewDate = inWord.nextReviewDate;
          local.stateUpdatedAt = inWord.stateUpdatedAt;
          needUpdate = true;
        } else if (inHasProgress && localHasProgress) {
          // 双端均有复习进度：采纳更高复习次数或更新时间戳
          if (inStateUtc.isAfter(localStateUtc.add(const Duration(seconds: 1))) || inWord.reps > local.reps) {
            local.state = incomingState;
            local.stability = inWord.stability;
            local.difficulty = inWord.difficulty;
            local.reps = max(local.reps, inWord.reps);
            local.lapses = max(local.lapses, inWord.lapses);
            local.lastReviewDate = (inWord.lastReviewDate != null && local.lastReviewDate != null)
                ? (inWord.lastReviewDate!.isAfter(local.lastReviewDate!) ? inWord.lastReviewDate : local.lastReviewDate)
                : (inWord.lastReviewDate ?? local.lastReviewDate);
            local.nextReviewDate = inWord.nextReviewDate ?? local.nextReviewDate;
            local.stateUpdatedAt = inWord.stateUpdatedAt;
            needUpdate = true;
          }
        }

        // 【维度 2：词单归属与元数据，由 metaUpdatedAt 独立裁决】
        // 防御铁律：远端若没有词单（wordListName 为空），且本地已有词单，绝不轻易清空本地词单，除非远端经历明确移出词单操作
        final inMetaUtc = inWord.metaUpdatedAt.toUtc().subtract(Duration(milliseconds: _clockSkewMs));
        final localMetaUtc = local.metaUpdatedAt.toUtc();
        final inHasList = inWord.wordListName != null && inWord.wordListName!.trim().isNotEmpty;
        final localHasList = local.isInList && (local.wordListName != null || local.wordListId != null);

        if (isAuthoritativeEpoch) {
          if (inHasList && localListMap.containsKey(inWord.wordListName!.trim().toLowerCase())) {
            final mappedList = localListMap[inWord.wordListName!.trim().toLowerCase()]!;
            local.wordListId = mappedList.id;
            local.wordListName = mappedList.name;
            local.isInList = true;
          } else {
            local.wordListId = null;
            local.wordListName = null;
            local.isInList = false;
          }
          local.metaUpdatedAt = inWord.metaUpdatedAt;
          needUpdate = true;
        } else if (inHasList) {
          if (localListMap.containsKey(inWord.wordListName!.trim().toLowerCase())) {
            final mappedList = localListMap[inWord.wordListName!.trim().toLowerCase()]!;
            if (local.wordListId != mappedList.id || local.wordListName != mappedList.name || !local.isInList) {
              local.wordListId = mappedList.id;
              local.wordListName = mappedList.name;
              local.isInList = true;
              local.metaUpdatedAt = inWord.metaUpdatedAt;
              needUpdate = true;
            }
          }
        } else if (localHasList) {
          final isExplicitUnlist = inWord.metaUpdatedAt.isAfter(inWord.createdAt.add(const Duration(seconds: 2))) &&
              inMetaUtc.isAfter(localMetaUtc.add(const Duration(seconds: 1)));
          if (isExplicitUnlist) {
            local.wordListId = null;
            local.wordListName = null;
            local.isInList = false;
            local.metaUpdatedAt = inWord.metaUpdatedAt;
            needUpdate = true;
          }
        }

        if (needUpdate) {
          await _db.updateWord(local);
          affectedWordIds.add(local.id);
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
        final newId = await _db.insertWord(inWord);
        inWord.id = newId;
        localWordMap[key] = inWord;
        affectedWordIds.add(newId);
        insertedWords++;
      }
    }

    // 3. 增量合并或原地更新复习日志（按 wordId + 自然日 yyyyMMdd 唯一归并）
    final incomingLogs = (data['reviewLogs'] as List? ?? [])
        .map((item) => ReviewLog.fromMap(item as Map<String, dynamic>))
        .toList();

    if (isAuthoritativeEpoch) {
      // 权威纪元恢复：清空本地残留的所有非快照复习日志，严格保证日志与电脑端快照 1:1 完全一致！
      final db = await _db.database;
      await db.delete('review_logs');
    }

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

      await db.transaction((txn) async {
        for (var log in incomingLogs) {
          final wordTextKey = (log.wordText != null && log.wordText!.trim().isNotEmpty)
              ? log.wordText!.trim().toLowerCase()
              : inWordMap[log.wordId];
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
                affectedWordIds.add(localWordId);
                syncedLogs++;
              } else {
                // 同一自然日已存在打卡记录：以较新时间戳为准原地更新参数，严禁伪造删除墓碑
                if (localLogDate.isAfter(existingLog.reviewDate) ||
                    (localLogDate.isAtSameMomentAs(existingLog.reviewDate) &&
                        (existingLog.rating != log.rating ||
                            (log.stability > 0 && (existingLog.stability - log.stability).abs() > 0.0001) ||
                            (log.difficulty > 0 && (existingLog.difficulty - log.difficulty).abs() > 0.0001)))) {
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
                  affectedWordIds.add(localWordId);
                }
              }
            }
          }
        }
      });
    }

    // 4. 重放并对齐 FSRS 状态 (根据日志与自然日排期回溯)
    // 性能关键优化：仅对本批次变动所波及的生词 (affectedWordIds) 进行回溯，彻底消除全库成千上万次 SQLite 查询卡顿
    // 若为权威纪元恢复 (isAuthoritativeEpoch)，单词与日志已精准 1:1 采纳电脑快照权威值，无需再次重放模拟
    if (!isAuthoritativeEpoch) {
      final db = await _db.database;
      final List<Word> targetWords;
      if (forceFull) {
        targetWords = await _db.getAllWords();
      } else if (affectedWordIds.isNotEmpty) {
        targetWords = await _db.getWordsByIds(affectedWordIds.toList());
      } else {
        targetWords = const <Word>[];
      }

      for (final w in targetWords) {
        final logsData = await db.query(
          'review_logs',
          where: 'wordId = ?',
          whereArgs: [w.id],
          orderBy: 'reviewDate ASC',
        );
        final wordLogs = logsData.map((m) => ReviewLog.fromMap(m)).toList();

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

        // 如果没有任何打卡日志，跳过重演；只要拥有日志，无论当前 state 是什么都必须参与自愈校验
        if (dedupedLogs.isEmpty) continue;

        bool needsReplay = w.reps != dedupedLogs.length ||
            w.state == WordLearningState.newWord.value ||
            w.lapses != dedupedLogs.where((l) => l.rating == 1).length;
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
            createdAt: w.createdAt,
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
          w.stateUpdatedAt = simWord.lastReviewDate ?? DateTime.now();
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
    }

    // 5. 清理超过 90 天的过期墓碑
    await _db.cleanupExpiredTombstones(retentionDays: 90);
    if (isAuthoritativeEpoch) {
      await _db.clearTombstones();
    }

    return SyncReport(
      syncedLists: syncedLists,
      insertedWords: insertedWords,
      updatedWords: updatedWords,
      syncedLogs: syncedLogs,
    );
  }

  /// 释放相关流监听、定时器与网络连接
  void dispose() {
    _dataEventSub?.cancel();
    _debounceTimer?.cancel();
    _sseReconnectTimer?.cancel();
    _sseSub?.cancel();
    _sseHttpClient?.close(force: true);
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
