using System;
using System.Globalization;

namespace NihongoVocab.Installer
{
    public class InstallerLocalization
    {
        public static InstallerLocalization Current { get; } = Detect();

        public string SetupTitle { get; set; } = "";
        public string SetupSubtitle { get; set; } = "";
        public string InstallPathLabel { get; set; } = "";
        public string BrowseButton { get; set; } = "";
        public string DesktopShortcut { get; set; } = "";
        public string StartMenuShortcut { get; set; } = "";
        public string InstallButton { get; set; } = "";
        public string InstallingTitle { get; set; } = "";
        public string StatusExtracting { get; set; } = "";
        public string StatusConfiguring { get; set; } = "";
        public string InstallSuccess { get; set; } = "";
        public string InstallSuccessDesc { get; set; } = "";
        public string LaunchApp { get; set; } = "";
        public string FinishButton { get; set; } = "";
        public string CloseButton { get; set; } = "";
        public string CancelButton { get; set; } = "";

        public string UninstallTitle { get; set; } = "";
        public string UninstallSubtitle { get; set; } = "";
        public string UninstallOptions { get; set; } = "";
        public string KeepUserData { get; set; } = "";
        public string KeepUserDataDesc { get; set; } = "";
        public string ConfirmUninstallButton { get; set; } = "";
        public string UninstallingTitle { get; set; } = "";
        public string UninstallSuccess { get; set; } = "";
        public string UninstallSuccessDesc { get; set; } = "";

        // 升级 / 重装 / 降级
        public string UpgradeBannerTitle { get; set; } = "";
        public string ReinstallBannerTitle { get; set; } = "";
        public string DowngradeBannerTitle { get; set; } = "";
        public string InstalledAt { get; set; } = "";
        public string UpgradeButton { get; set; } = "";
        public string ReinstallButton { get; set; } = "";
        public string DowngradeButton { get; set; } = "";
        public string CleaningOldFiles { get; set; } = "";

