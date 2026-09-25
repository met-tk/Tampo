using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public partial class StatisticsViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;

        [ObservableProperty]
        private DashboardStats _stats = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private Dictionary<DateTime, int> _importHeatmapData = new();

        [ObservableProperty]
        private Dictionary<DateTime, int> _studyHeatmapData = new();

        public ObservableCollection<Word> SearchResults { get; } = new();

        public StatisticsViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task LoadDataAsync()
        {
            Stats = await _databaseService.GetDashboardStatsAsync();
            ImportHeatmapData = await _databaseService.GetImportHeatmapDataAsync();
            StudyHeatmapData = await _databaseService.GetStudyHeatmapDataAsync();

            await SearchAsync();
        }

        partial void OnSearchQueryChanged(string value)
        {
            _ = SearchAsync();
        }

        [RelayCommand]
        public async Task SearchAsync()
        {
            var words = await _databaseService.SearchWordsAsync(SearchQuery);
            SearchResults.Clear();
            foreach (var w in words)
            {
                SearchResults.Add(w);
            }
        }
    }
}
