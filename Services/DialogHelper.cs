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
    }
}
