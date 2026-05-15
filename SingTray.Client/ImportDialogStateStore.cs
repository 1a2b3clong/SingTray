using System.Text.Json;
using SingTray.Shared;

namespace SingTray.Client;

public sealed class ImportDialogStateStore : IDisposable
{
    private static string StatePath => Path.Combine(AppPaths.ClientStateDirectory, "import-dialog-state.json");

    public async Task<ImportDialogState> ReadAsync()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ClientStateDirectory);
            if (!File.Exists(StatePath))
            {
                return new ImportDialogState();
            }

            await using var stream = File.OpenRead(StatePath);
            return await JsonSerializer.DeserializeAsync<ImportDialogState>(stream, PipeContracts.JsonOptions)
                ?? new ImportDialogState();
        }
        catch
        {
            return new ImportDialogState();
        }
    }

    public async Task WriteConfigDirectoryAsync(string? directory)
    {
        var state = await ReadAsync();
        state.ImportConfigDirectory = NormalizeDirectory(directory);
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAsync(state);
    }

    public async Task WriteCoreDirectoryAsync(string? directory)
    {
        var state = await ReadAsync();
        state.ImportCoreDirectory = NormalizeDirectory(directory);
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await WriteAsync(state);
    }

    public void Dispose()
    {
    }

    private static async Task WriteAsync(ImportDialogState state)
    {
        Directory.CreateDirectory(AppPaths.ClientStateDirectory);
        await using var stream = File.Create(StatePath);
        await JsonSerializer.SerializeAsync(stream, state, PipeContracts.JsonOptions);
    }

    private static string? NormalizeDirectory(string? directory)
    {
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.GetFullPath(directory);
    }
}

public sealed class ImportDialogState
{
    public string? ImportConfigDirectory { get; set; }
    public string? ImportCoreDirectory { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
