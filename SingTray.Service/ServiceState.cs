using System.Text.Json;
using SingTray.Service.Services;
using SingTray.Shared;
using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Service;

public sealed class ServiceState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LogService _logService;
    private TaskCompletionSource _changeSignal = CreateChangeSignal();
    private ServiceStateRecord _record = new();

    public ServiceState(LogService logService)
    {
        _logService = logService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureDataDirectories();

        if (!File.Exists(AppPaths.ServiceStatePath))
        {
            await PersistAsync(cancellationToken);
            return;
        }

        await using var stream = File.OpenRead(AppPaths.ServiceStatePath);
        var loaded = await JsonSerializer.DeserializeAsync<ServiceStateRecord>(stream, PipeContracts.JsonOptions, cancellationToken);
        if (loaded is not null)
        {
            _record = loaded;
        }
    }

    public async Task<ServiceStateRecord> GetRecordAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return Clone(_record);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Action<ServiceStateRecord> updater, CancellationToken cancellationToken)
    {
        RunState previousState;
        RunState currentState;
        TaskCompletionSource? completedSignal = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previousRecord = Clone(_record);
            previousState = _record.RunState;
            updater(_record);
            currentState = _record.RunState;
            if (HasMeaningfulChange(previousRecord, _record))
            {
                _record.UpdatedAt = DateTimeOffset.UtcNow;
                _record.StateRevision++;
                await PersistCoreAsync(cancellationToken);
                completedSignal = _changeSignal;
                _changeSignal = CreateChangeSignal();
            }
        }
        finally
        {
            _gate.Release();
        }

        completedSignal?.TrySetResult();

        if (previousState != currentState)
        {
            await _logService.WriteInfoAsync($"Runtime state changed: {previousState} -> {currentState}.", cancellationToken);
        }
    }

    public async Task<StatusInfo> WaitForStatusChangeAsync(long lastSeenRevision, TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            ServiceStateRecord currentRecord;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                currentRecord = Clone(_record);
                if (currentRecord.StateRevision != lastSeenRevision)
                {
                    return CreateStatusSnapshot(currentRecord);
                }

                waitTask = _changeSignal.Task;
            }
            finally
            {
                _gate.Release();
            }

            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                return await CreateStatusSnapshotAsync(cancellationToken);
            }
        }
    }

    public async Task<StatusInfo> CreateStatusSnapshotAsync(CancellationToken cancellationToken)
    {
        var record = await GetRecordAsync(cancellationToken);
        return CreateStatusSnapshot(record);
    }

    private static StatusInfo CreateStatusSnapshot(ServiceStateRecord record)
    {
        return new StatusInfo
        {
            ServiceAvailable = true,
            RunState = record.RunState,
            SingBoxRunning = record.RunState is RunState.Running or RunState.Starting,
            SingBoxPid = record.SingBoxPid,
            LastError = record.LastError,
            ExitStatus = record.ExitStatus,
            Core = new CoreInfo
            {
                Installed = record.CoreInstalled,
                Valid = record.CoreValid,
                Version = record.CoreVersion,
                ValidationMessage = record.CoreValidationMessage
            },
            Config = new ConfigInfo
            {
                Installed = record.ConfigInstalled,
                Valid = record.ConfigValid,
                FileName = record.ConfigName,
                ValidationMessage = record.ConfigValidationMessage
            },
            Paths = new PathInfo(),
            Timestamp = record.UpdatedAt,
            StateRevision = record.StateRevision
        };
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await PersistCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.StateDirectory);
        await using var stream = File.Create(AppPaths.ServiceStatePath);
        await JsonSerializer.SerializeAsync(stream, _record, PipeContracts.JsonOptions, cancellationToken);
    }

    private static ServiceStateRecord Clone(ServiceStateRecord source)
    {
        return new ServiceStateRecord
        {
            RunState = source.RunState,
            SingBoxPid = source.SingBoxPid,
            LastError = source.LastError,
            ExitStatus = source.ExitStatus,
            CoreInstalled = source.CoreInstalled,
            CoreValid = source.CoreValid,
            CoreVersion = source.CoreVersion,
            CoreValidationMessage = source.CoreValidationMessage,
            ConfigInstalled = source.ConfigInstalled,
            ConfigValid = source.ConfigValid,
            ConfigName = source.ConfigName,
            ConfigValidationMessage = source.ConfigValidationMessage,
            UpdatedAt = source.UpdatedAt,
            StateRevision = source.StateRevision
        };
    }

    private static bool HasMeaningfulChange(ServiceStateRecord previous, ServiceStateRecord current)
    {
        return previous.RunState != current.RunState
            || previous.SingBoxPid != current.SingBoxPid
            || previous.LastError != current.LastError
            || previous.ExitStatus != current.ExitStatus
            || previous.CoreInstalled != current.CoreInstalled
            || previous.CoreValid != current.CoreValid
            || previous.CoreVersion != current.CoreVersion
            || previous.CoreValidationMessage != current.CoreValidationMessage
            || previous.ConfigInstalled != current.ConfigInstalled
            || previous.ConfigValid != current.ConfigValid
            || previous.ConfigName != current.ConfigName
            || previous.ConfigValidationMessage != current.ConfigValidationMessage;
    }

    private static TaskCompletionSource CreateChangeSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
