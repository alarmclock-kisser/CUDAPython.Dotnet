using CUDAPython.Dotnet.Api.Contracts;

namespace CUDAPython.Dotnet.Api.Services;

/// <summary>Provides information and validation for the bundled Python environment.</summary>
public interface IPythonEnvironmentService
{
    /// <summary>Returns metadata about the active bundled Python environment.</summary>
    PythonEnvironmentInfoResponse GetInfo();

    /// <summary>Runs pip consistency and CUDA Python import checks using the bundled interpreter.</summary>
    Task<PythonEnvironmentValidationResponse> ValidateAsync(CancellationToken cancellationToken);
}
