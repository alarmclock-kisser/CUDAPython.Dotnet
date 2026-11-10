using System.Text.Json;
using CUDAPython.Dotnet.Api.Contracts;
using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Implements the API facade over the embedded CUDA Python runtime.</summary>
public sealed class CudaPythonService(CudaPythonRuntime runtime) : ICudaPythonService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public CudaPythonFunctionListResponse GetFunctions(
        string? modulePrefix,
        string? search,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        cancellationToken.ThrowIfCancellationRequested();

        var matchingFunctions = runtime.DiscoverFunctions()
            .Where(function => modulePrefix is null || function.ModuleName.StartsWith(modulePrefix, StringComparison.Ordinal))
            .Where(function => string.IsNullOrWhiteSpace(search) ||
                               function.PythonQualifiedName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                               (function.Documentation?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
        var page = matchingFunctions
            .Skip(offset)
            .Take(limit)
            .Select(function => new CudaPythonFunctionResponse(
                function.ModuleName,
                function.Name,
                function.PythonQualifiedName,
                function.Signature,
                function.Documentation))
            .ToArray();

        return new CudaPythonFunctionListResponse(offset, limit, matchingFunctions.Length, page);
    }

    /// <inheritdoc />
    public CudaPythonInvocationResponse Invoke(CudaPythonInvokeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var resultJson = runtime.InvokeJson(
            request.ModuleName,
            request.AttributePath,
            JsonSerializer.Serialize(request.Arguments, JsonOptions),
            JsonSerializer.Serialize(request.KeywordArguments, JsonOptions));
        using var resultDocument = JsonDocument.Parse(resultJson);

        return new CudaPythonInvocationResponse(
            request.ModuleName,
            request.AttributePath,
            resultDocument.RootElement.Clone(),
            DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public CudaDriverInfoResponse GetDriverInfo(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<CudaDriverInfoResponse>(runtime.GetCudaDriverInfoJson(), JsonOptions)
            ?? throw new JsonException("The CUDA driver helper returned an empty result.");
    }

    /// <inheritdoc />
    public CudaDevicesResponse GetDevices(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<CudaDevicesResponse>(runtime.GetCudaDevicesJson(), JsonOptions)
            ?? throw new JsonException("The CUDA device helper returned an empty result.");
    }
}
