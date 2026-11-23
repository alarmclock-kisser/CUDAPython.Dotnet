using global::Python.Runtime;
using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Bindings.Nvrtc;

/// <summary>
/// Curated, statically-typed bindings for the <c>cuda.bindings.nvrtc</c> module (NVIDIA Runtime Compiler).
/// Compile CUDA C++ kernel source to PTX or CUBIN at runtime.
/// </summary>
/// <remarks>
/// All methods are GIL-scoped internally. Call <see cref="CudaPython.Initialize"/> once per process.
/// </remarks>
public static class Nvrtc
{
    private const string Module = "cuda.bindings.nvrtc";

    /// <summary>Queries the NVRTC version (major, minor).</summary>
    public static (int Major, int Minor) Version()
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "nvrtcVersion", [], null);
            return (result.GetItem(1).As<int>(), result.GetItem(2).As<int>());
        }
    }

    /// <summary>Gets a human-readable error string for an NVRTC result code.</summary>
    public static string GetErrorString(NvrtcResult code)
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetErrorString", [(int)code], null);
            var pyString = result.GetItem(1);
            // nvrtcGetErrorString returns a Python `bytes` object, not `str`.
            return pyString.IsNone() ? string.Empty : System.Text.Encoding.UTF8.GetString(pyString.As<byte[]>()).TrimEnd('\0');
        }
    }

    /// <summary>Creates a new NVRTC program from CUDA C++ source code.</summary>
    /// <param name="name">Program name (for diagnostics, e.g. "kernel.cu").</param>
    /// <param name="source">CUDA C++ source code.</param>
    /// <param name="program">The created program handle. Call <see cref="DestroyProgram"/> when done.</param>
    public static NvrtcResult CreateProgram(string name, string source, out PyObject program)
    {
        using (Py.GIL())
        {
            using var sourceBytes = CudaInvoke.ToPyBytes(System.Text.Encoding.UTF8.GetBytes(source));
            using var nameBytes = CudaInvoke.ToPyBytes(System.Text.Encoding.UTF8.GetBytes(name));
            using var result = CudaInvoke.Runtime.Invoke(
                Module, "nvrtcCreateProgram", [sourceBytes, nameBytes, 0, null, null], null);
            program = result.GetItem(1);
            return (NvrtcResult)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Compiles a previously created NVRTC program with the specified options.</summary>
    /// <param name="program">The program handle from <see cref="CreateProgram"/>.</param>
    /// <param name="options">Compiler options (e.g. ["-arch=sm_80", "--use_fast_math"]). Null for no options.</param>
    public static NvrtcResult CompileProgram(PyObject program, string[]? options = null)
    {
        var count = options?.Length ?? 0;
        return CudaInvoke.InvokeSingle<NvrtcResult>(
            Module, "nvrtcCompileProgram", program, count, (object?)options ?? System.Array.Empty<string>());
    }

    /// <summary>Gets the compiled PTX from a program.</summary>
    /// <param name="program">The compiled program handle.</param>
    /// <param name="ptx">The PTX string (empty if no PTX was produced).</param>
    public static NvrtcResult GetPtx(PyObject program, out string ptx)
    {
        ptx = string.Empty;
        using (Py.GIL())
        {
            using var sizeResult = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetPTXSize", [program], null);
            var code = (NvrtcResult)sizeResult.GetItem(0).As<int>();
            var size = sizeResult.GetItem(1).As<ulong>();
            if (code != NvrtcResult.Success || size == 0)
                return code;

            using var pyBuf = CudaInvoke.NewPyByteArray((int)size);
            using var result = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetPTX", [program, pyBuf], null);
            var filled = pyBuf.As<byte[]>();
            ptx = System.Text.Encoding.UTF8.GetString(filled, 0, (int)size).TrimEnd('\0');
            return (NvrtcResult)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Gets the compiled CUBIN from a program.</summary>
    /// <param name="program">The compiled program handle.</param>
    /// <param name="cubin">The CUBIN byte array (empty if no CUBIN was produced).</param>
    public static NvrtcResult GetCubin(PyObject program, out byte[] cubin)
    {
        cubin = System.Array.Empty<byte>();
        using (Py.GIL())
        {
            using var sizeResult = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetCUBINSize", [program], null);
            var code = (NvrtcResult)sizeResult.GetItem(0).As<int>();
            var size = sizeResult.GetItem(1).As<ulong>();
            if (code != NvrtcResult.Success || size == 0)
                return code;

            using var pyBuf = CudaInvoke.NewPyByteArray((int)size);
            using var result = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetCUBIN", [program, pyBuf], null);
            cubin = pyBuf.As<byte[]>();
            return (NvrtcResult)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Gets the compiler log (diagnostics) from a program.</summary>
    /// <param name="program">The compiled program handle.</param>
    /// <param name="log">The compiler log string (empty if no log was produced).</param>
    public static NvrtcResult GetProgramLog(PyObject program, out string log)
    {
        log = string.Empty;
        using (Py.GIL())
        {
            using var sizeResult = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetProgramLogSize", [program], null);
            var code = (NvrtcResult)sizeResult.GetItem(0).As<int>();
            var size = sizeResult.GetItem(1).As<ulong>();
            if (code != NvrtcResult.Success || size <= 1)
                return code;

            using var pyBuf = CudaInvoke.NewPyByteArray((int)size);
            using var result = CudaInvoke.Runtime.Invoke(Module, "nvrtcGetProgramLog", [program, pyBuf], null);
            var filled = pyBuf.As<byte[]>();
            log = System.Text.Encoding.UTF8.GetString(filled, 0, (int)size).TrimEnd('\0');
            return (NvrtcResult)result.GetItem(0).As<int>();
        }
    }

    /// <summary>Destroys an NVRTC program and frees its resources.</summary>
    public static NvrtcResult DestroyProgram(PyObject program)
        => CudaInvoke.InvokeSingle<NvrtcResult>(Module, "nvrtcDestroyProgram", program);
}

/// <summary>NVIDIA Runtime Compiler result codes (mirror of nvrtcResult).</summary>
public enum NvrtcResult
{
    Success = 0,
    ErrorOutOfMemory = 1,
    ErrorProgramCreationFailure = 2,
    ErrorInvalidInput = 3,
    ErrorInvalidProgram = 4,
    ErrorInvalidOption = 5,
    ErrorCompilation = 6,
    ErrorBuiltinOperationFailure = 7,
    ErrorNoNameExpressionsAfterCompilation = 8,
    ErrorNoLoweredNamesBeforeCompilation = 9,
    ErrorNameExpressionNotValid = 10,
    ErrorInternalError = 11,
    ErrorTimeFileWriteFailed = 12,
    ErrorNoPchCreateAttempted = 13,
    ErrorPchCreateHeapExhausted = 14,
    ErrorPchCreate = 15,
    ErrorCancelled = 16,
    ErrorTimeTraceFileWriteFailed = 17,
    ErrorBusy = 18,
    ErrorUnknown = 10000
}
