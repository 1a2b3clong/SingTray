namespace SingTray.Shared;

public static class AppPaths
{
    public const string AppName = "SingTray";
    public const string PipeName = "SingTray.Pipe";
    public const string ServiceName = "SingTray.Service";
    public const string ProgramDataRoot = @"C:\ProgramData\SingTray";
    public const string InstallRoot = @"C:\Program Files\SingTray";

    public static string CoreDirectory => Path.Combine(ProgramDataRoot, "core");
    public static string ConfigDirectory => Path.Combine(ProgramDataRoot, "configs");
    public static string LogsDirectory => Path.Combine(ProgramDataRoot, "logs");
    public static string TempDirectory => Path.Combine(ProgramDataRoot, "tmp");
    public static string ImportsDirectory => Path.Combine(TempDirectory, "imports");
    public static string StateDirectory => Path.Combine(ProgramDataRoot, "state");

    public static string SingBoxExecutablePath => Path.Combine(CoreDirectory, "sing-box.exe");
    public static string ActiveConfigPath => Path.Combine(ConfigDirectory, "config.json");
    public static string AppLogPath => Path.Combine(LogsDirectory, "app.log");
    public static string SingBoxLogPath => Path.Combine(LogsDirectory, "singbox.log");
    public static string ServiceStatePath => Path.Combine(StateDirectory, "state.json");
    public static string LogSessionStatePath => Path.Combine(StateDirectory, "log-session.json");

    public static string ClientStateDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string ClientDesiredStatePath => Path.Combine(ClientStateDirectory, "desired-state.json");
    public static string ClientLogPath => Path.Combine(ClientStateDirectory, "client.log");

    public static IReadOnlyList<string> AllDataDirectories { get; } =
    [
        ProgramDataRoot,
        CoreDirectory,
        ConfigDirectory,
        LogsDirectory,
        TempDirectory,
        ImportsDirectory,
        StateDirectory
    ];

    public static void EnsureDataDirectories()
    {
        foreach (var directory in AllDataDirectories)
        {
            Directory.CreateDirectory(directory);
        }
    }
}
