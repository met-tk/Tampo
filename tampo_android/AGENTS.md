# Tampo Android - Agent 指南与工作区上下文

本项目是 Tampo 日语词汇宝库的 Android 移动端工程。
在开始任何代码修改前，请务必阅读本目录下的 [CONTEXT_HANDOVER.md](file:///CONTEXT_HANDOVER.md)。

## 关键核心规则：
1. 始终使用简体中文沟通与编写文档；
2. 保持黑白极简主义设计，杜绝在 UI 中添加多余彩色 Emoji；
3. 对外展示版本号保持纯净（如 v1.1.1），不展示内部 Build 编号；
4. 任何涉及数据库架构与 TTS 的改动，注意保持与 SQLite 墓碑同步机制及优先链回退策略兼容；
5. 完成修改后执行 `dart analyze lib` 确保 0 警告 0 错误。
