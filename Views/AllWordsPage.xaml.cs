using System;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class AllWordsPage : Page
    {
        public AllWordsViewModel ViewModel { get; }

        private bool _isResizing;
        private double _startPointerX;
        private double _startColumnWidth;

        public AllWordsPage()
        {
            ViewModel = App.GetService<AllWordsViewModel>();
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            RestorePreferences();
            this.Loaded += AllWordsPage_Loaded;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedStrings();

            // --- 顶部工具栏 ---

            // --- 时间树操作 ---
            DimensionComboBox.SelectionChanged += DimensionComboBox_SelectionChanged;
            ExpandAllButton.Click += ExpandAll_Click;
            CollapseAllButton.Click += CollapseAll_Click;
            FlatLevelModeCheckBox.Click += FlatLevelModeCheckBox_Click;
            HierarchyTreeView.ItemInvoked += HierarchyTreeView_ItemInvoked;

            // --- ColumnSplitter 拖拽 ---
            ColumnSplitter.PointerEntered += ColumnSplitter_PointerEntered;
            ColumnSplitter.PointerExited += ColumnSplitter_PointerExited;
            ColumnSplitter.PointerPressed += ColumnSplitter_PointerPressed;
            ColumnSplitter.PointerMoved += ColumnSplitter_PointerMoved;
            ColumnSplitter.PointerReleased += ColumnSplitter_PointerReleased;

            // --- RowSplitter 拖拽 ---
            RowSplitter.PointerEntered += RowSplitter_PointerEntered;
            RowSplitter.PointerExited += RowSplitter_PointerExited;
            RowSplitter.PointerPressed += RowSplitter_PointerPressed;
            RowSplitter.PointerMoved += RowSplitter_PointerMoved;
            RowSplitter.PointerReleased += RowSplitter_PointerReleased;

            // --- 右侧过滤区域 ---
            RightDetailGrid.SizeChanged += RightDetailGrid_SizeChanged;
            StateFilterAll.Checked += StateFilter_Checked;
            StateFilterNew.Checked += StateFilter_Checked;
            StateFilterLearning.Checked += StateFilter_Checked;
            StateFilterReviewing.Checked += StateFilter_Checked;
            StateFilterMastered.Checked += StateFilter_Checked;
            StateFilterUnarchived.Checked += StateFilter_Checked;
            StateFilterArchived.Checked += StateFilter_Checked;

            // --- 左侧下半部分模式切换与词单操作 ---
            TabTimeSliceRadio.Checked += (s, e) =>
            {
                TimeSliceViewGrid.Visibility = Visibility.Visible;
                CustomListsViewGrid.Visibility = Visibility.Collapsed;
            };
            TabCustomListsRadio.Checked += (s, e) =>
            {
                TimeSliceViewGrid.Visibility = Visibility.Collapsed;
                CustomListsViewGrid.Visibility = Visibility.Visible;
            };
            CustomWordListsListView.SelectionChanged += CustomWordListsListView_SelectionChanged;
            ClearWordListFilterButton.Click += ClearWordListFilter_Click;
            CustomWordListsListView.ContextRequested += (sender, args) =>
            {
                if (args.OriginalSource is DependencyObject dep)
                {
                    var item = FindVisualParent<ListViewItem>(dep);
                    if (item != null && CustomWordListsListView.ItemFromContainer(item) is WordList targetList)
                    {
                        CustomWordListsListView.SelectedItem = targetList;
                        ViewModel.SelectWordList(targetList);
                        MenuListRename.Tag = targetList;
                        MenuListMerge.Tag = targetList;
                        MenuListExport.Tag = targetList;
                        MenuListDissolve.Tag = targetList;
                    }
                }
            };
            MenuListRename.Click += MenuListRename_Click;
            MenuListMerge.Click += MenuListMerge_Click;
            MenuListExport.Click += MenuListExport_Click;
            MenuListDissolve.Click += MenuListDissolve_Click;

            // --- 多选工具栏 ---
            SelectAllCheckBox.Checked += SelectAllCheckBox_Checked;
            SelectAllCheckBox.Unchecked += SelectAllCheckBox_Unchecked;
            BatchCreateNewListButton.Click += BatchCreateNewList_Click;

            // --- 词汇列表 ---
            WordsListView.SelectionChanged += WordsListView_SelectionChanged;
            WordsListView.PointerReleased += WordsListView_PointerReleased;

            // --- 右键菜单 ---
            MenuBatchCreateNewList.Click += BatchCreateNewList_Click;
            MenuCopyLines.Click += MenuCopyLines_Click;
            MenuFormatExport.Click += MenuFormatExport_Click;
            MenuSetStateNew.Click += MenuSetStateNew_Click;
            MenuSetStateMastered.Click += MenuSetStateMastered_Click;
            MenuReimportToday.Click += MenuReimportToday_Click;
            MenuDeleteWords.Click += MenuDeleteWords_Click;

            // --- 切片操作按钮 ---
            PackSliceToListButton.Click += PackSliceToList_Click;
            ViewAllTimeButton.Click += ViewAllTime_Click;

            ViewModel.TreeRebuilt += () => DispatcherQueue.TryEnqueue(RefreshTreeRootNodes);
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AllWordsViewModel.IsFlatLevelMode) ||
                    e.PropertyName == nameof(AllWordsViewModel.SelectedDimension))
                {
                    DispatcherQueue.TryEnqueue(RefreshTreeRootNodes);
                }
            };
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            UpdateLocalizedStrings();
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;
            if (DimensionComboBox != null)
            {
                int curDimIdx = DimensionComboBox.SelectedIndex;
                foreach (ComboBoxItem item in DimensionComboBox.Items)
                {
                    if (item.Tag is string tag)
                    {
                        item.Content = tag switch
                        {
                            "Year" => loc.GetString("DimensionYear", "按年分类"),
                            "Quarter" => loc.GetString("DimensionQuarter", "按季分类"),
                            "Month" => loc.GetString("DimensionMonth", "按月分类"),
                            "Week" => loc.GetString("DimensionWeek", "按周分类"),
                            "Day" => loc.GetString("DimensionDay", "按日分类"),
                            _ => item.Content
                        };
                    }
                }
                if (curDimIdx >= 0)
                {
                    DimensionComboBox.SelectedIndex = -1;
                    DimensionComboBox.SelectedIndex = curDimIdx;
                }
            }

            if (MenuBatchCreateNewList != null) MenuBatchCreateNewList.Text = loc.GetString("ButtonCreateCustomList", "新建自选词单...");
            if (MenuCopyLines != null) MenuCopyLines.Text = loc.GetString("MenuCopyLines", "一键复制 (换行)");
            if (MenuFormatExport != null) MenuFormatExport.Text = loc.GetString("MenuFormatExport", "格式化导出...");
            if (MenuSetStateNew != null) MenuSetStateNew.Text = loc.GetString("MenuSetStateNew", "标记为：未学习");
            if (MenuSetStateMastered != null) MenuSetStateMastered.Text = loc.GetString("MenuSetStateMastered", "标记为：已掌握");
            if (MenuReimportToday != null) MenuReimportToday.Text = loc.GetString("MenuReimportToday", "重新在今天导入");
            if (MenuDeleteWords != null) MenuDeleteWords.Text = loc.GetString("ButtonDelete", "删除所选单词");
            if (SelectAllCheckBox != null) SelectAllCheckBox.Content = loc.GetString("SelectAllResults", "全选当前结果");
            if (MenuListRename != null) MenuListRename.Text = loc.GetString("MenuRenameList", "重命名词单");
            if (MenuListMerge != null) MenuListMerge.Text = loc.GetString("MenuMergeList", "合并至其他词单...");
            if (MenuListExport != null) MenuListExport.Text = loc.GetString("MenuExportList", "在浏览器中查看");
            if (MenuListDissolve != null) MenuListDissolve.Text = loc.GetString("MenuDissolveList", "解散词单（词汇恢复未归档）");

            int count = WordsListView?.SelectedItems?.Count ?? 0;
            if (SelectedCountTextBlock != null)
            {
                SelectedCountTextBlock.Text = string.Format(loc.GetString("SelectedCountFormat", "已选 {0} 项"), count);
            }

            UpdateFilterBarLayout();
            RefreshTreeRootNodes();
        }

        private void RestorePreferences()
        {
            try
            {
                var pref = UserPreferenceService.Instance;

                // 1. 恢复时间维度下拉
                string savedDim = pref.Get("AllWords_Dimension", "Month");
                foreach (ComboBoxItem item in DimensionComboBox.Items)
                {
                    if (item.Tag?.ToString() == savedDim)
                    {
                        DimensionComboBox.SelectedItem = item;
                        ViewModel.SelectedDimension = savedDim;
                        break;
                    }
                }

                // 2. 恢复单层扁平模式
                ViewModel.IsFlatLevelMode = pref.GetBool("AllWords_IsFlatLevelMode", false);

                // 3. 恢复分割条尺寸
                double colWidth = pref.GetDouble("AllWords_TreeColumnWidth", 320);
                if (colWidth >= 240 && colWidth <= 600)
                {
                    TreeColumn.Width = new GridLength(colWidth);
                }

                double rowHeight = pref.GetDouble("AllWords_TreeRowHeight", 0);
                if (rowHeight >= 140)
                {
                    TreeRow.Height = new GridLength(rowHeight);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsPage.RestorePreferences");
            }
        }

        private void FlatLevelModeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UserPreferenceService.Instance.SetBool("AllWords_IsFlatLevelMode", ViewModel.IsFlatLevelMode);
            RefreshTreeRootNodes();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateLocalizedStrings();
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
            DatabaseService.DataChanged += OnDatabaseDataChanged;
            try
            {
                await ViewModel.LoadDataAsync();
                RefreshTreeRootNodes();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsPage.OnNavigatedTo");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
        }

        private void OnDatabaseDataChanged()
        {
            try
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await ViewModel.LoadDataAsync();
                    RefreshTreeRootNodes();
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsPage.OnDatabaseDataChanged");
            }
        }

        private async void AllWordsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.LoadDataAsync();
                RefreshTreeRootNodes();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsPage.AllWordsPage_Loaded");
            }
        }



        #region Splitter 拖拽调整 (水平 ColumnSplitter 与 垂直 RowSplitter)
        private void ColumnSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        }

        private void ColumnSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizing && !_isRowResizing)
            {
                ProtectedCursor = null;
            }
        }

        private void ColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isResizing = true;
            _startPointerX = e.GetCurrentPoint(LayoutRootGrid).Position.X;
            _startColumnWidth = TreeColumn.ActualWidth;
            ColumnSplitter.CapturePointer(e.Pointer);
        }

        private void ColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizing) return;

            double currentX = e.GetCurrentPoint(LayoutRootGrid).Position.X;
            double delta = currentX - _startPointerX;
            double targetWidth = Math.Clamp(_startColumnWidth + delta, 240, 600);
            TreeColumn.Width = new GridLength(targetWidth);
        }

        private void ColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                ColumnSplitter.ReleasePointerCapture(e.Pointer);
                ProtectedCursor = null;
                UserPreferenceService.Instance.SetDouble("AllWords_TreeColumnWidth", TreeColumn.ActualWidth);
            }
        }

        private bool _isRowResizing;
        private double _startPointerY;
        private double _startTreeRowHeight;

        private void RowSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        }

        private void RowSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizing && !_isRowResizing)
            {
                ProtectedCursor = null;
            }
        }

        private void RowSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isRowResizing = true;
            _startPointerY = e.GetCurrentPoint(LayoutRootGrid).Position.Y;
            _startTreeRowHeight = TreeRow.ActualHeight;
            RowSplitter.CapturePointer(e.Pointer);
        }

        private void RowSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isRowResizing) return;

            double currentY = e.GetCurrentPoint(LayoutRootGrid).Position.Y;
            double delta = currentY - _startPointerY;
            double totalHeight = TreeRow.ActualHeight + BottomRow.ActualHeight;
            double minHeight = 140;
            double maxHeight = totalHeight > (minHeight * 2) ? (totalHeight - minHeight) : 500;
            double targetHeight = Math.Clamp(_startTreeRowHeight + delta, minHeight, maxHeight);
            TreeRow.Height = new GridLength(targetHeight);
        }

        private void RowSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isRowResizing)
            {
                _isRowResizing = false;
                RowSplitter.ReleasePointerCapture(e.Pointer);
                ProtectedCursor = null;
                UserPreferenceService.Instance.SetDouble("AllWords_TreeRowHeight", TreeRow.ActualHeight);
            }
        }
        #endregion

        #region 筛选与树节点交互
        private void DimensionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HierarchyTreeView == null || ViewModel == null) return;
            if (DimensionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.SelectedDimension = tag;
                UserPreferenceService.Instance.Set("AllWords_Dimension", tag);
                RefreshTreeRootNodes();
            }
        }

        private void RefreshTreeRootNodes()
        {
            try
            {
                if (HierarchyTreeView == null || ViewModel?.DateTreeNodes == null) return;
                HierarchyTreeView.RootNodes.Clear();
                foreach (var dataNode in ViewModel.DateTreeNodes)
                {
                    HierarchyTreeView.RootNodes.Add(BuildTreeNode(dataNode));
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsPage.RefreshTreeRootNodes");
            }
        }

        private TreeViewNode BuildTreeNode(DateHierarchyNode dataNode)
        {
            var node = new TreeViewNode
            {
                Content = dataNode,
                IsExpanded = dataNode.IsExpanded
            };
            foreach (var child in dataNode.Children)
            {
                node.Children.Add(BuildTreeNode(child));
            }
            return node;
        }

        private void SetExpandedRecursive(System.Collections.Generic.IList<TreeViewNode> nodes, bool isExpanded)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = isExpanded;
                if (node.Content is DateHierarchyNode dn)
                {
                    dn.IsExpanded = isExpanded;
                }
                SetExpandedRecursive(node.Children, isExpanded);
            }
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ExpandAll();
            SetExpandedRecursive(HierarchyTreeView.RootNodes, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CollapseAll();
            SetExpandedRecursive(HierarchyTreeView.RootNodes, false);
        }

        private void HierarchyTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var node = args.InvokedItem as DateHierarchyNode 
                       ?? (args.InvokedItem as TreeViewNode)?.Content as DateHierarchyNode;
            if (node != null)
            {
                CustomWordListsListView.SelectedItem = null;
                TabTimeSliceRadio.IsChecked = true;
                TimeSliceViewGrid.Visibility = Visibility.Visible;
                CustomListsViewGrid.Visibility = Visibility.Collapsed;
                ViewModel.SelectNode(node);
            }
        }

        private void ViewAllTime_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectNode(null);
        }

        private void StateFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                ViewModel.SelectedStateFilter = tag;
            }
        }
        #endregion

        #region 自定义专属词单交互与右键菜单
        private void CustomWordListsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomWordListsListView.SelectedItem is WordList list)
            {
                ViewModel.SelectWordList(list);
            }
        }

        private void ClearWordListFilter_Click(object sender, RoutedEventArgs e)
        {
            CustomWordListsListView.SelectedItem = null;
            ViewModel.SelectWordList(null);
        }

        private async void MenuListRename_Click(object sender, RoutedEventArgs e)
        {
            var list = (sender as MenuFlyoutItem)?.Tag as WordList ?? CustomWordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
            if (list != null)
            {
                var loc = LocalizationService.Instance;
                var input = new TextBox
                {
                    Text = list.Name,
                    PlaceholderText = loc.GetString("PlaceholderRenameList", "输入新的词单名称...")
                };

                var dialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleRenameList", "重命名词单"),
                    Content = input,
                    PrimaryButtonText = loc.GetString("ButtonConfirm", "确定"),
                    CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                MainWindow.RegisterActiveDialog(dialog);
                var res = await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);

                if (res == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
                {
                    await ViewModel.RenameWordListAsync(list, input.Text.Trim());
                }
            }
        }

        private async void MenuListMerge_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = (sender as MenuFlyoutItem)?.Tag as WordList ?? CustomWordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
            if (sourceList != null)
            {
                var loc = LocalizationService.Instance;
                var otherLists = ViewModel.WordLists.Where(l => l.Id != sourceList.Id).ToList();
                if (otherLists.Count == 0)
                {
                    var tip = new ContentDialog
                    {
                        Title = loc.GetString("DialogTitleMergeTip", "合并提示"),
                        Content = DialogHelper.CreateTextBlockContent(loc.GetString("DialogMsgNoOtherListsToMerge", "当前没有其他词单可供合并。")),
                        CloseButtonText = loc.GetString("ButtonConfirm", "确定"),
                        XamlRoot = this.XamlRoot
                    };
                    MainWindow.RegisterActiveDialog(tip);
                    await tip.ShowAsync();
                    MainWindow.UnregisterActiveDialog(tip);
                    return;
                }

                var comboBox = new ComboBox
                {
                    ItemsSource = otherLists,
                    DisplayMemberPath = "Name",
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var stack = new StackPanel { Spacing = 8 };
                stack.Children.Add(new TextBlock { Text = string.Format(loc.GetString("DialogMsgSelectTargetListFormat", "请选择将「{0}」合并入哪个目标词单："), sourceList.Name) });
                stack.Children.Add(comboBox);

                var dialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleMergeList", "合并词单"),
                    Content = stack,
                    PrimaryButtonText = loc.GetString("ButtonMerge", "合并"),
                    CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                MainWindow.RegisterActiveDialog(dialog);
                var res = await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);

                if (res == ContentDialogResult.Primary && comboBox.SelectedItem is WordList targetList)
                {
                    await ViewModel.MergeWordListsAsync(sourceList, targetList);
                }
            }
        }

        private async void MenuListExport_Click(object sender, RoutedEventArgs e)
        {
            var list = (sender as MenuFlyoutItem)?.Tag as WordList ?? CustomWordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
            if (list != null)
            {
                await ViewModel.ExportWordListToWebAsync(list);
            }
        }

        private async void MenuListDissolve_Click(object sender, RoutedEventArgs e)
        {
            var list = (sender as MenuFlyoutItem)?.Tag as WordList ?? CustomWordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
            if (list != null)
            {
                var loc = LocalizationService.Instance;
                var confirmDialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleDissolveList", "确认解散词单"),
                    Content = DialogHelper.CreateTextBlockContent(string.Format(loc.GetString("DialogMsgDissolveList", "确定要解散词单「{0}」吗？\n解散后单词本身不会丢失，将重新恢复为未归档状态，并依原来导入的时间进行排序。"), list.Name)),
                    PrimaryButtonText = loc.GetString("ButtonConfirmDissolve", "确认解散"),
                    CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                MainWindow.RegisterActiveDialog(confirmDialog);
                var res = await confirmDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(confirmDialog);

                if (res == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteWordListAsync(list);
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typed) return typed;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
        #endregion

        #region 多选与右键菜单状态管理
        private void WordsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int count = WordsListView.SelectedItems.Count;
            var loc = LocalizationService.Instance;
            SelectedCountTextBlock.Text = string.Format(loc.GetString("SelectedCountFormat", "已选 {0} 项"), count);
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            WordsListView.SelectAll();
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            WordsListView.SelectedItems.Clear();
        }

        private void MenuCopyLines_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0 && WordsListView.SelectedItem is Word sw)
            {
                selected.Add(sw);
            }
            if (selected.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            foreach (var w in selected)
            {
                if (!string.IsNullOrWhiteSpace(w.Kanji))
                {
                    sb.AppendLine(w.Kanji);
                }
            }
            string text = sb.ToString().TrimEnd('\r', '\n');
            if (string.IsNullOrEmpty(text)) return;

            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            SoundService.Instance.PlayCopySound();
        }

        private async void MenuFormatExport_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0 && WordsListView.SelectedItem is Word sw)
            {
                selected.Add(sw);
            }
            if (selected.Count == 0)
            {
                selected = ViewModel.DisplayWords.ToList();
            }
            if (selected.Count == 0) return;

            var dialog = new Dialogs.FormatExportDialog(selected)
            {
                XamlRoot = this.XamlRoot
            };

            MainWindow.RegisterActiveDialog(dialog);
            await dialog.ShowAsync();
            MainWindow.UnregisterActiveDialog(dialog);
        }

        private async void MenuSetStateNew_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.BatchUpdateStateAsync(selected, WordLearningState.New);
        }

        private async void MenuSetStateMastered_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.BatchUpdateStateAsync(selected, WordLearningState.Mastered);
        }

        private async void MenuReimportToday_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.ReimportWordsTodayAsync(selected);
        }

        private async void MenuDeleteWords_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            var loc = LocalizationService.Instance;
            var dialog = new ContentDialog
            {
                Title = loc.GetString("DialogTitleDeleteWords", "删除所选单词"),
                Content = DialogHelper.CreateTextBlockContent(string.Format(loc.GetString("DialogMsgDeleteWordsFormat", "确定要永久删除所选的 {0} 个单词吗？\n此操作将同时删除相关复习记录且不可撤销。"), selected.Count)),
                PrimaryButtonText = loc.GetString("ButtonConfirmDelete", "确认删除"),
                CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteWordsAsync(selected);
            }
        }

        private async void PackSliceToList_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedNode == null) return;

            var loc = LocalizationService.Instance;
            var inputTextBox = new TextBox
            {
                PlaceholderText = loc.GetString("PlaceholderNewListName", "请输入新词单名称..."),
                Text = $"{loc.GetString("DefaultSliceListNamePrefix", "切片词单")}_{ViewModel.SelectedNodeTitle.Split(' ')[0]}"
            };

            var dialog = new ContentDialog
            {
                Title = loc.GetString("DialogTitlePackSlice", "打包当前切片为新词单"),
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = string.Format(loc.GetString("DialogMsgPackSliceFormat", "即将把时间切片「{0}」下的所有词汇打包为新词单："), ViewModel.SelectedNodeTitle) },
                        inputTextBox
                    }
                },
                PrimaryButtonText = loc.GetString("ButtonConfirmPack", "确认打包"),
                CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                await ViewModel.PackCurrentSliceToListAsync(inputTextBox.Text.Trim());
            }
        }

        private async void BatchCreateNewList_Click(object sender, RoutedEventArgs e)
        {
            var selected = WordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            var loc = LocalizationService.Instance;
            var inputTextBox = new TextBox
            {
                PlaceholderText = loc.GetString("PlaceholderNewListName", "请输入新词单名称..."),
                Text = $"{loc.GetString("DefaultListNamePrefix", "自选词单")}_{DateTime.Now:MMdd_HHmmss}"
            };

            var dialog = new ContentDialog
            {
                Title = loc.GetString("DialogTitleCreateCustomList", "创建自选专属词单"),
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = string.Format(loc.GetString("DialogMsgPackSelectionFormat", "即将把当前选中的 {0} 个单词打包为新词单："), selected.Count) },
                        inputTextBox
                    }
                },
                PrimaryButtonText = loc.GetString("ButtonConfirmCreate", "确认创建"),
                CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                await ViewModel.CreateWordListFromWordsAsync(inputTextBox.Text.Trim(), selected.Select(w => w.Id));
            }
        }
        #endregion

        #region 滑动多选与反选手势交互 (Drag/Swipe-to-Select)
        private bool _isDragSelecting;
        private bool _dragSelectTargetState; // true = 正在批量勾选, false = 正在批量反选/取消勾选

        private void WordItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(sender as UIElement);
            if (sender is FrameworkElement fe && fe.DataContext is Word word)
            {
                if (ptr.Properties.IsMiddleButtonPressed)
                {
                    ClipboardHelper.CopyText(word.Text);
                    e.Handled = true;
                    return;
                }

                if (ptr.Properties.IsLeftButtonPressed)
                {
                    _isDragSelecting = true;
                    bool currentlySelected = WordsListView.SelectedItems.Contains(word);
                    _dragSelectTargetState = !currentlySelected;

                    if (_dragSelectTargetState)
                    {
                        if (!WordsListView.SelectedItems.Contains(word))
                        {
                            WordsListView.SelectedItems.Add(word);
                        }
                    }
                    else
                    {
                        WordsListView.SelectedItems.Remove(word);
                    }
                    e.Handled = true;
                }
            }
        }

        private void WordItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragSelecting && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                if (sender is FrameworkElement fe && fe.DataContext is Word word)
                {
                    if (_dragSelectTargetState)
                    {
                        if (!WordsListView.SelectedItems.Contains(word))
                        {
                            WordsListView.SelectedItems.Add(word);
                        }
                    }
                    else
                    {
                        WordsListView.SelectedItems.Remove(word);
                    }
                }
            }
        }

        private void WordItem_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragSelecting = false;
        }

        private void WordsListView_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragSelecting = false;
        }
        #endregion

        #region 右侧过滤工具栏自适应缩放响应式布局
        private void RightDetailGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateFilterBarLayout(e.NewSize.Width);
        }

        private void UpdateFilterBarLayout(double width = -1)
        {
            if (FilterBarGrid == null || WordSearchBox == null || StateFilterScrollViewer == null) return;

            if (width <= 0)
            {
                width = RightDetailGrid != null && RightDetailGrid.ActualWidth > 0 ? RightDetailGrid.ActualWidth : 1000;
            }

            // 当右侧可用宽度小于 960 时，折行为上下两行，彻底避免相互遮挡与挤压
            bool isCompact = width < 960;

            if (isCompact)
            {
                // 折行模式：单选按钮占满第一行，搜索框占满第二行
                Grid.SetRow(StateFilterScrollViewer, 0);
                Grid.SetColumn(StateFilterScrollViewer, 0);
                Grid.SetColumnSpan(StateFilterScrollViewer, 2);

                Grid.SetRow(WordSearchBox, 1);
                Grid.SetColumn(WordSearchBox, 0);
                Grid.SetColumnSpan(WordSearchBox, 2);

                WordSearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                WordSearchBox.Width = double.NaN;
                WordSearchBox.Margin = new Thickness(0, 10, 0, 0);

                if (SearchCol != null) SearchCol.Width = new GridLength(0);
                if (SearchRow != null) SearchRow.Height = GridLength.Auto;

                // 紧凑模式下提示文字简写
                if (ContextMenuHintTextBlock != null)
                {
                    ContextMenuHintTextBlock.Text = LocalizationService.Instance.GetString("RightClickMenuTip", "右键已选词弹出菜单");
                }
            }
            else
            {
                // 宽屏模式：单选按钮在左，搜索框在右并排
                Grid.SetRow(StateFilterScrollViewer, 0);
                Grid.SetColumn(StateFilterScrollViewer, 0);
                Grid.SetColumnSpan(StateFilterScrollViewer, 1);

                Grid.SetRow(WordSearchBox, 0);
                Grid.SetColumn(WordSearchBox, 1);
                Grid.SetColumnSpan(WordSearchBox, 1);

                WordSearchBox.HorizontalAlignment = HorizontalAlignment.Right;
                WordSearchBox.Width = 220;
                WordSearchBox.Margin = new Thickness(12, 0, 0, 0);

                if (SearchCol != null) SearchCol.Width = GridLength.Auto;
                if (SearchRow != null) SearchRow.Height = new GridLength(0);

                if (ContextMenuHintTextBlock != null)
                {
                    ContextMenuHintTextBlock.Text = LocalizationService.Instance.GetString("RightClickActionHint", "多选单词后右键已选词弹出操作菜单");
                }
            }
        }
        #endregion
    }
}
