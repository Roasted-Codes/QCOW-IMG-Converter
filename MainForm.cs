using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QemuImgWinForms;

public sealed class MainForm : Form
{
    private readonly TextBox _inputBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _outputBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _qemuBox = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly RadioButton _toRawRadio = new() { Text = "To .img (raw)", Checked = true, AutoSize = true };
    private readonly RadioButton _toQcow2Radio = new() { Text = "To .qcow2", AutoSize = true };
    private readonly Button _convertButton = new() { Text = "Convert", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Ready", Anchor = AnchorStyles.Left, Margin = new Padding(8, 8, 0, 0) };
    private readonly TextBox _logBox = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        WordWrap = false
    };

    private CancellationTokenSource? _cts;
    private Process? _process;

    public MainForm()
    {
        Text = "qemu-img GUI (.qcow2 ↔ .img)";
        Width = 820;
        Height = 600;
        MinimumSize = new System.Drawing.Size(720, 520);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // input
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // output
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // format
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // qemu-img
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // progress
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log

        layout.Controls.Add(new Label { Text = "Input file", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(RowWithBrowse(_inputBox, BrowseInput), 1, 0);

        layout.Controls.Add(new Label { Text = "Output file", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(RowWithBrowse(_outputBox, BrowseOutput), 1, 1);

        layout.Controls.Add(new Label { Text = "Target format", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        var radioPanel = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left };
        radioPanel.Controls.Add(_toRawRadio);
        radioPanel.Controls.Add(_toQcow2Radio);
        layout.Controls.Add(radioPanel, 1, 2);

        layout.Controls.Add(new Label { Text = "qemu-img", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(RowWithBrowse(_qemuBox, BrowseQemu), 1, 3);

        var buttonPanel = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left };
        buttonPanel.Controls.Add(_convertButton);
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_statusLabel);
        layout.Controls.Add(buttonPanel, 1, 4);

        layout.Controls.Add(_progressBar, 0, 5);
        layout.SetColumnSpan(_progressBar, 2);

        layout.Controls.Add(new Label { Text = "Log", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 2) }, 0, 6);
        layout.SetColumnSpan(_logBox, 2);
        layout.Controls.Add(_logBox, 0, 6);

        Controls.Add(layout);

        _convertButton.Click += async (_, _) => await StartConversionAsync();
        _cancelButton.Click += (_, _) => CancelConversion();
        _inputBox.TextChanged += (_, _) => UpdateOutputSuggestion();
        _toRawRadio.CheckedChanged += (_, _) => UpdateOutputSuggestion();
        _toQcow2Radio.CheckedChanged += (_, _) => UpdateOutputSuggestion();

        _qemuBox.Text = @"C:\Program Files\qemu\qemu-img.exe";
    }

    private Control RowWithBrowse(TextBox textBox, Action browseAction)
    {
        var inner = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        textBox.Margin = new Padding(0, 0, 6, 0);
        var btn = new Button { Text = "Browse…", AutoSize = true };
        btn.Click += (_, _) => browseAction();
        inner.Controls.Add(textBox, 0, 0);
        inner.Controls.Add(btn, 1, 0);
        return inner;
    }

    private void BrowseInput()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Images (*.qcow2;*.img;*.raw)|*.qcow2;*.img;*.raw|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _inputBox.Text = dlg.FileName;
        }
    }

    private void BrowseOutput()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = _toRawRadio.Checked
                ? "Raw image (*.img)|*.img|All files (*.*)|*.*"
                : "QCOW2 image (*.qcow2)|*.qcow2|All files (*.*)|*.*",
            FileName = _outputBox.Text
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _outputBox.Text = dlg.FileName;
        }
    }

    private void BrowseQemu()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Executable|*.exe;*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _qemuBox.Text = dlg.FileName;
        }
    }

    private void UpdateOutputSuggestion()
    {
        var input = NormalizePath(_inputBox.Text);
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            return;
        }
        var dir = Path.GetDirectoryName(input);
        var name = Path.GetFileNameWithoutExtension(input);
        var ext = _toRawRadio.Checked ? ".img" : ".qcow2";
        _outputBox.Text = Path.Combine(dir ?? string.Empty, name + ext);
    }

    private async Task StartConversionAsync()
    {
        var input = NormalizePath(_inputBox.Text);
        var output = NormalizePath(_outputBox.Text);
        var qemu = NormalizePath(_qemuBox.Text);

        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            AppendLog("Input file not found.");
            return;
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            AppendLog("Output path is empty.");
            return;
        }
        if (string.IsNullOrWhiteSpace(qemu) || !File.Exists(qemu))
        {
            AppendLog("qemu-img not found. Set the correct path.");
            return;
        }

        _convertButton.Enabled = false;
        _cancelButton.Enabled = true;
        _progressBar.Value = 0;
        _statusLabel.Text = "Running…";
        _logBox.Clear();

        var sourceFormat = GuessSourceFormat(input);
        var targetFormat = _toRawRadio.Checked ? "raw" : "qcow2";

        var psi = new ProcessStartInfo
        {
            FileName = qemu,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("convert");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(sourceFormat);
        psi.ArgumentList.Add("-O");
        psi.ArgumentList.Add(targetFormat);
        psi.ArgumentList.Add(input);
        psi.ArgumentList.Add(output);

        AppendLog($"Running: \"{psi.FileName}\" {string.Join(" ", psi.ArgumentList)}");

        _cts = new CancellationTokenSource();
        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => HandleProcessOutput(e.Data);
            _process.ErrorDataReceived += (_, e) => HandleProcessOutput(e.Data);

            if (!_process.Start())
            {
                AppendLog("Failed to start qemu-img.");
                ResetUi("Failed");
                return;
            }
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            await _process.WaitForExitAsync(_cts.Token);

            if (_cts.IsCancellationRequested)
            {
                AppendLog("Cancelled.");
                ResetUi("Cancelled");
            }
            else if (_process.ExitCode == 0)
            {
                _progressBar.Value = 100;
                AppendLog("Completed.");
                ResetUi("Done");
            }
            else
            {
                AppendLog($"Failed with exit code {_process.ExitCode}.");
                ResetUi("Failed");
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
            ResetUi("Cancelled");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            ResetUi("Failed");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CancelConversion()
    {
        try
        {
            _cts?.Cancel();
            _process?.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppendLog($"Error while cancelling: {ex.Message}");
        }
    }

    private void HandleProcessOutput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendLog(text);
        var pct = TryParseProgress(text);
        if (pct.HasValue)
        {
            SafeUi(() => _progressBar.Value = Math.Min(100, Math.Max(0, (int)Math.Round(pct.Value))));
        }
    }

    private static double? TryParseProgress(string line)
    {
        var match = Regex.Match(line, @"([0-9]{1,3}(?:\.[0-9]+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
        {
            return value;
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

    private void ResetUi(string status)
    {
        SafeUi(() =>
        {
            _statusLabel.Text = status;
            _convertButton.Enabled = true;
            _cancelButton.Enabled = false;
        });
    }

    private void AppendLog(string message)
    {
        SafeUi(() =>
        {
            var sb = new StringBuilder(_logBox.Text.Length + message.Length + 16);
            if (_logBox.TextLength > 0)
            {
                sb.AppendLine(_logBox.Text);
            }
            sb.Append($"{DateTime.Now:HH:mm:ss}  {message}");
            _logBox.Text = sb.ToString();
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        });
    }

    private static string NormalizePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        var trimmed = input.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
        {
            trimmed = trimmed[1..^1];
        }
        return trimmed;
    }

    private void SafeUi(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
