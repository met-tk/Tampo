$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$buildRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'

function Assert-SafeGeneratedDirectory([string]$path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated directory is outside the project: $fullPath"
    }
    $current = $fullPath
    while ($current.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Generated directory contains a link or junction: $current"
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

Write-Host "[0/5] Ensuring previous processes are closed..." -ForegroundColor Cyan
Get-Process | Where-Object {
    if ($_.ProcessName -notmatch '^(Tampo.*|NihongoVocab|Setup)$') { return $false }
    try {
        $exePath = $_.Path
        return $exePath -and [IO.Path]::GetFullPath($exePath).StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
} | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

Write-Host "[1/5] Cleaning old build caches and regenerating icons..." -ForegroundColor Cyan
@("$root\bin", "$root\obj", "$root\Installer\bin", "$root\Installer\obj") | ForEach-Object { Assert-SafeGeneratedDirectory $_ }
if (Test-Path "$root\bin")                 { Remove-Item "$root\bin"                 -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$root\obj")                 { Remove-Item "$root\obj"                 -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$root\Installer\bin")       { Remove-Item "$root\Installer\bin"       -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$root\Installer\obj")       { Remove-Item "$root\Installer\obj"       -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "  Generating smooth rounded icons from original ta.png..." -ForegroundColor DarkGray
python "$root\tools\generate_icons.py"
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed." }

Write-Host "[2/5] Publishing Release..." -ForegroundColor Cyan
dotnet publish "$root\Tampo.csproj" -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

Write-Host "[3/5] Syncing to publish directory..." -ForegroundColor Cyan
$binDir = "$root\bin\Release\net8.0-windows10.0.26100.0\win-x64"
$src    = "$binDir\publish"
$dst    = "$root\publish"
if (-not (Test-Path -LiteralPath $src -PathType Container)) { throw "Application publish output is missing: $src" }
Assert-SafeGeneratedDirectory $src
Assert-SafeGeneratedDirectory $dst
if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst | Out-Null }
robocopy $src $dst /MIR /XJ /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Publish directory sync failed with code $LASTEXITCODE." }

Write-Host "[4/5] Copying XBFs, Views, Controls, Assets and PRI indexes..." -ForegroundColor Cyan
if (Test-Path "$binDir\Views")            { Copy-Item "$binDir\Views"            "$dst\Views"            -Recurse -Force }
if (Test-Path "$binDir\Controls")         { Copy-Item "$binDir\Controls"         "$dst\Controls"         -Recurse -Force }
if (Test-Path "$binDir\App.xbf")          { Copy-Item "$binDir\App.xbf"          "$dst\App.xbf"          -Force }
if (Test-Path "$binDir\MainWindow.xbf")   { Copy-Item "$binDir\MainWindow.xbf"   "$dst\MainWindow.xbf"   -Force }
if (Test-Path "$binDir\Tampo.pri") {
    Copy-Item "$binDir\Tampo.pri" "$dst\Tampo.pri" -Force
    Copy-Item "$binDir\Tampo.pri" "$dst\resources.pri" -Force
} elseif (Test-Path "$binDir\NihongoVocab.pri") {
    Copy-Item "$binDir\NihongoVocab.pri" "$dst\NihongoVocab.pri" -Force
    Copy-Item "$binDir\NihongoVocab.pri" "$dst\resources.pri"    -Force
}
Assert-SafeGeneratedDirectory "$dst\Assets"
if (Test-Path "$dst\Assets") { Remove-Item "$dst\Assets" -Recurse -Force }
Copy-Item "$root\Assets" "$dst\Assets" -Recurse -Force

Write-Host "Publishing TampoSyncService..." -ForegroundColor Cyan
dotnet publish "$root\SyncService\TampoSyncService.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$dst" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Sync service publish failed." }

Write-Host "[5/5] Cleaning up unnecessary files from publish directory..." -ForegroundColor Cyan
Get-ChildItem -Path $dst -File -Recurse |
    Where-Object { $_.Extension -in ".bat",".cmd",".pdb",".log" } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Done! Publish complete at $dst" -ForegroundColor Green

# ---- Prepare Setup output directory ----------------------------------------
$version = "1.5.2"
$csprojContent = Get-Content "$root\Tampo.csproj" -Raw
if ($csprojContent -match '<Version>([0-9\.]+)</Version>') {
    $version = $matches[1]
}
Write-Host "Target packaging version: v$version" -ForegroundColor Cyan

$setupDir       = "$root\Setup"
$setupExeFinal  = "$setupDir\Tampo_Setup_v$version.exe"
$updateExeFinal = "$setupDir\Tampo_Update_v$version.exe"

Assert-SafeGeneratedDirectory $setupDir
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

# ---- Pack payload and compile installer ------------------------------------
$payloadZip = "$root\Installer\payload.zip"
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue }

Write-Host "  Packing application payload into installer archive..." -ForegroundColor DarkGray
Compress-Archive -Path "$dst\*" -DestinationPath $payloadZip -CompressionLevel Optimal

Write-Host "  Compiling standalone single-file installer..." -ForegroundColor DarkGray
# Pass AssemblyName directly so the output exe is already correctly named -- no rename step needed
dotnet publish "$root\Installer\Tampo.Installer.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:AssemblyName="Tampo_Setup_v$version" `
    -o $setupDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed." }

# ---- Clean intermediate artifacts ------------------------------------------
if (Test-Path $payloadZip)           { Remove-Item $payloadZip           -Force -ErrorAction SilentlyContinue }
Get-ChildItem -Path $setupDir -File | Where-Object { $_.Extension -eq ".pdb" } | Remove-Item -Force -ErrorAction SilentlyContinue

if (Test-Path $setupExeFinal) {
    Copy-Item $setupExeFinal $updateExeFinal -Force
    Write-Host "Done! Publish complete at $dst" -ForegroundColor Green
    Write-Host "Done! Setup installer generated at $setupExeFinal" -ForegroundColor Green
    Write-Host "Done! Update executable generated at $updateExeFinal" -ForegroundColor Green
} else {
    Write-Warning "Installer exe not found at expected path: $setupExeFinal"
    Write-Host "Files in Setup dir:" -ForegroundColor Yellow
    Get-ChildItem -Path $setupDir | Select-Object Name | Format-Table
}
