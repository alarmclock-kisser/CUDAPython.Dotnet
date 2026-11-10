using CUDAPython.Dotnet.Api.Contracts;
using CUDAPython.Dotnet.Api.Middleware;
using CUDAPython.Dotnet.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CUDAPython.Dotnet.Api.Controllers;

/// <summary>Exposes authenticated Development-only Python environment and NuGet maintenance operations.</summary>
[ApiController]
[Route("api/maintenance")]
[ServiceFilter(typeof(AdminApiKeyAuthorizationFilter))]
[Produces("application/json")]
public sealed class MaintenanceController(
    IMaintenanceService maintenanceService,
    IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Downloads and prepares a refreshed Python environment for activation after restart.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The staged Python environment version and restart requirement.</returns>
    [EndpointSummary("Stage Python environment update")]
    [EndpointDescription("Downloads the pinned CPython runtime and latest compatible cuda-python requirements into a staging directory. The update is activated at the next API startup.")]
    [HttpPost("python-environment/update", Name = "StagePythonEnvironmentUpdate")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(PythonEnvironmentUpdateResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<PythonEnvironmentUpdateResponse>> UpdatePythonEnvironment(
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await maintenanceService.UpdatePythonEnvironmentAsync(cancellationToken);
        return Accepted(result);
    }

    /// <summary>Builds the self-contained Windows x64 NuGet package from a local repository checkout.</summary>
    /// <param name="request">The release build configuration.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>Package identity, size, hash, and creation time.</returns>
    [EndpointSummary("Build NuGet package")]
    [EndpointDescription("Runs dotnet pack in Release from a local source checkout and returns the package SHA-256. Available only in Development.")]
    [HttpPost("nuget/build", Name = "BuildCudaPythonNuGetPackage")]
    [ProducesResponseType(typeof(NuGetPackageBuildResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<NuGetPackageBuildResponse>> BuildNuGetPackage(
        [FromBody] BuildNuGetPackageRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await maintenanceService.BuildNuGetPackageAsync(request.Configuration, cancellationToken);
        return Ok(result);
    }
}
