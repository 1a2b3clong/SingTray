using SingTray.Shared.Enums;

namespace SingTray.Shared.Models;

public sealed class StatusInfo
{
    public bool ServiceAvailable { get; set; }
    public RunState RunState { get; set; }
    public bool SingBoxRunning { get; set; }
    public int? SingBoxPid { get; set; }
    public string? ExitStatus { get; set; }
    public CoreInfo Core { get; set; } = new();
    public ConfigInfo Config { get; set; } = new();
    public string? LastError { get; set; }
    public OperationErrorKind? LastErrorKind { get; set; }
    public PathInfo Paths { get; set; } = new();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public long StateRevision { get; set; }
}
