import 'package:flutter/material.dart';
import 'db_service.dart';

class ThemeService {
  static final ThemeService instance = ThemeService._internal();
  ThemeService._internal();

  final DatabaseService _db = DatabaseService.instance;
  final ValueNotifier<ThemeMode> themeModeNotifier = ValueNotifier<ThemeMode>(ThemeMode.light);

  ThemeMode get currentThemeMode => themeModeNotifier.value;

  Future<void> init() async {
    final saved = await _db.getSetting('app_theme_mode');
    if (saved == 'dark') {
      themeModeNotifier.value = ThemeMode.dark;
    } else if (saved == 'light') {
      themeModeNotifier.value = ThemeMode.light;
    } else if (saved == 'system') {
      themeModeNotifier.value = ThemeMode.system;
    } else {
      // 默认使用单色明亮主题
      themeModeNotifier.value = ThemeMode.light;
    }
  }

  Future<void> setThemeMode(ThemeMode mode) async {
    themeModeNotifier.value = mode;
    String val = 'light';
    if (mode == ThemeMode.dark) val = 'dark';
    if (mode == ThemeMode.system) val = 'system';
    await _db.setSetting('app_theme_mode', val);
  }
}
