# GenericDeepL MSI installer build script (WiX Toolset 6 / WixToolset.Sdk)
# Usage: .\build-msi.ps1 [-Configuration Release|Debug]
# Requires: .NET SDK (for dotnet build). WiX SDK is restored via NuGet.

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "Building GenericDeepL MSI installer..." -ForegroundColor Green

# Project directory
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppProjectPath = Join-Path $ProjectDir "GenericDeepL.csproj"
$WixProjectPath = Join-Path $ProjectDir "GenericDeepL.Installer.wixproj"
$AppBinPath = Join-Path $ProjectDir "bin\$Configuration\net6.0-windows"

# 1. Build application (publish でデプロイ用出力を生成し、MSI に含めるファイルを確実に揃える)
# ※ 出力フォルダは削除せず publish で上書き。exe 等が起動中の場合は事前に終了すること。
Write-Host "`n1. Building application..." -ForegroundColor Yellow
Write-Host "Project: $AppProjectPath" -ForegroundColor Gray
$null = dotnet publish "$AppProjectPath" -c $Configuration -o "$AppBinPath"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Application publish failed." -ForegroundColor Red
    exit 1
}

# 2. Verify build output exists
if (-not (Test-Path $AppBinPath)) {
    Write-Host "Error: Build output not found: $AppBinPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path (Join-Path $AppBinPath "GenericDeepL.exe"))) {
    Write-Host "Error: GenericDeepL.exe not found." -ForegroundColor Red
    exit 1
}

# 3. Build MSI with WiX 6 SDK (dotnet build restores WixToolset.Sdk from NuGet)
Write-Host "`n2. Building MSI installer (WiX Toolset 6)..." -ForegroundColor Yellow
$appBinPathArg = $AppBinPath.TrimEnd('\') + "\"
$wixArgs = @(
    "build",
    "`"$WixProjectPath`"",
    "-p:Configuration=$Configuration",
    "-p:AppBinPath=$appBinPathArg"
)
& dotnet @wixArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "MSI installer build failed." -ForegroundColor Red
    exit 1
}

# 4. Show output file location (WiX SDK typically outputs to bin\<Configuration>\)
$msiOutputPath = Join-Path $ProjectDir "bin\$Configuration\GenericDeepL.msi"
if (Test-Path $msiOutputPath) {
    Write-Host "`nMSI installer created successfully!" -ForegroundColor Green
    Write-Host "Location: $msiOutputPath" -ForegroundColor Cyan
    $fileInfo = Get-Item $msiOutputPath
    Write-Host "Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
} else {
    Write-Host "`nWarning: MSI file not found at expected path. Check bin\$Configuration\ for output." -ForegroundColor Yellow
}

Write-Host "`nDone!" -ForegroundColor Green
