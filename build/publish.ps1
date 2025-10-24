param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$projectPath = Join-Path $PSScriptRoot "..\src\VideoSplitter.App\VideoSplitter.App.csproj"

Write-Host "Publishing VideoSplitter.App..."
dotnet publish $projectPath -c $Configuration -r $Runtime /p:PublishSingleFile=true /p:PublishReadyToRun=true --self-contained false

Write-Host "Copy FFmpeg binaries..."
$publishDir = Join-Path $PSScriptRoot "..\src\VideoSplitter.App\bin\$Configuration\net8.0-windows\$Runtime\publish"
$ffmpegSource = Join-Path $PSScriptRoot "..\third_party\ffmpeg\win-x64"
if (Test-Path $ffmpegSource) {
    Copy-Item -Path (Join-Path $ffmpegSource "*") -Destination $publishDir -Recurse -Force
} else {
    Write-Warning "FFmpeg binaries not found. Please place them under third_party/ffmpeg/win-x64."
}
