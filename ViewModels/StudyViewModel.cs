using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public partial class StudyViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;
        private readonly HashSet<int> _sessionReviewedIds = new();

        [ObservableProperty]
        private int _completedCount = 0;

        [ObservableProperty]
        private int _lapsedCount = 0;

        [ObservableProperty]
        private bool _isSessionFinished = false;

        [ObservableProperty]
        private WordList? _selectedList;

        private readonly WordList _allDueWordsOption = new WordList
        {
            Id = -1,
            Name = LocalizationService.Instance.GetString("AllDueWordsOption", "全部到期词汇")
        };

        public ObservableCollection<WordList> AvailableLists { get; } = new();
        public ObservableCollection<StudyCardItem> Cards { get; } = new();

        public string AllDueWordsPlaceholder => LocalizationService.Instance.GetString("AllDueWordsPlaceholder", "全部到期词汇");

        public StudyViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
            SelectedList = _allDueWordsOption;

            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                _allDueWordsOption.Name = LocalizationService.Instance.GetString("AllDueWordsOption", "全部到期词汇");
                OnPropertyChanged(string.Empty);
            };

            DatabaseService.DataChanged += OnDatabaseDataChanged;
        }

        private bool _isInternalReviewUpdate;

        private void OnDatabaseDataChanged()
        {
            if (_isInternalReviewUpdate) return;
            var dispatcher = App.UIThreadDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(async () =>
                {
                    // 实时智能同步待学队列（无缝追加新导入词汇，同步更新卡片数与完成状态）
                    await SyncSessionQueueAsync();
                });
            }
        }

        public async Task SyncSessionQueueAsync()
        {
            if (_isLoadingSession) return;
            try
            {
                int? targetId = (SelectedList?.Id != -1) ? SelectedList?.Id : null;
                var latestWords = await _databaseService.GetStudyQueueAsync(targetId);
                var latestDict = latestWords.ToDictionary(w => w.Id);

                // 1. 移除非过渡态且已不在待学队列中的卡片（或本轮会话已完成的卡片，防止跨午夜 00:00 时回流）
                for (int i = Cards.Count - 1; i >= 0; i--)
                {
                    var card = Cards[i];
                    if (card.State == CardInteractionState.Normal &&
                        (!latestDict.ContainsKey(card.Word.Id) || _sessionReviewedIds.Contains(card.Word.Id)))
                    {
                        Cards.RemoveAt(i);
                    }
                }

                // 2. 将最新待学词中尚未进入 Cards 队列且本轮会话尚未打卡的新词实时追加进队列
                var existingCardWordIds = Cards.Select(c => c.Word.Id).ToHashSet();
                foreach (var w in latestWords)
                {
                    if (!existingCardWordIds.Contains(w.Id) && !_sessionReviewedIds.Contains(w.Id))
                    {
                        Cards.Add(new StudyCardItem(w));
                    }
                }

                // 3. 动态更新完成状态（绝不清空当前会话已累计的 CompletedCount 与 LapsedCount）
                IsSessionFinished = Cards.Count == 0;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyViewModel.SyncSessionQueueAsync");
            }
        }

        private bool _isLoadingSession = false;

        public async Task LoadSessionAsync(int? listId = null)
        {
            if (_isLoadingSession) return;
            _isLoadingSession = true;

            try
            {
                var lists = await _databaseService.GetAllWordListsAsync();
                
                var allLists = new List<WordList> { _allDueWordsOption };
                allLists.AddRange(lists);

                // 智能更新 AvailableLists，避免暴力 Clear 导致 ComboBox SelectedItem 丢失并引发死循环 SelectionChanged
                var currentIds = AvailableLists.Select(l => l.Id).ToHashSet();
                var newIds = allLists.Select(l => l.Id).ToHashSet();

                if (!currentIds.SetEquals(newIds))
                {
                    var prevSelectedId = SelectedList?.Id ?? -1;
                    AvailableLists.Clear();
                    foreach (var l in allLists)
                    {
                        AvailableLists.Add(l);
                    }

                    SelectedList = AvailableLists.FirstOrDefault(l => l.Id == prevSelectedId) ?? _allDueWordsOption;
                }
                else if (SelectedList == null)
                {
                    SelectedList = _allDueWordsOption;
                }

                int? targetId = (listId.HasValue && listId.Value != -1) ? listId.Value : (SelectedList?.Id != -1 ? SelectedList?.Id : null);
                var words = await _databaseService.GetStudyQueueAsync(targetId);

                _sessionReviewedIds.Clear();
                Cards.Clear();
                foreach (var w in words)
                {
                    Cards.Add(new StudyCardItem(w));
                }

                CompletedCount = 0;
                LapsedCount = 0;
                IsSessionFinished = Cards.Count == 0;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyViewModel.LoadSessionAsync");
            }
            finally
            {
                _isLoadingSession = false;
            }
        }

        /// <summary>
        /// 处理卡片左键点击（记得逻辑分支）
        /// </summary>
        public async Task OnCardLeftClickAsync(StudyCardItem card)
        {
            try
            {
                if (card.State == CardInteractionState.PendingRemember)
                {
                    // 第二次左键：确认记得！
                    await ConfirmRememberAsync(card);
                }
                else if (card.State == CardInteractionState.PendingForget)
                {
                    // 反向按键：重置过渡态
                    card.ResetState();
                }
                else
                {
                    // 第一次左键：进入待确认记得状态，并重置其他卡片的过渡态
                    ResetOtherPending(card);
                    card.State = CardInteractionState.PendingRemember;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyViewModel.OnCardLeftClickAsync");
            }
        }

        /// <summary>
        /// 处理卡片右键点击（遗忘重学逻辑分支）
        /// </summary>
        public async Task OnCardRightClickAsync(StudyCardItem card)
        {
            try
            {
                if (card.State == CardInteractionState.PendingForget)
                {
                    // 第二次右键：确认遗忘并重学！
                    await ConfirmForgetAsync(card);
                }
                else if (card.State == CardInteractionState.PendingRemember)
                {
                    // 反向按键：重置过渡态
                    card.ResetState();
                }
                else
                {
                    // 第一次右键：进入待确认遗忘状态，并重置其他卡片
                    ResetOtherPending(card);
                    card.State = CardInteractionState.PendingForget;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyViewModel.OnCardRightClickAsync");
            }
        }

        private async Task ConfirmRememberAsync(StudyCardItem card)
        {
            _sessionReviewedIds.Add(card.Word.Id);
            CompletedCount++;
            Cards.Remove(card);
            if (Cards.Count == 0)
            {
                IsSessionFinished = true;
            }

            // 触发 FSRS 算法计算新的稳定性与间隔 (Rating 3 = Good)
            try { _isInternalReviewUpdate = true; await _databaseService.UpdateWordFSRSAsync(card.Word, 3, DateTime.Now); } finally { _isInternalReviewUpdate = false; }
            SoundService.Instance.PlayStudyMarkSound();
        }

        private async Task ConfirmForgetAsync(StudyCardItem card)
        {
            _sessionReviewedIds.Add(card.Word.Id);
            LapsedCount++;
            card.ResetState();
            // 确认遗忘后完成今日该卡片的复习处理（记为遗忘进入学习中，下次排期至明天），绝不再塞回队尾死循环
            Cards.Remove(card);
            if (Cards.Count == 0)
            {
                IsSessionFinished = true;
            }

            // 触发 FSRS 算法计算遗忘更新 (Rating 1 = Again)
            try { _isInternalReviewUpdate = true; await _databaseService.UpdateWordFSRSAsync(card.Word, 1, DateTime.Now); } finally { _isInternalReviewUpdate = false; }
            SoundService.Instance.PlayStudyMarkSound();
        }

        public void ResetOtherPending(StudyCardItem? activeCard = null)
        {
            foreach (var item in Cards)
            {
                if (item != activeCard && item.State != CardInteractionState.Normal)
                {
                    item.ResetState();
                }
            }
        }
    }
}
