using System.Diagnostics;
using System.Runtime.InteropServices;
using SingTray.Shared;
using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Client;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly PipeClient _pipeClient;
    private readonly FileImportService _fileImportService;
    private readonly DesiredStateStore _desiredStateStore;
    private readonly ClientLogService _clientLogService;
    private readonly StatusPoller _statusPoller;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuBuilder _menuBuilder;
    private readonly ContextMenuStrip _menu;
    private StatusInfo? _lastStatus;
    private string? _lastConnectionFailure;
    private bool _isBusy;
    private bool _isExiting;
    private readonly Icon _runningIcon;
    private readonly Icon _stoppedIcon;
    private readonly Icon _errorIcon;

    public TrayApplicationContext(PipeClient pipeClient, FileImportService fileImportService, DesiredStateStore desiredStateStore, ClientLogService clientLogService)
    {
        _pipeClient = pipeClient;
        _fileImportService = fileImportService;
        _desiredStateStore = desiredStateStore;
        _clientLogService = clientLogService;
        _statusPoller = new StatusPoller(pipeClient);
        _statusPoller.StatusUpdated += (_, status) =>
        {
            if (_isExiting)
            {
                return;
            }

            _lastStatus = status;
            _lastConnectionFailure = null;
            RefreshUi(serviceAvailable: true);
        };
        _statusPoller.PollFailed += async (_, ex) =>
        {
            if (_isExiting && IsExpectedExitException(ex))
            {
                await _clientLogService.WriteInfoAsync($"Status poll suppressed during exit: {BuildConnectionFailureMessage(ex)}");
                return;
            }

            _lastStatus = null;
            _lastConnectionFailure = BuildConnectionFailureMessage(ex);
            await _clientLogService.WriteWarningAsync($"Status poll failed: {_lastConnectionFailure}");
            RefreshUi(serviceAvailable: false);
        };

        _menuBuilder = new TrayMenuBuilder(OnToggleRequested, OnImportConfigRequested, OnImportCoreRequested, OnOpenDataFolderRequested, OnExitRequested);
        _menu = _menuBuilder.Build();
        _runningIcon = CreateStatusIcon(Color.ForestGreen);
        _stoppedIcon = CreateStatusIcon(Color.Gray);
        _errorIcon = CreateStatusIcon(Color.Firebrick);
        _notifyIcon = new NotifyIcon
        {
            Text = "SingTray",
            Visible = true,
            ContextMenuStrip = _menu,
            Icon = _stoppedIcon
        };

        RefreshUi(serviceAvailable: false);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RunConnectionSelfTestAsync();
        await _statusPoller.PollNowAsync();
        _statusPoller.Start();
        await CoordinateDesiredStateAsync();
    }

    private async Task RunConnectionSelfTestAsync()
    {
        try
        {
            var ping = await _pipeClient.PingAsync(CancellationToken.None);
            await _clientLogService.WriteInfoAsync($"Pipe self-test succeeded. Service={ping.ServiceName}, Pipe={ping.PipeName}.");
        }
        catch (Exception ex)
        {
            _lastConnectionFailure = BuildConnectionFailureMessage(ex);
            await _clientLogService.WriteWarningAsync(
                $"Pipe self-test failed. Reason={_lastConnectionFailure}. ServiceState={_clientLogService.DescribeServiceState()}");
        }
    }

    private async Task CoordinateDesiredStateAsync()
    {
        try
        {
            var shouldBeRunning = await _desiredStateStore.ReadAsync();
            if (_lastStatus is null)
            {
                return;
            }

            if (shouldBeRunning && !_lastStatus.SingBoxRunning)
            {
                await ExecuteOperationAsync(() => _pipeClient.StartAsync(BuildStartRequest(), CancellationToken.None), showMessageOnSuccess: false);
                await _statusPoller.PollNowAsync();
            }
            else if (!shouldBeRunning && _lastStatus.SingBoxRunning)
            {
                await ExecuteOperationAsync(() => _pipeClient.StopAsync(CancellationToken.None), showMessageOnSuccess: false);
                await _statusPoller.PollNowAsync();
            }
        }
        catch
        {
            await _clientLogService.WriteWarningAsync("Desired state coordination skipped because service connection is unavailable.");
            RefreshUi(serviceAvailable: false);
        }
    }

    private void RefreshUi(bool serviceAvailable)
    {
        _menuBuilder.ApplyStatus(_lastStatus, serviceAvailable);
        _notifyIcon.Icon = _lastStatus?.RunState switch
        {
            RunState.Running => _runningIcon,
            RunState.Error => _errorIcon,
            _ => _stoppedIcon
        };
        _notifyIcon.Text = BuildToolTip(serviceAvailable, _lastStatus, _lastConnectionFailure);
    }

    private async void OnToggleRequested(object? sender, EventArgs e)
    {
        if (_isBusy || _lastStatus is null)
        {
            return;
        }

        if (_lastStatus.RunState is RunState.Starting or RunState.Stopping)
        {
            return;
        }

        if (_lastStatus.RunState == RunState.Running)
        {
            await _desiredStateStore.WriteAsync(false);
            await ExecuteOperationAsync(() => _pipeClient.StopAsync(CancellationToken.None), showMessageOnSuccess: false);
        }
        else if (_lastStatus.RunState == RunState.Error)
        {
            await _desiredStateStore.WriteAsync(true);
            await ExecuteOperationAsync(() => _pipeClient.RestartAsync(BuildStartRequest(), CancellationToken.None));
        }
        else
        {
            await _desiredStateStore.WriteAsync(true);
            await ExecuteOperationAsync(() => _pipeClient.StartAsync(BuildStartRequest(), CancellationToken.None));
        }

        await _statusPoller.PollNowAsync();
    }

    private async void OnImportConfigRequested(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            Title = "Import Config"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            var importedName = await _fileImportService.PrepareImportAsync(dialog.FileName, CancellationToken.None);
            if (importedName is null)
            {
                return;
            }

            await ExecuteOperationAsync(() => _pipeClient.ImportConfigAsync(importedName, CancellationToken.None));
            await _statusPoller.PollNowAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void OnImportCoreRequested(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Official sing-box zip (*.zip)|*.zip",
            Multiselect = false,
            Title = "Import Core"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            var importedName = await _fileImportService.PrepareImportAsync(dialog.FileName, CancellationToken.None);
            if (importedName is null)
            {
                return;
            }

            await ExecuteOperationAsync(() => _pipeClient.ImportCoreAsync(importedName, CancellationToken.None));
            await _statusPoller.PollNowAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnOpenDataFolderRequested(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.ProgramDataRoot,
            UseShellExecute = true
        });
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        if (_isBusy || _isExiting)
        {
            return;
        }

        _isExiting = true;
        _statusPoller.Stop();

        var shouldRestore = _lastStatus?.SingBoxRunning ?? false;
        await _desiredStateStore.WriteAsync(shouldRestore);
        try
        {
            await ExecuteOperationAsync(
                () => _pipeClient.StopAsync(CancellationToken.None),
                showMessageOnSuccess: false,
                suppressExpectedExitErrors: true);
        }
        catch (Exception ex)
        {
            await _clientLogService.WriteWarningAsync($"Exit stop request completed with a suppressed error: {ex.Message}");
        }

        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _statusPoller.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _runningIcon.Dispose();
        _stoppedIcon.Dispose();
        _errorIcon.Dispose();
        base.ExitThreadCore();
    }

    private async Task ExecuteOperationAsync(
        Func<Task<OperationResult>> action,
        bool showMessageOnSuccess = true,
        bool suppressExpectedExitErrors = false)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            var result = await action();
            if (!result.Success)
            {
                if (suppressExpectedExitErrors && _isExiting)
                {
                    await _clientLogService.WriteWarningAsync($"Suppressed exit-time service error: {result.Message}");
                    return;
                }

                ShowError(result.Message);
                return;
            }

            if (showMessageOnSuccess)
            {
                _notifyIcon.ShowBalloonTip(1500, "SingTray", result.Message, ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            if (suppressExpectedExitErrors && _isExiting && IsExpectedExitException(ex))
            {
                await _clientLogService.WriteInfoAsync($"Suppressed expected exit-time exception: {ex.Message}");
                return;
            }

            ShowError(ex.Message);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ShowError(string message)
    {
        _ = _clientLogService.WriteErrorAsync($"User-facing error: {message}");
        MessageBox.Show(message, "SingTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string BuildToolTip(bool serviceAvailable, StatusInfo? status, string? lastConnectionFailure)
    {
        if (!serviceAvailable || status is null)
        {
            var detail = string.IsNullOrWhiteSpace(lastConnectionFailure)
                ? "Service unavailable"
                : lastConnectionFailure;
            var unavailableText = $"SingTray\r\n{detail}";
            return unavailableText.Length > 127 ? unavailableText[..127] : unavailableText;
        }

        var state = status.RunState.ToString();
        var config = status.Config.Installed
            ? status.Config.Valid ? $"Config: {status.Config.FileName}" : "Config: Invalid"
            : "Config: Not installed";
        var core = status.Core.Installed
            ? status.Core.Valid ? $"Core: {status.Core.Version}" : "Core: Invalid"
            : "Core: Not installed";
        var error = string.IsNullOrWhiteSpace(status.LastError) ? string.Empty : $"\r\nError: {status.LastError}";

        var text = $"SingTray\r\n{state}\r\n{config}\r\n{core}{error}";
        return text.Length > 127 ? text[..127] : text;
    }

    private static string BuildConnectionFailureMessage(Exception ex)
    {
        if (ex is PipeClientException pipeEx)
        {
            return pipeEx.Kind switch
            {
                PipeFailureKind.ServiceNotRunning => "Service not running",
                PipeFailureKind.PipeNotFound => "Pipe not found",
                PipeFailureKind.Timeout => "Pipe connection timed out",
                PipeFailureKind.AccessDenied => "Pipe access denied",
                PipeFailureKind.InvalidResponse => "Pipe response invalid",
                PipeFailureKind.ServiceError => pipeEx.Message,
                _ => pipeEx.Message
            };
        }

        return ex.Message;
    }

    private StartRequest BuildStartRequest()
    {
        return new StartRequest
        {
            LastError = _lastStatus?.LastError
        };
    }

    private static bool IsExpectedExitException(Exception ex)
    {
        if (ex is PipeClientException pipeEx)
        {
            return pipeEx.Kind is PipeFailureKind.Timeout or PipeFailureKind.PipeNotFound;
        }

        return ex is TimeoutException or IOException;
    }

    private static Icon CreateStatusIcon(Color color)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        using var border = new Pen(Color.FromArgb(70, 70, 70));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillEllipse(brush, 2, 2, 11, 11);
        graphics.DrawEllipse(border, 2, 2, 11, 11);
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
