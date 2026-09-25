<div align="center">

<img src="Assets/AppIcon_Rounded_1000.png" width="128" height="128" alt="Tampo Logo" />

# Tampo (丹保)

**现代化日语生词记忆与 FSRS 智能间隔重复宝库**

*A Modern Japanese Vocabulary & FSRS Spaced Repetition Learning Desktop App for Windows.*

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?logo=windows)](https://github.com/met-tk/Tampo)
[![Framework](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-512BD4?logo=dotnet)](https://github.com/microsoft/WindowsAppSDK)
[![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Algorithm](https://img.shields.io/badge/Algorithm-FSRS%20v4-FF6F00)](https://github.com/open-spaced-repetition/fsrs4anki)
[![Release](https://img.shields.io/github/v/release/met-tk/Tampo?color=brightgreen)](https://github.com/met-tk/Tampo/releases/latest)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[📥 下载最新安装包](https://github.com/met-tk/Tampo/releases/latest) • [📖 开发与架构交接文档](CONTEXT_HANDOVER.md) • [✨ 功能特性](#-核心特性)

</div>

---

## 📖 项目简介

**Tampo** 是一款专为日语学习者量身定制的现代化桌面端单词宝库与智能记忆工具。

不同于传统机械式的背单词软件，Tampo 将强大的**词库切片检索管理**与前沿的 **FSRS (Free Spaced Repetition Scheduler)** 智能复习调度算法完美融合，搭配 Windows 11 Fluent Design（Mica / 亚克力材料与平滑暗黑模式），为您带来沉浸、高效且优雅的日语词汇积累体验。

---

## ✨ 核心特性

- 🧠 **前沿 FSRS 记忆算法**
  - 内置纯数学实现的 FSRS v4 算法模型，结合每张卡片的记忆稳定性（Stability）与提取难度（Difficulty），动态自适应预测艾宾浩斯遗忘临界点。
  - 支持【全部到期词汇】一键综合复习，亦可指定特定专属词单深度突击。

- 📚 **全量词库与多维切片**
  - **时间切片时间轴**：按创建时间回溯（今日/近7天/本月/季度），清晰掌握词汇积累节奏。
  - **专属词单体系**：支持词单自由归纳、右键重命名、同类词单合并、导出 Web 排版预览及无损解散。
  - **归档隔离流**：支持【未归档】与【已归档】一键单选过滤，960px 响应式自适应排版。

- 🎨 **极具美感的 Windows 11 视觉体验**
  - 采用 WinUI 3 (Windows App SDK) 原生桌面技术栈，支持 Mica（云母）半透明材质、流光动画与深浅色主题无缝热切换。
  - 4× 超采样平滑抗锯齿高清圆角图标（16px ~ 256px 全分辨率适配）。

- 🔒 **单实例原生互斥唤醒**
  - 自定义底层 Win32 互斥体入口拦截，杜绝重复启动产生多窗口，二次打开时秒速激活已有主窗体并置顶。

- 🌐 **三语原生无缝本地化**
  - 界面与安装向导深度支持 **简体中文**、**English**、**日本語** 原生热切换，内存级备援字典确保任何系统环境下零崩溃。

- 📦 **免环境依赖的便携安装体系**
  - 自研轻量级 WPF 单文件安装向导（`Tampo_Setup_v1.1.0.exe`），强制追加 `\Tampo` 子目录防磁盘污染。
  - 具备旧版本增量检测、无损升级以及卸载时保留个人词库与学习历史的贴心设计。

---

## 🚀 快速开始

### 📥 直接下载使用 (推荐)

直接前往 [Releases 页面](https://github.com/met-tk/Tampo/releases/latest) 下载最新的单文件安装器：
- 文件名：`Tampo_Setup_v1.1.0.exe`
- 双击运行向导即可自动部署并生成桌面圆角快捷方式，无需手动配置任何 .NET 运行时或环境依赖。

---

## 🛠️ 本地编译与构建

### 开发环境要求
- **操作系统**：Windows 10 (1809 及以上) 或 Windows 11
- **开发工具**：Visual Studio 2022 (具有 .NET 桌面开发与 Windows App SDK 工作负载) 或 VS Code
- **SDK**：[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. 克隆代码仓库
```bash
git clone https://github.com/met-tk/Tampo.git
cd Tampo
```

### 2. 运行单元测试
```powershell
dotnet test Tests/NihongoVocab.Tests.csproj
```
*(包含 FSRS 调度计算、记忆曲线与词库模型的 35 项单元测试将全部通过)*

### 3. 本地启动调试
```powershell
dotnet run --project NihongoVocab.csproj
```

### 4. 编译单文件安装包 (Release)
在项目根目录下执行预置的自动化打包脚本：
```powershell
powershell -ExecutionPolicy Bypass -File ".\build_publish.ps1"
```
脚本将全自动清洗历史缓存、同步 XBF/PRI/静态资源，并在 `Setup/` 目录下生成独立的单文件分发安装包 `Tampo_Setup_v1.1.0.exe`。

---

## 📁 项目架构一览

```text
├── Models/                # 核心数据模型 (Word, ReviewLog, CustomSlice 等)
├── Services/              # 核心业务服务 (DatabaseService, FsrsEngine, LocalizationService 等)
├── ViewModels/            # MVVM 视图模型 (StudyViewModel, AllWordsViewModel 等)
├── Views/                 # WinUI 3 XAML 视图层
├── Controls/              # 自定义 UI 控件 (卡片翻转、时间切片胶囊等)
├── Assets/                # 4x 超采样圆角应用图标与静态素材
├── Installer/             # 自研轻量化 WPF 独立安装/卸载器源码
├── Tests/                 # xUnit 单元测试套件
├── tools/                 # 图标生成与本地化维护工具集
├── build_publish.ps1      # 自动化编译打包发布流水线脚本
└── CONTEXT_HANDOVER.md    # 开发者与后续 Agent 标准技术交接文档
```

> 详细的系统架构决策（Why over What）与不可触碰的硬性约束，请阅读根目录下的 [**`CONTEXT_HANDOVER.md`**](CONTEXT_HANDOVER.md)。

---

## 📄 开源许可证

本项目采用 [MIT License](LICENSE) 授权许可。
