using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class StatisticsPage : Page
    {
        public StatisticsViewModel ViewModel => App.GetService<StatisticsViewModel>();

        public StatisticsPage()
        {
            this.InitializeComponent();
            this.Loaded += StatisticsPage_Loaded;
        }

        private async void StatisticsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadDataAsync();
            ImportHeatmapControl?.RenderGrid();
            StudyHeatmapControl?.RenderGrid();
        }
    }
}