        private static InstallerLocalization Detect()
        {
            string culture = CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            if (culture.StartsWith("ja"))
            {
                return new InstallerLocalization
                {
                    SetupTitle = "Tampo セットアップ",
                    SetupSubtitle = "v1.1.0 現代的な日本語語彙・FSRS記憶学習ツール",
                    InstallPathLabel = "インストール先フォルダー",
                    BrowseButton = "参照...",
                    DesktopShortcut = "デスクトップにショートカットを作成",
                    StartMenuShortcut = "スタートメニューに追加",
                    InstallButton = "今すぐインストール",
                    InstallingTitle = "Tampo をインストールしています...",
                    StatusExtracting = "コアコンポーネントを展開しています...",
                    StatusConfiguring = "ショートカットとシステム設定を構成しています...",
                    InstallSuccess = "インストールが完了しました！",
                    InstallSuccessDesc = "Tampo が正常にコンピューターへ配置されました。",
                    LaunchApp = "今すぐ Tampo を起動する",
                    FinishButton = "完了",
                    CloseButton = "閉じる",
                    CancelButton = "キャンセル",
                    UninstallTitle = "Tampo のアンインストール",
                    UninstallSubtitle = "コンピューターから Tampo アプリケーションを完全に削除します。",
                    UninstallOptions = "アンインストール設定",
                    KeepUserData = "単語帳と学習履歴データベースを保持する (推奨)",
                    KeepUserDataDesc = "チェックを入れるとローカル単語データが保持され、将来の再インストール時に引き継がれます。",
                    ConfirmUninstallButton = "完全にアンインストール",
                    UninstallingTitle = "アンインストール中...",
                    UninstallSuccess = "アンインストール完了",
                    UninstallSuccessDesc = "Tampo およびすべてのプログラムファイルが完全に削除されました。",
                    UpgradeBannerTitle = "インストール済みバージョンを検出しました",
                    ReinstallBannerTitle = "同じバージョンがインストールされています",
                    DowngradeBannerTitle = "バージョンダウングレードの警告",
                    InstalledAt = "インストール先",
                    UpgradeButton = "今すぐアップグレード",
                    ReinstallButton = "再インストール（修復）",
                    DowngradeButton = "このまま続ける",
                    CleaningOldFiles = "古いプログラムファイルをクリーンアップしています..."
                };
            }
            else if (culture.StartsWith("zh"))
            {
                return new InstallerLocalization
                {
                    SetupTitle = "Tampo 安装向导",
                    SetupSubtitle = "v1.1.0 现代日语生词与 FSRS 记忆宝库",
                    InstallPathLabel = "安装目标目录",
                    BrowseButton = "浏览...",
                    DesktopShortcut = "创建桌面快捷方式",
                    StartMenuShortcut = "添加到开始菜单",
                    InstallButton = "立即安装",
                    InstallingTitle = "正在安装 Tampo...",
                    StatusExtracting = "正在解压应用核心组件...",
                    StatusConfiguring = "正在配置系统快捷方式与注册表...",
                    InstallSuccess = "安装完成！",
                    InstallSuccessDesc = "Tampo 已成功部署至您的计算机。",
                    LaunchApp = "立即启动 Tampo",
                    FinishButton = "完成",
                    CloseButton = "关闭",
                    CancelButton = "取消",
                    UninstallTitle = "卸载 Tampo",
                    UninstallSubtitle = "本向导将从您的计算机中彻底移除 Tampo 应用程序。",
                    UninstallOptions = "卸载选项",
                    KeepUserData = "保留我的生词本与学习历史记录数据库 (推荐)",
                    KeepUserDataDesc = "勾选后将保留您的所有本地生词库，将来重新安装可直接继续学习。",
                    ConfirmUninstallButton = "确认彻底卸载",
                    UninstallingTitle = "正在卸载，请稍候...",
                    UninstallSuccess = "卸载成功",
                    UninstallSuccessDesc = "Tampo 及其所有程序文件夹已被彻底干净地从计算机中移除。",
                    UpgradeBannerTitle = "检测到已安装的旧版本",
                    ReinstallBannerTitle = "检测到已安装相同版本",
                    DowngradeBannerTitle = "降级安装警告",
                    InstalledAt = "当前安装位置",
                    UpgradeButton = "立即升级",
                    ReinstallButton = "重新安装（修复）",
                    DowngradeButton = "仍然继续",
                    CleaningOldFiles = "正在清理旧版程序文件..."
                };
            }
            else
            {
                return new InstallerLocalization
                {
                    SetupTitle = "Tampo Setup Wizard",
                    SetupSubtitle = "v1.1.0 Modern Japanese Vocabulary & FSRS Memory Scheduler",
                    InstallPathLabel = "Installation Directory",
                    BrowseButton = "Browse...",
                    DesktopShortcut = "Create Desktop Shortcut",
                    StartMenuShortcut = "Add to Start Menu",
                    InstallButton = "Install Now",
                    InstallingTitle = "Installing Tampo...",
                    StatusExtracting = "Extracting core application components...",
                    StatusConfiguring = "Configuring shortcuts and system registry...",
                    InstallSuccess = "Installation Completed!",
                    InstallSuccessDesc = "Tampo has been successfully installed on your computer.",
                    LaunchApp = "Launch Tampo now",
                    FinishButton = "Finish",
                    CloseButton = "Close",
                    CancelButton = "Cancel",
                    UninstallTitle = "Uninstall Tampo",
                    UninstallSubtitle = "This wizard will completely remove Tampo from your computer.",
                    UninstallOptions = "Uninstall Options",
                    KeepUserData = "Keep vocabulary lists and study history database (Recommended)",
                    KeepUserDataDesc = "When checked, your personal word database will be preserved for future use.",
                    ConfirmUninstallButton = "Confirm Uninstall",
                    UninstallingTitle = "Uninstalling, please wait...",
                    UninstallSuccess = "Uninstall Successful",
                    UninstallSuccessDesc = "Tampo and all associated program files have been completely removed.",
                    UpgradeBannerTitle = "Existing installation detected",
                    ReinstallBannerTitle = "Same version already installed",
                    DowngradeBannerTitle = "Downgrade warning",
                    InstalledAt = "Currently installed at",
                    UpgradeButton = "Upgrade Now",
                    ReinstallButton = "Reinstall (Repair)",
                    DowngradeButton = "Continue Anyway",
                    CleaningOldFiles = "Cleaning up old program files..."
                };
            }
        }
    }
}
