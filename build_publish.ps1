$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "[0/5] Ensuring previous processes are closed..." -ForegroundColor Cyan
Get-Process -Name "NihongoVocab","Setup","Tampo_Setup_v1.0.0","Tampo_Setup_v1.1.0" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

Write-Host "[1/5] Cleaning old build caches and regenerating icons..." -ForegroundColor Cyan
if (Test-Path "$root\bin")                 { Remove-Item "$root\bin"                 -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$root\obj")                 { Remove-Item "$root\obj"                 -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$root\Installer\bin")       { Remove-Item "$root\Installer\bin"       -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$root\Installer\obj")       { Remove-Item "$root\Installer\obj"       -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "  Generating smooth rounded icons from original ta.png..." -ForegroundColor DarkGray
python "$root\tools\generate_icons.py"

Write-Host "[2/5] Publishing Release..." -ForegroundColor Cyan
dotnet publish "$root\NihongoVocab.csproj" -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false

Write-Host "[3/5] Syncing to publish directory..." -ForegroundColor Cyan
$binDir = "$root\bin\Release\net8.0-windows10.0.26100.0\win-x64"
$src    = "$binDir\publish"
$dst    = "$root\publish"
if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst | Out-Null }
robocopy $src $dst /MIR /R:1 /W:1 | Out-Null

Write-Host "[4/5] Copying XBFs, Views, Controls, Assets and PRI indexes..." -ForegroundColor Cyan
if (Test-Path "$binDir\Views")            { Copy-Item "$binDir\Views"            "$dst\Views"            -Recurse -Force }
if (Test-Path "$binDir\Controls")         { Copy-Item "$binDir\Controls"         "$dst\Controls"         -Recurse -Force }
if (Test-Path "$binDir\App.xbf")          { Copy-Item "$binDir\App.xbf"          "$dst\App.xbf"          -Force }
if (Test-Path "$binDir\MainWindow.xbf")   { Copy-Item "$binDir\MainWindow.xbf"   "$dst\MainWindow.xbf"   -Force }
if (Test-Path "$binDir\NihongoVocab.pri") {
    Copy-Item "$binDir\NihongoVocab.pri" "$dst\NihongoVocab.pri" -Force
    Copy-Item "$binDir\NihongoVocab.pri" "$dst\resources.pri"    -Force
}
if (Test-Path "$dst\Assets") { Remove-Item "$dst\Assets" -Recurse -Force }
Copy-Item "$root\Assets" "$dst\Assets" -Recurse -Force

Write-Host "[5/5] Cleaning up unnecessary files from publish directory..." -ForegroundColor Cyan
Get-ChildItem -Path $dst -File -Recurse |
    Where-Object { $_.Extension -in ".bat",".cmd",".pdb",".log" } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Done! Publish complete at $dst" -ForegroundColor Green

# ---- Prepare Setup output directory ----------------------------------------
$setupDir      = "$root\Setup"
$setupExeFinal = "$setupDir\Tampo_Setup_v1.1.0.exe"

if (Test-Path $setupDir) {
    Remove-Item $setupDir -Recurse -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

# ---- Pack payload and compile installer ------------------------------------
$payloadZip = "$root\Installer\payload.zip"
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue }

Write-Host "  Packing application payload into installer archive..." -ForegroundColor DarkGray
Compress-Archive -Path "$dst\*" -DestinationPath $payloadZip -CompressionLevel Optimal

Write-Host "  Compiling standalone single-file installer..." -ForegroundColor DarkGray
# Pass AssemblyName directly so the output exe is already correctly named -- no rename step needed
dotnet publish "$root\Installer\NihongoVocab.Installer.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:AssemblyName=Tampo_Setup_v1.1.0 `
    -o $setupDir | Out-Null

# ---- Clean intermediate artifacts ------------------------------------------
if (Test-Path $payloadZip)           { Remove-Item $payloadZip           -Force -ErrorAction SilentlyContinue }
Get-ChildItem -Path $setupDir -File | Where-Object { $_.Extension -eq ".pdb" } | Remove-Item -Force -ErrorAction SilentlyContinue

if (Test-Path $setupExeFinal) {
    Write-Host "Done! Publish complete at $dst" -ForegroundColor Green
    Write-Host "Done! Setup installer generated at $setupExeFinal" -ForegroundColor Green
} else {
    Write-Warning "Installer exe not found at expected path: $setupExeFinal"
    Write-Host "Files in Setup dir:" -ForegroundColor Yellow
    Get-ChildItem -Path $setupDir | Select-Object Name | Format-Table
}