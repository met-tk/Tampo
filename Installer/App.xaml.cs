using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace NihongoVocab.Installer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string processName = Process.GetCurrentProcess().ProcessName;
            bool isUninstall = e.Args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase))
                               || processName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isUninstall)
            {
                var uninstallWnd = new UninstallWindow();
                uninstallWnd.Show();
            }
            else
            {
                var installWnd = new MainWindow();
                installWnd.Show();
            }
        }
    }
}
