# CUDA Python for .NET

This .NET 10 wrapper embeds CPython and exposes the callable surface of the Python package `cuda-python` through Python.NET. Public module functions and class methods are discovered at runtime with their signatures and documentation, then invoked through descriptors without a host-side Python installation.

## Self-contained Windows x64 package

`CUDAPython.Dotnet.win-x64` packages the managed wrapper, CPython 3.11.18, the latest `cuda-python` release compatible with CPython 3.11, and all resolved pip dependencies. The build downloads a pinned CPython standalone archive and verifies its SHA-256 before extraction. It installs the Python dependencies into `python-env`, records the resolved package versions in `requirements.lock.txt`, and includes the complete environment in the NuGet package.

On first build, `CUDAPython.Dotnet.Python` provisions the environment into its assembly output directory. `dotnet pack -c Release` refreshes the pip dependencies and bundles the resulting environment. A package consumer receives the `python-env` directory beside the application assemblies; no Python installation or startup network access is required on the server.

The initial bundle targets **Windows x64 only**. Linux and other Windows architectures need separate runtime assets and packages. Python.NET 3.2.1 requires Python 3.11 or later; 3.11 is used as the lowest compatible minor version. The CPython patch version and source archive are pinned in `scripts/python-runtime-win-x64.json`.

The NVIDIA driver and CUDA-capable GPU remain host requirements for device operations. They cannot be supplied by a Python wheel or this NuGet package. CUDA user-mode components are included only when they are dependencies of the installed pip package set and may be redistributed.

## Use the runtime

The default initializer loads `python-env/runtime.json` from the application output directory:

```csharp
using CUDAPython.Dotnet.Python;
using Python.Runtime;

var runtime = CudaPythonRuntime.Initialize();
var functions = runtime.DiscoverFunctions();
```

Discovery imports the configured root module (default `cuda`) and its submodules. To invoke a member, select its descriptor and pass positional or keyword arguments:

```csharp
var function = functions.Single(item => item.PythonQualifiedName == "cuda.bindings.driver.cuInit");
using (Py.GIL())
using (var result = runtime.Invoke(function, [0]))
{
	Console.WriteLine(result);
}
```

Arguments can be ordinary .NET values or `PyObject` instances returned from previous calls. Returned `PyObject` instances are owned by the caller; access and dispose them only while holding `Py.GIL()`. Discovery imports Python modules, so package initialization failures are surfaced directly.

## Build and test

```powershell
dotnet test CUDAPython.Dotnet.Tests\CUDAPython.Dotnet.Tests.csproj
dotnet pack CUDAPython.Dotnet.Nuget\CUDAPython.Dotnet.Nuget.csproj -c Release
dotnet run --project CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.csproj
```

Build and pack require network access to the pinned CPython release and PyPI. The deployed application does not. Set `CUDAPythonProvisionEnvironment=false` only when intentionally building the managed wrapper without its bundled runtime.

## REST API

The .NET 10 API references the wrapper and includes the same self-contained `python-env` beside its assemblies. In Development, the OpenAPI document is available at `/openapi/v1.json` and the interactive Scalar UI at `/swagger`.

| Route | Purpose |
| --- | --- |
| `GET /api/info` | API version and Python runtime readiness. |
| `GET /api/info/python-environment` | Python/CUDA Python versions, locked dependency count, and update state. |
| `GET /api/info/python-environment/validation` | Runs `pip check`, compares installed packages to the lock file, and imports CUDA driver bindings using bundled CPython. |
| `GET /api/info/cuda/driver` | CUDA driver version and initialization status. |
| `GET /api/info/cuda/devices` | Device names, UUIDs, compute capability, memory, and selected device limits. |
| `GET /api/cuda-python/functions` | Filtered and paged reflection-based discovery of public callables. |
| `POST /api/cuda-python/invoke` | Invokes a public callable below `cuda` with JSON-compatible arguments. |
| `POST /api/maintenance/python-environment/update` | Stages a fresh Python runtime and pip dependency set. Development only. |
| `POST /api/maintenance/nuget/build` | Runs the Release NuGet pack operation from a local checkout. Development only. |

Environment updates are staged as `python-env.pending` and activated at the next API startup, avoiding replacement of the currently loaded CPython DLL. The updater and NuGet build endpoints are Development-only. Administrative invocation and maintenance requests are allowed from loopback in Development; other requests require `X-API-Key` matching the `CUDAPYTHON_ADMIN_KEY` environment variable. Do not expose the Development profile to untrusted networks.

Generic REST invocation is intentionally limited to JSON-compatible values and public `cuda.*` member paths. CUDA pointers, buffers, handles, and Python objects with custom lifetimes need specialized managed bindings; returned non-JSON values are represented with type metadata rather than reconstructed as live objects. Driver/device endpoints report native CUDA status codes when the host driver or GPU is unavailable. See `CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.http` for request examples.
