using System.Text.Json;

namespace CUDAPython.Dotnet.Api.Contracts;

/// <summary>Provides the API identity and bundled Python readiness state.</summary>
public sealed record ApiInfoResponse(
    string ApiName,
    string Version,
    bool PythonEnvironmentReady);

/// <summary>Describes the bundled Python runtime and installed CUDA Python packages.</summary>
public sealed record PythonEnvironmentInfoResponse(
    bool IsReady,
    string? RuntimeIdentifier,
    string? PythonVersion,
    string? CudaPythonVersion,
    int LockedDependencyCount,
    bool UpdateToolAvailable,
    bool PendingUpdateAvailable);

/// <summary>Reports the result of validating the bundled Python environment.</summary>
public sealed record PythonEnvironmentValidationResponse(
    bool IsValid,
    string? PythonVersion,
    string? CudaPythonVersion,
    IReadOnlyList<string> Diagnostics,
    DateTimeOffset ValidatedAt);

/// <summary>Reports the CUDA driver version and initialization state.</summary>
public sealed record CudaDriverInfoResponse(
    int DriverVersionStatus,
    int? DriverVersion,
    int? DriverVersionMajor,
    int? DriverVersionMinor,
    int InitializationStatus,
    bool Available);

/// <summary>Describes the query status and specifications for one CUDA device.</summary>
public sealed record CudaDeviceInfoResponse(
    int Ordinal,
    int Status,
    string? Name,
    int? ComputeCapabilityMajor,
    int? ComputeCapabilityMinor,
    int ComputeCapabilityStatus,
    long? MemoryBytes,
    int MemoryStatus,
    string? Uuid,
    int UuidStatus,
    int? MultiprocessorCount,
    int MultiprocessorStatus,
    int? MaxThreadsPerBlock,
    int MaxThreadsStatus,
    int? ClockRateKHz,
    int ClockRateStatus);

/// <summary>Returns the CUDA device count query status and discovered device specifications.</summary>
public sealed record CudaDevicesResponse(
    int Status,
    IReadOnlyList<CudaDeviceInfoResponse> Devices);

/// <summary>Describes one public Python function discovered from the CUDA package.</summary>
public sealed record CudaPythonFunctionResponse(
    string ModuleName,
    string Name,
    string PythonQualifiedName,
    string? Signature,
    string? Documentation);

/// <summary>Returns a page of discovered CUDA Python functions.</summary>
public sealed record CudaPythonFunctionListResponse(
    int Offset,
    int Limit,
    int TotalCount,
    IReadOnlyList<CudaPythonFunctionResponse> Functions);

/// <summary>Returns a JSON-compatible result from a CUDA Python call.</summary>
public sealed record CudaPythonInvocationResponse(
    string ModuleName,
    string AttributePath,
    JsonElement Result,
    DateTimeOffset CompletedAt);

/// <summary>Reports a staged Python environment update that will be applied after restart.</summary>
public sealed record PythonEnvironmentUpdateResponse(
    bool Updated,
    bool RestartRequired,
    string PythonVersion,
    string CudaPythonVersion,
    string Message);

/// <summary>Reports the package file produced by a development-only NuGet build.</summary>
public sealed record NuGetPackageBuildResponse(
    string PackageId,
    string Version,
    long SizeBytes,
    string Sha256,
    DateTimeOffset BuiltAt);
