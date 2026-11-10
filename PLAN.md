# Plan: CUDA Python .NET 10 Wrapper

> Interactive work plan in English. Completed implementation steps are checked; remaining platform and release decisions stay open.

## Goal

Build a .NET 10 library that discovers and invokes the public callable surface exposed by the official `cuda-python` package. Builds provision CPython and its Python dependencies automatically, and the RID-specific NuGet package carries the complete environment. The ASP.NET Core API exposes diagnostics, generic discovery/invocation, and development maintenance endpoints.

## Current implementation

- `CUDAPython.Dotnet.Python` hosts Python.NET, discovers public CUDA Python functions and class methods, and invokes them directly.
- `CUDAPython.Dotnet.Nuget` creates the `CUDAPython.Dotnet.win-x64` package with the wrapper, CPython, and pip-installed CUDA Python dependencies.
- `CUDAPython.Dotnet.Tests` contains runtime-configuration tests and a bundled-Python integration test.
- `scripts/Update-CudaPythonEnvironment.ps1` downloads a pinned standalone CPython build, installs the latest compatible `cuda-python` dependency set, and writes a lock file and relocatable runtime configuration.
- `CUDAPython.Dotnet.Api` exposes CUDA/Python diagnostics, reflection-based callable discovery and invocation, a Scalar OpenAPI UI, and guarded Development-only environment/package maintenance.
- Staged Python environment updates are promoted during API startup, before CPython initialization.
- The solution includes the API, wrapper, pack project, and tests.

## Design principles

- **Self-contained deployment:** The `win-x64` NuGet pack target downloads CPython 3.11.18, verifies its SHA-256, installs `cuda-python` and all resolved pip requirements, and includes the runtime beside the application assemblies.
- **No host Python requirement:** Build and pack provision Python automatically. The deployed application uses its packaged `python-env` and makes no Python installation or startup download.
- **Update and lock:** Package creation refreshes to the latest stable `cuda-python` release compatible with CPython 3.11 and records every installed package in `requirements.lock.txt`.
- **Supported interpreter:** Python.NET 3.2.1 requires CPython 3.11 or later. The bundle uses the minimum supported minor, with a pinned stable 3.11 patch build.
- **Dynamic reflection is not code generation:** Discovery exposes runtime functions and class methods with signatures and docstrings. It does not emit a statically typed C# method for every Python member.
- **Direct API mapping:** Callables can be invoked through their discovered descriptors or directly by module and attribute path, without an extra policy layer.
- **Administrative API safeguards:** Generic invocation requires loopback in Development or `CUDAPYTHON_ADMIN_KEY`; environment updates and package builds are Development-only and staged where necessary.
- **JSON API boundary:** REST invocation accepts JSON-compatible values and public `cuda.*` paths. Python pointers, opaque handles, and custom resource lifetimes require specialized bindings.
- **Native hot path remains future work:** Direct P/Invoke/Source-Generated P/Invoke can reduce Python/.NET dispatch overhead for selected hot APIs. The current Python.NET bridge provides broader dynamic coverage, not a low-latency guarantee.
- **CUDA is not SIMD:** CUDA kernels typically use SIMT execution. Interop choices do not automatically accelerate kernels; launch overhead, synchronization, and data movement remain important.

## Interactive work plan

### 0. Architecture decisions
- [ ] Select supported operating systems and architectures.
- [x] Set the initial Python.NET interpreter floor to CPython 3.11.
- [x] Implement in-process Python.NET reflection for dynamic package coverage.
- [ ] Add direct native bindings for selected latency-critical APIs after profiling.
- [x] Keep the call path direct: do not gate methods behind safety classifications.
- [ ] Decide which native/Python components may be redistributed and review their licenses.

### 1. Project structure and dependencies
- [x] Implement the `CUDAPython.Dotnet.Python` Python.NET wrapper library.
- [x] Create the `CUDAPython.Dotnet.Nuget` pack project.
- [x] Create the `CUDAPython.Dotnet.Tests` MSTest project.
- [x] Add all projects to the solution.
- [x] Document the public API and runtime requirements in English, including XML API documentation.

