using System.IO.Compression;
using SingTray.Shared;
using SingTray.Shared.Enums;
using SingTray.Shared.Models;

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
        var importedConfigName = Path.GetFileName(importedFileName);
        var sourcePath = GetControlledImportPath(importedConfigName);
        var validationCopyPath = Path.Combine(AppPaths.TempDirectory, $"config-validation-{Guid.NewGuid():N}.json");
        var destinationPath = AppPaths.GetConfigPath(importedConfigName);
        var backupPath = Path.Combine(AppPaths.TempDirectory, $"config-backup-{Guid.NewGuid():N}.json");
        var movedNewConfig = false;
        try
        {
            var status = await _singBoxManager.GetStatusAsync(cancellationToken);
            if (status.SingBoxRunning)
            {
                return OperationResult.Fail("Please stop sing-box first.");
            }

            if (!File.Exists(sourcePath))
            {
                return OperationResult.Fail("Imported config file not found.");
            }

            File.Copy(sourcePath, validationCopyPath, overwrite: true);
            var tempValidation = await ValidateConfigAsync(validationCopyPath, cancellationToken);
            if (!tempValidation.Success)
            {
                await _logService.WriteWarningAsync($"Config validation failed: {tempValidation.Message}", cancellationToken);
                await RefreshConfigStateAsync(cancellationToken);
                return tempValidation;
            }

            Directory.CreateDirectory(AppPaths.ConfigDirectory);
            var previousConfigPath = await _singBoxManager.GetActiveConfigPathAsync(cancellationToken);
            var replacingSameFile = PathsEqual(previousConfigPath, destinationPath);

            if (File.Exists(destinationPath))
            {
                File.Move(destinationPath, backupPath, overwrite: true);
            }

            try
            {
                File.Move(validationCopyPath, destinationPath, overwrite: true);
                movedNewConfig = true;
                await RefreshConfigStateAsync(destinationPath, cancellationToken);

                if (!replacingSameFile)
                {
                    TryDeleteFile(previousConfigPath);
                }

                TryDeleteFile(backupPath);
            }
            catch
            {
                if (movedNewConfig)
                {
                    TryDeleteFile(destinationPath);
                }

                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, destinationPath, overwrite: true);
                }

                throw;
            }

            await _logService.WriteInfoAsync("Config replaced successfully.", cancellationToken);
            await RefreshConfigStateAsync(cancellationToken);
            return OperationResult.Ok("Config imported successfully.");
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"Import config failed for {importedFileName}.", ex, cancellationToken);
            await RefreshConfigStateAsync(cancellationToken);
            return OperationResult.Fail($"Failed to import config: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(validationCopyPath);
            await CleanupImportsDirectoryAsync(cancellationToken);
        }
    }

    public async Task<OperationResult> ImportCoreAsync(string importedFileName, CancellationToken cancellationToken)
    {
        var sourceZipPath = GetControlledImportPath(importedFileName);
        var extractRoot = Path.Combine(AppPaths.TempDirectory, $"core-import-{Guid.NewGuid():N}");
        var stagedCoreRoot = Path.Combine(AppPaths.TempDirectory, $"core-staged-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(AppPaths.TempDirectory, $"core-backup-{Guid.NewGuid():N}");

        try
        {
            var status = await _singBoxManager.GetStatusAsync(cancellationToken);
            if (status.SingBoxRunning)
            {
                return OperationResult.Fail("Please stop sing-box first.");
            }

            if (!File.Exists(sourceZipPath))
            {
                return OperationResult.Fail("Imported core archive not found.");
            }

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
                await _logService.WriteWarningAsync($"Core validation failed: {validation.Message}", cancellationToken);
                await RefreshCoreStateAsync(cancellationToken);
                return OperationResult.Fail($"Core invalid: {validation.Message}", OperationErrorKind.CoreInvalid);
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

            await _logService.WriteInfoAsync("Core replaced successfully.", cancellationToken);
            await RefreshCoreStateAsync(cancellationToken);
            return OperationResult.Ok($"Core imported successfully: {validation.Message}");
        }
        catch (Exception ex)
        {
            await _logService.WriteErrorAsync($"Import core failed for {importedFileName}.", ex, cancellationToken);
            await RefreshCoreStateAsync(cancellationToken);
            return OperationResult.Fail($"Failed to import core: {ex.Message}");
        }
        finally
        {
            await CleanupImportsDirectoryAsync(cancellationToken);
            TryDeleteDirectory(extractRoot);
            TryDeleteDirectory(stagedCoreRoot);
        }
    }

    private async Task RefreshConfigStateAsync(CancellationToken cancellationToken)
    {
        var config = await _singBoxManager.GetConfigInfoAsync(cancellationToken);
        await UpdateConfigStateAsync(config, cancellationToken);
    }

    private async Task RefreshConfigStateAsync(string configPath, CancellationToken cancellationToken)
    {
        var config = await _singBoxManager.ValidateConfigFileAsync(configPath, cancellationToken);
        await UpdateConfigStateAsync(config, cancellationToken);
    }

    private async Task UpdateConfigStateAsync(ConfigInfo config, CancellationToken cancellationToken)
    {
        await _serviceState.UpdateAsync(record =>
        {
            record.ConfigInstalled = config.Installed;
            record.ConfigValid = config.Valid;
            record.ConfigName = config.FileName;
            record.ConfigValidationMessage = config.ValidationMessage;
        }, cancellationToken);
    }

    private async Task RefreshCoreStateAsync(CancellationToken cancellationToken)
    {
        var core = await _singBoxManager.GetCoreInfoAsync(cancellationToken);
        await _serviceState.UpdateAsync(record =>
        {
            record.CoreInstalled = core.Installed;
            record.CoreValid = core.Valid;
            record.CoreVersion = core.Version;
            record.CoreValidationMessage = core.ValidationMessage;
        }, cancellationToken);
    }

    private async Task<OperationResult> ValidateConfigAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return OperationResult.Fail("Config file does not exist.", OperationErrorKind.ConfigInvalid);
        }

        try
        {
            await using var stream = File.OpenRead(configPath);
            await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Config invalid: {ex.Message}", OperationErrorKind.ConfigInvalid);
        }

        var configInfo = await _singBoxManager.ValidateConfigFileAsync(configPath, cancellationToken);
        return configInfo.Valid
            ? OperationResult.Ok("Config validation passed.")
            : OperationResult.Fail(configInfo.ValidationMessage ?? "Config invalid.", OperationErrorKind.ConfigInvalid);
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

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
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

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            // Best effort cleanup.
            return false;
        }
    }

    private async Task CleanupImportsDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ImportsDirectory);
            var deletedFiles = 0;
            var failedFiles = 0;
            var deletedDirectories = 0;
            var failedDirectories = 0;

            foreach (var filePath in Directory.GetFiles(AppPaths.ImportsDirectory))
            {
                if (!TryDeleteFile(filePath))
                {
                    failedFiles++;
                    await _logService.WriteWarningAsync($"Failed to delete import temp file: {filePath}", cancellationToken);
                }
                else
                {
                    deletedFiles++;
                }
            }

            foreach (var directoryPath in Directory.GetDirectories(AppPaths.ImportsDirectory))
            {
                if (!TryDeleteDirectory(directoryPath))
                {
                    failedDirectories++;
                    await _logService.WriteWarningAsync($"Failed to delete import temp directory: {directoryPath}", cancellationToken);
                }
                else
                {
                    deletedDirectories++;
                }
            }

            if (deletedFiles > 0 || deletedDirectories > 0 || failedFiles > 0 || failedDirectories > 0)
            {
                await _logService.WriteInfoAsync(
                    $"Imports cleanup completed. DeletedFiles={deletedFiles}, DeletedDirectories={deletedDirectories}, FailedFiles={failedFiles}, FailedDirectories={failedDirectories}.",
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await _logService.WriteWarningAsync($"Failed to clean imports directory: {ex.Message}", cancellationToken);
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch
        {
            // Best effort cleanup.
            return false;
        }
    }
}
