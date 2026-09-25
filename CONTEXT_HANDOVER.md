# Tampo（日语词汇宝库）上下文交接文档 (Context Handover)

> **文档适用对象**：后续接手此工程的任何全新 Agent 或工程师。请在不修改现有核心架构、不推翻既定方案的前提下，遵循本文档约定继续迭代。

---

## 1. 项目定位与核心技术栈

### 1.1 项目目标与核心功能
**一句话定位**：面向日语学习者的高性能、现代化 Windows 桌面端生词宝库与 FSRS 智能间隔重复记忆工具。
- **生词管理**：支持假名、汉字、声调、词性、多义释义、例句及双语注音的本地化存储与极速检索。
- **FSRS 记忆调度**：基于最新 **FSRS (Free Spaced Repetition Scheduler)** 算法，精准预测遗忘曲线，自适应调度复习周期。
- **多维切片与专属词单**：支持时间轴切片回溯、自定义专属词单（重命名/合并/解散/Web查看）、归档状态筛选（未归档/已归档）。
- **多语言与无缝体验**：原生支持简体中文、英语、日语热切换；支持 Windows 11 Fluent Design（Mica/深浅主题色）；单实例防多开；自研独立安装/升级/卸载体系。

### 1.2 核心技术选型及版本
| 层次 / 组件 | 技术选型 | 版本 / 说明 |
| :--- | :--- | :--- |
| **运行时** | .NET 8 SDK (`net8.0-windows10.0.26100.0`) | 目标平台 Windows 10/11 x64 |
| **UI 框架** | Windows App SDK / WinUI 3 | `1.6.250205002` (非打包/Unpackaged 模式运行) |
| **架构模式** | MVVM | `CommunityToolkit.Mvvm` 8.4.0 (弱引用消息、ObservableProperty) |
| **存储层** | SQLite (单文件数据库) | `Microsoft.Data.Sqlite` 9.0.2，WAL 模式并发，纯原生 SQL 驱动 |
| **安装器** | 自研独立 WPF 单执行文件 | `Installer\NihongoVocab.Installer.csproj`，内嵌 Zip Payload，零全局运行时依赖 |
| **单元测试** | xUnit + FluentAssertions | `Tests\NihongoVocab.Tests.csproj` (35 项核心业务与 FSRS 算法测试全绿) |

---

## 2. 当前系统架构与关键约定

### 2.1 目录结构与模块分工

```text
├── App.xaml / App.xaml.cs       # 应用程序级生命周期、主题与未捕获异常兜底
├── Program.cs                   # 自定义入口点：单实例互斥锁与原生 Win32 窗口唤醒置顶
├── NihongoVocab.csproj          # 主程序工程配置 (含 DISABLE_XAML_GENERATED_MAIN)
├── Models/                      # 核心数据模型 (WordItem, StudySession, CustomSliceItem 等)
├── Services/                    # 业务服务层
│   ├── DatabaseService.cs       # SQLite 读写单例、数据库升级脚本、事务安全操作
│   ├── FsrsEngine.cs            # FSRS v4 记忆算法纯数学实现 (稳定性/难度/间隔计算)
│   ├── CustomSliceService.cs    # 专属词单增删改查、合并、迁移、归档切片管理
│   ├── LocalizationService.cs   # 三语本地化字典与热更新事件分发服务
│   ├── AudioService.cs          # 本地/网络 TTS 音频播放与缓存控制
│   └── ThemeService.cs          # WinUI 3 Mica/Acrylic 及暗黑/浅色模式动态切换
├── ViewModels/                  # MVVM 视图模型
│   ├── MainViewModel.cs         # 主窗体导航与全局通知调度
│   ├── StudyViewModel.cs        # 智能复习模块：支持“全部到期词汇”及各专属词单学习
│   ├── AllWordsViewModel.cs     # 词库检索、时间切片与专属词单双模态、已/未归档筛选
│   ├── ArchiveViewModel.cs      # 归档词库管理、词单解散与恢复
│   ├── StatsViewModel.cs        # 遗忘曲线图表、记忆留存度与统计数据
│   └── SettingsViewModel.cs     # 语言、主题、FSRS 参数与数据库导入导出配置
├── Views/                       # WinUI 3 XAML 视图层 (StudyPage, AllWordsPage, etc.)
├── Controls/                    # 可复用自定义 UI 控件 (卡片翻转、时间切片胶囊等)
├── Strings/                     # .resw 多语言资源文件 (zh-CN, en-US, ja-JP)
├── Assets/                      # 超采样圆角应用图标 (AppIcon.ico, StoreLogo.png 等)
├── Installer/                   # 自研单文件安装与卸载器源码
│   ├── MainWindow.xaml.cs       # 安装向导：旧版本检测、增量升级、文件解包
│   ├── UninstallWindow.xaml.cs  # 卸载向导：用户数据库保留勾选、强制 Tampo 目录深度清理
│   └── InstallerLocalization.cs # 安装向导三语本地化资源
├── Tests/                       # 核心算法与数据逻辑 xUnit 测试套件
└── build_publish.ps1            # 自动化全量编译、资产同步、打包输出脚本
```

