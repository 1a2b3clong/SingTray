using System.Text;
using Microsoft.Extensions.Logging;
using SingTray.Shared;

namespace SingTray.Service.Services;

public sealed class LogService
{
    private static readonly bool EnableDebugLogging = false;

    private readonly SemaphoreSlim _appLogLock = new(1, 1);
    private readonly SemaphoreSlim _singBoxLogLock = new(1, 1);
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureDataDirectories();
        await File.WriteAllTextAsync(AppPaths.AppLogPath, string.Empty, Encoding.UTF8, cancellationToken);
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
        var entry = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{source}] {line}{Environment.NewLine}";
        await _singBoxLogLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(AppPaths.SingBoxLogPath, entry, Encoding.UTF8, cancellationToken);
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
            await File.WriteAllTextAsync(AppPaths.SingBoxLogPath, string.Empty, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _singBoxLogLock.Release();
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
}
