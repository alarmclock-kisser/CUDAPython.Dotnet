# CUDA Python for .NET

This .NET 10 wrapper embeds CPython and exposes the callable surface of the Python package `cuda-python` through Python.NET. Public module functions and class methods are discovered at runtime with their signatures and documentation, then invoked through descriptors without a host-side Python installation.

## Self-contained Windows x64 and Linux x64 package

`CUDAPython.Dotnet` (version 0.2.1) packages the managed wrapper, CPython 3.11.18, the latest `cuda-python` release compatible with CPython 3.11, and all resolved pip dependencies. The build downloads a pinned CPython standalone archive and verifies its SHA-256 before extraction. It installs the Python dependencies into `python-env`, records the resolved package versions in `requirements.lock.txt`, and includes the complete environment in the NuGet package.

The package includes RID-scoped content files for **Windows x64** and **Linux x64**. NuGet copies the `python-env` directory that matches the consuming application's RID beside the application assemblies; no Python installation or startup network access is required. A package built on Windows contains the Windows environment only; the Linux environment is bundled when the package is built on a Linux host (or WSL). The CPython patch version and source archive are pinned per RID in `scripts/python-runtime-win-x64.json` and `scripts/python-runtime-linux-x64.json`.

Python.NET 3.2.1 is platform-agnostic: on Windows it loads `python311.dll` via `LoadLibrary`; on Linux it dlopen's the CPython shared library. x64 is required on both platforms.

The NVIDIA driver and CUDA-capable GPU remain host requirements for device operations. They cannot be supplied by a Python wheel or this NuGet package. CUDA user-mode components are included only when they are dependencies of the installed pip package set and may be redistributed.

## Use the runtime

The default initializer loads `python-env-<rid>/runtime.json` (or the legacy `python-env/runtime.json`) from the application output directory, where `<rid>` is `win-x64` or `linux-x64` based on the running OS and architecture:

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

## Curated bindings (`CUDAPython.Dotnet.Bindings`)

The reflection-based `CudaPythonRuntime` API above is generic and complete, but it is untyped: callers pass `object[]` arguments and get back `PyObject` results that must be converted manually. `CUDAPython.Dotnet.Bindings` is a second, complementary NuGet-included assembly that layers hand-written, statically-typed, IntelliSense-friendly C# wrappers on top of the same embedded CPython runtime for the most commonly used parts of `cuda-python`: the **CUDA Driver API**, **NVRTC** (runtime kernel compilation), and **NVML** (device monitoring). Use the reflection API when you need generic/dynamic access to arbitrary `cuda.*` members; use the Bindings classes when you want compile-time-checked, documented methods for everyday driver/compiler/monitoring workflows.

```csharp
using CUDAPython.Dotnet.Bindings;
using CUDAPython.Dotnet.Bindings.Driver;
using CUDAPython.Dotnet.Bindings.Nvrtc;
using Python.Runtime;

CudaPython.Initialize();
CudaDriver.Init();
CudaDriver.DeviceGet(0, out var device);

using (Py.GIL())
using (device)
{
    CudaDriver.CtxCreate(0, device, out var context);
    using (context)
    {
        // Compile a kernel from source at runtime via NVRTC.
        const string src = "extern \"C\" __global__ void add(float* a, float* b, float* c) { int i = threadIdx.x; c[i] = a[i] + b[i]; }";
        Nvrtc.CreateProgram("add.cu", src, out var program);
        using (program)
        {
            Nvrtc.CompileProgram(program);
            Nvrtc.GetPtx(program, out var ptx);

            CudaDriver.ModuleLoadData(ptx, out var module);
            using (module)
            {
                CudaDriver.ModuleGetFunction(module, "add", out var function);
                using (function)
                {
                    // ... CudaDriver.MemAlloc / MemcpyHtoD / LaunchKernel / MemcpyDtoH / MemFree ...
                }
            }
        }

        CudaDriver.CtxDestroy(context);
    }
}
```

