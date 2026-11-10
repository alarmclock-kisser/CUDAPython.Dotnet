using Microsoft.Extensions.Logging;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Applies a staged Python environment update before the API initializes CPython.</summary>
public sealed class PythonEnvironmentPromotion(ILogger<PythonEnvironmentPromotion> logger)
{
    /// <summary>Promotes a valid pending environment into the active application directory.</summary>
    public void PromotePendingUpdate()
    {
        var applicationDirectory = AppContext.BaseDirectory;
        var activePath = Path.Combine(applicationDirectory, "python-env");
        var pendingPath = Path.Combine(applicationDirectory, "python-env.pending");
        if (!Directory.Exists(pendingPath))
        {
            return;
        }

        if (!File.Exists(Path.Combine(pendingPath, "runtime.json")) ||
            !File.Exists(Path.Combine(pendingPath, "python", "python311.dll")))
        {
            throw new InvalidDataException("The pending Python environment is incomplete and cannot be activated.");
        }

        var backupPath = Path.Combine(applicationDirectory, $"python-env.previous-{Guid.NewGuid():N}");
        var hasBackup = false;
        if (Directory.Exists(activePath))
        {
            Directory.Move(activePath, backupPath);
            hasBackup = true;
        }

        try
        {
            Directory.Move(pendingPath, activePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (hasBackup)
            {
                Directory.Move(backupPath, activePath);
            }

            logger.LogError(exception, "Failed to activate the pending Python environment.");
            throw;
        }

        logger.LogInformation("Activated the pending Python environment at application startup.");
        if (hasBackup)
        {
            try
            {
                Directory.Delete(backupPath, recursive: true);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "The previous Python environment could not be removed from {BackupPath}.", backupPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning(exception, "The previous Python environment could not be removed from {BackupPath}.", backupPath);
            }
        }
    }
}
