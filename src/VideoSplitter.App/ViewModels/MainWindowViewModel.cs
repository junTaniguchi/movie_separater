using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using VideoSplitter.Core.Logging;
using VideoSplitter.Core.Models;
using VideoSplitter.Core.Services;

namespace VideoSplitter.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly VideoSplitter.Core.Services.VideoSplitter _splitter;
    private readonly Logger _logger = Logger.Instance;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _inputFilePath;
    private string _outputDirectory;
    private double _maxSizeGb = 1.5;
    private double _maxDurationMinutes = 50;
    private bool _isBusy;
    private double _progressValue;
    private bool _isIndeterminate;
    private string _statusMessage = "準備完了";

    public MainWindowViewModel(VideoSplitter.Core.Services.VideoSplitter splitter)
    {
        _splitter = splitter;
        _outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "split");
        Directory.CreateDirectory(_outputDirectory);

        Logs = new ObservableCollection<string>();
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);

        _logger.AddSink(entry =>
        {
            void Append()
            {
                Logs.Add($"[{entry.Timestamp:HH:mm:ss}] {entry.Level}: {entry.Message}");
                while (Logs.Count > 2000)
                {
                    Logs.RemoveAt(0);
                }
            }

            if (_uiContext != null)
            {
                _uiContext.Post(_ => Append(), null);
            }
            else
            {
                Append();
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Logs { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

    public string? InputFilePath
    {
        get => _inputFilePath;
        set
        {
            if (SetProperty(ref _inputFilePath, value))
            {
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                StartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double MaxSizeGb
    {
        get => _maxSizeGb;
        set => SetProperty(ref _maxSizeGb, value);
    }

    public double MaxDurationMinutes
    {
        get => _maxDurationMinutes;
        set => SetProperty(ref _maxDurationMinutes, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Func<SplitPlan, Task<bool>>? OverwriteConfirmationHandler { get; set; }

    public Func<string, Task>? OpenFolderHandler { get; set; }

    public async Task OpenOutputFolderAsync()
    {
        if (OpenFolderHandler != null)
        {
            await OpenFolderHandler(OutputDirectory);
        }
    }

    private bool CanStart() => !IsBusy && !string.IsNullOrWhiteSpace(InputFilePath) && Directory.Exists(Path.GetDirectoryName(InputFilePath!) ?? string.Empty);

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
        {
            return;
        }

        if (!File.Exists(InputFilePath))
        {
            _logger.Error($"入力ファイルが見つかりません: {InputFilePath}");
            return;
        }

        Directory.CreateDirectory(OutputDirectory);

        var cts = new CancellationTokenSource();
        _cancellationTokenSource = cts;
        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = "処理中...";

        try
        {
            var plan = await _splitter.CreatePlanAsync(InputFilePath, MaxSizeGb, MaxDurationMinutes, cts.Token).ConfigureAwait(false);

            if (OverwriteConfirmationHandler != null)
            {
                var shouldContinue = await OverwriteConfirmationHandler(plan).ConfigureAwait(false);
                if (!shouldContinue)
                {
                    _logger.Info("ユーザーによりキャンセルされました (上書き確認)");
                    return;
                }
            }

            var progress = new Progress<SplitProgress>(p =>
            {
                StatusMessage = p.Message ?? string.Empty;
                if (p.Phase == SplitPhase.Reencoding)
                {
                    IsIndeterminate = true;
                }
                else
                {
                    IsIndeterminate = false;
                    ProgressValue = p.Percentage;
                }
            });

            var results = await _splitter.SplitAsync(InputFilePath, OutputDirectory, MaxSizeGb, MaxDurationMinutes, overwrite: true, progress, cts.Token).ConfigureAwait(false);

            StatusMessage = "完了";
            ProgressValue = 100;
            IsIndeterminate = false;

            _logger.Info($"出力ファイル: {string.Join(", ", results.Select(Path.GetFileName))}");
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("ユーザーによりキャンセルされました");
            StatusMessage = "キャンセルしました";
        }
        catch (Exception ex)
        {
            _logger.Error("分割処理でエラーが発生しました", ex);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            _cancellationTokenSource = null;
        }
    }

    private void Cancel()
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
