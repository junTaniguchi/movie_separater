using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VideoSplitter.Core.Logging;
using VideoSplitter.Core.Models;

namespace VideoSplitter.Core.Services;

public sealed class VideoSplitter
{
    private readonly FfmpegLocator _locator;
    private readonly FfprobeClient _ffprobeClient;
    private readonly Logger _logger = Logger.Instance;

    public VideoSplitter(FfmpegLocator locator, FfprobeClient ffprobeClient)
    {
        _locator = locator;
        _ffprobeClient = ffprobeClient;
    }

    public async Task<SplitPlan> CreatePlanAsync(
        string inputFile,
        double maxGigabytes,
        double maxMinutes,
        CancellationToken cancellationToken)
    {
        var probe = await _ffprobeClient.GetInfoAsync(inputFile, cancellationToken).ConfigureAwait(false);
        var baseName = Path.GetFileNameWithoutExtension(inputFile);
        return SplitPlan.FromProbe(probe, maxGigabytes, maxMinutes, baseName);
    }

    public async Task<IReadOnlyList<string>> SplitAsync(
        string inputFile,
        string outputDirectory,
        double maxGigabytes,
        double maxMinutes,
        bool overwrite,
        IProgress<SplitProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SplitProgress(SplitPhase.Preparing, 0, 0, "解析中..."));

        Directory.CreateDirectory(outputDirectory);

        var plan = await CreatePlanAsync(inputFile, maxGigabytes, maxMinutes, cancellationToken).ConfigureAwait(false);
        _logger.Info($"分割計画: parts={plan.PartCount}, segment={plan.SegmentLengthSeconds:F2}s");

        var location = await _locator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var segmentSeconds = Math.Max(1, plan.SegmentLengthSeconds);
        var segmentTimeArg = segmentSeconds.ToString(CultureInfo.InvariantCulture);

        var outputBaseName = Path.GetFileNameWithoutExtension(inputFile);
        if (string.IsNullOrWhiteSpace(outputBaseName))
        {
            outputBaseName = "output";
        }

        var outputExtension = Path.GetExtension(inputFile);
        if (string.IsNullOrEmpty(outputExtension))
        {
            outputExtension = ".mp4";
        }

        var patternPath = Path.Combine(outputDirectory, $"{outputBaseName}_%02d{outputExtension}");
        if (!overwrite)
        {
            EnsureNoOverwrite(plan, outputDirectory, outputBaseName, outputExtension);
        }
        else
        {
            CleanupExistingParts(outputDirectory, outputBaseName, outputExtension);
        }

        progress?.Report(new SplitProgress(SplitPhase.CopySplitting, 0, plan.PartCount, "コピー分割中..."));
        await RunFfmpegAsync(location.FfmpegPath,
            $"-y -v error -i \"{inputFile}\" -c copy -map 0 -f segment -reset_timestamps 1 -segment_time {segmentTimeArg} \"{patternPath}\"",
            Path.GetDirectoryName(inputFile) ?? Environment.CurrentDirectory,
            cancellationToken).ConfigureAwait(false);

        var searchPattern = $"{outputBaseName}_*{outputExtension}";
        var actualParts = Directory.EnumerateFiles(outputDirectory, searchPattern)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var completedParts = 0;
        foreach (var partPath in actualParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(partPath);
            if (info.Length > plan.MaximumSizeBytes)
            {
                progress?.Report(new SplitProgress(SplitPhase.Reencoding, completedParts, plan.PartCount, $"再エンコード中: {Path.GetFileName(partPath)}"));
                await ReencodeAsync(location, partPath, plan.MaximumSizeBytes, cancellationToken).ConfigureAwait(false);
            }

            completedParts++;
            progress?.Report(new SplitProgress(SplitPhase.CopySplitting, completedParts, plan.PartCount, $"{completedParts}/{plan.PartCount} 完了"));
        }

        progress?.Report(new SplitProgress(SplitPhase.Completed, plan.PartCount, plan.PartCount, "完了"));
        return actualParts;
    }

    private void EnsureNoOverwrite(SplitPlan plan, string outputDirectory, string outputBaseName, string outputExtension)
    {
        foreach (var part in plan.Parts)
        {
            var fileName = part.GetFileName(outputBaseName, outputExtension);
            var path = Path.Combine(outputDirectory, fileName);
            if (File.Exists(path))
            {
                throw new IOException($"出力ファイル {path} が既に存在します");
            }
        }
    }

    private void CleanupExistingParts(string outputDirectory, string outputBaseName, string outputExtension)
    {
        var searchPattern = $"{outputBaseName}_*{outputExtension}";
        foreach (var file in Directory.EnumerateFiles(outputDirectory, searchPattern))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.Warn($"既存パートの削除に失敗しました: {file} ({ex.Message})");
            }
        }
    }

    private async Task ReencodeAsync(FfmpegLocation location, string partPath, double maxSizeBytes, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetDirectoryName(partPath)!, Path.GetFileNameWithoutExtension(partPath) + "_reencode.mp4");
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        ProbeResult probe;
        try
        {
            probe = await _ffprobeClient.GetInfoAsync(partPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"再エンコード対象ファイルの解析に失敗しました。CRFで再エンコードします: {ex.Message}");
            probe = new ProbeResult(0, 0, 0);
        }

        var targetBytes = maxSizeBytes * 0.98;
        var durationSeconds = probe.DurationSeconds;

        string arguments;
        if (durationSeconds > 0)
        {
            var totalTargetBitrate = (targetBytes * 8) / durationSeconds;
            const int audioBitrate = 192_000;
            var videoBitrate = Math.Max(totalTargetBitrate - audioBitrate, 500_000);
            arguments = $"-y -v error -i \"{partPath}\" -c:v libx264 -preset medium -b:v {videoBitrate.ToString(CultureInfo.InvariantCulture)} -maxrate {Math.Round(videoBitrate * 1.1)} -bufsize {Math.Round(videoBitrate * 2)} -c:a aac -b:a 192k -movflags +faststart \"{tempPath}\"";
        }
        else
        {
            arguments = $"-y -v error -i \"{partPath}\" -c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -movflags +faststart \"{tempPath}\"";
        }

        await RunFfmpegAsync(location.FfmpegPath, arguments, Path.GetDirectoryName(partPath)!, cancellationToken).ConfigureAwait(false);

        var newInfo = new FileInfo(tempPath);
        if (!newInfo.Exists)
        {
            throw new InvalidOperationException("再エンコード結果のファイルが生成されませんでした");
        }

        if (newInfo.Length > maxSizeBytes)
        {
            _logger.Warn($"再エンコード後もファイルサイズが上限を超えています ({newInfo.Length} bytes)");
        }

        File.Delete(partPath);
        File.Move(tempPath, partPath);
    }

    private async Task RunFfmpegAsync(string executablePath, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        _logger.Info($"コマンド実行: {executablePath} {arguments}");
        if (!process.Start())
        {
            throw new InvalidOperationException("ffmpeg の起動に失敗しました");
        }

        var outputLines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (outputLines)
                {
                    outputLines.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (outputLines)
                {
                    outputLines.Add(e.Data);
                }
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var message = string.Join(Environment.NewLine, outputLines);
            _logger.Error($"ffmpeg 実行がエラー終了しました: {message}");
            throw new InvalidOperationException("ffmpeg 実行に失敗しました。ログを確認してください。");
        }

        if (outputLines.Count > 0)
        {
            _logger.Info(string.Join(Environment.NewLine, outputLines));
        }
    }
}
