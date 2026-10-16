$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

# 1. Parse version from pubspec.yaml
$pubspecPath = "$scriptDir\pubspec.yaml"
$version = "unknown"
if (Test-Path $pubspecPath) {
    $content = Get-Content $pubspecPath
    foreach ($line in $content) {
        if ($line -match '^version:\s*([0-9\.]+)') {
            $version = $matches[1]
            break
        }
    }
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building Tampo Android APK (v$version)..." -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 2. Map virtual ASCII drive to bypass gen_snapshot non-ASCII path limitation on Windows
$drive = "Z"
$substMapped = $false
try {
    $existing = (Get-PSDrive -Name $drive -ErrorAction SilentlyContinue)
    if (-not $existing) {
        cmd /c "subst $drive`: `"$scriptDir`""
        $substMapped = $true
        Set-Location "$drive`:\"
    } else {
        Set-Location $scriptDir
    }

    flutter build apk --release --no-tree-shake-icons
    if ($LASTEXITCODE -ne 0) {
        throw "flutter build apk exited with code $LASTEXITCODE"
    }
} finally {
    Set-Location $scriptDir
    if ($substMapped) {
        cmd /c "subst $drive`: /D" 2>$null
    }
}

# 3. Archive to apks directory
$apksDir = "$scriptDir\apks"
if (-not (Test-Path $apksDir)) {
    New-Item -ItemType Directory -Path $apksDir -Force | Out-Null
}

$releaseApk = "$scriptDir\build\app\outputs\flutter-apk\app-release.apk"
$targetApk = "$apksDir\Tampo_v$version.apk"

if (Test-Path $releaseApk) {
    Copy-Item -Path $releaseApk -Destination $targetApk -Force
    $sizeMb = [math]::Round(((Get-Item $targetApk).Length / 1MB), 2)
    Write-Host ""
    Write-Host "Build Successful!" -ForegroundColor Green
    Write-Host "APK output at: $targetApk ($sizeMb MB)" -ForegroundColor Green
} else {
    Write-Error "Release APK not found at: $releaseApk"
}
