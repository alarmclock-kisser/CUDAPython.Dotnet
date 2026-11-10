using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using CUDAPython.Dotnet.Api.Contracts;
using Microsoft.AspNetCore.Hosting;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Runs development-only environment updates and NuGet package builds.</summary>
public sealed class MaintenanceService(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<MaintenanceService> logger) : IMaintenanceService
{
    /// <inheritdoc />
    public async Task<PythonEnvironmentUpdateResponse> UpdatePythonEnvironmentAsync(CancellationToken cancellationToken)
    {
        EnsureDevelopmentEnvironment();
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
        {
            throw new PlatformNotSupportedException("Python environment updates currently support Windows x64 only.");
        }

        var updaterPath = Path.Combine(AppContext.BaseDirectory, "tools", "Update-CudaPythonEnvironment.ps1");
        if (!File.Exists(updaterPath))
        {
            throw new InvalidOperationException("The Python environment updater is not included in this application output.");
        }

        var pendingEnvironmentPath = Path.Combine(AppContext.BaseDirectory, "python-env.pending");
        var result = await ExternalProcessRunner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", updaterPath,
                "-EnvironmentPath", pendingEnvironmentPath,
                "-Upgrade"
            ],
            AppContext.BaseDirectory,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            logger.LogError("Python environment update failed with exit code {ExitCode}: {ErrorOutput}",
                result.ExitCode, result.StandardError);
            throw new MaintenanceOperationException("The Python environment update failed. See server logs for details.");
        }

        var pendingRuntimePath = Path.Combine(pendingEnvironmentPath, "runtime.json");
        using var runtimeStream = File.OpenRead(pendingRuntimePath);
        var runtimeMetadata = JsonSerializer.Deserialize<RuntimeMetadata>(runtimeStream, JsonOptions)
            ?? throw new InvalidDataException("The updated Python environment did not produce valid runtime metadata.");

        return new PythonEnvironmentUpdateResponse(
            true,
            true,
            runtimeMetadata.PythonVersion ?? "unknown",
            runtimeMetadata.CudaPythonVersion ?? "unknown",
            "The updated environment is staged and will become active after the API restarts.");
    }

    /// <inheritdoc />
    public async Task<NuGetPackageBuildResponse> BuildNuGetPackageAsync(
        string configuration,
        CancellationToken cancellationToken)
    {
        EnsureDevelopmentEnvironment();
        if (!string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("NuGet packages must be built with the Release configuration.", nameof(configuration));
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            throw new InvalidOperationException("The repository checkout is not available to this API process.");
        }

        var projectPath = Path.Combine(repositoryRoot, "CUDAPython.Dotnet.Nuget", "CUDAPython.Dotnet.Nuget.csproj");
        if (!File.Exists(projectPath))
        {
            throw new InvalidOperationException("The NuGet pack project was not found in the repository checkout.");
        }

        var result = await ExternalProcessRunner.RunAsync(
            "dotnet",
            ["pack", projectPath, "-c", "Release"],
            repositoryRoot,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            logger.LogError("NuGet package build failed with exit code {ExitCode}: {ErrorOutput}",
                result.ExitCode, result.StandardError);
            throw new MaintenanceOperationException("The NuGet package build failed. See server logs for details.");
        }

        var project = XDocument.Load(projectPath);
        var properties = project.Root?.Elements("PropertyGroup").FirstOrDefault();
        var packageId = properties?.Element("PackageId")?.Value
            ?? throw new InvalidDataException("The NuGet pack project does not define a package ID.");
        var packageVersion = properties?.Element("Version")?.Value
            ?? throw new InvalidDataException("The NuGet pack project does not define a package version.");
        var packagePath = Path.Combine(repositoryRoot, "CUDAPython.Dotnet.Nuget", "bin", "Release", $"{packageId}.{packageVersion}.nupkg");
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("The package build completed without producing the expected nupkg.", packagePath);
        }

        await using var packageStream = File.OpenRead(packagePath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken));
        var packageInfo = new FileInfo(packagePath);
        return new NuGetPackageBuildResponse(
            packageId,
            packageVersion,
            packageInfo.Length,
            hash,
            new DateTimeOffset(packageInfo.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private void EnsureDevelopmentEnvironment()
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("This maintenance operation is available only in the Development environment.");
        }
    }

    private string? FindRepositoryRoot()
    {
        var configuredRoot = configuration["CudaPython:RepositoryRoot"] ?? Environment.GetEnvironmentVariable("CUDAPYTHON_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot) && File.Exists(Path.Combine(configuredRoot, "CUDAPython.Dotnet.slnx")))
        {
            return Path.GetFullPath(configuredRoot);
        }

        return FindRepositoryRoot(Environment.CurrentDirectory) ?? FindRepositoryRoot(AppContext.BaseDirectory);
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CUDAPython.Dotnet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record RuntimeMetadata(string? PythonVersion, string? CudaPythonVersion);
}

/// <summary>Reports a failure from an external maintenance operation.</summary>
public sealed class MaintenanceOperationException(string message) : Exception(message);
