using System.Text;
using Microsoft.Extensions.Logging;
using SingTray.Shared;

namespace SingTray.Service.Services;

public sealed class LogService : IDisposable, IAsyncDisposable
{
    private static readonly bool EnableDebugLogging = false;

    private readonly SemaphoreSlim _appLogLock = new(1, 1);
    private readonly ILogger<LogService> _logger;
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

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _appLogLock.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
