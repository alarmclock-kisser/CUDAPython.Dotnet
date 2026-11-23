using global::Python.Runtime;
using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Bindings.Driver;

/// <summary>
/// Curated, statically-typed bindings for the <c>cuda.bindings.driver</c> module.
/// Covers the essential CUDA driver API: device query, context management,
/// memory allocation/copy, module loading, kernel launch, streams, and events.
/// </summary>
/// <remarks>
/// All methods are GIL-scoped internally. Call <see cref="CudaPython.Initialize"/>
/// once per process before using any method. For functions not covered here,
/// use the reflection-based API: <c>CudaPythonRuntime.Initialize()</c> +
/// <c>DiscoverFunctions()</c> + <c>Invoke()</c>.
/// </remarks>
public static class CudaDriver
{
    private const string Module = "cuda.bindings.driver";

    // ── Init & Version ─────────────────────────────────────────────────────

    /// <summary>Initializes the CUDA driver. Must be called before any other driver API.</summary>
    /// <param name="flags">Initialization flags (0 for default).</param>
    public static CudaError Init(uint flags = 0)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuInit", (object)flags);

    /// <summary>Queries the CUDA driver version (e.g. 13000 for 13.0.0).</summary>
    public static CudaError DriverGetVersion(out int version)
        => CudaInvoke.InvokeDouble<int, CudaError>(Module, "cuDriverGetVersion", out version, out _);

    // ── Device Query ───────────────────────────────────────────────────────

    /// <summary>Queries the number of CUDA-capable devices.</summary>
    public static CudaError DeviceGetCount(out int deviceCount)
        => CudaInvoke.InvokeDouble<int, CudaError>(Module, "cuDeviceGetCount", out deviceCount, out _);

    /// <summary>Gets the CUDA device handle for a device ordinal.</summary>
    /// <param name="ordinal">Zero-based device ordinal.</param>
    /// <param name="device">The device handle (PyObject). Dispose when done.</param>
    public static CudaError DeviceGet(int ordinal, out PyObject device)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuDeviceGet", [ordinal], null);
            device = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Gets the name of a CUDA device (e.g. "NVIDIA H100 80GB HBM3").</summary>
    /// <param name="device">The device handle.</param>
    /// <param name="name">The device name string.</param>
    public static CudaError DeviceGetName(PyObject device, out string name)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuDeviceGetName", [1024, device], null);
            var pyName = result.GetItem(1);
            // cuDeviceGetName returns a Python `bytes` object, not `str`.
            name = pyName.IsNone() ? string.Empty : System.Text.Encoding.UTF8.GetString(pyName.As<byte[]>()).TrimEnd('\0');
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Queries an attribute of a CUDA device.</summary>
    /// <param name="attribute">The attribute to query.</param>
    /// <param name="device">The device handle.</param>
    /// <param name="value">The attribute value.</param>
    public static CudaError DeviceGetAttribute(CudaDeviceAttribute attribute, PyObject device, out long value)
        => CudaInvoke.InvokeDouble<long, CudaError>(Module, "cuDeviceGetAttribute", out value, out _,
            (object)(int)attribute, device);

    /// <summary>Queries the total global memory of a CUDA device (in bytes).</summary>
    public static CudaError DeviceTotalMem(PyObject device, out ulong totalMemory)
        => CudaInvoke.InvokeDouble<ulong, CudaError>(Module, "cuDeviceTotalMem", out totalMemory, out _, device);

