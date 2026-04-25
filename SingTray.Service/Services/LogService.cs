using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SingTray.Shared;

namespace SingTray.Service.Services;

public sealed class LogService
{
    private static readonly bool EnableDebugLogging = false;
    private static readonly TimeSpan BootSessionComparisonTolerance = TimeSpan.FromMinutes(1);
    private static readonly string[] SuppressedSingBoxCategories =
    [
        " connection:",
        " dns:",
        " router:"
    ];

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

        var currentBootSessionUtc = GetCurrentBootSessionUtc();
        var previousBootSessionUtc = await LoadBootSessionUtcAsync(cancellationToken);
        var shouldResetLogs = previousBootSessionUtc is null ||
            Math.Abs((currentBootSessionUtc - previousBootSessionUtc.Value).TotalMinutes) > BootSessionComparisonTolerance.TotalMinutes;

        if (shouldResetLogs)
        {
            await ResetAllLogsAsync(cancellationToken);
        }

        await SaveBootSessionUtcAsync(currentBootSessionUtc, cancellationToken);
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
        if (ShouldSuppressSingBoxOutput(source, line))
        {
            return;
        }

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

    private static bool ShouldSuppressSingBoxOutput(string source, string line)
    {
        if (!string.Equals(source, "stderr", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var category in SuppressedSingBoxCategories)
        {
            if (line.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ResetAllLogsAsync(CancellationToken cancellationToken)
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

    private async Task<DateTimeOffset?> LoadBootSessionUtcAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.LogSessionStatePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(AppPaths.LogSessionStatePath);
        var state = await JsonSerializer.DeserializeAsync<LogSessionState>(stream, cancellationToken: cancellationToken);
        return state?.BootSessionUtc;
    }

    private static DateTimeOffset GetCurrentBootSessionUtc()
    {
        return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    private async Task SaveBootSessionUtcAsync(DateTimeOffset bootSessionUtc, CancellationToken cancellationToken)
    {
        var state = new LogSessionState
        {
            BootSessionUtc = bootSessionUtc
        };

        await using var stream = File.Create(AppPaths.LogSessionStatePath);
        await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken);
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

    private sealed class LogSessionState
    {
        public DateTimeOffset BootSessionUtc { get; set; }
    }
}
