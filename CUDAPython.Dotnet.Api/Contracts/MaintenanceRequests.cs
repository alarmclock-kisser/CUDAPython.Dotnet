using System.ComponentModel.DataAnnotations;

namespace CUDAPython.Dotnet.Api.Contracts;

/// <summary>Contains optional package build configuration for a development checkout.</summary>
public sealed record BuildNuGetPackageRequest
{
    /// <summary>Gets the NuGet build configuration. The default is <c>Release</c>.</summary>
    [Required, RegularExpression("^Release$")]
    public string Configuration { get; init; } = "Release";
}
