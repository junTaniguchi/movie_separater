using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoSplitter.Core.Logging;
using VideoSplitter.Core.Models;

namespace VideoSplitter.Core.Services;

public sealed class FfprobeClient
{
    private readonly FfmpegLocator _locator;
    private readonly Logger _logger = Logger.Instance;

    public FfprobeClient(FfmpegLocator locator)
    {
        _locator = locator;
    }

    public async Task<ProbeResult> GetInfoAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("入力ファイルが存在しません", inputPath);
        }

        var location = await _locator.LocateAsync(cancellationToken).ConfigureAwait(false);
        var arguments = $"-v error -show_entries format=duration,size,bit_rate -of json \"{inputPath}\"";
        var startInfo = location.CreateStartInfo(location.FfprobePath, arguments, Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory);

        using var process = new Process { StartInfo = startInfo };        
        if (!process.Start())
        {
            throw new InvalidOperationException("ffprobe の起動に失敗しました");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outputTask, errorTask).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.Error($"ffprobe がエラー終了しました: {await errorTask}");
            throw new InvalidOperationException("ffprobe による解析に失敗しました。ログを確認してください。");
        }

        using var doc = JsonDocument.Parse(await outputTask);
        if (!doc.RootElement.TryGetProperty("format", out var formatElement))
        {
            throw new InvalidOperationException("ffprobe の出力から format セクションを取得できませんでした");
        }

        var duration = formatElement.GetPropertyOrDefault("duration", 0d);
        var size = formatElement.GetPropertyOrDefault("size", 0d);
        var bitRate = formatElement.GetPropertyOrDefault("bit_rate", 0d);

        return new ProbeResult(
            DurationSeconds: duration,
            FileSizeBytes: size,
            BitRateBitsPerSecond: bitRate);
    }
}

internal static class JsonElementExtensions
{
    public static double GetPropertyOrDefault(this JsonElement element, string propertyName, double defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), out var parsedFromString))
            {
                return parsedFromString;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var parsed))
            {
                return parsed;
            }
        }

        return defaultValue;
    }
}
