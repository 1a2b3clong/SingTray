using System.Text.Json;
using System.Text.Json.Serialization;
using SingTray.Shared.Models;

namespace SingTray.Shared;

public static class PipeContracts
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public sealed class PipeRequest
{
    public string Action { get; set; } = string.Empty;
    public JsonElement? Payload { get; set; }
}

public sealed class PipeResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonElement? Data { get; set; }

    public static PipeResponse FromSuccess<T>(T data)
    {
        return new PipeResponse
        {
            Success = true,
            Data = JsonSerializer.SerializeToElement(data, PipeContracts.JsonOptions)
        };
    }

    public static PipeResponse Ok()
    {
        return new PipeResponse { Success = true };
    }

    public static PipeResponse FromError(string message)
    {
        return new PipeResponse { Success = false, ErrorMessage = message };
    }
}

public sealed class ImportRequest
{
    public string ImportedFileName { get; set; } = string.Empty;
}

public sealed class StatusChangeRequest
{
    public long? LastSeenRevision { get; set; }
}

public sealed class StartRequest
{
    public string? LastError { get; set; }
}

public sealed class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public static OperationResult Ok(string message) => new() { Success = true, Message = message };
    public static OperationResult Fail(string message) => new() { Success = false, Message = message };
}

public sealed class PingInfo
{
    public string ServiceName { get; set; } = AppPaths.ServiceName;
    public string PipeName { get; set; } = AppPaths.PipeName;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
