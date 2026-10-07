import 'dart:async';

/// 全局数据更新事件总线
class DataEventService {
  static final DataEventService instance = DataEventService._internal();
  DataEventService._internal();

  final _streamController = StreamController<String>.broadcast();

  Stream<String> get onDataChanged => _streamController.stream;

  void notify(String reason) {
    _streamController.add(reason);
  }
}
