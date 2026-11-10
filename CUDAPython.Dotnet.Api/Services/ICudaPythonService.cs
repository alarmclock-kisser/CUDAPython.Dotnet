using CUDAPython.Dotnet.Api.Contracts;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Provides discovery, invocation, and diagnostic access to the CUDA Python package.</summary>
public interface ICudaPythonService
{
    /// <summary>Returns a filtered page of public CUDA Python functions.</summary>
    CudaPythonFunctionListResponse GetFunctions(
        string? modulePrefix,
        string? search,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Invokes a public CUDA Python callable using JSON-compatible arguments.</summary>
    CudaPythonInvocationResponse Invoke(CudaPythonInvokeRequest request, CancellationToken cancellationToken);

    /// <summary>Returns CUDA driver version and initialization information.</summary>
    CudaDriverInfoResponse GetDriverInfo(CancellationToken cancellationToken);

    /// <summary>Returns CUDA device specifications from the current host.</summary>
    CudaDevicesResponse GetDevices(CancellationToken cancellationToken);
}
