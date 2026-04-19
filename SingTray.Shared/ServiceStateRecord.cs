using SingTray.Shared.Enums;

namespace SingTray.Shared;

public sealed class ServiceStateRecord
{
    public RunState RunState { get; set; } = RunState.Stopped;
    public int? SingBoxPid { get; set; }
    public string? LastError { get; set; }
    public string? ExitStatus { get; set; }
    public bool CoreInstalled { get; set; }
    public bool CoreValid { get; set; }
    public string? CoreVersion { get; set; }
    public string? CoreValidationMessage { get; set; }
    public bool ConfigInstalled { get; set; }
    public bool ConfigValid { get; set; }
    public string? ConfigName { get; set; }
    public string? ConfigValidationMessage { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
