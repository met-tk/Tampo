$pages = @(
    'AllWordsPage',
    'ActivityCalendarPage',
    'MemoryAnalyticsPage',
    'RecentStudyPage',
    'StudyPage',
    'TodayStudyPage'
)

$viewsDir = 'X:\AI project\Tampo\Views'

foreach ($page in $pages) {
    $file = Join-Path $viewsDir ($page + '.xaml.cs')
    Write-Host "Checking: $file"
    if (-not (Test-Path $file)) { Write-Host "  SKIP (not found)"; continue }

    $content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

    if ($content.Contains('NavigationCacheMode')) {
        Write-Host "  ALREADY PATCHED"
        continue
    }

    $patch = 'this.InitializeComponent();' + "`r`n            " + 'this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;'
    $content = $content.Replace('this.InitializeComponent();', $patch)

    [System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
    Write-Host "  PATCHED OK"
}

Write-Host "Done."
