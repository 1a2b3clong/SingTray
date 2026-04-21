using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using SingTray.Shared;
using SingTray.Shared.Constants;
using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Service.Services;

public sealed class SingBoxManager
{
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ServiceState _serviceState;
    private readonly LogService _logService;
    private Process? _currentProcess;
    private string? _lastStdErrLine;
    private bool _stopRequested;
    private int _fatalKillTriggered;

    public SingBoxManager(ServiceState serviceState, LogService logService)
    {
        _serviceState = serviceState;
        _logService = logService;
    }

    public async Task<CoreInfo> GetCoreInfoAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.SingBoxExecutablePath))
        {
            return new CoreInfo
            {
                Installed = false,
                Valid = false,
                ValidationMessage = "Core not installed"
            };
        }

        var versionResult = await GetVersionFromExecutableAsync(AppPaths.SingBoxExecutablePath, cancellationToken);
        return versionResult.Success
            ? new CoreInfo { Installed = true, Valid = true, Version = versionResult.Message, ValidationMessage = "OK" }
            : new CoreInfo { Installed = true, Valid = false, ValidationMessage = versionResult.Message };
    }

    public Task<ConfigInfo> GetConfigInfoAsync(CancellationToken cancellationToken) =>
        ValidateConfigFileAsync(AppPaths.ActiveConfigPath, cancellationToken);

    public Task<ConfigInfo> ValidateConfigFileAsync(string configPath, CancellationToken cancellationToken) =>
        ValidateConfigPathAsync(configPath, cancellationToken);

    public async Task<OperationResult> StartAsync(StartRequest? startRequest, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsTrackedProcessAlive())
            {
                if (!string.IsNullOrWhiteSpace(startRequest?.LastError) &&
                    startRequest.LastError.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
                {
                    await _logService.WriteWarningAsync("Previous lastError contains FATAL, forcing cleanup before continuing.", cancellationToken);
                    await CleanupCurrentProcessAsync(cancellationToken);
                }
                else
                {
                    return OperationResult.Ok("sing-box is already running.");
                }
            }

            if (!string.IsNullOrWhiteSpace(startRequest?.LastError) &&
                startRequest.LastError.Contains("FATAL", StringComparison.OrdinalIgnoreCase) &&
                _currentProcess is not null)
            {
                await _logService.WriteWarningAsync("Start request carried FATAL lastError, cleaning up residual sing-box process.", cancellationToken);
                await CleanupCurrentProcessAsync(cancellationToken);
            }

            if (IsTrackedProcessAlive())
            {
                return OperationResult.Ok("sing-box is already running.");
            }

            var (core, config) = await RefreshComponentStateAsync(cancellationToken);

            if (!core.Installed)
            {
                return await FailStartAsync("Core not installed", cancellationToken);
            }

            if (!core.Valid)
            {
                return await FailStartAsync(core.ValidationMessage ?? "Core invalid", cancellationToken);
            }

            if (!config.Installed)
            {
                return await FailStartAsync("Config not installed", cancellationToken);
            }

            if (!config.Valid)
            {
                return await FailStartAsync(config.ValidationMessage ?? "Config invalid", cancellationToken);
            }

            await _serviceState.UpdateAsync(record =>
            {
                record.RunState = RunState.Starting;
                record.LastError = null;
            }, cancellationToken);

            _lastStdErrLine = null;
            Interlocked.Exchange(ref _fatalKillTriggered, 0);
            await _logService.ResetSingBoxLogAsync(cancellationToken);
            var process = BuildProcess(AppPaths.SingBoxExecutablePath, SingBoxConstants.RunArguments, AppPaths.CoreDirectory);
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    _ = _logService.WriteSingBoxOutputAsync("stdout", args.Data, CancellationToken.None);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    var cleaned = StripAnsi(args.Data);
                    _lastStdErrLine = cleaned;
                    _ = _logService.WriteSingBoxOutputAsync("stderr", cleaned, CancellationToken.None);
                    if (cleaned.Contains("FATAL[0009]", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = ForceKillOnFatalAsync(process, cleaned);
                    }
                }
            };
            process.Exited += async (_, _) => await OnProcessExitedAsync(process);

            if (!process.Start())
            {
                return await FailStartAsync("Failed to launch sing-box.", cancellationToken);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _currentProcess = process;

            await Task.Delay(SingBoxConstants.StartProbeDelayMilliseconds, cancellationToken);

            if (!IsProcessAlive(process))
            {
                var exitCode = process.ExitCode;
                var error = BuildExitMessage(exitCode, _lastStdErrLine);
                return await FailStartAsync(error, cancellationToken);
            }

            await _serviceState.UpdateAsync(record =>
            {
                record.RunState = RunState.Running;
                record.SingBoxPid = process.Id;
                record.LastError = null;
            }, cancellationToken);

            await _logService.WriteInfoAsync($"sing-box started with PID {process.Id}.", cancellationToken);
            return OperationResult.Ok("sing-box started.");
        }
        catch (Exception ex)
        {
            return await FailStartAsync($"Failed to start sing-box: {ex.Message}", cancellationToken, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var process = _currentProcess;
            if (!IsProcessAlive(process))
            {
                await _serviceState.UpdateAsync(record =>
                {
                    record.RunState = RunState.Stopped;
                    record.SingBoxPid = null;
                }, cancellationToken);
                return OperationResult.Ok("sing-box is already stopped.");
            }

            var trackedProcess = process!;
            await _serviceState.UpdateAsync(record => record.RunState = RunState.Stopping, cancellationToken);
            var pid = TryGetProcessId(trackedProcess, out var processId)
                ? processId.ToString(CultureInfo.InvariantCulture)
                : "unknown";
            await _logService.WriteInfoAsync($"Stopping sing-box PID {pid}.", cancellationToken);
            _stopRequested = true;

            if (TryGetMainWindowHandle(trackedProcess) != IntPtr.Zero)
            {
                TryCloseMainWindow(trackedProcess);
            }

            if (!await WaitForExitAsync(trackedProcess, SingBoxConstants.StopTimeoutMilliseconds, cancellationToken))
            {
                await ForceTerminateProcessAsync(trackedProcess, "stop request timeout", cancellationToken);
                if (IsProcessAlive(trackedProcess))
                {
                    return OperationResult.Fail("Failed to stop sing-box within timeout.");
                }
            }

            await _serviceState.UpdateAsync(record =>
            {
                record.RunState = RunState.Stopped;
                record.SingBoxPid = null;
                record.LastError = null;
            }, cancellationToken);

            if (ReferenceEquals(_currentProcess, trackedProcess))
            {
                _currentProcess.Dispose();
                _currentProcess = null;
            }

            return OperationResult.Ok("sing-box stopped.");
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync("Stop sing-box failed.", ex, cancellationToken);
            await _serviceState.UpdateAsync(record =>
            {
                record.RunState = RunState.Error;
                record.LastError = ex.Message;
            }, cancellationToken);
            return OperationResult.Fail($"Failed to stop sing-box: {ex.Message}");
        }
        finally
        {
            _stopRequested = false;
            _gate.Release();
        }
    }

    public async Task<OperationResult> RestartAsync(StartRequest? startRequest, CancellationToken cancellationToken)
    {
        var stop = await StopAsync(cancellationToken);
        if (!stop.Success)
        {
            return stop;
        }

        return await StartAsync(startRequest, cancellationToken);
    }

    private async Task<ConfigInfo> ValidateConfigPathAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return new ConfigInfo
            {
                Installed = false,
                Valid = false,
                ValidationMessage = "Config not installed"
            };
        }

        try
        {
            await using var stream = File.OpenRead(configPath);
            await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return new ConfigInfo
            {
                Installed = true,
                Valid = false,
                FileName = Path.GetFileName(configPath),
                ValidationMessage = $"Config invalid: {ex.Message}"
            };
        }

        if (File.Exists(AppPaths.SingBoxExecutablePath))
        {
            var checkArgs = SingBoxConstants.CheckArguments.Concat([configPath]).ToArray();
            var result = await RunProcessCaptureAsync(AppPaths.SingBoxExecutablePath, checkArgs, AppPaths.CoreDirectory, cancellationToken);
            if (!result.Success)
            {
                return new ConfigInfo
                {
                    Installed = true,
                    Valid = false,
                    FileName = Path.GetFileName(configPath),
                    ValidationMessage = result.Message
                };
            }
        }

        return new ConfigInfo
        {
            Installed = true,
            Valid = true,
            FileName = Path.GetFileName(configPath),
            ValidationMessage = "OK"
        };
    }

    public async Task<OperationResult> ValidateCoreExecutableAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            return OperationResult.Fail("Core not installed");
        }

        return await GetVersionFromExecutableAsync(executablePath, cancellationToken);
    }

    public async Task<StatusInfo> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _serviceState.UpdateAsync(record =>
        {
            if (!IsTrackedProcessAlive())
            {
                if (record.RunState is RunState.Running or RunState.Starting or RunState.Stopping)
                {
                    record.RunState = RunState.Stopped;
                    record.SingBoxPid = null;
                }
            }
        }, cancellationToken);

        return await _serviceState.CreateStatusSnapshotAsync(cancellationToken);
    }

    private async Task<OperationResult> GetVersionFromExecutableAsync(string executablePath, CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(executablePath, SingBoxConstants.VersionArguments, Path.GetDirectoryName(executablePath)!, cancellationToken);
        if (!result.Success)
        {
            return OperationResult.Fail(result.Message);
        }

        var versionLine = result.Message
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("sing-box", StringComparison.OrdinalIgnoreCase))
            ?? result.Message.Trim();

        return OperationResult.Ok(versionLine);
    }

    private static Process BuildProcess(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { }
            }
        }.Also(process =>
        {
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
        });
    }

    private async Task<OperationResult> RunProcessCaptureAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = BuildProcess(fileName, arguments, workingDirectory);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    stdout.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    stderr.AppendLine(StripAnsi(args.Data));
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = BuildExitMessage(process.ExitCode, stderr.ToString().Trim());
                return OperationResult.Fail(error);
            }

            return OperationResult.Ok(stdout.ToString().Trim());
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    private async Task OnProcessExitedAsync(Process process)
    {
        var exitCode = process.ExitCode;
        var isIntentionalStop = _stopRequested;
        var stderrMessage = string.IsNullOrWhiteSpace(_lastStdErrLine) ? null : _lastStdErrLine;
        var exitStatus = BuildExitStatus(exitCode);

        await _logService.WriteInfoAsync($"sing-box exited with code {exitCode}.", CancellationToken.None);
        await _serviceState.UpdateAsync(record =>
        {
            record.SingBoxPid = null;
            record.RunState = exitCode == 0 || isIntentionalStop ? RunState.Stopped : RunState.Error;
            record.LastError = isIntentionalStop ? null : stderrMessage;
            record.ExitStatus = exitStatus;
        }, CancellationToken.None);

        if (ReferenceEquals(_currentProcess, process))
        {
            _currentProcess.Dispose();
            _currentProcess = null;
        }
    }

    private async Task<OperationResult> FailStartAsync(string message, CancellationToken cancellationToken, Exception? ex = null)
    {
        await _logService.WriteErrorAsync(message, ex, cancellationToken);
        await _serviceState.UpdateAsync(record =>
        {
            record.RunState = RunState.Error;
            record.LastError = _lastStdErrLine ?? message;
            record.ExitStatus = "Start failed";
            record.SingBoxPid = null;
        }, cancellationToken);

        await CleanupCurrentProcessAsync(cancellationToken);

        return OperationResult.Fail(message);
    }

    private async Task<(CoreInfo Core, ConfigInfo Config)> RefreshComponentStateAsync(CancellationToken cancellationToken)
    {
        var core = await GetCoreInfoAsync(cancellationToken);
        var config = await GetConfigInfoAsync(cancellationToken);

        await _serviceState.UpdateAsync(record =>
        {
            record.CoreInstalled = core.Installed;
            record.CoreValid = core.Valid;
            record.CoreVersion = core.Version;
            record.CoreValidationMessage = core.ValidationMessage;
            record.ConfigInstalled = config.Installed;
            record.ConfigValid = config.Valid;
            record.ConfigName = config.FileName;
            record.ConfigValidationMessage = config.ValidationMessage;
        }, cancellationToken);

        return (core, config);
    }

    private async Task CleanupCurrentProcessAsync(CancellationToken cancellationToken)
    {
        if (_currentProcess is null)
        {
            return;
        }

        try
        {
            if (IsTrackedProcessAlive())
            {
                var pid = TryGetProcessId(_currentProcess, out var trackedPid)
                    ? trackedPid.ToString(CultureInfo.InvariantCulture)
                    : "unknown";
                await _logService.WriteWarningAsync($"Cleaning up sing-box PID {pid} after start failure.", cancellationToken);
                await ForceTerminateProcessAsync(_currentProcess, "start failure cleanup", cancellationToken);
            }
        }
        catch (Exception cleanupEx)
        {
            await _logService.WriteErrorAsync("Failed to clean up sing-box after start failure.", cleanupEx, cancellationToken);
        }
        finally
        {
            _currentProcess.Dispose();
            _currentProcess = null;
        }
    }

    private async Task ForceKillOnFatalAsync(Process process, string fatalMessage)
    {
        if (Interlocked.Exchange(ref _fatalKillTriggered, 1) == 1)
        {
            return;
        }

        try
        {
            await _logService.WriteWarningAsync($"Fatal startup marker detected, forcing sing-box shutdown: {fatalMessage}", CancellationToken.None);

            if (!IsProcessAlive(process))
            {
                await CleanupManagedProcessesAsync("fatal startup marker orphan cleanup", CancellationToken.None);
                return;
            }

            await ForceTerminateProcessAsync(process, "fatal startup marker", CancellationToken.None);
            await CleanupManagedProcessesAsync("fatal startup marker orphan cleanup", CancellationToken.None);
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync("Failed to force-kill sing-box after fatal startup marker.", ex, CancellationToken.None);
        }
    }

    private async Task ForceTerminateProcessAsync(Process process, string reason, CancellationToken cancellationToken)
    {
        if (!IsProcessAlive(process))
        {
            return;
        }

        if (!TryGetProcessId(process, out var pid))
        {
            await _logService.WriteWarningAsync($"Process handle was no longer associated while attempting to terminate sing-box due to {reason}.", cancellationToken);
            return;
        }

        try
        {
            await _logService.WriteWarningAsync($"Force terminating sing-box PID {pid} due to {reason}.", cancellationToken);
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"Managed Kill failed for sing-box PID {pid}.", ex, cancellationToken);
        }

        if (await WaitForExitAsync(process, 2500, cancellationToken))
        {
            await _logService.WriteInfoAsync($"sing-box PID {pid} terminated by managed kill.", cancellationToken);
            return;
        }

        await _logService.WriteWarningAsync($"Managed kill did not fully terminate sing-box PID {pid}, falling back to taskkill.", cancellationToken);
        await ForceTerminateWithTaskKillAsync(pid, cancellationToken);

        if (await WaitForExitAsync(process, 2500, cancellationToken))
        {
            await _logService.WriteInfoAsync($"sing-box PID {pid} terminated by taskkill fallback.", cancellationToken);
            return;
        }

        await _logService.WriteWarningAsync($"sing-box PID {pid} still appears alive after taskkill fallback.", cancellationToken);
    }

    public async Task StopForServiceShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (IsTrackedProcessAlive())
            {
                await _logService.WriteInfoAsync("Service shutdown requested, stopping managed sing-box process.", cancellationToken);
                await StopAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync("Graceful sing-box stop during service shutdown failed.", ex, cancellationToken);
        }

        await CleanupManagedProcessesAsync("service shutdown cleanup", cancellationToken);
    }

    public async Task CleanupManagedProcessesAsync(string reason, CancellationToken cancellationToken)
    {
        var trackedProcess = _currentProcess;
        if (IsProcessAlive(trackedProcess))
        {
            await ForceTerminateProcessAsync(trackedProcess!, reason, cancellationToken);
        }

        foreach (var process in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                if (!IsManagedSingBoxProcess(process))
                {
                    continue;
                }

                if (!TryGetProcessId(process, out var pid))
                {
                    continue;
                }

                await _logService.WriteWarningAsync($"Found managed orphan sing-box PID {pid} during {reason}.", cancellationToken);
                await ForceTerminateProcessAsync(process, reason, cancellationToken);
            }
            catch (Exception ex)
            {
                await _logService.WriteErrorAsync("Failed while cleaning up orphan sing-box process.", ex, cancellationToken);
            }
            finally
            {
                process.Dispose();
            }
        }

        if (_currentProcess is not null && !IsProcessAlive(_currentProcess))
        {
            _currentProcess.Dispose();
            _currentProcess = null;
        }
    }

    private async Task ForceTerminateWithTaskKillAsync(int pid, CancellationToken cancellationToken)
    {
        try
        {
            using var taskKill = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            taskKill.StartInfo.ArgumentList.Add("/PID");
            taskKill.StartInfo.ArgumentList.Add(pid.ToString());
            taskKill.StartInfo.ArgumentList.Add("/T");
            taskKill.StartInfo.ArgumentList.Add("/F");

            taskKill.Start();
            var stdoutTask = taskKill.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = taskKill.StandardError.ReadToEndAsync(cancellationToken);
            await taskKill.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (taskKill.ExitCode == 0)
            {
                await _logService.WriteInfoAsync($"taskkill succeeded for sing-box PID {pid}: {stdout.Trim()}", cancellationToken);
            }
            else
            {
                await _logService.WriteWarningAsync($"taskkill exit code {taskKill.ExitCode} for sing-box PID {pid}: {stderr.Trim()}", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"taskkill fallback failed for sing-box PID {pid}.", ex, cancellationToken);
        }
    }

    private static string BuildExitMessage(int exitCode, string? stderr)
    {
        return string.IsNullOrWhiteSpace(stderr)
            ? $"sing-box exited with code {exitCode}."
            : $"sing-box exited with code {exitCode}: {StripAnsi(stderr).Trim()}";
    }

    private static string BuildExitStatus(int exitCode)
    {
        return exitCode == 0 ? "Exited normally" : $"Exited with code {exitCode}";
    }

    private static string StripAnsi(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? value : AnsiEscapeRegex.Replace(value, string.Empty);
    }

    private static bool IsProcessAlive(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsTrackedProcessAlive() => IsProcessAlive(_currentProcess);

    private static bool TryGetProcessId(Process process, out int pid)
    {
        try
        {
            pid = process.Id;
            return true;
        }
        catch (InvalidOperationException)
        {
            pid = 0;
            return false;
        }
    }

    private static IntPtr TryGetMainWindowHandle(Process process)
    {
        try
        {
            return process.MainWindowHandle;
        }
        catch (InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }

    private static void TryCloseMainWindow(Process process)
    {
        try
        {
            process.CloseMainWindow();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsManagedSingBoxProcess(Process process)
    {
        var processPath = TryGetProcessPath(process);
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(processPath),
            Path.GetFullPath(AppPaths.SingBoxExecutablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitForExitAsync(Process process, int milliseconds, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(milliseconds);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return IsProcessAlive(process) is false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}

internal static class FunctionalExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
