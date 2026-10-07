import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'services/event_service.dart';
import 'theme/app_theme.dart';
import 'views/words_page.dart';
import 'views/study_page.dart';
import 'views/today_page.dart';
import 'views/import_sync_page.dart';

import 'services/theme_service.dart';
import 'services/tts_service.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await ThemeService.instance.init();
  await TtsService.instance.init();

  SystemChrome.setSystemUIOverlayStyle(
    const SystemUiOverlayStyle(
      statusBarColor: Colors.transparent,
      statusBarIconBrightness: Brightness.dark,
    ),
  );
  runApp(const TampoApp());
}

class TampoApp extends StatelessWidget {
  const TampoApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ValueListenableBuilder<ThemeMode>(
      valueListenable: ThemeService.instance.themeModeNotifier,
      builder: (context, themeMode, _) {
        return MaterialApp(
          title: 'Tampo',
          debugShowCheckedModeBanner: false,
          theme: AppTheme.lightTheme,
          darkTheme: AppTheme.darkTheme,
          themeMode: themeMode,
          home: const MainPage(),
        );
      },
    );
  }
}

class MainPage extends StatefulWidget {
  const MainPage({super.key});

  @override
  State<MainPage> createState() => _MainPageState();
}

class _MainPageState extends State<MainPage> with WidgetsBindingObserver {
  int _currentIndex = 0;

  final List<Widget> _pages = const [
    WordsPage(),
    StudyPage(),
    TodayPage(),
    ImportSyncPage(),
  ];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      DataEventService.instance.notify('app_resumed');
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.detached) {
      TtsService.instance.handleExitCleanup();
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(
        index: _currentIndex,
        children: _pages,
      ),
      bottomNavigationBar: BottomNavigationBar(
        currentIndex: _currentIndex,
        onTap: (idx) {
          setState(() => _currentIndex = idx);
          DataEventService.instance.notify('tab_switch');
        },
        items: const [
          BottomNavigationBarItem(
            icon: Icon(Icons.menu_book_outlined),
            activeIcon: Icon(Icons.menu_book),
            label: '词库',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.school_outlined),
            activeIcon: Icon(Icons.school),
            label: '学习',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.today_outlined),
            activeIcon: Icon(Icons.today),
            label: '今日',
          ),
          BottomNavigationBarItem(
            icon: Icon(Icons.sync_alt_outlined),
            activeIcon: Icon(Icons.sync_alt),
            label: '导入/同步',
          ),
        ],
      ),
    );
  }
}
