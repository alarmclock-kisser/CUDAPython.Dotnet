using CUDAPython.Dotnet.Api.Contracts;
using CUDAPython.Dotnet.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CUDAPython.Dotnet.Api.Controllers;

/// <summary>Reports API, bundled Python, CUDA driver, and CUDA device information.</summary>
[ApiController]
[Route("api/info")]
[Produces("application/json")]
public sealed class InfoController(
    IPythonEnvironmentService pythonEnvironmentService,
    ICudaPythonService cudaPythonService) : ControllerBase
{
    /// <summary>Returns the API version and whether its bundled Python runtime is available.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>API and Python environment readiness information.</returns>
    [EndpointSummary("Get API readiness")]
    [EndpointDescription("Returns the API version and whether the bundled Python runtime is ready.")]
    [HttpGet(Name = "GetApiInfo")]
    [ProducesResponseType(typeof(ApiInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<ApiInfoResponse> Get(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var python = pythonEnvironmentService.GetInfo();
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        return Ok(new ApiInfoResponse("CUDA Python API", version, python.IsReady));
    }

    /// <summary>Returns version and dependency-lock information for the bundled Python environment.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>Python and CUDA Python environment details.</returns>
    [EndpointSummary("Get Python environment details")]
    [EndpointDescription("Returns the bundled CPython and cuda-python versions, lock-file package count, and update state.")]
    [HttpGet("python-environment", Name = "GetPythonEnvironmentInfo")]
    [ProducesResponseType(typeof(PythonEnvironmentInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<PythonEnvironmentInfoResponse> GetPythonEnvironment(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(pythonEnvironmentService.GetInfo());
    }

    /// <summary>Checks pip dependencies and imports the CUDA Python driver bindings.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>Environment validation details.</returns>
    [EndpointSummary("Validate Python environment")]
    [EndpointDescription("Runs pip dependency consistency checks and imports the CUDA Python driver bindings using the bundled interpreter.")]
    [HttpGet("python-environment/validation", Name = "ValidatePythonEnvironment")]
    [ProducesResponseType(typeof(PythonEnvironmentValidationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PythonEnvironmentValidationResponse>> ValidatePythonEnvironment(
        CancellationToken cancellationToken)
    {
        var result = await pythonEnvironmentService.ValidateAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>Returns the CUDA driver version and initialization state.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>CUDA driver version information.</returns>
    [EndpointSummary("Get CUDA driver information")]
    [EndpointDescription("Returns CUDA driver version and initialization status. Driver status codes are retained when no compatible driver is available.")]
    [HttpGet("cuda/driver", Name = "GetCudaDriverInfo")]
    [ProducesResponseType(typeof(CudaDriverInfoResponse), StatusCodes.Status200OK)]
    public ActionResult<CudaDriverInfoResponse> GetCudaDriver(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(cudaPythonService.GetDriverInfo(cancellationToken));
    }

    /// <summary>Returns device names, UUIDs, compute capability, memory, clock rate, and selected device limits.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>CUDA device specifications and query status.</returns>
    [EndpointSummary("List CUDA devices")]
    [EndpointDescription("Returns CUDA device count status and available device names, UUIDs, compute capabilities, memory, and selected device limits.")]
    [HttpGet("cuda/devices", Name = "GetCudaDevices")]
    [ProducesResponseType(typeof(CudaDevicesResponse), StatusCodes.Status200OK)]
    public ActionResult<CudaDevicesResponse> GetCudaDevices(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(cudaPythonService.GetDevices(cancellationToken));
    }
}
