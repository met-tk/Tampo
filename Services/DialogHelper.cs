using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 统一弹窗与排版辅助类：彻底规范化换行符号，确保所有提示弹窗文本折行与排版工整美观
    /// </summary>
    public static class DialogHelper
    {
        /// <summary>
        /// 创建支持多行自动折行、识别真实与转义换行符的弹窗内容控件
        /// </summary>
        public static TextBlock CreateTextBlockContent(string rawText, double fontSize = 13, double lineHeight = 22)
        {
            if (string.IsNullOrEmpty(rawText))
            {
                return new TextBlock { Text = string.Empty };
            }

            // 深度清洗各类转义换行符号
            string clean = rawText
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\r\n", "\n");

            return new TextBlock
            {
                Text = clean,
                TextWrapping = TextWrapping.Wrap,
                FontSize = fontSize,
                LineHeight = lineHeight
            };
        }

        /// <summary>
        /// 统一为所有 ContentDialog 注入当前运行时的深浅主题，确保暗黑模式下彻底消除白底并正常反色
        /// </summary>
        public static ContentDialog ApplyTheme(ContentDialog dialog, XamlRoot? xamlRoot = null)
        {
            if (dialog == null) return dialog!;
            try
            {
                if (xamlRoot != null)
                {
                    dialog.XamlRoot = xamlRoot;
                }
                var targetTheme = Converters.ThemeHelper.GetActualTheme();
                dialog.RequestedTheme = targetTheme;
                if (dialog.Content is FrameworkElement fe)
                {
                    fe.RequestedTheme = targetTheme;
                }

                // 显式注入深邃半透明暗色背景遮罩画刷，彻底清除系统浅色模式下产生的白色滤镜与白雾反光
                bool isDark = targetTheme == ElementTheme.Dark;
                dialog.Resources["ContentDialogSmokeFill"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    isDark ? Windows.UI.Color.FromArgb(166, 0, 0, 0) : Windows.UI.Color.FromArgb(89, 0, 0, 0));
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DialogHelper.ApplyTheme");
            }
            return dialog;
        }
    }
}