### 2.2 核心数据流向与状态管理
1. **持久化层**：数据存储于用户本地应用程序数据目录（生产环境位于 `%LocalAppData%\Tampo\Database\nihongo_vocab.db` 或便携目录），通过 `DatabaseService` 维持单例连接，全部数据库变更均走参数化查询及事务机制。
2. **状态流转**：
   - 视图模型（ViewModel）通过事件或弱引用订阅数据变更。
   - `CustomSliceService` 和 `DatabaseService` 中的数据变动触发各页面 ViewModel 自动刷新或触发特定通知。
3. **FSRS 记忆流**：
   - 学习模块从数据库拉取到期词汇（支持全部到期词汇或切片词单）。
   - 用户给出评分（Again / Hard / Good / Easy）-> `FsrsEngine.CalculateNextReview` 计算最新 $S$ (Stability)、$D$ (Difficulty)、下次复习时间戳 -> 异步原子写入 `words` 表及 `review_logs` 审计日志。

### 2.3 硬性代码规范与约束（必须严格遵守）
- **禁止引入重型重量级外部框架**：禁止引入 Prism、ReactiveUI 等重型第三方 MVVM 框架，保持使用标准的 `CommunityToolkit.Mvvm`。
- **环境隔离与免全局依赖**：本工程为免安装/便携友好的独立应用，不得依赖全局 GAC、全局注册表驱动，第三方原生动态库必须随应用目录输出（如 `e_sqlite3.dll`）。
- **非打包（Unpackaged）应用规则**：由于采用非打包分发，XAML 控件内绝对不可直接硬编码调用 `Windows.ApplicationModel.Package.Current`（会导致 Crash），获取版本号使用 `Assembly.GetExecutingAssembly().GetName().Version`。
- **UI 线程安全**：SQLite 耗时查询必须使用 `Task.Run` 放至后台线程，变更 `ObservableCollection` 或 UI 绑定的属性必须切回 `DispatcherQueue`。

---

## 3. 关键决策记录（Why over What）

### 3.1 自定义 Main 入口与程序唯一性 (Single Instance)
- **问题**：WinUI 3 默认在生成代码中内建隐藏的 `Program.Main`，一旦用户快速双击或重复启动，会弹出多个相互独立的实例窗口，造成 SQLite 数据库争用与复习进度冲突。
- **推翻方案**：在 `App.xaml.cs` 的 `OnLaunched` 中检测。该方案进程已被拉起并加载了 WinAppSDK 运行时，响应过慢且销毁进程容易发生资源泄露。
- **最终方案**：
  1. 在 `NihongoVocab.csproj` 中配置 `<DefineConstants>DISABLE_XAML_GENERATED_MAIN</DefineConstants>` 禁用 XAML 自动生成入口。
  2. 新建根目录 `Program.cs`，使用全局命名互斥量 `Local\Tampo_App_SingleInstance_Mutex_2026` 进行零开销前置判定。
  3. 若已有实例运行，利用 Win32 API (`FindWindowEx` + `ShowWindowAsync(SW_RESTORE)` + `SetForegroundWindow`) 将既有窗口激活置顶，当前进程直接秒退，干净彻底。

