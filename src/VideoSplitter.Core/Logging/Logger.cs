using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VideoSplitter.Core.Logging;

public sealed class Logger
{
    private static readonly Lazy<Logger> _instance = new(() => new Logger());
    private readonly object _syncRoot = new();
    private readonly List<Action<LogEntry>> _sinks = new();
    private string? _logDirectory;
    private string? _logFilePath;
    private const long MaxLogFileBytes = 1_000_000; // 1MB

    public static Logger Instance => _instance.Value;

    private Logger()
    {
    }

    public void Initialize(string applicationName)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logDirectory = Path.Combine(baseDir, applicationName, "logs");
        Directory.CreateDirectory(_logDirectory);
        _logFilePath = Path.Combine(_logDirectory, "app.log");
    }

    public void AddSink(Action<LogEntry> sink)
    {
        lock (_syncRoot)
        {
            _sinks.Add(sink);
        }
    }

    public void Info(string message) => WriteLog(LogLevel.Information, message);

    public void Warn(string message) => WriteLog(LogLevel.Warning, message);

    public void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.AppendLine(ex.ToString());
        }
        WriteLog(LogLevel.Error, sb.ToString());
    }

    private void WriteLog(LogLevel level, string message)
    {
        var entry = new LogEntry(level, DateTimeOffset.Now, message);
        lock (_syncRoot)
        {
            if (_logFilePath != null)
            {
                RollIfNeeded();
                File.AppendAllText(_logFilePath, FormatEntry(entry) + Environment.NewLine, Encoding.UTF8);
            }

            foreach (var sink in _sinks.ToList())
            {
                try
                {
                    sink(entry);
                }
                catch (Exception sinkEx)
                {
                    Debug.WriteLine($"Logger sink failed: {sinkEx}");
                }
            }
        }
    }

    private void RollIfNeeded()
    {
        if (_logFilePath == null)
        {
            return;
        }

        try
        {
            if (File.Exists(_logFilePath))
            {
                var info = new FileInfo(_logFilePath);
                if (info.Length > MaxLogFileBytes)
                {
                    var archivePath = Path.Combine(Path.GetDirectoryName(_logFilePath)!, $"app_{DateTime.Now:yyyyMMddHHmmss}.log");
                    File.Move(_logFilePath, archivePath, overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Logger roll failed: {ex}");
        }
    }

    private static string FormatEntry(LogEntry entry)
        => $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Level}: {entry.Message}";
}

public record LogEntry(LogLevel Level, DateTimeOffset Timestamp, string Message);

public enum LogLevel
{
    Information,
    Warning,
    Error
}
