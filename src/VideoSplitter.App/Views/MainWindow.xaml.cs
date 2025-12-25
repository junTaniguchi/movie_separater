using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoSplitter.App.ViewModels;
using VideoSplitter.Core.Models;
using VideoSplitter.Core.Services;
using WinForms = System.Windows.Forms;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfIDataObject = System.Windows.IDataObject;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace VideoSplitter.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var locator = new FfmpegLocator();
        var ffprobe = new FfprobeClient(locator);
        var splitter = new VideoSplitter.Core.Services.VideoSplitter(locator, ffprobe);
        _viewModel = new MainWindowViewModel(splitter)
        {
            OverwriteConfirmationHandler = ConfirmOverwriteAsync,
            AudioOverwriteConfirmationHandler = ConfirmAudioOverwriteAsync,
            OpenFolderHandler = OpenFolderAsync
        };

        DataContext = _viewModel;

        _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_viewModel.IsBusy)
        {
            _viewModel.CancelCommand.Execute(null);
        }
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var last = LogListBox.Items[^1];
                LogListBox.ScrollIntoView(last);
            });
        }
    }

    private async Task<bool> ConfirmOverwriteAsync(SplitPlan plan)
    {
        var inputFilePath = _viewModel.InputFilePath;
        if (string.IsNullOrWhiteSpace(inputFilePath))
        {
            return true;
        }

        var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "output";
        }

        var extension = Path.GetExtension(inputFilePath);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".mp4";
        }

        var existing = plan.Parts
            .Select(p => Path.Combine(_viewModel.OutputDirectory, p.GetFileName(baseName, extension)))
            .Where(File.Exists)
            .ToList();

        if (existing.Count == 0)
        {
            return true;
        }

        var message = "以下のファイルが既に存在します。上書きしますか？\n" + string.Join("\n", existing.Select(Path.GetFileName));
        var result = WpfMessageBox.Show(this, message, "上書き確認", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Question, WpfMessageBoxResult.No);
        return await Task.FromResult(result == WpfMessageBoxResult.Yes);
    }

    private Task OpenFolderAsync(string path)
    {
        if (Directory.Exists(path))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        return Task.CompletedTask;
    }

    private Task<bool> ConfirmAudioOverwriteAsync(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            return Task.FromResult(true);
        }

        var fileName = Path.GetFileName(outputPath);
        var message = $"{fileName} が既に存在します。上書きしますか？";
        var result = WpfMessageBox.Show(this, message, "上書き確認", WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Question, WpfMessageBoxResult.No);
        return Task.FromResult(result == WpfMessageBoxResult.Yes);
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "MP4 ファイル (*.mp4)|*.mp4|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.InputFilePath = dialog.FileName;
        }
    }

    private void ClearInput_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.InputFilePath = null;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            SelectedPath = _viewModel.OutputDirectory
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _viewModel.OutputDirectory = dialog.SelectedPath;
        }
    }

    private void SizeUp_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MaxSizeGb = Math.Round(_viewModel.MaxSizeGb + 0.1, 2);
    }

    private void SizeDown_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MaxSizeGb = Math.Max(0.1, Math.Round(_viewModel.MaxSizeGb - 0.1, 2));
    }

    private void DurationUp_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MaxDurationMinutes = Math.Round(_viewModel.MaxDurationMinutes + 5, 0);
    }

    private void DurationDown_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MaxDurationMinutes = Math.Max(5, Math.Round(_viewModel.MaxDurationMinutes - 5, 0));
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.OpenOutputFolderAsync();
    }

    private void Window_DragOver(object sender, WpfDragEventArgs e)
    {
        if (HasMp4File(e.Data))
        {
            e.Effects = WpfDragDropEffects.Copy;
        }
        else
        {
            e.Effects = WpfDragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, WpfDragEventArgs e)
    {
        if (HasMp4File(e.Data))
        {
            var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
            _viewModel.InputFilePath = files.FirstOrDefault();
        }
    }

    private void InputTextBox_PreviewDragOver(object sender, WpfDragEventArgs e)
    {
        Window_DragOver(sender, e);
    }

    private void InputTextBox_PreviewDrop(object sender, WpfDragEventArgs e)
    {
        Window_Drop(sender, e);
    }

    private static bool HasMp4File(WpfIDataObject data)
    {
        if (data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            var files = (string[])data.GetData(WpfDataFormats.FileDrop);
            return files.Length > 0 && files[0].EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
