using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CUDAPython.Dotnet.Api.Contracts;

/// <summary>Contains JSON-compatible arguments for one public CUDA Python callable.</summary>
public sealed record CudaPythonInvokeRequest
{
    /// <summary>Gets the CUDA package module containing the callable.</summary>
    [Required, StringLength(256)]
    [RegularExpression(@"^cuda(?:\.[A-Za-z][A-Za-z0-9_]*)*$")]
    public required string ModuleName { get; init; }

    /// <summary>Gets the public function or class-method path relative to the module.</summary>
    [Required, StringLength(256)]
    [RegularExpression(@"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)*$")]
    public required string AttributePath { get; init; }

    /// <summary>Gets the positional JSON arguments.</summary>
    [MaxLength(64)]
    public IReadOnlyList<JsonElement> Arguments { get; init; } = Array.Empty<JsonElement>();

    /// <summary>Gets the keyword JSON arguments.</summary>
    public IReadOnlyDictionary<string, JsonElement> KeywordArguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
