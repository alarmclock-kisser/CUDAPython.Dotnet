using global::Python.Runtime;
using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Bindings;

/// <summary>
/// Static entry point for the curated CUDA Python bindings.
/// Call <see cref="Initialize"/> once per process before using any binding.
/// </summary>
public static class CudaPython
{
    /// <summary>Initializes the embedded CPython environment (once per process).</summary>
    public static void Initialize() => CudaPythonRuntime.Initialize();

    /// <summary>Whether the runtime has been initialized in this process.</summary>
    public static bool IsInitialized => PythonEngine.IsInitialized;
}

/// <summary>
/// Thin invoke layer bridging C# typed calls to the Python <c>cuda.bindings.*</c> modules.
/// All methods are GIL-scoped internally.
/// </summary>
/// <remarks>
/// <para>
/// Most <c>cuda.bindings</c> Python functions return a <b>tuple</b>: either
/// <c>(status,)</c> for void-returning calls, or <c>(status, value)</c> / <c>(status, value1, value2, ...)</c>
/// for calls with out-parameters. Use <see cref="InvokeSingle{TResult}"/> for the former
/// and <see cref="InvokeDouble{TValue, TStatus}"/> for the latter.
/// </para>
/// </remarks>
internal static class CudaInvoke
{
    private static CudaPythonRuntime? _runtime;

    internal static CudaPythonRuntime Runtime =>
        _runtime ??= CudaPythonRuntime.Initialize()
            ?? throw new InvalidOperationException(
                "CudaPythonRuntime is not initialized. Call CudaPython.Initialize() first.");

    private static PyObject? _builtinsBytes;
    private static PyObject? _builtinsBytearray;

    /// <summary>
    /// Converts a C# byte array into a genuine Python <c>bytes</c> object.
    /// </summary>
    /// <remarks>
    /// <see cref="PyObject.FromManagedObject(object)"/> wraps <c>byte[]</c> as a raw CLR-interop
    /// object (exposed to Python as <c>System.Byte[]</c>), not a native Python <c>bytes</c>/<c>bytearray</c>.
    /// That CLR wrapper supports the buffer protocol (enough for e.g. <c>cuMemcpyHtoD</c>/<c>cuMemcpyDtoH</c>,
    /// which only need raw memory access), but some cuda-bindings functions (e.g. <c>nvrtcCreateProgram</c>)
    /// perform a strict <c>isinstance(x, bytes)</c> check and reject it. Use this helper for those cases.
    /// </remarks>
    internal static PyObject ToPyBytes(byte[] data)
    {
        using (Py.GIL())
        {
            _builtinsBytes ??= Py.Import("builtins").GetAttr("bytes");
            using var pyByteArray = PyObject.FromManagedObject(data);
            return _builtinsBytes.Invoke(pyByteArray);
        }
    }

    /// <summary>
    /// Creates a genuine, zero-filled, writable Python <c>bytearray</c> of the given size.
    /// </summary>
    /// <remarks>
    /// Use this (not <see cref="PyObject.FromManagedObject(object)"/>) as an out-buffer for
    /// cuda-bindings functions that strictly type-check their buffer argument (e.g.
    /// <c>nvrtcGetPTX</c>, <c>nvrtcGetCUBIN</c>, <c>nvrtcGetProgramLog</c>). After the call,
    /// read the written bytes back with <see cref="PyObject.As{T}"/> (<c>byte[]</c>).
    /// </remarks>
    internal static PyObject NewPyByteArray(int size)
    {
        using (Py.GIL())
        {
            _builtinsBytearray ??= Py.Import("builtins").GetAttr("bytearray");
            return _builtinsBytearray.Invoke(new PyInt(size));
        }
    }

    private static PyObject? _builtinsInt;

    /// <summary>
    /// Converts an opaque handle object (e.g. a <c>CUdeviceptr</c>) to its raw numeric address.
    /// </summary>
    /// <remarks>
    /// cuda-bindings handle wrapper classes (<c>CUdeviceptr</c>, <c>CUcontext</c>, ...) implement
    /// Python's <c>__index__</c>/<c>__int__</c> protocol but are not native Python <c>int</c>
    /// instances, so <see cref="PyObject.As{T}"/> cannot convert them directly. Route the value
    /// through Python's <c>int()</c> builtin first.
    /// </remarks>
    internal static ulong ToUInt64(PyObject handle)
    {
        using (Py.GIL())
        {
            _builtinsInt ??= Py.Import("builtins").GetAttr("int");
            using var pyInt = _builtinsInt.Invoke(handle);
            return pyInt.As<ulong>();
        }
    }

