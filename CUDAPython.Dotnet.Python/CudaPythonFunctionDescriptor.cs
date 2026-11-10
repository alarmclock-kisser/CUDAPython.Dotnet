namespace CUDAPython.Dotnet.Python;

/// <summary>Describes a public function or class method discovered in the configured CUDA Python modules.</summary>
public sealed class CudaPythonFunctionDescriptor
{
    internal CudaPythonFunctionDescriptor(
        string moduleName,
        string name,
        string? signature,
        string? documentation)
    {
        ModuleName = moduleName;
        Name = name;
        Signature = signature;
        Documentation = documentation;
    }

    /// <summary>Gets the Python module containing the function.</summary>
    public string ModuleName { get; }

    /// <summary>Gets the Python attribute path relative to the module, including a containing class when applicable.</summary>
    public string Name { get; }

    /// <summary>Gets the function's fully qualified Python name.</summary>
    public string PythonQualifiedName => $"{ModuleName}.{Name}";

    /// <summary>Gets the Python signature when the function exposes one.</summary>
    public string? Signature { get; }

    /// <summary>Gets the Python documentation string, if provided by the package.</summary>
    public string? Documentation { get; }

}
