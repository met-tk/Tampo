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

        private static readonly Windows.Foundation.TypedEventHandler<ContentDialog, ContentDialogOpenedEventArgs> _dialogOpenedHandler = (s, e) =>
        {
            ApplySmokeLayer(s);
            s.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ApplySmokeLayer(s);
            });
        };
        private static readonly RoutedEventHandler _dialogLoadedHandler = (s, e) =>
        {
            if (s is ContentDialog cd)
            {
                ApplySmokeLayer(cd);
                cd.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    ApplySmokeLayer(cd);
                });
            }
        };
        private static readonly Windows.Foundation.TypedEventHandler<FrameworkElement, object> _dialogThemeChangedHandler = (s, e) =>
        {
            if (s is ContentDialog cd) ApplySmokeLayer(cd);
        };

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

                // 挂载生命周期与主题变动事件，确保弹窗呈现及主题切换时精准覆写底层 SmokeLayerBackground
                dialog.Loaded -= _dialogLoadedHandler;
                dialog.Loaded += _dialogLoadedHandler;
                dialog.Opened -= _dialogOpenedHandler;
                dialog.Opened += _dialogOpenedHandler;
                dialog.ActualThemeChanged -= _dialogThemeChangedHandler;
                dialog.ActualThemeChanged += _dialogThemeChangedHandler;

                dialog.Closed += (s, e) =>
                {
                    dialog.Loaded -= _dialogLoadedHandler;
                    dialog.Opened -= _dialogOpenedHandler;
                    dialog.ActualThemeChanged -= _dialogThemeChangedHandler;
                };

                // 立即执行一次遮罩画刷设置
                ApplySmokeLayer(dialog);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DialogHelper.ApplyTheme");
            }
            return dialog;
        }

        /// <summary>
        /// 针对暗色与浅色模式，动态覆写 ContentDialog 内部及宿主 Popup 的 SmokeLayerBackground 遮罩画刷，彻底清除白雾滤镜
        /// </summary>
        public static void ApplySmokeLayer(ContentDialog dialog)
        {
            if (dialog == null) return;
            try
            {
                var targetTheme = Converters.ThemeHelper.GetActualTheme();
                bool isDark = targetTheme == ElementTheme.Dark;
                dialog.RequestedTheme = targetTheme;

                // 暗黑模式下：使用深邃半透明暗色画刷（#B3000000，约 70% 透明度纯黑），令背景优雅沉降、消除白雾滤镜；
                // 浅色模式下：使用轻量半透明暗色画刷（#59000000，约 35% 透明度纯黑）。
                var smokeColor = isDark ? Windows.UI.Color.FromArgb(179, 0, 0, 0) : Windows.UI.Color.FromArgb(89, 0, 0, 0);
                var smokeBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(smokeColor);

                // 1. 设置 dialog 本身资源作为静态层备援（全覆盖所有可能被 WinUI 3 引用的遮罩画刷键名）
                dialog.Resources["ContentDialogSmokeFill"] = smokeBrush;
                dialog.Resources["SmokeFillColorDefaultBrush"] = smokeBrush;
                dialog.Resources["SmokeFillColorDefault"] = smokeColor;
                dialog.Resources["SystemControlPageBackgroundBaseMediumBrush"] = smokeBrush;
                dialog.Resources["ContentDialogDimmingThemeBrush"] = smokeBrush;
                dialog.Resources["SystemControlPageBackgroundMediumAltMediumBrush"] = smokeBrush;

                // 2. 深入 dialog 自身模板视觉树查找并覆写 SmokeLayerBackground
                ApplySmokeToVisualTree(dialog, smokeBrush);

                // 3. 深入当前 XamlRoot 下所有激活的 Popup 查找并同步赋权深色主题与覆写遮罩
                if (dialog.XamlRoot != null)
                {
                    try
                    {
                        var popups = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(dialog.XamlRoot);
                        if (popups != null)
                        {
                            foreach (var popup in popups)
                            {
                                if (popup != null)
                                {
                                    popup.RequestedTheme = targetTheme;
                                    ApplySmokeToVisualTree(popup, smokeBrush);
                                    if (popup.Child != null)
                                    {
                                        if (popup.Child is FrameworkElement childFe)
                                        {
                                            childFe.RequestedTheme = targetTheme;
                                        }
                                        ApplySmokeToVisualTree(popup.Child, smokeBrush);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DialogHelper.ApplySmokeLayer");
            }
        }

        private static void ApplySmokeToVisualTree(DependencyObject parent, Microsoft.UI.Xaml.Media.Brush smokeBrush)
        {
            if (parent == null) return;
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe)
                {
                    if (fe.Name == "SmokeLayerBackground")
                    {
                        if (fe is Microsoft.UI.Xaml.Shapes.Shape shape)
                        {
                            shape.Fill = smokeBrush;
                            shape.Opacity = 1.0;
                        }
                        else if (fe is Border border)
                        {
                            border.Background = smokeBrush;
                        }
                        else if (fe is Panel panel)
                        {
                            panel.Background = smokeBrush;
                        }
                    }
                    else if (fe.Name == "LayoutRoot" && fe is Panel layoutPanel)
                    {
                        // 确保 LayoutRoot 没有多余的发白背景干扰
                        if (layoutPanel.Background != null && layoutPanel.Background != smokeBrush)
                        {
                            layoutPanel.Background = null;
                        }
                    }
                }
                ApplySmokeToVisualTree(child, smokeBrush);
            }
        }
    }
}
