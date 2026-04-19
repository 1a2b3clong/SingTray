using System.IO.Compression;
using SingTray.Shared;

namespace SingTray.Service.Services;

public sealed class ImportService
{
    private readonly SingBoxManager _singBoxManager;
    private readonly ServiceState _serviceState;
    private readonly LogService _logService;

    public ImportService(SingBoxManager singBoxManager, ServiceState serviceState, LogService logService)
    {
        _singBoxManager = singBoxManager;
        _serviceState = serviceState;
        _logService = logService;
    }

    public async Task<OperationResult> ImportConfigAsync(string importedFileName, CancellationToken cancellationToken)
    {
        var status = await _singBoxManager.GetStatusAsync(cancellationToken);
        if (status.SingBoxRunning)
        {
            return OperationResult.Fail("Please stop sing-box first.");
        }

        var sourcePath = GetControlledImportPath(importedFileName);
        if (!File.Exists(sourcePath))
        {
            return OperationResult.Fail("Imported config file not found.");
        }

        var validationCopyPath = Path.Combine(AppPaths.TempDirectory, $"config-validation-{Guid.NewGuid():N}.json");
        try
        {
            File.Copy(sourcePath, validationCopyPath, overwrite: true);
            var tempValidation = await ValidateConfigAsync(validationCopyPath, cancellationToken);
            if (!tempValidation.Success)
            {
                await _logService.WriteWarningAsync($"Config import rejected: {tempValidation.Message}", cancellationToken);
                return tempValidation;
            }

            Directory.CreateDirectory(AppPaths.ConfigDirectory);
            var backupPath = Path.Combine(AppPaths.TempDirectory, $"config-backup-{Guid.NewGuid():N}.json");
            if (File.Exists(AppPaths.ActiveConfigPath))
            {
                File.Move(AppPaths.ActiveConfigPath, backupPath, overwrite: true);
            }

            try
            {
                File.Move(validationCopyPath, AppPaths.ActiveConfigPath, overwrite: true);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch
            {
                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, AppPaths.ActiveConfigPath, overwrite: true);
                }

                throw;
            }

            await _logService.WriteInfoAsync($"Config imported from {importedFileName}.", cancellationToken);
            return OperationResult.Ok("Config imported successfully.");
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"Import config failed for {importedFileName}.", ex, cancellationToken);
            return OperationResult.Fail($"Failed to import config: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(validationCopyPath);
        }
    }

    public async Task<OperationResult> ImportCoreAsync(string importedFileName, CancellationToken cancellationToken)
    {
        var status = await _singBoxManager.GetStatusAsync(cancellationToken);
        if (status.SingBoxRunning)
        {
            return OperationResult.Fail("Please stop sing-box first.");
        }

        var sourceZipPath = GetControlledImportPath(importedFileName);
        if (!File.Exists(sourceZipPath))
        {
            return OperationResult.Fail("Imported core archive not found.");
        }

        var extractRoot = Path.Combine(AppPaths.TempDirectory, $"core-import-{Guid.NewGuid():N}");
        var stagedCoreRoot = Path.Combine(AppPaths.TempDirectory, $"core-staged-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(AppPaths.TempDirectory, $"core-backup-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(sourceZipPath, extractRoot);

            var extractedExe = Directory.GetFiles(extractRoot, "sing-box.exe", SearchOption.AllDirectories).SingleOrDefault();
            if (extractedExe is null)
            {
                return OperationResult.Fail("The selected zip does not contain sing-box.exe.");
            }

            var extractedRoot = Path.GetDirectoryName(extractedExe)!;
            CopyDirectory(extractedRoot, stagedCoreRoot);

            var validation = await _singBoxManager.ValidateCoreExecutableAsync(Path.Combine(stagedCoreRoot, "sing-box.exe"), cancellationToken);
            if (!validation.Success)
            {
                return OperationResult.Fail($"Core invalid: {validation.Message}");
            }

            try
            {
                if (Directory.Exists(AppPaths.CoreDirectory))
                {
                    Directory.Move(AppPaths.CoreDirectory, backupRoot);
                }

                Directory.Move(stagedCoreRoot, AppPaths.CoreDirectory);
                TryDeleteDirectory(backupRoot);
            }
            catch
            {
                if (Directory.Exists(backupRoot) && !Directory.Exists(AppPaths.CoreDirectory))
                {
                    Directory.Move(backupRoot, AppPaths.CoreDirectory);
                }

                throw;
            }

            await _logService.WriteInfoAsync($"Core imported from {importedFileName}.", cancellationToken);
            return OperationResult.Ok($"Core imported successfully: {validation.Message}");
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"Import core failed for {importedFileName}.", ex, cancellationToken);
            return OperationResult.Fail($"Failed to import core: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(extractRoot);
            TryDeleteDirectory(stagedCoreRoot);
        }
    }

    private async Task<OperationResult> ValidateConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return OperationResult.Fail("Config file does not exist.");
        }

        try
        {
            await using var stream = File.OpenRead(configPath);
            await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Config invalid: {ex.Message}");
        }

        var configInfo = await _singBoxManager.ValidateConfigFileAsync(configPath, cancellationToken);
        return configInfo.Valid
            ? OperationResult.Ok("Config validation passed.")
            : OperationResult.Fail(configInfo.ValidationMessage ?? "Config invalid.");
    }

    private static string GetControlledImportPath(string importedFileName)
    {
        if (string.IsNullOrWhiteSpace(importedFileName))
        {
            throw new InvalidOperationException("Import file name is required.");
        }

        var safeName = Path.GetFileName(importedFileName);
        return Path.Combine(AppPaths.ImportsDirectory, safeName);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.GetFiles(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (var directoryPath in Directory.GetDirectories(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, destinationPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
