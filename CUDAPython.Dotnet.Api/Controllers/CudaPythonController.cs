using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CUDAPython.Dotnet.Api.Contracts;
using CUDAPython.Dotnet.Api.Middleware;
using CUDAPython.Dotnet.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CUDAPython.Dotnet.Api.Controllers;

/// <summary>Discovers and invokes public CUDA Python functions.</summary>
[ApiController]
[Route("api/cuda-python")]
[Produces("application/json")]
public sealed class CudaPythonController(ICudaPythonService cudaPythonService) : ControllerBase
{
    /// <summary>Lists a paged set of public functions and class methods discovered from the CUDA package.</summary>
    /// <param name="modulePrefix">Optional module name prefix, such as <c>cuda.bindings.driver</c>.</param>
    /// <param name="search">Optional text matched against function names and Python documentation.</param>
    /// <param name="offset">Number of matching functions to skip.</param>
    /// <param name="limit">Maximum number of functions to return. The maximum is 500.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A page of discovered CUDA Python callables.</returns>
    [EndpointSummary("Discover CUDA Python functions")]
    [EndpointDescription("Returns public functions and class methods from the bundled cuda-python package, optionally filtered and paged.")]
    [HttpGet("functions", Name = "ListCudaPythonFunctions")]
    [ProducesResponseType(typeof(CudaPythonFunctionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<CudaPythonFunctionListResponse> GetFunctions(
        [FromQuery, StringLength(256)] string? modulePrefix,
        [FromQuery, StringLength(128)] string? search,
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        [FromQuery, Range(1, 500)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = cudaPythonService.GetFunctions(modulePrefix, search, offset, limit, cancellationToken);
        return Ok(result);
    }

    /// <summary>Invokes a public CUDA Python callable using JSON-compatible arguments.</summary>
    /// <remarks>
    /// This operation can launch device work and must be restricted to trusted callers. In Development it is
    /// available only from loopback clients; other environments require the configured administrator API key.
    /// </remarks>
    /// <param name="request">The CUDA module, attribute path, and JSON arguments.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The Python result encoded as JSON.</returns>
    [EndpointSummary("Invoke a CUDA Python callable")]
    [EndpointDescription("Invokes a public callable below the cuda package using JSON-compatible positional and keyword arguments. Requires a trusted local Development request or administrator API key.")]
    [HttpPost("invoke", Name = "InvokeCudaPythonFunction")]
    [ServiceFilter(typeof(AdminApiKeyAuthorizationFilter))]
    [RequestSizeLimit(131072)]
    [ProducesResponseType(typeof(CudaPythonInvocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<CudaPythonInvocationResponse> Invoke(
        [FromBody] CudaPythonInvokeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Arguments.Count > 64 || request.KeywordArguments.Count > 64)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Too many arguments",
                Detail = "A CUDA Python call may contain at most 64 positional and 64 keyword arguments."
            });
        }

        var response = cudaPythonService.Invoke(request, cancellationToken);
        return Ok(response);
    }
}
