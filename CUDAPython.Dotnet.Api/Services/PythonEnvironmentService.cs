using System.Text.Json;
using CUDAPython.Dotnet.Api.Contracts;
using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Reads and validates the Python runtime bundled beside the API assemblies.</summary>
public sealed class PythonEnvironmentService : IPythonEnvironmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _environmentDirectory = Path.Combine(AppContext.BaseDirectory, "python-env");
    private readonly string _runtimeConfigurationPath;

    /// <summary>Initializes the environment service from the application output directory.</summary>
    public PythonEnvironmentService()
    {
        _runtimeConfigurationPath = Path.Combine(_environmentDirectory, "runtime.json");
    }

    /// <inheritdoc />
    public PythonEnvironmentInfoResponse GetInfo()
    {
        if (!File.Exists(_runtimeConfigurationPath))
        {
            return new PythonEnvironmentInfoResponse(false, null, null, null, 0, IsUpdateToolAvailable(),
                Directory.Exists(Path.Combine(AppContext.BaseDirectory, "python-env.pending")));
        }

        var metadata = ReadRuntimeMetadata();
        var lockPath = Path.Combine(_environmentDirectory, "requirements.lock.txt");
        var lockedDependencies = File.Exists(lockPath)
            ? File.ReadLines(lockPath).Count(line => !string.IsNullOrWhiteSpace(line))
            : 0;

        return new PythonEnvironmentInfoResponse(
            File.Exists(Path.Combine(_environmentDirectory, "python", "python.exe")) &&
            File.Exists(Path.Combine(_environmentDirectory, "python", "python311.dll")),
            metadata.RuntimeId,
            metadata.PythonVersion,
            metadata.CudaPythonVersion,
            lockedDependencies,
            IsUpdateToolAvailable(),
            Directory.Exists(Path.Combine(AppContext.BaseDirectory, "python-env.pending")));
    }

    /// <inheritdoc />
    public async Task<PythonEnvironmentValidationResponse> ValidateAsync(CancellationToken cancellationToken)
    {
        var info = GetInfo();
        var diagnostics = new List<string>();
        if (!info.IsReady)
        {
            diagnostics.Add("The bundled CPython runtime is missing or incomplete.");
            return new PythonEnvironmentValidationResponse(false, info.PythonVersion, info.CudaPythonVersion,
                diagnostics, DateTimeOffset.UtcNow);
        }

        var options = CudaPythonRuntimeOptions.LoadFromFile(_runtimeConfigurationPath);
        var pythonExecutable = Path.Combine(options.PythonHome!, "python.exe");
        var lockPath = Path.Combine(_environmentDirectory, "requirements.lock.txt");
        if (!File.Exists(lockPath))
        {
            diagnostics.Add("The Python dependency lock file is missing.");
        }

        var pipCheck = await ExternalProcessRunner.RunAsync(
            pythonExecutable,
            ["-m", "pip", "check"],
            _environmentDirectory,
            cancellationToken);
        if (pipCheck.ExitCode == 0)
        {
            diagnostics.Add("Python package dependencies are consistent.");
        }
        else
        {
            diagnostics.Add("Python package dependency validation reported conflicts.");
        }

        var freeze = await ExternalProcessRunner.RunAsync(
            pythonExecutable,
            ["-m", "pip", "freeze", "--all"],
            _environmentDirectory,
            cancellationToken);
        var lockMatchesInstalledPackages = freeze.ExitCode == 0 && File.Exists(lockPath) &&
            NormalizeRequirements(freeze.StandardOutput).SequenceEqual(
                NormalizeRequirements(File.ReadAllText(lockPath)),
                StringComparer.OrdinalIgnoreCase);
        diagnostics.Add(lockMatchesInstalledPackages
            ? "Installed Python packages match the dependency lock file."
            : "Installed Python packages do not match the dependency lock file.");

        var importCheck = await ExternalProcessRunner.RunAsync(
            pythonExecutable,
            ["-c", "import cuda.bindings.driver, importlib.metadata; importlib.metadata.version('cuda-python')"],
            _environmentDirectory,
            cancellationToken);
        if (importCheck.ExitCode == 0)
        {
            diagnostics.Add("The CUDA Python driver bindings import successfully.");
        }
        else
        {
            diagnostics.Add("The CUDA Python driver bindings could not be imported.");
        }

        return new PythonEnvironmentValidationResponse(
            pipCheck.ExitCode == 0 && lockMatchesInstalledPackages && importCheck.ExitCode == 0,
            info.PythonVersion,
            info.CudaPythonVersion,
            diagnostics,
            DateTimeOffset.UtcNow);
    }

    private static string[] NormalizeRequirements(string requirements) => requirements
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private bool IsUpdateToolAvailable() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "Update-CudaPythonEnvironment.ps1")) &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "python-runtime-win-x64.json"));

    private RuntimeMetadata ReadRuntimeMetadata()
    {
        using var stream = File.OpenRead(_runtimeConfigurationPath);
        return JsonSerializer.Deserialize<RuntimeMetadata>(stream, JsonOptions)
            ?? throw new JsonException("The bundled Python runtime metadata is empty.");
    }

    private sealed record RuntimeMetadata(
        string? PythonDllPath,
        string? PythonHome,
        string? PythonPath,
        string? PythonVersion,
        string? CudaPythonVersion,
        string? RuntimeId);
}
