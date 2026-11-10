using CUDAPython.Dotnet.Python;
using CUDAPython.Dotnet.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CUDAPython.Dotnet.Api.Middleware;

/// <summary>Maps API exceptions to safe RFC 7807 problem responses.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found", "The requested resource was not found."),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request", "The request contains invalid values."),
            CudaPythonException => (StatusCodes.Status422UnprocessableEntity, "CUDA Python operation failed", "The CUDA Python operation could not be completed."),
            MaintenanceOperationException => (StatusCodes.Status502BadGateway, "Maintenance operation failed", "The requested maintenance operation failed. See server logs for details."),
            FileNotFoundException => (StatusCodes.Status503ServiceUnavailable, "Python environment unavailable", "The bundled Python environment is missing or incomplete."),
            PlatformNotSupportedException => (StatusCodes.Status501NotImplemented, "Platform not supported", "This operation is not supported on the current platform."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Operation conflict", "The operation cannot be completed in the current state."),
            _ => (0, string.Empty, string.Empty)
        };

        if (statusCode == 0)
        {
            return false;
        }

        logger.LogWarning(exception, "Mapped API exception to HTTP {StatusCode}: {Title}", statusCode, title);
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
