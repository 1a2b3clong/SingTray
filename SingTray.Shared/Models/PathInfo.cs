namespace SingTray.Shared.Models;

public sealed class PathInfo
{
    public string DataDirectory { get; set; } = AppPaths.ProgramDataRoot;
    public string AppLogPath { get; set; } = AppPaths.AppLogPath;
    public string ImportsDirectory { get; set; } = AppPaths.ImportsDirectory;
}