### 2. Python environment and CUDA dependencies
- [x] Download the pinned CPython standalone runtime without requiring a host Python installation.
- [x] Install `cuda-python` and its dependencies, verify them, and write the resolved package lock.
- [x] Write relocatable runtime JSON with the Python DLL, Python home, module path, interpreter version, and RID.
- [ ] Decide supported RIDs, CUDA driver minimums, and native library requirements.
- [x] Bundle CPython and resolved Python wheels in the initial `win-x64` package.
- [ ] Add integrity verification and safe concurrent initialization for any packaged runtime cache.

### 3. .NET bridge
- [x] Initialize CPython once per process and acquire the GIL around Python calls.
- [x] Discover public module functions and public class methods with signatures and docstrings.
- [x] Invoke discovered functions with positional and keyword arguments and surface Python exceptions.
- [x] Support direct invocation by module name and Python attribute path without policy friction.
- [ ] Add managed conversions for common CUDA result types and resource lifetimes.
- [ ] Implement and profile direct native interop for hot APIs.

### 4. NuGet package
- [x] Pack the wrapper assembly, XML documentation, README, CPython, and pip-installed CUDA Python dependencies.
- [ ] Add additional RID-specific packages and any licensed native CUDA assets required by those RIDs.
- [x] Generate the NuGet package with `dotnet pack` and inspect its contents.
- [x] Test installation and initialization in a separate consumer project without a host Python installation.
- [ ] Define package versioning and release provenance for bundled dependencies.

### 5. Quality and tests
- [x] Add MSTest coverage for runtime JSON relocation and embedded-Python discovery/invocation.
- [x] Run discovery and invocation against the bundled CPython 3.11 runtime without preinstalled Python or GPU hardware.
- [ ] Add optional GPU tests on suitable hardware and document supported RID-specific build/pack steps.

### 6. REST API
- [x] Integrate `.Api` against the .NET wrapper and bundle the Python environment beside API assemblies.
- [x] Add OpenAPI and Scalar UI plus DTO-backed info, environment validation, device/driver, generic discovery/invocation, and maintenance endpoints.
- [x] Protect generic invocation and maintenance operations; stage runtime updates for activation at restart.
- [x] Add an HTTP request file covering the API routes.
- [ ] Add automated API tests and account for GPU operation duration and resource limits.

## First milestone acceptance criteria

- [x] Implement dynamic Python.NET discovery and direct invocation.
- [x] Create a NuGet package containing the managed wrapper, CPython, CUDA Python wheels, resolved pip dependencies, and XML documentation.
- [x] Run unit tests without a GPU; keep GPU-dependent tests opt-in.
- [x] Document package platform limits and the NVIDIA driver/GPU host requirements.
- [x] Provide REST diagnostics and a generic JSON invocation surface with explicit administrative access controls.

## Decision log

| Topic | Status | Decision |
| --- | --- | --- |
| Supported RIDs | First RID implemented | `win-x64`; Linux and other architectures require matching runtimes and packages. |
| Python and CUDA Python versions | Implemented | CPython 3.11.18 is the minimum-compatible minor; pack installs the latest stable `cuda-python` compatible with it and records the complete lock. |
| Bridge and C# API style | Implemented first slice | Python.NET reflection provides descriptor-based and direct module/path invocation. Native P/Invoke remains future work for profiled hot paths. |
| Bundled components and system requirements | Partial | NuGet includes CPython and pip dependencies; NVIDIA driver and CUDA-capable hardware remain host requirements. |
| Update and release procedure | Implemented first slice | `dotnet pack` downloads/verifies CPython and refreshes the pip environment before packing. |
| Runtime location | Implemented | `python-env` is included in the NuGet content and copied beside the consuming application's assemblies. |
| REST invocation | Implemented initial JSON surface | Public `cuda.*` callables are discovered and invoked dynamically; values needing native pointers or custom Python lifetimes remain outside generic JSON invocation. |
| API maintenance | Guarded | Environment updates are staged until restart; update/package-build endpoints are Development-only, with loopback/API-key access rules. |