### 3.2 自研安装器 vs 第三方打包工具 (Inno Setup / MSIX)
- **问题**：MSIX 在非应用商店环境需要用户配置受信任证书，且沙盒机制容易导致词库数据库文件被隐式重定向或在更新时丢失；传统的 Inno Setup 界面粗糙、与现代化 Windows 11 Fluent 视觉割裂，且多语言支持笨重。
- **最终方案**：
  - 基于 WPF (.NET 8) 打造现代化无边框毛玻璃质感安装/卸载器（`NihongoVocab.Installer`）。
  - **内嵌 Payload 机制**：将编译发布的完整绿色应用压缩后直接作为二进制内嵌资源打入单文件 `Tampo_Setup_v1.1.0.exe`。
  - **强制目录隔离与防污染**：无论用户选择何种父目录，安装器底层强制追加生成 `\Tampo` 专用目录，防止散落文件污染用户磁盘；卸载器提供【保留我的生词本与学习历史记录数据库】选项，卸载时仅清理程序文件与注册表，确保用户珍贵学习数据永不丢失。
  - **版本自动识别**：根据注册表及现有安装文件比对版本号，智能呈现【立即升级】、【重新安装（修复）】或【降级警告】。

### 3.3 本地化三语备援字典
- **问题**：WinUI 3 原生 `ResourceLoader` 在 Unpackaged 独立运行模式下，若遇到特定 Windows 系统区域设置缺失，会偶发抛出 `COMException / ResourceNotFound` 甚至闪退。
- **最终方案**：
  - 采用“双保险”架构：在保留标准 `Strings\*\Resources.resw` 的同时，在 `LocalizationService.cs` 内部建立完全同构的静态内存三语 Dictionary。
  - 界面获取文案时优先匹配内存字典并提供 Default Fallback，确保即使在极度异常的精简版 Windows 系统上也能 100% 优雅展示，绝不崩溃。

### 3.4 学习范围“全部到期词汇”置顶项与词库时间切片双模态
- **问题**：旧版本在有自定义词单时，学习下拉列表被自定义词单完全占据，用户无法一键开始“全库所有到期词汇”的综合复习；同时词库页面无法便捷对专属词单进行右键重命名、合并、解散。
- **最终方案**：
  - 学习模块中引入特殊标记 `AllDueWordsSliceId = "__ALL_DUE_WORDS__"`，并作为首项固定置顶，保证任何情况下用户均可无障碍发起全库复习。
  - 词库页面在左侧切片区设计了模式切换胶囊，支持在“时间切片（最近7天/30天/历史）”与“专属词单”之间无缝切换，并在词库页面注入了与归档模块完全同构的 ContextMenu 交互逻辑。
  - 词库顶部筛选栏新增【未归档】与【已归档】独立单选过滤，并设置了 960px 响应式断点排版。

