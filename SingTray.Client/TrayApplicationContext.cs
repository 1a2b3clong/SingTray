using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
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
    private readonly StatusWatcher _statusWatcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuBuilder _menuBuilder;
    private readonly ContextMenuStrip _menu;
    private StatusInfo? _lastStatus;
    private string? _lastConnectionFailure;
    private readonly HashSet<string> _shownErrorSignatures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeErrorDialogSignatures = new(StringComparer.Ordinal);
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
        _statusWatcher = new StatusWatcher(pipeClient);
        _statusWatcher.StatusUpdated += (_, status) =>
        {
            if (_isExiting)
            {
                return;
            }

            var previousStatus = _lastStatus;
            _lastStatus = status;
            _lastConnectionFailure = null;
            RefreshUi(serviceAvailable: true);
            ShowStatusErrorIfNeeded(previousStatus, status);
        };
        _statusWatcher.WatchFailed += async (_, ex) =>
        {
            if (_isExiting && IsExpectedExitException(ex))
            {
                await _clientLogService.WriteInfoAsync($"Status poll suppressed during exit: {BuildConnectionFailureMessage(ex)}");
                return;
            }

            _lastStatus = null;
            _lastConnectionFailure = BuildConnectionFailureMessage(ex);
            _shownErrorSignatures.Clear();
            await _clientLogService.WriteWarningAsync($"Status poll failed: {_lastConnectionFailure}");
            RefreshUi(serviceAvailable: false);
        };

        _menuBuilder = new TrayMenuBuilder(OnToggleRequested, OnImportConfigRequested, OnImportCoreRequested, OnOpenDataFolderRequested, OnExitRequested);
        _menu = _menuBuilder.Build();
        _runningIcon = CreateStatusIcon(Color.FromArgb(56, 189, 104));
        _stoppedIcon = CreateStatusIcon(Color.FromArgb(234, 179, 8));
        _errorIcon = CreateStatusIcon(Color.FromArgb(220, 38, 38));
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
        await _statusWatcher.RefreshNowAsync();
        _statusWatcher.Start();
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
                await ExecuteOperationAsync(() => _pipeClient.StartAsync(CancellationToken.None), showMessageOnSuccess: false);
                await _statusWatcher.RefreshNowAsync();
            }
            else if (!shouldBeRunning && _lastStatus.SingBoxRunning)
            {
                await ExecuteOperationAsync(
                    () => _pipeClient.StopAsync(CancellationToken.None),
                    showMessageOnSuccess: false,
                    suppressExpectedStopErrors: true);
                await _statusWatcher.RefreshNowAsync();
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
            ApplyLocalRunState(RunState.Stopping);
            var stopped = await ExecuteOperationAsync(
                () => _pipeClient.StopAsync(CancellationToken.None),
                showMessageOnSuccess: false,
                suppressExpectedStopErrors: true);
            if (stopped)
            {
                ApplyLocalRunState(RunState.Stopped);
            }
        }
        else if (_lastStatus.RunState == RunState.Error)
        {
            var restartRequired = _lastStatus.SingBoxRunning;
            await _desiredStateStore.WriteAsync(true);
            ApplyLocalRunState(RunState.Starting);
            var started = restartRequired
                ? await ExecuteOperationAsync(() => _pipeClient.RestartAsync(CancellationToken.None))
                : await ExecuteOperationAsync(() => _pipeClient.StartAsync(CancellationToken.None));
            if (started)
            {
                ApplyLocalRunState(RunState.Running);
            }
        }
        else
        {
            await _desiredStateStore.WriteAsync(true);
            ApplyLocalRunState(RunState.Starting);
            var started = await ExecuteOperationAsync(() => _pipeClient.StartAsync(CancellationToken.None));
            if (started)
            {
                ApplyLocalRunState(RunState.Running);
            }
        }

        await _statusWatcher.RefreshNowAsync();
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

        string? importedName = null;
        try
        {
            importedName = await _fileImportService.PrepareImportAsync(dialog.FileName, CancellationToken.None);
            if (importedName is null)
            {
                return;
            }

            await ExecuteOperationAsync(() => _pipeClient.ImportConfigAsync(importedName, CancellationToken.None));
            await _statusWatcher.RefreshNowAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _fileImportService.DeletePreparedImport(importedName);
            _fileImportService.CleanupPreparedImports();
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

        string? importedName = null;
        try
        {
            importedName = await _fileImportService.PrepareImportAsync(dialog.FileName, CancellationToken.None);
            if (importedName is null)
            {
                return;
            }

            await ExecuteOperationAsync(() => _pipeClient.ImportCoreAsync(importedName, CancellationToken.None));
            await _statusWatcher.RefreshNowAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _fileImportService.DeletePreparedImport(importedName);
            _fileImportService.CleanupPreparedImports();
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
        _statusWatcher.Stop();

        var shouldRestore = _lastStatus?.SingBoxRunning ?? false;
        await _desiredStateStore.WriteAsync(shouldRestore);
        try
        {
            await ExecuteOperationAsync(
                () => _pipeClient.StopAsync(CancellationToken.None),
                showMessageOnSuccess: false,
                suppressExpectedExitErrors: true,
                suppressExpectedStopErrors: true);
        }
        catch (Exception ex)
        {
            await _clientLogService.WriteWarningAsync($"Exit stop request completed with a suppressed error: {ex.Message}");
        }

        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _statusWatcher.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _runningIcon.Dispose();
        _stoppedIcon.Dispose();
        _errorIcon.Dispose();
        base.ExitThreadCore();
    }

    private void ApplyLocalRunState(RunState runState)
    {
        if (_lastStatus is null)
        {
            return;
        }

        _lastStatus.RunState = runState;
        _lastStatus.SingBoxRunning = runState is RunState.Running or RunState.Starting;
        if (runState == RunState.Stopped)
        {
            _lastStatus.SingBoxPid = null;
        }

        if (runState is RunState.Starting or RunState.Running or RunState.Stopping or RunState.Stopped)
        {
            _lastStatus.LastError = null;
            _lastStatus.LastErrorKind = null;
            _shownErrorSignatures.Clear();
        }

        RefreshUi(serviceAvailable: true);
    }

    private async Task<bool> ExecuteOperationAsync(
        Func<Task<OperationResult>> action,
        bool showMessageOnSuccess = true,
        bool suppressExpectedExitErrors = false,
        bool suppressExpectedStopErrors = false)
    {
        if (_isBusy)
        {
            return false;
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
                    return true;
                }

                if (suppressExpectedStopErrors && IsExpectedStopMessage(result.Message))
                {
                    await _clientLogService.WriteInfoAsync($"Suppressed expected stop-time service error: {result.Message}");
                    return true;
                }

                ShowOperationError(result);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (suppressExpectedExitErrors && _isExiting && IsExpectedExitException(ex))
            {
                await _clientLogService.WriteInfoAsync($"Suppressed expected exit-time exception: {ex.Message}");
                return true;
            }

            if (suppressExpectedStopErrors && IsExpectedStopException(ex))
            {
                await _clientLogService.WriteInfoAsync($"Suppressed expected stop-time exception: {ex.Message}");
                return true;
            }

            ShowError(ex.Message);
            return false;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ShowError(string message)
    {
        ShowError(message, "SingTray", MessageBoxIcon.Error);
    }

    private void ShowOperationError(OperationResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message) ? "Operation failed." : result.Message;
        if (!TryMarkTypedErrorShown(result.ErrorKind, message))
        {
            return;
        }

        ShowTypedError(result.ErrorKind, message);
    }

    private void ShowStatusErrorIfNeeded(StatusInfo? previousStatus, StatusInfo status)
    {
        if (previousStatus is null)
        {
            return;
        }

        if (status.RunState != RunState.Error)
        {
            return;
        }

        if (!IsUserFacingStatusError(status.LastErrorKind))
        {
            return;
        }

        var message = BuildStatusErrorMessage(status);
        var signature = BuildErrorSignature(status.LastErrorKind, message);
        if (_shownErrorSignatures.Contains(signature))
        {
            return;
        }

        if (previousStatus?.RunState == RunState.Error
            && previousStatus.LastErrorKind == status.LastErrorKind
            && string.Equals(BuildStatusErrorMessage(previousStatus), message, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryMarkTypedErrorShown(status.LastErrorKind, message))
        {
            return;
        }

        ShowTypedError(status.LastErrorKind, message);
    }

    private void ShowTypedError(OperationErrorKind? errorKind, string message)
    {
        switch (errorKind)
        {
            case OperationErrorKind.ConfigInvalid:
                ShowError(
                    $"Config validation failed.\r\n\r\n{message}",
                    "SingTray - Config Error",
                    MessageBoxIcon.Warning);
                break;
            case OperationErrorKind.SingBoxFatal:
                ShowError(
                    $"sing-box exited with a fatal error.\r\n\r\n{message}",
                    "SingTray - sing-box Fatal Error",
                    MessageBoxIcon.Error);
                break;
            case OperationErrorKind.SingBoxUnexpectedExit:
                ShowError(
                    $"sing-box stopped unexpectedly.\r\n\r\n{message}",
                    "SingTray - sing-box Error",
                    MessageBoxIcon.Error);
                break;
            case OperationErrorKind.SingBoxStartFailed:
                ShowError(
                    $"sing-box failed to start.\r\n\r\n{message}",
                    "SingTray - sing-box Start Error",
                    MessageBoxIcon.Error);
                break;
            default:
                ShowError(message);
                break;
        }
    }

    private void ShowError(string message, string title, MessageBoxIcon icon)
    {
        var dialogSignature = BuildDialogSignature(title, message);
        if (!_activeErrorDialogSignatures.Add(dialogSignature))
        {
            return;
        }

        try
        {
            _ = _clientLogService.WriteErrorAsync($"User-facing error ({title}): {message}");
            MessageBox.Show(message, title, MessageBoxButtons.OK, icon);
        }
        finally
        {
            _activeErrorDialogSignatures.Remove(dialogSignature);
        }
    }

    private static string BuildStatusErrorMessage(StatusInfo status)
    {
        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            return status.LastError;
        }

        if (status.LastErrorKind == OperationErrorKind.ConfigInvalid
            && !string.IsNullOrWhiteSpace(status.Config.ValidationMessage))
        {
            return status.Config.ValidationMessage;
        }

        return status.ExitStatus ?? "sing-box failed.";
    }

    private static string BuildErrorSignature(OperationErrorKind? errorKind, string message)
    {
        return $"{errorKind?.ToString() ?? "Unknown"}|{NormalizeErrorMessage(message)}";
    }

    private static string BuildDialogSignature(string title, string message)
    {
        return $"{NormalizeErrorMessage(title)}|{NormalizeErrorMessage(message)}";
    }

    private static string NormalizeErrorMessage(string message)
    {
        return message
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private bool TryMarkTypedErrorShown(OperationErrorKind? errorKind, string message)
    {
        if (!IsDeduplicatedTypedError(errorKind))
        {
            return true;
        }

        var signature = BuildErrorSignature(errorKind, message);
        return _shownErrorSignatures.Add(signature);
    }

    private static bool IsUserFacingStatusError(OperationErrorKind? errorKind)
    {
        return errorKind is OperationErrorKind.ConfigInvalid
            or OperationErrorKind.SingBoxFatal
            or OperationErrorKind.SingBoxStartFailed
            or OperationErrorKind.SingBoxUnexpectedExit;
    }

    private static bool IsDeduplicatedTypedError(OperationErrorKind? errorKind)
    {
        return errorKind is OperationErrorKind.ConfigInvalid
            or OperationErrorKind.SingBoxFatal
            or OperationErrorKind.SingBoxStartFailed
            or OperationErrorKind.SingBoxUnexpectedExit;
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

    private static bool IsExpectedExitException(Exception ex)
    {
        if (ex is PipeClientException pipeEx)
        {
            return pipeEx.Kind is PipeFailureKind.Timeout or PipeFailureKind.PipeNotFound;
        }

        return ex is TimeoutException or IOException;
    }

    private static bool IsExpectedStopException(Exception ex)
    {
        if (ex is PipeClientException pipeEx)
        {
            return pipeEx.Kind is PipeFailureKind.Timeout or PipeFailureKind.PipeNotFound;
        }

        return ex is TimeoutException or IOException;
    }

    private static bool IsExpectedStopMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || message.Contains("pipe", StringComparison.OrdinalIgnoreCase)
            || message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase);
    }

    private static Icon CreateStatusIcon(Color color)
    {
        const int size = 32;
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var accentPath = CreateSpeedSPath();
        graphics.TranslateTransform(0.8f, 0.9f);
        using var shadowPen = new Pen(Color.FromArgb(84, 0, 0, 0), 4.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawPath(shadowPen, accentPath);
        graphics.ResetTransform();

        using var accentPen = new Pen(color, 4.25f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawPath(accentPen, accentPath);

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

    private static GraphicsPath CreateSpeedSPath()
    {
        var path = new GraphicsPath();
        path.AddBezier(
            new PointF(22.0f, 7.8f),
            new PointF(17.6f, 4.2f),
            new PointF(9.2f, 5.3f),
            new PointF(10.0f, 10.6f));
        path.AddBezier(
            new PointF(10.0f, 10.6f),
            new PointF(10.9f, 14.2f),
            new PointF(22.8f, 12.5f),
            new PointF(21.9f, 18.1f));
        path.AddBezier(
            new PointF(21.9f, 18.1f),
            new PointF(21.0f, 23.0f),
            new PointF(12.9f, 25.5f),
            new PointF(8.8f, 22.0f));
        return path;
    }
}
