using System;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace NihongoVocab.Controls
{
    /// <summary>
    /// 专为日历 7 列网格设计的精准布局面板。
    /// 彻底消除虚拟化流式布局（UniformGridLayout）在无限高度或动态度量时可能引起的行重叠 Bug。
    /// 保证 7 列水平严格等分并与星期表头（周日到周六）像素级对齐。
    /// </summary>
    public sealed class CalendarGridPanel : Panel
    {
        public double RowSpacing { get; set; } = 4;
        public double ColumnSpacing { get; set; } = 4;
        public double CellHeight { get; set; } = 24;

        protected override Size MeasureOverride(Size availableSize)
        {
            double availableWidth = availableSize.Width;
            if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
            {
                availableWidth = 200;
            }
            double colWidth = Math.Max(0, (availableWidth - ColumnSpacing * 6) / 7.0);
            var childAvailableSize = new Size(colWidth, Math.Max(0, CellHeight));

            foreach (var child in Children)
            {
                child.Measure(childAvailableSize);
            }

            int count = Children.Count;
            int rowCount = (count + 6) / 7;
            if (rowCount == 0) rowCount = 6;

            double totalHeight = rowCount * CellHeight + Math.Max(0, rowCount - 1) * RowSpacing;
            return new Size(availableWidth, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double colWidth = Math.Max(0, (finalSize.Width - ColumnSpacing * 6) / 7.0);

            for (int i = 0; i < Children.Count; i++)
            {
                int row = i / 7;
                int col = i % 7;

                double x = col * (colWidth + ColumnSpacing);
                double y = row * (CellHeight + RowSpacing);

                Children[i].Arrange(new Rect(x, y, colWidth, CellHeight));
            }

            int count = Children.Count;
            int rowCount = (count + 6) / 7;
            if (rowCount == 0) rowCount = 6;

            double totalHeight = rowCount * CellHeight + Math.Max(0, rowCount - 1) * RowSpacing;
            return new Size(finalSize.Width, totalHeight);
        }
    }
}