    /// <summary>Gets the 8-byte UUID of a CUDA device.</summary>
    public static CudaError DeviceGetUuid(PyObject device, out byte[] uuid)
    {
        uuid = Array.Empty<byte>();
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuDeviceGetUuid", [device], null);
            var pyUuid = result.GetItem(1);
            if (!pyUuid.IsNone())
                uuid = pyUuid.As<byte[]>();
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    // ── Context Management ─────────────────────────────────────────────────

    /// <summary>Creates a new CUDA context on the specified device.</summary>
    /// <param name="flags">Context creation flags (0 for default).</param>
    /// <param name="device">The device handle.</param>
    /// <param name="context">The newly created context handle. Dispose when done.</param>
    public static CudaError CtxCreate(uint flags, PyObject device, out PyObject context)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuCtxCreate", [null, (object)flags, device], null);
            context = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Destroys a CUDA context previously created with <see cref="CtxCreate"/>.</summary>
    public static CudaError CtxDestroy(PyObject context)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuCtxDestroy", context);

    /// <summary>Synchronizes the current CUDA context (blocks until all prior work is complete).</summary>
    public static CudaError CtxSynchronize()
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuCtxSynchronize");

    /// <summary>Gets the current CUDA context handle (null if no context is current).</summary>
    public static CudaError CtxGetCurrent(out PyObject? context)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuCtxGetCurrent", [], null);
            var pyCtx = result.GetItem(1);
            context = pyCtx.IsNone() ? null : pyCtx;
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Gets the device ordinal associated with the current CUDA context.</summary>
    public static CudaError CtxGetDevice(out int device)
        => CudaInvoke.InvokeDouble<int, CudaError>(Module, "cuCtxGetDevice", out device, out _);

    /// <summary>Pushes a context onto the current thread's context stack.</summary>
    public static CudaError CtxPushCurrent(PyObject context)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuCtxPushCurrent", context);

    /// <summary>Pops the current context from the current thread's context stack.</summary>
    public static CudaError CtxPopCurrent(out PyObject? context)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuCtxPopCurrent", [], null);
            var pyCtx = result.GetItem(1);
            context = pyCtx.IsNone() ? null : pyCtx;
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Retains the primary context for a device (reference count +1).</summary>
    /// <param name="device">The device handle.</param>
    /// <param name="context">The primary context handle. Dispose when done.</param>
    public static CudaError DevicePrimaryCtxRetain(PyObject device, out PyObject context)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuDevicePrimaryCtxRetain", [device], null);
            context = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Releases the primary context for a device (reference count -1).</summary>
    public static CudaError DevicePrimaryCtxRelease(PyObject device)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuDevicePrimaryCtxRelease", device);

    /// <summary>Sets flags on the primary context for a device.</summary>
    public static CudaError DevicePrimaryCtxSetFlags(PyObject device, uint flags)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuDevicePrimaryCtxSetFlags", device, (object)flags);

    // ── Memory Management ──────────────────────────────────────────────────

    /// <summary>Allocates device memory on the current device.</summary>
    /// <param name="bytesize">Size in bytes.</param>
    /// <param name="devicePointer">The device memory pointer handle. Dispose when done.</param>
    public static CudaError MemAlloc(ulong bytesize, out PyObject devicePointer)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuMemAlloc", [bytesize], null);
            devicePointer = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Frees device memory previously allocated with <see cref="MemAlloc"/>.</summary>
    public static CudaError MemFree(PyObject devicePointer)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuMemFree", devicePointer);

    /// <summary>Allocates pinned host memory.</summary>
    /// <param name="bytesize">Size in bytes.</param>
    /// <param name="hostPointer">The pinned host memory pointer handle. Dispose when done.</param>
    public static CudaError MemAllocHost(ulong bytesize, out PyObject hostPointer)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuMemAllocHost", [bytesize], null);
            hostPointer = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Frees pinned host memory previously allocated with <see cref="MemAllocHost"/>.</summary>
    public static CudaError MemFreeHost(PyObject hostPointer)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuMemFreeHost", hostPointer);

    /// <summary>Copies data from host to device (synchronous).</summary>
    /// <param name="devicePointer">Destination device pointer.</param>
    /// <param name="hostData">Source host data (byte array).</param>
    /// <param name="bytesize">Size in bytes.</param>
    public static CudaError MemcpyHtoD(PyObject devicePointer, byte[] hostData, ulong bytesize)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuMemcpyHtoD", devicePointer, hostData, bytesize);

    /// <summary>Copies data from device to host (synchronous).</summary>
    /// <param name="hostData">Destination host buffer (byte array, must be large enough).</param>
    /// <param name="devicePointer">Source device pointer.</param>
    /// <param name="bytesize">Size in bytes.</param>
    public static CudaError MemcpyDtoH(byte[] hostData, PyObject devicePointer, ulong bytesize)
    {
        using (Py.GIL())
        {
            using var pyHost = PyObject.FromManagedObject(hostData);
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuMemcpyDtoH", [pyHost, devicePointer, bytesize], null);
            var filled = pyHost.As<byte[]>();
            Buffer.BlockCopy(filled, 0, hostData, 0, (int)Math.Min((ulong)filled.Length, bytesize));
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    // ── Module & Kernel ────────────────────────────────────────────────────

    /// <summary>Loads a CUDA module from a CUBIN image (byte array).</summary>
    /// <param name="cubin">The CUBIN binary image.</param>
    /// <param name="module">The loaded module handle. Dispose when done.</param>
    public static CudaError ModuleLoadData(byte[] cubin, out PyObject module)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuModuleLoadData", [cubin], null);
            module = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Loads a CUDA module from PTX source text (as produced by <see cref="Nvrtc.Nvrtc.GetPtx"/>).</summary>
    /// <param name="ptx">The PTX assembly text.</param>
    /// <param name="module">The loaded module handle. Dispose when done.</param>
    public static CudaError ModuleLoadData(string ptx, out PyObject module)
        => ModuleLoadData(System.Text.Encoding.UTF8.GetBytes(ptx), out module);

    /// <summary>Unloads a CUDA module previously loaded with <see cref="ModuleLoadData"/>.</summary>
    public static CudaError ModuleUnload(PyObject module)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuModuleUnload", module);

    /// <summary>Gets a kernel function from a loaded module.</summary>
    /// <param name="module">The loaded module handle.</param>
    /// <param name="funcName">Name of the kernel function in the module.</param>
    /// <param name="function">The kernel function handle. Dispose when done.</param>
    public static CudaError ModuleGetFunction(PyObject module, string funcName, out PyObject function)
    {
        using (Py.GIL())
        {
            using var nameBytes = CudaInvoke.ToPyBytes(System.Text.Encoding.UTF8.GetBytes(funcName));
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuModuleGetFunction", [module, nameBytes], null);
            function = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>
    /// Launches a kernel function.
    /// </summary>
    /// <param name="function">The kernel function handle (from <see cref="ModuleGetFunction"/>).</param>
    /// <param name="gridDimX">Grid dimensions X.</param>
    /// <param name="gridDimY">Grid dimensions Y.</param>
    /// <param name="gridDimZ">Grid dimensions Z.</param>
    /// <param name="blockDimX">Block dimensions X.</param>
    /// <param name="blockDimY">Block dimensions Y.</param>
    /// <param name="blockDimZ">Block dimensions Z.</param>
    /// <param name="sharedMemBytes">Shared memory per block (bytes).</param>
    /// <param name="stream">Stream to launch on (null for the default stream).</param>
    /// <param name="kernelArgs">
    /// Kernel parameter values, in the exact order declared by the kernel signature.
    /// Use <see cref="CudaKernelArg.DevicePointer(PyObject)"/> for pointer arguments
    /// (device buffers from <see cref="MemAlloc"/>) and the scalar factories
    /// (<see cref="CudaKernelArg.Int32"/>, <see cref="CudaKernelArg.Float"/>, etc.) for by-value arguments.
    /// </param>
    /// <remarks>
    /// Internally builds a native <c>ctypes</c> argument-pointer array (mirroring the standard
    /// CUDA driver API convention for <c>cuLaunchKernel</c>'s <c>kernelParams</c>), since the
    /// cuda-bindings Python wrapper expects a raw pointer to an array of per-argument pointers
    /// rather than a Python list of values. Verified empirically against a live GPU.
    /// </remarks>
    public static CudaError LaunchKernel(
        PyObject function,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes,
        PyObject? stream,
        params CudaKernelArg[] kernelArgs)
    {
        using (Py.GIL())
        {
            object streamArgument = (object?)stream ?? 0;

            if (kernelArgs is null || kernelArgs.Length == 0)
            {
                using var resultNoArgs = CudaInvoke.Runtime.Invoke(Module, "cuLaunchKernel",
                    [function, gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, streamArgument, 0, 0], null);
                return (CudaError)resultNoArgs.GetItem(0).As<int>();
            }

            using var specs = new PyList();
            foreach (var arg in kernelArgs)
            {
                using var kindPy = new PyString(arg.Kind);
                using var valuePy = PyObject.FromManagedObject(arg.Value);
                using var tuple = new PyTuple([kindPy, valuePy]);
                specs.Append(tuple);
            }

            using var helpers = KernelArgHelpers.Value;
            using var builder = helpers.GetItem("_cuda_dotnet_build_kernel_args");
            using var built = builder.Invoke(specs);
            using var kernelParamsPointer = built.GetItem(2);

            // `built` (which keeps the native argument array and boxed scalars alive) must remain
            // referenced until after cuLaunchKernel copies the parameters, hence disposed only below.
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuLaunchKernel",
                [function, gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, streamArgument, kernelParamsPointer, 0], null);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>
    /// Lazily-initialized Python globals dict hosting the small <c>ctypes</c>-based helper
    /// used by <see cref="LaunchKernel"/> to marshal kernel arguments (see remarks there).
    /// </summary>
    private static readonly Lazy<PyDict> KernelArgHelpers = new(() =>
    {
        using (Py.GIL())
        {
            var globals = new PyDict();
            PythonEngine.Exec(KernelArgBuilderScript, globals, globals);
            return globals;
        }
    });

    private const string KernelArgBuilderScript = """
        import ctypes as _ctypes

        def _cuda_dotnet_build_kernel_args(arg_specs):
            boxes = []
            for kind, value in arg_specs:
                if kind == 'u64':
                    box = _ctypes.c_uint64(value)
                elif kind == 'i32':
                    box = _ctypes.c_int32(value)
                elif kind == 'u32':
                    box = _ctypes.c_uint32(value)
                elif kind == 'i64':
                    box = _ctypes.c_int64(value)
                elif kind == 'f32':
                    box = _ctypes.c_float(value)
                elif kind == 'f64':
                    box = _ctypes.c_double(value)
                else:
                    raise ValueError('unknown kernel arg kind: ' + kind)
                boxes.append(box)
            n = len(boxes)
            arr_type = _ctypes.c_void_p * n
            arr = arr_type(*[_ctypes.cast(_ctypes.pointer(b), _ctypes.c_void_p) for b in boxes])
            ptr = _ctypes.cast(arr, _ctypes.c_void_p).value or 0
            return (arr, boxes, ptr)
        """;

    // ── Stream Management ──────────────────────────────────────────────────

    /// <summary>Creates a new stream.</summary>
    /// <param name="flags">Stream creation flags (0 for default).</param>
    /// <param name="stream">The newly created stream handle. Dispose when done.</param>
    public static CudaError StreamCreate(out PyObject stream, uint flags = 0)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuStreamCreate", [flags], null);
            stream = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Destroys a stream previously created with <see cref="StreamCreate"/>.</summary>
    public static CudaError StreamDestroy(PyObject stream)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuStreamDestroy", stream);

    /// <summary>Synchronizes a stream (blocks until all prior work on the stream is complete).</summary>
    public static CudaError StreamSynchronize(PyObject stream)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuStreamSynchronize", stream);

    /// <summary>Queries whether a stream has completed all prior work.</summary>
    /// <returns><see cref="CudaError.Success"/> if idle, <see cref="CudaError.NotReady"/> if work pending.</returns>
    public static CudaError StreamQuery(PyObject stream)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuStreamQuery", stream);

    // ── Event Management ───────────────────────────────────────────────────

    /// <summary>Creates a new CUDA event.</summary>
    /// <param name="flags">Event creation flags (0 for default).</param>
    /// <param name="event">The newly created event handle. Dispose when done.</param>
    public static CudaError EventCreate(out PyObject @event, uint flags = 0)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "cuEventCreate", [flags], null);
            @event = result.GetItem(1);
            return (CudaError)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Destroys a CUDA event previously created with <see cref="EventCreate"/>.</summary>
    public static CudaError EventDestroy(PyObject @event)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuEventDestroy", @event);

    /// <summary>Records an event on a stream.</summary>
    /// <param name="event">The event to record.</param>
    /// <param name="stream">The stream to record the event on.</param>
    public static CudaError EventRecord(PyObject @event, PyObject stream)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuEventRecord", @event, stream);

    /// <summary>Synchronizes a CUDA event (blocks until the event is complete).</summary>
    public static CudaError EventSynchronize(PyObject @event)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuEventSynchronize", @event);

    /// <summary>Queries whether a CUDA event has completed.</summary>
    /// <returns><see cref="CudaError.Success"/> if complete, <see cref="CudaError.NotReady"/> if pending.</returns>
    public static CudaError EventQuery(PyObject @event)
        => CudaInvoke.InvokeSingle<CudaError>(Module, "cuEventQuery", @event);

    /// <summary>Measures the elapsed time between two events (in milliseconds).</summary>
    /// <param name="start">Start event.</param>
    /// <param name="end">End event.</param>
    /// <param name="milliseconds">Elapsed time in ms.</param>
    public static CudaError EventElapsedTime(PyObject start, PyObject end, out float milliseconds)
        => CudaInvoke.InvokeDouble<float, CudaError>(Module, "cuEventElapsedTime", out milliseconds, out _, start, end);

    // ── Context Refresh (Windows-specific) ─────────────────────────────────

    /// <summary>
    /// Refreshes the CUDA context for a device by releasing and re-acquiring the primary context.
    /// </summary>
    /// <remarks>
    /// On Windows, the CUDA context can be lost after a system restart, driver reset,
    /// or GPU power state change. This method performs a best-effort recovery:
    /// <list type="number">
    /// <item><description>Releases the primary context (reference count -1)</description></item>
    /// <item><description>Resets flags to 0</description></item>
    /// <item><description>Re-acquires the primary context (reference count +1)</description></item>
    /// </list>
    /// If the device is truly gone (e.g., physically removed or driver failure),
    /// this will return an error code and the context cannot be recovered.
    /// </remarks>
    /// <param name="device">The device handle (from <see cref="DeviceGet"/>).</param>
    /// <returns><see cref="CudaError.Success"/> if the context was successfully refreshed.</returns>
    public static CudaError RefreshContext(PyObject device)
    {
        var releaseResult = DevicePrimaryCtxRelease(device);
        if (releaseResult != CudaError.Success)
            return DevicePrimaryCtxRetain(device, out _);

        var setFlagsResult = DevicePrimaryCtxSetFlags(device, 0);
        if (setFlagsResult != CudaError.Success)
            return setFlagsResult;

        return DevicePrimaryCtxRetain(device, out _);
    }
}
