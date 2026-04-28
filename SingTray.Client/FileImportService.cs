using SingTray.Shared;

namespace SingTray.Client;

public sealed class FileImportService : IDisposable
{
    public FileImportService()
    {
        Directory.CreateDirectory(AppPaths.ImportsDirectory);
    }

    public async Task<string?> PrepareImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.ImportsDirectory);
        CleanupPreparedImports();

        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(AppPaths.ImportsDirectory, fileName);

        await using var source = File.OpenRead(sourcePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
        return fileName;
    }

    public void DeletePreparedImport(string? importedFileName)
    {
        if (string.IsNullOrWhiteSpace(importedFileName))
        {
            return;
        }

        try
        {
            var safeName = Path.GetFileName(importedFileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return;
            }

            var path = Path.Combine(AppPaths.ImportsDirectory, safeName);
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup; the service may already have removed it.
        }
    }

    public void CleanupPreparedImports()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ImportsDirectory);

            foreach (var filePath in Directory.GetFiles(AppPaths.ImportsDirectory))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            }

            foreach (var directoryPath in Directory.GetDirectories(AppPaths.ImportsDirectory))
            {
                foreach (var filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup; the service also cleans this directory after import requests.
        }
    }

    public void Dispose()
    {
    }
}
