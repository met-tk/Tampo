# Tampo Android (日语词汇宝库移动端) - 上下文与架构交接文档

> **项目名称**：Tampo Android (日语词汇宝库移动端)  
> **基线源头**：移植自 Windows 电脑端原版 Tampo (WinUI 3 / .NET 8 / C#)  
> **当前版本**：v1.2.3 (纯净版本，无构建号)  
> **制作人署名**：AI-driven by Taketo  
> **设计哲学**：黑白极简主义（Black & White Minimalism），零多余 Emoji，极速响应，离线优先。

---

## 1. 项目定位与背景

本项目是将原 Windows 桌面端高效日语词汇记忆软件 **Tampo** 完整移植为可在 Android 设备（手机/平板/墨水屏）上原生运行的移动应用。
移动端与电脑端拥有**完全等价的核心数据模型与双向同步协议**，支持在家庭或办公局域网内通过 UDP 广播自发现，并基于 TCP 实现一键无感增量双向同步。

---

## 2. 技术栈与目录分层结构

- **核心框架**：Flutter 3.13+ / Dart 3.x
- **本地存储**：`sqflite` (SQLite) + `path`
- **网络与音频**：`http` + Android 原生 MethodChannel (`android.media.MediaPlayer` & `TextToSpeech`)
- **状态与事件**：单例服务模式 + `StreamController.broadcast` 全局事件总线 (`DataEventService`)

### 目录分层架构
```text
tampo_android/
├── android/                   # 原生 Android 工程配置与 MainActivity
├── apks/                      # 历史版本安装包归档 (v1.0.0 ~ v1.2.0)
├── lib/
│   ├── main.dart              # 应用入口、主题监听、生命周期与 IndexedStack 四主页管理
│   ├── models/
│   │   └── word_models.dart   # 数据实体 (Word, WordList, ReviewLog, SyncTombstone 等)
│   ├── services/
│   │   ├── db_service.dart    # SQLite 核心服务 (单例，CRUD、FSRS 算法、同步与事务清空)
│   │   ├── event_service.dart # DataEventService 全局数据事件总线 (Stream 广播)
│   │   ├── theme_service.dart # 明暗主题与跟随系统状态管理
│   │   └── tts_service.dart   # 优先链式 TTS 发音引擎、网络缓存与自动清理
│   ├── theme/
│   │   └── app_theme.dart     # 黑白极简调色板与全局 Material 3 控件样式
│   ├── views/
│   │   ├── words_page.dart    # 词库页：分类统计数量显示、多选批量标记[掌握/重学/删除]
│   │   ├── study_page.dart    # 学习页：FSRS 智能卡片复习、增量队列实时同步、即时打卡
│   │   ├── today_page.dart    # 今日打卡页：打卡明细流、记忆留存率、纠偏与撤销
│   │   ├── import_sync_page.dart # 批量导入清洗、UDP 设备广播自发现、TCP 增量同步
│   │   └── settings_dialog.dialog # 设置项：TTS 配置与试听、音频缓存管理、词库清空危险区、版本署名
│   └── widgets/
│       └── word_progress_bg.dart # 单词背景半透进度条与极简 UI 组件
├── tools/                     # Python 辅助脚本 (TTS 测试等)
├── pubspec.yaml               # 依赖配置与版本号定义
└── CONTEXT_HANDOVER.md        # 本上下文交接文档
```

---

## 3. 核心机制与关键技术实现

### 3.1 本地 SQLite 数据模型与墓碑机制 (`db_service.dart`)
数据库包含四个核心表，严格与电脑端保持字段互通：
- `words`：生词主表 (`text`, `reading`, `meaning`, `state`, `due_date`, `stability`, `difficulty`, `reps`, `lapses`, `isInList`, `wordListId`, `wordListName` 等)；
- `word_lists`：词单表 (`id`, `name`, `createdAt`, `updatedAt`)；
- `review_logs`：打卡历史表 (`id`, `wordId`, `rating`, `reviewDate`, `elapsedDays`, `lastState`, `newState`)；
- `sync_tombstones`：同步墓碑表 (`wordText`, `deletedAt`)，用于跟踪本地物理删除的单词，防止同步时再次被对端重新恢复，遵循时间戳合并胜出（Last-Write-Wins）。

### 3.2 FSRS 现代记忆调度算法
- 基于复习评分（1: 遗忘, 3: 记得, 4: 掌握）实时计算卡片难易度与稳定性；
- 学习页面的卡片留存率动态公式：`R = (1 + elapsed / (9 * stability))^(-1)`。

### 3.3 优先链式 TTS 发音引擎 (`tts_service.dart`)
- **多引擎容灾回退**：内置支持多个 TTS API（包括 JapanesePod101 高清真人发音、原生 TTS 以及自定义模板）；
- 请求发音时按优先级依次尝试，超时或返回 404 时自动秒级回退至下一备用源；
- **智能本地缓存**：发音音频自动下载至应用缓存目录（md5 命名），提供手动一键清理以及「退出程序自动清空音频缓存」开关。

### 3.4 局域网无感双向同步 (`import_sync_page.dart`)
- **UDP 广播协议**：端口 41527 发送设备广播，自动探测并握手局域网内开启的 Windows 电脑端或其它 Android 设备；
- **TCP 增量同步**：动态端口传输序列化 JSON 增量报文，双向同步后各自落库，并触发全局事件通知。

### 3.5 全局数据流与实时增量同步 (`DataEventService`)
- 任何模块进行**新词导入、打卡提交、批量标记、词库清空或网络同步**后，均通过 `DataEventService.instance.notify(...)` 广播全局事件；
- 各功能模块即时响应，学习队列采用智能增量合并，正在复习中的卡片不会被打断，新词与新打卡无感即时刷新。

---

## 4. 规范与准则 (必须遵守)

1. **版本号纯净原则**：
   - 界面上对用户仅展示纯净版本号（如 `Tampo Android v1.1.1`），**严禁在界面中暴露构建号 Build / +15**；
   - 内部构建号仅在 `pubspec.yaml` 中作为 `versionCode` 递增（如 `1.1.1+15`），用于 Android 系统识别覆盖升级。
2. **UI 风格规范**：
   - 保持严格的**黑白极简主义（Black & White Minimalism）**；
   - 严禁擅自引入彩色 Emoji 或突兀的鲜艳渐变；
   - 控件尺寸保持紧凑精致，适配不同尺寸的移动屏。
3. **数据安全防护**：
   - 清空本地数据库属不可逆危险操作，必须保持二次警示确认弹窗与原子事务操作。

---

## 5. 打包构建指引

在 `x:\AI project\tampo_android` 目录下执行：
- 静态分析：`dart analyze lib` (必须保证 0 警告 0 错误)
- 生成 APK：`flutter build apk --debug`
- 产物位置：`build/app/outputs/flutter-apk/app-debug.apk`，打包后请复制覆盖至根目录 `Tampo_v1.2.3.apk` 及 `apks/` 目录归档。
