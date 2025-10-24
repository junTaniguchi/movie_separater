param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$srcMain = Join-Path $projectRoot "src/app.py"
$iconPath = Join-Path $projectRoot "assets/icon.ico"
$ffmpegDir = Join-Path $projectRoot "third_party/ffmpeg/win-x64"

if (-not (Test-Path $srcMain)) {
    Write-Error "src/app.py が見つかりません。スクリプトの配置を確認してください。"
}

if (-not (Test-Path (Join-Path $ffmpegDir "ffmpeg.exe"))) {
    Write-Warning "ffmpeg.exe が見つかりません。公式バイナリを third_party/ffmpeg/win-x64 に配置してください。"
}

if (-not (Test-Path (Join-Path $ffmpegDir "ffprobe.exe"))) {
    Write-Warning "ffprobe.exe が見つかりません。公式バイナリを third_party/ffmpeg/win-x64 に配置してください。"
}

$pyInstallerArgs = @(
    "--onefile",
    "--windowed",
    "--icon", $iconPath,
    "--name", "VideoSplitter",
    "--add-binary", (Join-Path $ffmpegDir "ffmpeg.exe") + ";.",
    "--add-binary", (Join-Path $ffmpegDir "ffprobe.exe") + ";.",
    $srcMain
)

Write-Host "PyInstaller を実行します..."
& $Python -m PyInstaller @pyInstallerArgs

