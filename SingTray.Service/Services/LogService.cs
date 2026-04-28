using System.Text;
using Microsoft.Extensions.Logging;
using SingTray.Shared;

namespace SingTray.Service.Services;

public sealed class LogService : IDisposable, IAsyncDisposable
{
    private static readonly bool EnableDebugLogging = false;
    private const int SingBoxLogBufferSize = 64 * 1024;
    private static readonly TimeSpan SingBoxLogFlushInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _appLogLock = new(1, 1);
    private readonly SemaphoreSlim _singBoxLogLock = new(1, 1);
    private readonly ILogger<LogService> _logger;
    private StreamWriter? _singBoxLogWriter;
    private CancellationTokenSource? _singBoxFlushCts;
    private Task? _singBoxFlushTask;
    private bool _disposed;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureDataDirectories();
        DeleteLegacyLogSessionState();
        await ResetAppLogAsync(cancellationToken);
        await WriteAppLogAsync("Service logging initialized.", cancellationToken);
    }

    public Task WriteInfoAsync(string message, CancellationToken cancellationToken) =>
        WriteAppLogAsync($"INFO {message}", LogLevel.Information, cancellationToken);

    public Task WriteWarningAsync(string message, CancellationToken cancellationToken) =>
        WriteAppLogAsync($"WARN {message}", LogLevel.Warning, cancellationToken);

    public Task WriteDebugAsync(string message, CancellationToken cancellationToken)
    {
        if (!EnableDebugLogging)
        {
            return Task.CompletedTask;
        }

        return WriteAppLogAsync($"DEBUG {message}", LogLevel.Debug, cancellationToken);
    }

    public Task WriteErrorAsync(string message, Exception? exception, CancellationToken cancellationToken)
    {
        var fullMessage = exception is null
            ? $"ERROR {message}"
            : $"ERROR {message}{Environment.NewLine}{exception}";
        return WriteAppLogAsync(fullMessage, LogLevel.Error, cancellationToken);
    }

    public async Task WriteSingBoxOutputAsync(string source, string line, CancellationToken cancellationToken)
    {
        var entry = line + Environment.NewLine;
        await _singBoxLogLock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return;
            }

            EnsureSingBoxLogWriter(append: true);
            await _singBoxLogWriter!.WriteAsync(entry.AsMemory(), cancellationToken);
        }
        finally
        {
            _singBoxLogLock.Release();
        }
    }

    public async Task ResetSingBoxLogAsync(CancellationToken cancellationToken)
    {
        await _singBoxLogLock.WaitAsync(cancellationToken);
        try
        {
            await CloseSingBoxLogWriterCoreAsync();
            EnsureSingBoxLogWriter(append: false);
        }
        finally
        {
            _singBoxLogLock.Release();
        }
    }

    public async Task FlushSingBoxLogAsync(CancellationToken cancellationToken)
    {
        await _singBoxLogLock.WaitAsync(cancellationToken);
        try
        {
            if (_singBoxLogWriter is not null)
            {
                await _singBoxLogWriter.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            _singBoxLogLock.Release();
        }
    }

    private async Task ResetAppLogAsync(CancellationToken cancellationToken)
    {
        await _appLogLock.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(AppPaths.AppLogPath, string.Empty, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _appLogLock.Release();
        }
    }

    private static void DeleteLegacyLogSessionState()
    {
        try
        {
            var legacyPath = Path.Combine(AppPaths.StateDirectory, "log-session.json");
            if (File.Exists(legacyPath))
            {
                File.SetAttributes(legacyPath, FileAttributes.Normal);
                File.Delete(legacyPath);
            }
        }
        catch
        {
            // Best effort cleanup for the old boot-session log state file.
        }
    }

    private async Task WriteAppLogAsync(string message, CancellationToken cancellationToken) =>
        await WriteAppLogAsync(message, LogLevel.Information, cancellationToken);

    private async Task WriteAppLogAsync(string message, LogLevel level, CancellationToken cancellationToken)
    {
        var entry = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
        _logger.Log(level, "{Message}", message);

        await _appLogLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(AppPaths.AppLogPath, entry, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _appLogLock.Release();
        }
    }

    private void EnsureSingBoxLogWriter(bool append)
    {
        if (_singBoxLogWriter is not null)
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.LogsDirectory);
        var stream = new FileStream(
            AppPaths.SingBoxLogPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            SingBoxLogBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        _singBoxLogWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), SingBoxLogBufferSize)
        {
            AutoFlush = false
        };

        EnsureSingBoxFlushLoop();
    }

    private void EnsureSingBoxFlushLoop()
    {
        if (_singBoxFlushTask is not null)
        {
            return;
        }

        _singBoxFlushCts = new CancellationTokenSource();
        _singBoxFlushTask = Task.Run(() => RunSingBoxFlushLoopAsync(_singBoxFlushCts.Token));
    }

    private async Task RunSingBoxFlushLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SingBoxLogFlushInterval, cancellationToken);
                await FlushSingBoxLogAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to flush sing-box log.");
            }
        }
    }

    private async Task CloseSingBoxLogWriterCoreAsync()
    {
        if (_singBoxLogWriter is null)
        {
            return;
        }

        await _singBoxLogWriter.FlushAsync();
        await _singBoxLogWriter.DisposeAsync();
        _singBoxLogWriter = null;
    }

    private async Task StopSingBoxFlushLoopAsync()
    {
        var cts = _singBoxFlushCts;
        var task = _singBoxFlushTask;
        _singBoxFlushCts = null;
        _singBoxFlushTask = null;

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (task is not null)
            {
                await task;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopSingBoxFlushLoopAsync();

        await _singBoxLogLock.WaitAsync();
        try
        {
            await CloseSingBoxLogWriterCoreAsync();
        }
        finally
        {
            _singBoxLogLock.Release();
        }

        _appLogLock.Dispose();
        _singBoxLogLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
