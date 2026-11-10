using CUDAPython.Dotnet.Api.Contracts;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Provides authenticated development maintenance operations.</summary>
public interface IMaintenanceService
{
    /// <summary>Builds a pending Python environment update that is applied after an application restart.</summary>
    Task<PythonEnvironmentUpdateResponse> UpdatePythonEnvironmentAsync(CancellationToken cancellationToken);

    /// <summary>Builds the RID-specific NuGet package from a local source checkout.</summary>
    Task<NuGetPackageBuildResponse> BuildNuGetPackageAsync(string configuration, CancellationToken cancellationToken);
}