    /// <summary>
    /// Converts a C# integer to a genuine Python <c>int</c> object.
    /// </summary>
    /// <remarks>
    /// <see cref="PyObject.FromManagedObject(object)"/> wraps C# integers (<c>int</c>, <c>long</c>, ...)
    /// as raw CLR-interop objects (exposed to Python as e.g. <c>System.Int64</c>), not native Python
    /// <c>int</c> instances. Most cuda-bindings functions tolerate this via duck typing, but some
    /// (notably <c>cuda.bindings.nvml</c>, which performs strict <c>isinstance(x, int)</c> checks)
    /// reject it with <c>TypeError: an integer is required</c>. Use this helper for those cases.
    /// </remarks>
    internal static PyObject ToPyInt(long value)
    {
        using (Py.GIL())
        {
            return new PyInt(value);
        }
    }

    /// <summary>
    /// Invokes a Python function and returns the raw result as <typeparamref name="TResult"/>.
    /// Use this only for functions that do NOT return a <c>cuda.bindings</c>-style status tuple.
    /// </summary>
    public static TResult Invoke<TResult>(string module, string function, params object[] arguments)
    {
        using (Py.GIL())
        {
            using var result = Runtime.Invoke(module, function, arguments, null);
            return ConvertResult<TResult>(result);
        }
    }

    /// <summary>
    /// Invokes a function that returns a single-element tuple <c>(status,)</c>.
    /// Returns the status as <typeparamref name="TResult"/>.
    /// </summary>
    public static TResult InvokeSingle<TResult>(string module, string function, params object[] arguments)
    {
        using (Py.GIL())
        {
            using var result = Runtime.Invoke(module, function, arguments, null);
            return ConvertResult<TResult>(result.GetItem(0));
        }
    }

    /// <summary>
    /// Invokes a function that returns a two-element tuple <c>(status, value)</c>.
    /// Returns the status as <typeparamref name="TStatus"/> and the value as <typeparamref name="TValue"/>.
    /// </summary>
    public static TStatus InvokeDouble<TValue, TStatus>(
        string module, string function,
        out TValue value, out TStatus status,
        params object[] arguments)
    {
        using (Py.GIL())
        {
            using var result = Runtime.Invoke(module, function, arguments, null);
            value = result.Length() > 1 ? ConvertResult<TValue>(result.GetItem(1)) : default!;
            status = ConvertResult<TStatus>(result.GetItem(0));
            return status;
        }
    }

    private static TResult ConvertResult<TResult>(PyObject result)
    {
        if (typeof(TResult) == typeof(void))
            return default!;

        if (typeof(TResult) == typeof(PyObject))
            return (TResult)(object)result;

        // int / uint / long / ulong — call PyObject.As<T>() per exact target type;
        // a boxed int cannot be unboxed directly as uint/long/ulong (runtime type mismatch).
        if (typeof(TResult) == typeof(int))
            return (TResult)(object)result.As<int>();
        if (typeof(TResult) == typeof(uint))
            return (TResult)(object)result.As<uint>();
        if (typeof(TResult) == typeof(long))
            return (TResult)(object)result.As<long>();
        if (typeof(TResult) == typeof(ulong))
            return (TResult)(object)result.As<ulong>();

        // float / double
        if (typeof(TResult) == typeof(float))
            return (TResult)(object)result.As<float>();
        if (typeof(TResult) == typeof(double))
            return (TResult)(object)result.As<double>();

        // string
        if (typeof(TResult) == typeof(string))
        {
            return (TResult)(object)result.As<string>();
        }

        // bool
        if (typeof(TResult) == typeof(bool))
        {
            return (TResult)(object)result.As<bool>();
        }

        // enum (C# enum backed by int)
        if (typeof(TResult).IsEnum)
        {
            return (TResult)Enum.ToObject(typeof(TResult), result.As<int>());
        }

        // PyObject passthrough
        return (TResult)(object)result;
    }
}
