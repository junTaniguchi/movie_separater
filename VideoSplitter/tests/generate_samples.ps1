param(
    [string]$FfmpegPath = "..\third_party\ffmpeg\win-x64\ffmpeg.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $FfmpegPath)) {
    Write-Error "FFmpeg が見つかりません: $FfmpegPath"
}

$outputDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "samples"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$scenarios = @(
    @{ Name = "sample_10min.mp4"; Duration = 600 },
    @{ Name = "sample_30min.mp4"; Duration = 1800 },
    @{ Name = "sample_120min.mp4"; Duration = 7200 }
)

foreach ($scenario in $scenarios) {
    $outputPath = Join-Path $outputDir $scenario.Name
    Write-Host "Generating $($scenario.Name) ..."

    & $FfmpegPath `
        -y `
        -f lavfi -i "testsrc=size=1280x720:rate=30:duration=$($scenario.Duration)" `
        -f lavfi -i "sine=frequency=1000:duration=$($scenario.Duration)" `
        -c:v libx264 -pix_fmt yuv420p `
        -c:a aac -b:a 128k `
        -shortest `
        -movflags +faststart `
        $outputPath
}

Write-Host "Dummy videos created in $outputDir"

