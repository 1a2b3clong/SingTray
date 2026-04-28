using SingTray.Shared.Models;

namespace SingTray.Client;

public sealed class StatusWatcher : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly PipeClient _pipeClient;
    private readonly SynchronizationContext? _uiContext;
    private readonly object _gate = new();
    private CancellationTokenSource? _watchCts;
    private Task? _watchTask;
    private long? _lastSeenRevision;
    private int _isRefreshing;
    private bool _disposed;

    public event EventHandler<StatusInfo>? StatusUpdated;
    public event EventHandler<Exception>? WatchFailed;

    public StatusWatcher(PipeClient pipeClient)
    {
        _pipeClient = pipeClient;
        _uiContext = SynchronizationContext.Current;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchTask is not null)
            {
                return;
            }

            _watchCts = new CancellationTokenSource();
            _watchTask = Task.Run(() => WatchStatusChangesAsync(_watchCts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? watchTask;
        lock (_gate)
        {
            cts = _watchCts;
            watchTask = _watchTask;
            _watchCts = null;
            _watchTask = null;
        }

        cts?.Cancel();
        if (cts is not null)
        {
            _ = (watchTask ?? Task.CompletedTask).ContinueWith(
                _ => cts.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    public async Task RefreshNowAsync()
    {
        if (Interlocked.Exchange(ref _isRefreshing, 1) == 1)
        {
            return;
        }

        try
        {
            var status = await _pipeClient.GetStatusAsync(CancellationToken.None);
            PublishStatus(status);
        }
        catch (Exception ex)
        {
            PublishFailure(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isRefreshing, 0);
        }
    }

    private async Task WatchStatusChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var status = await _pipeClient.WaitStatusChangeAsync(GetLastSeenRevision(), cancellationToken);
                PublishStatus(status);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                PublishFailure(ex);
                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private long? GetLastSeenRevision()
    {
        lock (_gate)
        {
            return _lastSeenRevision;
        }
    }

    private void PublishStatus(StatusInfo status)
    {
        lock (_gate)
        {
            _lastSeenRevision = status.StateRevision;
        }

        PostToUi(() => StatusUpdated?.Invoke(this, status));
    }

    private void PublishFailure(Exception exception)
    {
        PostToUi(() => WatchFailed?.Invoke(this, exception));
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _disposed = true;
        }
    }
}
