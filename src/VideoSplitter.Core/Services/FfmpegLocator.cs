using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoSplitter.Core.Services;

public sealed class FfmpegLocator
{
    private readonly string[] _executableNames = ["ffmpeg.exe", "ffmpeg"];
    private readonly string[] _probeExecutableNames = ["ffprobe.exe", "ffprobe"];

    public Task<FfmpegLocation> LocateAsync(CancellationToken cancellationToken)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var bundledDirectory = Path.Combine(baseDirectory, "third_party", "ffmpeg", "win-x64");
        var searchPaths = new List<string> { baseDirectory, bundledDirectory };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        searchPaths.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        string? ffmpegPath = null;
        string? ffprobePath = null;

        foreach (var directory in searchPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ffmpegPath ??= FindExecutable(directory, _executableNames);
            ffprobePath ??= FindExecutable(directory, _probeExecutableNames);

            if (ffmpegPath != null && ffprobePath != null)
            {
                break;
            }
        }

        if (ffmpegPath == null || ffprobePath == null)
        {
            throw new FileNotFoundException("FFmpeg または ffprobe が見つかりませんでした。アプリの third_party フォルダーに配置してください。");
        }

        return Task.FromResult(new FfmpegLocation(ffmpegPath, ffprobePath));
    }

    private static string? FindExecutable(string directory, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed record FfmpegLocation(string FfmpegPath, string FfprobePath)
{
    public ProcessStartInfo CreateStartInfo(string executablePath, string arguments, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
    }
}