Driver and NVRTC calls return a `CudaError`/`NvrtcResult` status enum (`CudaError.Success` / `NvrtcResult.Success` on success) via the method's return value, with any produced handle or data delivered through an `out` parameter — mirroring the native CUDA Driver/NVRTC C APIs 1:1 rather than throwing on failure, so you can inspect error codes the same way C/C++ callers do. NVML methods are different: they return values directly and throw `CudaPythonException` on failure (see the table below).

**Important — GIL and disposal:** every Bindings method that returns a `PyObject` handle (devices, contexts, modules, functions, device pointers, NVRTC programs, ...) follows the same ownership rule as the reflection API: the caller owns the handle and **must only access or dispose it while holding `Py.GIL()`**. Disposing a `PyObject` outside the GIL is not a managed exception — it is a native access violation that crashes the process. Always wrap the full lifetime of borrowed handles (including their `using` blocks) inside `using (Py.GIL()) { ... }`.

Three different wire conventions are bridged transparently by the Bindings layer so you don't need to know about them:

| Area | Convention |
| --- | --- |
| CUDA Driver (`cuda.bindings.driver`) | The underlying Python call returns a `(status, ...)` tuple; the wrapper splits this into a returned `CudaError` status and `out` value(s) — it does **not** throw on a non-success status, mirroring the native C API. Handles (`CUdevice`, `CUcontext`, `CUmodule`, `CUdeviceptr`, ...) are opaque Python objects. |
| NVRTC (`cuda.bindings.nvrtc`) | Same tuple/status-to-`out`-parameter convention as the driver, returning `NvrtcResult`. Source code, program names, and compiled output (PTX/CUBIN/log) are exchanged as genuine Python `bytes`/`bytearray`, handled internally by the wrapper. |
| NVML (`cuda.bindings.nvml`) | A hand-written binding style: functions return bare values directly (no status tuple) and raise Python exceptions (e.g. `UninitializedError`) on failure. Device handles are plain integers. Its Cython layer performs strict `isinstance` checks, so the wrapper always passes genuine Python `int` objects rather than relying on default interop conversion. |

Launching a kernel requires passing typed arguments (device pointers and scalars) through the CUDA driver's raw `void**` convention. `CudaKernelArg` hides this behind factory methods:

```csharp
CudaDriver.LaunchKernel(function, gridDimX: 1, gridDimY: 1, gridDimZ: 1,
    blockDimX: 32, blockDimY: 1, blockDimZ: 1, sharedMemBytes: 0, stream: null,
    CudaKernelArg.DevicePointer(devA), CudaKernelArg.DevicePointer(devB),
    CudaKernelArg.DevicePointer(devC));
```

**Not yet covered:** cuBLAS, cuFFT, cuSPARSE, and cuSOLVER are not part of this curated assembly (their Python wheels are distributed separately from `cuda-python`). They remain reachable through the generic reflection API once their packages are added to the bundled Python environment — see the open follow-up item in the project backlog.

## Build and test

```powershell
dotnet test CUDAPython.Dotnet.Tests\CUDAPython.Dotnet.Tests.csproj
dotnet pack CUDAPython.Dotnet.Nuget\CUDAPython.Dotnet.Nuget.csproj -c Release
dotnet run --project CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.csproj
```

Build and pack require network access to the pinned CPython release and PyPI. The deployed application does not. Set `CUDAPythonProvisionEnvironment=false` only when intentionally building the managed wrapper without its bundled runtime. On Windows, the build provisions the `win-x64` environment only; the `linux-x64` environment must be provisioned on a Linux host (or WSL) and is included in the package when present.

## REST API

The .NET 10 API (Windows x64 only) references the wrapper and includes the same self-contained `python-env` beside its assemblies. In Development, NSwag generates the OpenAPI document at `/swagger/v1/swagger.json` and serves the familiar interactive Swagger UI at `/swagger`. The NuGet library itself is cross-platform (win-x64 and linux-x64); only the API host is Windows-specific.

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
