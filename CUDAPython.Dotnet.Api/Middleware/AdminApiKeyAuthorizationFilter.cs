using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CUDAPython.Dotnet.Api.Middleware;

/// <summary>Restricts CUDA invocation and maintenance actions to loopback development clients or API-key holders.</summary>
public sealed class AdminApiKeyAuthorizationFilter(
    IConfiguration configuration,
    IWebHostEnvironment environment) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        if (environment.IsDevelopment() && IsLoopback(remoteAddress))
        {
            await next();
            return;
        }

        var configuredKey = configuration["CudaPython:AdminApiKey"] ?? Environment.GetEnvironmentVariable("CUDAPYTHON_ADMIN_KEY");
        if (string.IsNullOrEmpty(configuredKey))
        {
            context.Result = Problem(
                context.HttpContext,
                StatusCodes.Status503ServiceUnavailable,
                "Administrative API is disabled",
                "Configure CUDAPYTHON_ADMIN_KEY to enable CUDA invocation and maintenance endpoints.");
            return;
        }

        var suppliedKey = context.HttpContext.Request.Headers["X-API-Key"].ToString();
        var expectedBytes = Encoding.UTF8.GetBytes(configuredKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            context.Result = Problem(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "A valid X-API-Key header is required for this operation.");
            return;
        }

        await next();
    }

    private static bool IsLoopback(IPAddress? address) =>
        address is not null &&
        (IPAddress.IsLoopback(address) ||
         (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));

    private static ObjectResult Problem(HttpContext context, int statusCode, string title, string detail) =>
        new(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        })
        {
            StatusCode = statusCode
        };
}
