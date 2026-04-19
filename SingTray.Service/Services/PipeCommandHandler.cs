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
        await _logService.WriteInfoAsync($"IPC request: {request.Action}", cancellationToken);

        try
        {
            return request.Action switch
            {
                "ping" => PipeResponse.FromSuccess(new PingInfo()),
                "get_status" => PipeResponse.FromSuccess(await _singBoxManager.GetStatusAsync(cancellationToken)),
                "start" => ToResponse(await _singBoxManager.StartAsync(ReadStartRequest(request), cancellationToken)),
                "stop" => ToResponse(await _singBoxManager.StopAsync(cancellationToken)),
                "restart" => ToResponse(await _singBoxManager.RestartAsync(ReadStartRequest(request), cancellationToken)),
                "import_config" => ToResponse(await _importService.ImportConfigAsync(ReadImportRequest(request).ImportedFileName, cancellationToken)),
                "import_core" => ToResponse(await _importService.ImportCoreAsync(ReadImportRequest(request).ImportedFileName, cancellationToken)),
                "get_paths" => PipeResponse.FromSuccess(new PathInfo()),
                _ => PipeResponse.FromError($"Unknown action: {request.Action}")
            };
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"IPC action failed: {request.Action}", ex, cancellationToken);
            return PipeResponse.FromError(ex.Message);
        }
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

    private static StartRequest ReadStartRequest(PipeRequest request)
    {
        if (request.Payload is null)
        {
            return new StartRequest();
        }

        return JsonSerializer.Deserialize<StartRequest>(request.Payload.Value.GetRawText(), PipeContracts.JsonOptions)
            ?? new StartRequest();
    }

    private static PipeResponse ToResponse(OperationResult result) =>
        result.Success ? PipeResponse.FromSuccess(result) : PipeResponse.FromError(result.Message);
}
