using System.Text.Json;

namespace CUDAPython.Dotnet.Python;

/// <summary>Configures the process-wide embedded Python runtime and CUDA Python discovery.</summary>
public sealed record CudaPythonRuntimeOptions
{
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Gets or initializes the Python shared-library path; defaults to <c>PYTHONNET_PYDLL</c>.</summary>
    public string? PythonDllPath { get; init; }

    /// <summary>Gets or initializes the Python home directory.</summary>
    public string? PythonHome { get; init; }

    /// <summary>Gets or initializes the Python module search path.</summary>
    public string? PythonPath { get; init; }

    /// <summary>Gets or initializes the RID of the bundled Python runtime.</summary>
    public string? RuntimeId { get; init; }

    /// <summary>Gets or initializes the root module to inspect. The default is <c>cuda</c>.</summary>
    public string PackageRootModule { get; init; } = "cuda";

    /// <summary>Gets or initializes whether submodules below the root module are inspected.</summary>
    public bool IncludeSubmodules { get; init; } = true;

    /// <summary>Loads runtime settings written beside the bundled Python environment.</summary>
    /// <param name="filePath">The path to the runtime configuration JSON file.</param>
    /// <returns>The loaded runtime options.</returns>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    /// <exception cref="FileNotFoundException">The configuration file does not exist.</exception>
    /// <exception cref="JsonException">The configuration file is invalid.</exception>
    public static CudaPythonRuntimeOptions LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        var options = JsonSerializer.Deserialize<CudaPythonRuntimeOptions>(stream, RuntimeJsonOptions)
            ?? throw new JsonException($"The runtime configuration '{filePath}' contains no options.");
        var configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath))!;

        return options with
        {
            PythonDllPath = ResolvePath(options.PythonDllPath, configurationDirectory),
            PythonHome = ResolvePath(options.PythonHome, configurationDirectory),
            PythonPath = ResolvePythonPath(options.PythonPath, configurationDirectory)
        };
    }

    private static string? ResolvePath(string? path, string baseDirectory) =>
        path is null || Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDirectory, path));

    private static string? ResolvePythonPath(string? pythonPath, string baseDirectory) =>
        pythonPath is null
            ? null
            : string.Join(
                Path.PathSeparator,
                pythonPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => ResolvePath(path, baseDirectory)));
}