### 3.5 图标资产生成与超采样防毛边设计
- **问题**：早期曾尝试通过代码绘制矢量多边形拟合“た”假名，导致笔画交界处破损缺角；若直接将方形白底图转为图标，Windows 桌面显示会非常生硬且有锯齿。
- **最终方案**：
  - 以根目录提供的 1000×1000 像素原版 [`ta.png`](file:///x:/AI%20project/日语词汇宝库/ta.png) 为唯一真源（Source of Truth）。
  - 在 [`tools/generate_icons.py`](file:///x:/AI%20project/日语词汇宝库/tools/generate_icons.py) 中采用 4× 超采样（4000×4000 空间）渲染标准 22% 曲率圆角矩形蒙版，并使用 Lanczos 滤波降采样，生成完美无缝抗锯齿的圆角图像。
  - 自动输出 Windows 规范支持的全部 8 组尺寸（16, 24, 32, 48, 64, 96, 128, 256px）嵌入式多帧 `AppIcon.ico` 以及 WinUI 3 全套 scale 资源，并在 `build_publish.ps1` 步骤 [1/5] 自动执行同步。

---

## 4. 当前进展与下一阶段规划

### 4.1 当前状态
> **本阶段所有既定功能已完成，测试均已通过，代码处于可交付/稳定基准（Stable Baseline）。**
- 版本号全链路已完全同步至 **`v1.1.0`**（包含安装向导、元数据清单、本地化资源字典、主程序界面）。
- 打包输出产物验证完毕：`Setup\Tampo_Setup_v1.1.0.exe` 编译生成并通过架构与运行检验。

### 4.2 未决事项 / 已知限制
- **暂无阻塞项**。所有业务流、本地数据库与安装卸载逻辑均工作正常。

### 4.3 下一步演进建议（Next Phase）
若后续需要继续丰富 Tampo 的功能，建议优先考虑以下自然演进方向：
1. **词库批量导入导出插件化**：支持 Anki `.apkg` 格式直接无损导入/导出（含音频与标签映射）。
2. **多设备 WebDAV / 坚果云云端同步**：在保证本地 SQLite 优先的基础上，支持单文件数据库的加密增量云端备份与冲突合并。
3. **日语例句形态学分析（Mecab / Kagome）**：在例句展示区引入分词注音悬浮框（Furigana Hover），提升阅读长难句体验。

---

## 5. 验证与运行命令

开发环境前提：已安装 Windows 10/11，.NET 8 SDK，具备 PowerShell。

### 5.1 运行单元测试
在项目根目录下执行：
```powershell
dotnet test Tests/NihongoVocab.Tests.csproj
```
*预期结果*：35 个测试全部通过（通过率 100%），验证 FSRS 调度计算与词单切片模型逻辑。

### 5.2 启动本地调试开发
在 Visual Studio 2022 或使用命令行启动主程序（Unpackaged 模式）：
```powershell
dotnet run --project NihongoVocab.csproj
```

### 5.3 一键构建与打包发行版 (Release)
在项目根目录运行预置打包脚本：
```powershell
powershell -ExecutionPolicy Bypass -File ".\build_publish.ps1"
```
*该脚本将自动执行*：
1. 清理旧构建缓存与占用进程；
2. 执行 `dotnet publish` 输出独立非打包 WinUI 3 应用至 `publish\`；
3. 同步并补全 XBF、PRI 索引文件与 Assets 静态资源；
4. 将 Payload 打包并编译生成单文件安装器 `Setup\Tampo_Setup_v1.1.0.exe`。

### 5.4 接手者功能验证清单 (Smoke Test)
1. **安装器体验**：运行 `Setup\Tampo_Setup_v1.1.0.exe`，检查界面标题是否为 `v1.1.0`，检查是否智能识别安装路径并在路径末尾安全补全 `\Tampo`。
2. **多开拦截**：连续两次双击启动 Tampo，检查是否仅出现一个窗口且第二次启动时原窗口被自动置顶激活。
3. **学习模块**：进入学习页面，确认学习范围下拉列表第一项为【全部到期词汇】。
4. **词库模块**：
   - 检查顶部筛选栏右侧是否包含【未归档】与【已归档】选项，且窗口拉窄至 960px 以下时自适应排版。
   - 检查左侧切片区是否支持切换至“专属词单”，且在专属词单项上右键可弹出重命名/合并/解散菜单。
