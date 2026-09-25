using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class WordListPage : Page
    {
        public WordListViewModel ViewModel => App.GetService<WordListViewModel>();

        public ObservableCollection<Word> FilteredWords { get; } = new();

        public Visibility GetListSelectedVisibility(bool isCustomListSelected)
        {
            return isCustomListSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool _isActionBusy = false;
        private DateTime _lastActionClickTime = DateTime.MinValue;

        private bool _isResizing;
        private double _startPointerX;
        private double _startColumnWidth;
        private string _searchFilter = string.Empty;

        public WordListPage()
        {
            this.InitializeComponent();
            // 关键：确保导航时复用同一页面实例，避免多次构造
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            RestorePreferences();
            this.Loaded += WordListPage_Loaded;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedStrings();

            // --- 顶部工具栏 ---
            ExportWebButton.Click += ExportWeb_Click;

            // --- 时间树操作 ---
            DimensionComboBox.SelectionChanged += DimensionComboBox_SelectionChanged;
            ExpandAllButton.Click += ExpandAll_Click;
            CollapseAllButton.Click += CollapseAll_Click;
            FlatLevelModeCheckBox.Click += FlatLevelModeCheckBox_Click;
            DateHierarchyTreeView.ItemInvoked += DateHierarchyTreeView_ItemInvoked;
            CreateFromTreeButton.Click += CreateFromTree_Click;

            // --- 词单列表 ---
            WordListsListView.SelectionChanged += WordListsListView_SelectionChanged;
            WordListsListView.ContextRequested += (sender, args) =>
            {
                if (args.OriginalSource is DependencyObject dep)
                {
                    var item = FindVisualParent<ListViewItem>(dep);
                    if (item != null && WordListsListView.ItemFromContainer(item) is WordList targetList)
                    {
                        WordListsListView.SelectedItem = targetList;
                        ViewModel.SelectedWordList = targetList;
                        _ = ViewModel.SelectWordListAsync(targetList);
                        MenuRenameList.Tag = targetList;
                        MenuMergeList.Tag = targetList;
                        MenuExportList.Tag = targetList;
                        MenuDissolveList.Tag = targetList;
                    }
                }
            };
            MenuRenameList.Click += MenuRenameList_Click;
            MenuMergeList.Click += MenuMergeList_Click;
            MenuExportList.Click += MenuExportList_Click;
            MenuDissolveList.Click += MenuDissolveList_Click;
            MenuTopDissolveList.Click += MenuDissolveList_Click;
            MenuTopRenameList.Click += MenuRenameList_Click;

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

            // --- 右侧搜索与多选 ---
            WordSearchBox.TextChanged += WordSearchBox_TextChanged;
            SelectAllCheckBox.Checked += SelectAllCheckBox_Checked;
            SelectAllCheckBox.Unchecked += SelectAllCheckBox_Unchecked;
            BatchCreateNewListButton.Click += BatchCreateNewList_Click;
            RemoveFromListButton.Click += BatchRemoveFromList_Click;

            // --- 单词列表 ---
            ActiveWordsListView.SelectionChanged += ActiveWordsListView_SelectionChanged;
            ActiveWordsListView.PointerReleased += ActiveWordsListView_PointerReleased;
            ContextMenuCreateNewList.Click += ContextMenuCreateNewList_Click;

            ViewModel.TreeRebuilt += () => DispatcherQueue.TryEnqueue(RefreshTreeRootNodes);
            ViewModel.ActiveWords.CollectionChanged += (s, e) => ApplySearchFilter();
            ViewModel.WordLists.CollectionChanged += (s, e) => UpdateBatchMoveFlyout();
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WordListViewModel.IsFlatLevelMode) ||
                    e.PropertyName == nameof(WordListViewModel.SelectedDimension))
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
            if (TreeListNameInput != null) TreeListNameInput.PlaceholderText = loc.GetString("TreeListNameInputPlaceholder", "输入打包后的新词单名称...");
            if (SelectAllCheckBox != null) SelectAllCheckBox.Content = loc.GetString("SelectAll", "全选当前列表");
            if (ManageCurrentListText != null) ManageCurrentListText.Text = loc.GetString("ButtonManageList", "管理词单");
            if (MenuRenameList != null) MenuRenameList.Text = loc.GetString("MenuRenameList", "重命名词单");
            if (TopMenuRenameList != null) TopMenuRenameList.Text = loc.GetString("MenuRenameList", "重命名词单");
            if (MenuMergeList != null) MenuMergeList.Text = loc.GetString("MenuMergeList", "合并至其他词单...");
            if (TopMenuMergeList != null) TopMenuMergeList.Text = loc.GetString("MenuMergeList", "合并至其他词单...");
            if (MenuExportList != null) MenuExportList.Text = loc.GetString("MenuExportList", "在浏览器中查看");
            if (MenuDissolveList != null) MenuDissolveList.Text = loc.GetString("MenuDissolveList", "解散词单（词汇恢复未归档）");
            if (TopMenuDissolveList != null) TopMenuDissolveList.Text = loc.GetString("MenuDissolveList", "解散词单（词汇恢复未归档）");
            if (ContextMenuCreateNewList != null) ContextMenuCreateNewList.Text = loc.GetString("MenuCreateListFromSelected", "将已选单词新建为词单...");

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

            RefreshTreeRootNodes();
            ApplySearchFilter();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // 离开页面时彻底注销事件监听，防止后台隐式刷新与重复执行
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
        }

        private void RestorePreferences()
        {
            try
            {
                var pref = UserPreferenceService.Instance;

                // 1. 恢复时间维度下拉
                string savedDim = pref.Get("WordLists_Dimension", "Month");
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
                ViewModel.IsFlatLevelMode = pref.GetBool("WordLists_IsFlatLevelMode", false);

                // 3. 恢复分割条尺寸
                double colWidth = pref.GetDouble("WordLists_MasterColumnWidth", 360);
                if (colWidth >= 260 && colWidth <= 750)
                {
                    MasterColumn.Width = new GridLength(colWidth);
                }

                double rowHeight = pref.GetDouble("WordLists_TreeRowHeight", 0);
                if (rowHeight >= 140)
                {
                    TreeRow.Height = new GridLength(rowHeight);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.RestorePreferences");
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            try
            {
                UpdateLocalizedStrings();
                DatabaseService.DataChanged -= OnDatabaseDataChanged;
                DatabaseService.DataChanged += OnDatabaseDataChanged;

                await ViewModel.LoadDataAsync();
                RefreshTreeRootNodes();
                ApplySearchFilter();
                UpdateBatchMoveFlyout();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.OnNavigatedTo");
            }
        }

        private void OnDatabaseDataChanged()
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ViewModel.LoadDataAsync();
                RefreshTreeRootNodes();
                ApplySearchFilter();
                UpdateBatchMoveFlyout();
            });
        }

        private void FlatLevelModeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UserPreferenceService.Instance.SetBool("WordLists_IsFlatLevelMode", ViewModel.IsFlatLevelMode);
            RefreshTreeRootNodes();
        }

        private async void WordListPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.LoadDataAsync();
                RefreshTreeRootNodes();
                ApplySearchFilter();
                UpdateBatchMoveFlyout();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.WordListPage_Loaded");
            }
        }



        #region 分割滑块 Splitter 拖拽逻辑 (水平 ColumnSplitter 与 垂直 RowSplitter)
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
            _startColumnWidth = MasterColumn.ActualWidth;
            ColumnSplitter.CapturePointer(e.Pointer);
        }

        private void ColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizing) return;

            double currentX = e.GetCurrentPoint(LayoutRootGrid).Position.X;
            double delta = currentX - _startPointerX;
            double targetWidth = Math.Clamp(_startColumnWidth + delta, 260, 750);
            MasterColumn.Width = new GridLength(targetWidth);
        }

        private void ColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                ColumnSplitter.ReleasePointerCapture(e.Pointer);
                ProtectedCursor = null;
                UserPreferenceService.Instance.SetDouble("WordLists_MasterColumnWidth", MasterColumn.ActualWidth);
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
            double totalHeight = TreeRow.ActualHeight + ListRow.ActualHeight;
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
                UserPreferenceService.Instance.SetDouble("WordLists_TreeRowHeight", TreeRow.ActualHeight);
            }
        }
        #endregion

        #region 时间层级树与打包逻辑
        private void DimensionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DateHierarchyTreeView == null || ViewModel == null) return;
            if (DimensionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.SelectedDimension = tag;
                UserPreferenceService.Instance.Set("WordLists_Dimension", tag);
                RefreshTreeRootNodes();
            }
        }

        private void RefreshTreeRootNodes()
        {
            try
            {
                if (DateHierarchyTreeView == null || ViewModel?.DateTreeNodes == null) return;
                DateHierarchyTreeView.RootNodes.Clear();
                foreach (var dataNode in ViewModel.DateTreeNodes)
                {
                    DateHierarchyTreeView.RootNodes.Add(BuildTreeNode(dataNode));
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.RefreshTreeRootNodes");
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
            SetExpandedRecursive(DateHierarchyTreeView.RootNodes, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CollapseAll();
            SetExpandedRecursive(DateHierarchyTreeView.RootNodes, false);
        }

        private void NodeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is DateHierarchyNode node)
            {
                node.SetChecked(cb.IsChecked);
            }
        }

        private async void TreeNodeViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DateHierarchyNode node)
            {
                WordListsListView.SelectedItem = null;
                await ViewModel.SelectNodeAsync(node);
                ApplySearchFilter();
            }
        }

        private async void DateHierarchyTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var node = args.InvokedItem as DateHierarchyNode 
                       ?? (args.InvokedItem as TreeViewNode)?.Content as DateHierarchyNode;
            if (node != null)
            {
                WordListsListView.SelectedItem = null;
                await ViewModel.SelectNodeAsync(node);
                ApplySearchFilter();
            }
        }

        private async void CreateFromTree_Click(object sender, RoutedEventArgs e)
        {
            // 双重保护：ViewModel 原子锁 + UI 防抖
            if (ViewModel.IsCreatingList) return;
            if (_isActionBusy || (DateTime.Now - _lastActionClickTime).TotalMilliseconds < 800)
            {
                return;
            }

            _isActionBusy = true;
            _lastActionClickTime = DateTime.Now;

            var triggerBtn = sender as Button;
            if (triggerBtn != null) triggerBtn.IsEnabled = false;

            try
            {
                await ViewModel.CreateListFromTreeSelectionAsync();
                RefreshTreeRootNodes();
                ApplySearchFilter();
                UpdateBatchMoveFlyout();

                var loc = LocalizationService.Instance;
                var dialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleCreateTip", "创建提示"),
                    Content = DialogHelper.CreateTextBlockContent(ViewModel.StatusMessage),
                    CloseButtonText = loc.GetString("ButtonConfirm", "确定"),
                    XamlRoot = this.XamlRoot
                };
                MainWindow.RegisterActiveDialog(dialog);
                await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.CreateFromTree_Click");
            }
            finally
            {
                if (triggerBtn != null) triggerBtn.IsEnabled = true;
                _isActionBusy = false;
            }
        }
        #endregion

        #region 词单卡片交互与右键菜单
        private async void WordListsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WordListsListView.SelectedItem is WordList list)
            {
                await ViewModel.SelectWordListAsync(list);
                ApplySearchFilter();
            }
        }

        private async void MenuRenameList_Click(object sender, RoutedEventArgs e)
        {
            var list = (sender as MenuFlyoutItem)?.Tag as WordList ?? WordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
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
                    UpdateBatchMoveFlyout();
                }
            }
        }

        private async void MenuMergeList_Click(object sender, RoutedEventArgs e)
        {
            var sourceList = (sender as MenuFlyoutItem)?.Tag as WordList ?? WordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
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
                    UpdateBatchMoveFlyout();
                }
            }
        }

        private async void MenuExportList_Click(object sender, RoutedEventArgs e)
        {
            var list = (sender as MenuFlyoutItem)?.Tag as WordList ?? WordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
            if (list != null)
            {
                await ViewModel.SelectWordListAsync(list);
                await ViewModel.ExportCurrentViewToWebAsync();
            }
        }

        private async void MenuDissolveList_Click(object sender, RoutedEventArgs e)
        {
            var list = (sender as MenuFlyoutItem)?.Tag as WordList ?? WordListsListView.SelectedItem as WordList ?? ViewModel.SelectedWordList;
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
                    UpdateBatchMoveFlyout();
                }
            }
        }

        private void WordListItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is WordList list)
            {
                WordListsListView.SelectedItem = list;
                ViewModel.SelectedWordList = list;
                _ = ViewModel.SelectWordListAsync(list);

                MenuRenameList.Tag = list;
                MenuMergeList.Tag = list;
                MenuExportList.Tag = list;
                MenuDissolveList.Tag = list;
            }
        }
        #endregion

        #region 右侧明细与多选批量操作
        private void WordSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _searchFilter = sender.Text.Trim();
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            FilteredWords.Clear();
            var query = ViewModel.ActiveWords.AsEnumerable();
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                query = query.Where(w => w.Text.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var w in query)
            {
                FilteredWords.Add(w);
            }
            UpdateSelectionCount();
        }

        private void ActiveWordsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            var loc = LocalizationService.Instance;
            int count = ActiveWordsListView.SelectedItems.Count;
            SelectedCountTextBlock.Text = string.Format(loc.GetString("SelectedCountFormat", "已选 {0} 项"), count);
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ActiveWordsListView.SelectAll();
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ActiveWordsListView.SelectedItems.Clear();
        }

        private void UpdateBatchMoveFlyout()
        {
            BatchMoveMenuFlyout.Items.Clear();
            foreach (var list in ViewModel.WordLists)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = list.Name,
                    Tag = list
                };
                menuItem.Click += async (s, e) =>
                {
                    var selected = ActiveWordsListView.SelectedItems.OfType<Word>().ToList();
                    if (selected.Count == 0) return;
                    await ViewModel.BatchMoveWordsAsync(selected, list);
                    ApplySearchFilter();
                };
                BatchMoveMenuFlyout.Items.Add(menuItem);
            }
        }

        private async void BatchRemoveFromList_Click(object sender, RoutedEventArgs e)
        {
            // 严格限制：仅在选中自定义词单时生效，未归档时间层级视图下严禁生效
            if (!ViewModel.IsCustomListSelected || ViewModel.SelectedWordList == null)
            {
                return;
            }

            var selected = ActiveWordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.BatchMoveWordsAsync(selected, null);
            ActiveWordsListView.SelectedItems.Clear();
            SelectAllCheckBox.IsChecked = false;
            ApplySearchFilter();
        }

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
                    bool currentlySelected = ActiveWordsListView.SelectedItems.Contains(word);
                    _dragSelectTargetState = !currentlySelected; // 未选中则设为勾选模式；已选中则设为反选模式

                    if (_dragSelectTargetState)
                    {
                        if (!ActiveWordsListView.SelectedItems.Contains(word))
                        {
                            ActiveWordsListView.SelectedItems.Add(word);
                        }
                    }
                    else
                    {
                        ActiveWordsListView.SelectedItems.Remove(word);
                    }
                    e.Handled = true; // 拦截默认点击，防止 WinUI ListView 二次反转
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
                        if (!ActiveWordsListView.SelectedItems.Contains(word))
                        {
                            ActiveWordsListView.SelectedItems.Add(word);
                        }
                    }
                    else
                    {
                        ActiveWordsListView.SelectedItems.Remove(word);
                    }
                }
            }
        }

        private void WordItem_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragSelecting = false;
        }

        private void ActiveWordsListView_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragSelecting = false;
        }
        #endregion

        private async void BatchCreateNewList_Click(object sender, RoutedEventArgs e)
        {
            await DoCreateNewListFromSelectionAsync(sender as Button);
        }

        private async void ContextMenuCreateNewList_Click(object sender, RoutedEventArgs e)
        {
            // 右键菜单没有 Button，但逻辑完全相同
            await DoCreateNewListFromSelectionAsync(null);
        }

        private async Task DoCreateNewListFromSelectionAsync(Button? triggerBtn)
        {
            // 使用 ViewModel 的原子锁：已在创建中则直接拒绝，彻底防止重入
            if (ViewModel.IsCreatingList) return;

            // 额外 UI 防抖：800ms 内不重复
            if (_isActionBusy || (DateTime.Now - _lastActionClickTime).TotalMilliseconds < 800)
                return;

            var selected = ActiveWordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0)
            {
                var loc = LocalizationService.Instance;
                var tipDialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleCreateTip", "创建提示"),
                    Content = DialogHelper.CreateTextBlockContent(loc.GetString("DialogMsgPleaseSelectWordsToPack", "请先在右侧列表中勾选需要打包为新词单的单词！")),
                    CloseButtonText = loc.GetString("ButtonConfirm", "确定"),
                    XamlRoot = this.XamlRoot
                };
                MainWindow.RegisterActiveDialog(tipDialog);
                await tipDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(tipDialog);
                return;
            }

            _isActionBusy = true;
            _lastActionClickTime = DateTime.Now;
            if (triggerBtn != null) triggerBtn.IsEnabled = false;

            try
            {
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

                MainWindow.RegisterActiveDialog(dialog);
                var result = await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);

                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
                {
                    string listName = inputTextBox.Text.Trim();
                    await ViewModel.CreateWordListFromWordsAsync(listName, selected.Select(w => w.Id));
                    ActiveWordsListView.SelectedItems.Clear();
                    SelectAllCheckBox.IsChecked = false;
                    ApplySearchFilter();
                    UpdateBatchMoveFlyout();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.DoCreateNewListFromSelectionAsync");
            }
            finally
            {
                if (triggerBtn != null) triggerBtn.IsEnabled = true;
                _isActionBusy = false;
            }
        }

        private async void ExportWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.ExportCurrentViewToWebAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListPage.ExportWeb_Click");
            }
        }
        #endregion

        private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T parent) return parent;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }
    }
}
