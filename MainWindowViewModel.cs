using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ConverterApp;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private Window? _window;
    private string _inputPath = string.Empty;
    private string _outputPath = string.Empty;
    private string _qemuPath = "qemu-img";
    private TargetFormat _targetFormat = TargetFormat.Raw;
    private bool _isConverting;
    private double _progress;
    private string _statusText = "Ready";
    private string _logText = string.Empty;
    private Process? _process;
    private CancellationTokenSource? _cts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachWindow(Window window) => _window = window;

    public string InputPath
    {
        get => _inputPath;
        set
        {
            var normalized = NormalizePath(value);
            if (SetField(ref _inputPath, normalized))
            {
                UpdateOutputPathFromInput();
                RaiseCanExecuteChanged();
            }
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            var normalized = NormalizePath(value);
            if (SetField(ref _outputPath, normalized))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public string QemuPath
    {
        get => _qemuPath;
        set => SetField(ref _qemuPath, NormalizePath(value));
    }

    public bool IsTargetRaw
    {
        get => _targetFormat == TargetFormat.Raw;
        set
        {
            if (value)
            {
                _targetFormat = TargetFormat.Raw;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTargetQcow2));
                UpdateOutputPathFromInput();
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTargetQcow2
    {
        get => _targetFormat == TargetFormat.Qcow2;
        set
        {
            if (value)
            {
                _targetFormat = TargetFormat.Qcow2;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTargetRaw));
                UpdateOutputPathFromInput();
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            if (SetField(ref _isConverting, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsConverting;

    public double Progress
    {
        get => _progress;
        private set
        {
            if (SetField(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressLabel));
            }
        }
    }

    public string ProgressLabel => $"{Progress:0}%";

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    public ICommand BrowseInputCommand => new AsyncCommand(BrowseInputAsync, () => IsIdle);
    public ICommand BrowseOutputDirectoryCommand => new AsyncCommand(BrowseOutputDirectoryAsync, () => IsIdle);
    public ICommand BrowseQemuCommand => new AsyncCommand(BrowseQemuAsync, () => IsIdle);
    public ICommand StartCommand => new AsyncCommand(StartConversionAsync, () => CanStart);
    public ICommand CancelCommand => new DelegateCommand(Cancel, () => IsConverting);

    public bool CanStart =>
        IsIdle &&
        !string.IsNullOrWhiteSpace(InputPath) &&
        !string.IsNullOrWhiteSpace(OutputPath) &&
        File.Exists(InputPath) &&
        (_targetFormat == TargetFormat.Raw || _targetFormat == TargetFormat.Qcow2);

    private async Task BrowseInputAsync()
    {
        if (_window is null) return;
        var sp = _window.StorageProvider;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a .qcow2 or .img file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Disk images") { Patterns = new[] { "*.qcow2", "*.img", "*.raw" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            InputPath = files[0].Path.LocalPath;
        }
    }

    private async Task BrowseOutputDirectoryAsync()
    {
        if (_window is null) return;
        var sp = _window.StorageProvider;

        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folder = folders[0].Path.LocalPath;
            var name = Path.GetFileNameWithoutExtension(InputPath);
            var ext = _targetFormat == TargetFormat.Raw ? ".img" : ".qcow2";
            OutputPath = Path.Combine(folder, string.IsNullOrWhiteSpace(name) ? $"output{ext}" : $"{name}{ext}");
        }
    }

    private async Task BrowseQemuAsync()
    {
        if (_window is null) return;
        var sp = _window.StorageProvider;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select qemu-img",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable") { Patterns = new[] { "*.exe", "*" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            QemuPath = files[0].Path.LocalPath;
        }
    }

    private void UpdateOutputPathFromInput()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            return;
        }

        var ext = _targetFormat == TargetFormat.Raw ? ".img" : ".qcow2";
        var dir = Path.GetDirectoryName(InputPath);
        var name = Path.GetFileNameWithoutExtension(InputPath);
        if (!string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(name))
        {
            OutputPath = Path.Combine(dir, name + ext);
        }
    }

    private async Task StartConversionAsync()
    {
        if (!CanStart)
        {
            return;
        }

        if (!File.Exists(InputPath))
        {
            AppendLog("Input file does not exist.");
            return;
        }

        if (string.IsNullOrWhiteSpace(QemuPath))
        {
            AppendLog("qemu-img path is empty.");
            return;
        }

        Progress = 0;
        StatusText = "Starting…";
        LogText = string.Empty;
        IsConverting = true;
        _cts = new CancellationTokenSource();

        var sourceFormat = GuessSourceFormat(InputPath);
        var targetFormat = _targetFormat == TargetFormat.Raw ? "raw" : "qcow2";

        var startInfo = new ProcessStartInfo
        {
            FileName = QemuPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("convert");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(sourceFormat);
        startInfo.ArgumentList.Add("-O");
        startInfo.ArgumentList.Add(targetFormat);
        startInfo.ArgumentList.Add(InputPath);
        startInfo.ArgumentList.Add(OutputPath);

        AppendLog($"Running: {QemuPath} {string.Join(" ", startInfo.ArgumentList)}");

        try
        {
            _process = Process.Start(startInfo);
            if (_process is null)
            {
                AppendLog("Failed to start qemu-img.");
                StatusText = "Failed";
                return;
            }

            _process.OutputDataReceived += (_, args) => HandleProcessOutput(args.Data);
            _process.ErrorDataReceived += (_, args) => HandleProcessOutput(args.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await _process.WaitForExitAsync(_cts.Token);

            if (_cts.IsCancellationRequested)
            {
                StatusText = "Cancelled";
                AppendLog("Conversion cancelled.");
            }
            else if (_process.ExitCode == 0)
            {
                Progress = 100;
                StatusText = "Done";
                AppendLog("Conversion completed.");
            }
            else
            {
                StatusText = $"Failed (exit {_process.ExitCode})";
                AppendLog($"qemu-img exited with code {_process.ExitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            AppendLog("Conversion cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = "Failed";
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _cts?.Dispose();
            _cts = null;
            IsConverting = false;
            RaiseCanExecuteChanged();
        }
    }

    private void HandleProcessOutput(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        AppendLog(data);
        var pct = TryParseProgress(data);
        if (pct.HasValue)
        {
            Progress = pct.Value;
            OnPropertyChanged(nameof(ProgressLabel));
        }
    }

    private static double? TryParseProgress(string text)
    {
        var match = Regex.Match(text, @"([0-9]{1,3}(?:\.[0-9]+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
        {
            return Math.Max(0, Math.Min(100, value));
        }
        return null;
    }

    private static string GuessSourceFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".qcow2" => "qcow2",
            ".img" => "raw",
            ".raw" => "raw",
            _ => "qcow2"
        };
    }

    private static string NormalizePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
        return trimmed;
    }

    private void Cancel()
    {
        if (!IsConverting)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
            _process?.Kill(true);
        }
        catch (Exception ex)
        {
            AppendLog($"Error while cancelling: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        var builder = new StringBuilder(LogText);
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }
        builder.Append($"{DateTime.Now:HH:mm:ss}  {message}");
        LogText = builder.ToString();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseCanExecuteChanged()
    {
        (BrowseInputCommand as INotifyingCommand)?.RaiseCanExecuteChanged();
        (BrowseOutputDirectoryCommand as INotifyingCommand)?.RaiseCanExecuteChanged();
        (BrowseQemuCommand as INotifyingCommand)?.RaiseCanExecuteChanged();
        (StartCommand as INotifyingCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as INotifyingCommand)?.RaiseCanExecuteChanged();
    }
}

public enum TargetFormat
{
    Raw,
    Qcow2
}

public interface INotifyingCommand : ICommand
{
    void RaiseCanExecuteChanged();
}

public sealed class DelegateCommand : INotifyingCommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public DelegateCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncCommand : INotifyingCommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter) => await _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
