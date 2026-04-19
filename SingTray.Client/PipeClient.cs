using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using SingTray.Shared;
using SingTray.Shared.Constants;
using SingTray.Shared.Models;

namespace SingTray.Client;

public sealed class PipeClient : IDisposable
{
    public Task<PingInfo> PingAsync(CancellationToken cancellationToken) =>
        SendForDataAsync<PingInfo>(new PipeRequest { Action = "ping" }, SingBoxConstants.PipeTimeoutMilliseconds, cancellationToken);

    public Task<StatusInfo> GetStatusAsync(CancellationToken cancellationToken) =>
        SendForDataAsync<StatusInfo>(new PipeRequest { Action = "get_status" }, SingBoxConstants.PipeTimeoutMilliseconds, cancellationToken);

    public Task<OperationResult> StartAsync(StartRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<OperationResult>(
            new PipeRequest { Action = "start", Payload = JsonSerializer.SerializeToElement(request, PipeContracts.JsonOptions) },
            SingBoxConstants.PipeTimeoutMilliseconds,
            cancellationToken);

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken) =>
        SendForDataAsync<OperationResult>(new PipeRequest { Action = "stop" }, SingBoxConstants.PipeTimeoutMilliseconds, cancellationToken);

    public Task<OperationResult> RestartAsync(StartRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<OperationResult>(
            new PipeRequest { Action = "restart", Payload = JsonSerializer.SerializeToElement(request, PipeContracts.JsonOptions) },
            SingBoxConstants.PipeTimeoutMilliseconds,
            cancellationToken);

    public Task<OperationResult> ImportConfigAsync(string importedFileName, CancellationToken cancellationToken) =>
        SendForDataAsync<OperationResult>(
            new PipeRequest { Action = "import_config", Payload = JsonSerializer.SerializeToElement(new ImportRequest { ImportedFileName = importedFileName }, PipeContracts.JsonOptions) },
            SingBoxConstants.PipeImportTimeoutMilliseconds,
            cancellationToken);

    public Task<OperationResult> ImportCoreAsync(string importedFileName, CancellationToken cancellationToken) =>
        SendForDataAsync<OperationResult>(
            new PipeRequest { Action = "import_core", Payload = JsonSerializer.SerializeToElement(new ImportRequest { ImportedFileName = importedFileName }, PipeContracts.JsonOptions) },
            SingBoxConstants.PipeImportTimeoutMilliseconds,
            cancellationToken);

    private async Task<T> SendForDataAsync<T>(PipeRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, timeoutMilliseconds, cancellationToken);
        if (!response.Success)
        {
            throw new PipeClientException(PipeFailureKind.ServiceError, response.ErrorMessage ?? "Pipe request failed.");
        }

        if (response.Data is null)
        {
            throw new PipeClientException(PipeFailureKind.InvalidResponse, "Response payload is missing.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(response.Data.Value.GetRawText(), PipeContracts.JsonOptions)
                ?? throw new PipeClientException(PipeFailureKind.InvalidResponse, "Response payload is invalid.");
        }
        catch (JsonException ex)
        {
            throw new PipeClientException(PipeFailureKind.InvalidResponse, "Response payload is invalid.", ex);
        }
    }

    private static async Task<PipeResponse> SendAsync(PipeRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        await using var client = new NamedPipeClientStream(".", AppPaths.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await client.ConnectAsync(timeoutCts.Token);
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, PipeContracts.JsonOptions));
            var responseJson = await reader.ReadLineAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new InvalidOperationException("Service returned an empty response.");
            }

            return JsonSerializer.Deserialize<PipeResponse>(responseJson, PipeContracts.JsonOptions)
                ?? throw new PipeClientException(PipeFailureKind.InvalidResponse, "Service returned an invalid response.");
        }
        catch (JsonException ex)
        {
            throw new PipeClientException(PipeFailureKind.InvalidResponse, "Service returned malformed JSON.", ex);
        }
        catch (OperationCanceledException)
        {
            throw new PipeClientException(PipeFailureKind.Timeout, "Timed out while communicating with SingTray.Service.");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PipeClientException(PipeFailureKind.AccessDenied, "Access denied while connecting to SingTray.Service.", ex);
        }
        catch (IOException ex) when (IsPipeMissing(ex))
        {
            var serviceState = GetServiceStateDescription();
            throw new PipeClientException(
                serviceState == "NotInstalled" || serviceState == "Stopped"
                    ? PipeFailureKind.ServiceNotRunning
                    : PipeFailureKind.PipeNotFound,
                serviceState == "NotInstalled" || serviceState == "Stopped"
                    ? $"SingTray.Service is not running ({serviceState})."
                    : $"Named pipe '{AppPaths.PipeName}' is not available.",
                ex);
        }
        catch (IOException ex)
        {
            throw new PipeClientException(PipeFailureKind.PipeNotFound, $"Named pipe '{AppPaths.PipeName}' connection failed.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new PipeClientException(PipeFailureKind.InvalidResponse, ex.Message, ex);
        }
    }

    private static bool IsPipeMissing(IOException ex) =>
        ex.HResult == unchecked((int)0x80070002) || ex.HResult == unchecked((int)0x800700E7);

    private static string GetServiceStateDescription()
    {
        try
        {
            using var controller = new ServiceController(AppPaths.ServiceName);
            return controller.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "NotInstalled";
        }
        catch
        {
            return "Unknown";
        }
    }

    public void Dispose()
    {
    }
}
