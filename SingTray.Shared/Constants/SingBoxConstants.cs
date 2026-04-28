namespace SingTray.Shared.Constants;

public static class SingBoxConstants
{
    public static string[] BuildRunArguments(string configPath) => ["run", "-c", configPath];

    public static readonly string[] CheckArguments = ["check", "-c"];
    public static readonly string[] VersionArguments = ["version"];
    public const int PipeTimeoutMilliseconds = 8000;
    public const int PipeImportTimeoutMilliseconds = 45000;
    public const int StatusWaitTimeoutMilliseconds = 60000;
    public const int PipeStatusWaitTimeoutMilliseconds = StatusWaitTimeoutMilliseconds + 5000;
}
