using System.Text.Json;
using SingTray.Shared;
using SingTray.Shared.Models;

namespace SingTray.Service.Services;

public sealed class PipeCommandHandler
{
    private readonly SingBoxManager _singBoxManager;
    private readonly ImportService _importService;
    private readonly LogService _logService;

    public PipeCommandHandler(SingBoxManager singBoxManager, ImportService importService, LogService logService)
    {
        _singBoxManager = singBoxManager;
        _importService = importService;
        _logService = logService;
    }

    public async Task<PipeResponse> HandleAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Action switch
            {
                "ping" => PipeResponse.FromSuccess(new PingInfo()),
                "get_status" => PipeResponse.FromSuccess(await _singBoxManager.GetStatusAsync(cancellationToken)),
                "wait_status_change" => PipeResponse.FromSuccess(await HandleWaitStatusChangeAsync(request, cancellationToken)),
                "start" => ToResponse(await HandleStartAsync(cancellationToken)),
                "stop" => ToResponse(await HandleStopAsync(cancellationToken)),
                "restart" => ToResponse(await HandleRestartAsync(cancellationToken)),
                "import_config" => ToResponse(await HandleImportConfigAsync(request, cancellationToken)),
                "import_core" => ToResponse(await HandleImportCoreAsync(request, cancellationToken)),
                "get_paths" => PipeResponse.FromSuccess(new PathInfo()),
                _ => await HandleUnknownActionAsync(request.Action, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"IPC action failed: {request.Action}", ex, cancellationToken);
            return PipeResponse.FromError(ex.Message);
        }
    }

    private async Task<OperationResult> HandleStartAsync(CancellationToken cancellationToken)
    {
        await _logService.WriteInfoAsync("Start requested.", cancellationToken);
        return await _singBoxManager.StartAsync(cancellationToken);
    }

    private async Task<StatusInfo> HandleWaitStatusChangeAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        return await _singBoxManager.WaitForStatusChangeAsync(ReadStatusChangeRequest(request).LastSeenRevision, cancellationToken);
    }

    private async Task<OperationResult> HandleStopAsync(CancellationToken cancellationToken)
    {
        await _logService.WriteInfoAsync("Stop requested.", cancellationToken);
        return await _singBoxManager.StopAsync(cancellationToken);
    }

    private async Task<OperationResult> HandleRestartAsync(CancellationToken cancellationToken)
    {
        await _logService.WriteInfoAsync("Restart requested.", cancellationToken);
        return await _singBoxManager.RestartAsync(cancellationToken);
    }

    private async Task<OperationResult> HandleImportConfigAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        await _logService.WriteInfoAsync("Import config requested.", cancellationToken);
        return await _importService.ImportConfigAsync(ReadImportRequest(request).ImportedFileName, cancellationToken);
    }

    private async Task<OperationResult> HandleImportCoreAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        await _logService.WriteInfoAsync("Import core requested.", cancellationToken);
        return await _importService.ImportCoreAsync(ReadImportRequest(request).ImportedFileName, cancellationToken);
    }

    private async Task<PipeResponse> HandleUnknownActionAsync(string action, CancellationToken cancellationToken)
    {
        await _logService.WriteWarningAsync($"Unknown IPC action received: {action}", cancellationToken);
        return PipeResponse.FromError($"Unknown action: {action}");
    }

    private static ImportRequest ReadImportRequest(PipeRequest request)
    {
        if (request.Payload is null)
        {
            throw new InvalidOperationException("Import payload is missing.");
        }

        return JsonSerializer.Deserialize<ImportRequest>(request.Payload.Value.GetRawText(), PipeContracts.JsonOptions)
            ?? throw new InvalidOperationException("Import payload is invalid.");
    }

    private static StatusChangeRequest ReadStatusChangeRequest(PipeRequest request)
    {
        if (request.Payload is null)
        {
            return new StatusChangeRequest();
        }

        return JsonSerializer.Deserialize<StatusChangeRequest>(request.Payload.Value.GetRawText(), PipeContracts.JsonOptions)
            ?? new StatusChangeRequest();
    }

    private static PipeResponse ToResponse(OperationResult result) =>
        PipeResponse.FromSuccess(result);
}
