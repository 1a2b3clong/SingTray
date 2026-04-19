using System.Text.Json;
using SingTray.Shared;
using SingTray.Shared.Enums;
using SingTray.Shared.Models;

namespace SingTray.Service;

public sealed class ServiceState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ServiceStateRecord _record = new();

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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            updater(_record);
            _record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatusInfo> CreateStatusSnapshotAsync(CancellationToken cancellationToken)
    {
        var record = await GetRecordAsync(cancellationToken);
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
            Timestamp = DateTimeOffset.UtcNow
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
            UpdatedAt = source.UpdatedAt
        };
    }
}
