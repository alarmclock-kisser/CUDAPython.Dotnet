using global::Python.Runtime;

namespace CUDAPython.Dotnet.Bindings.Driver;

/// <summary>
/// A single kernel launch argument, used with <see cref="CudaDriver.LaunchKernel"/>.
/// </summary>
/// <remarks>
/// CUDA kernels receive arguments by value, copied from host memory at launch time
/// (see the CUDA driver API documentation for <c>cuLaunchKernel</c>). Device memory
/// pointers must be passed as their raw 64-bit address (use <see cref="DevicePointer(PyObject)"/>).
/// </remarks>
public readonly struct CudaKernelArg
{
    internal string Kind { get; }
    internal object Value { get; }

    private CudaKernelArg(string kind, object value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>A device memory pointer argument (e.g. from <see cref="CudaDriver.MemAlloc"/>).</summary>
    public static CudaKernelArg DevicePointer(PyObject pointer)
        => new("u64", CUDAPython.Dotnet.Bindings.CudaInvoke.ToUInt64(pointer));

    /// <summary>A raw device memory address argument.</summary>
    public static CudaKernelArg DevicePointer(ulong rawAddress) => new("u64", rawAddress);

    /// <summary>A 32-bit signed integer argument.</summary>
    public static CudaKernelArg Int32(int value) => new("i32", value);

    /// <summary>A 32-bit unsigned integer argument.</summary>
    public static CudaKernelArg UInt32(uint value) => new("u32", value);

    /// <summary>A 64-bit signed integer argument.</summary>
    public static CudaKernelArg Int64(long value) => new("i64", value);

    /// <summary>A 64-bit unsigned integer argument.</summary>
    public static CudaKernelArg UInt64(ulong value) => new("u64", value);

    /// <summary>A 32-bit floating point argument.</summary>
    public static CudaKernelArg Float(float value) => new("f32", value);

    /// <summary>A 64-bit floating point (double) argument.</summary>
    public static CudaKernelArg Double(double value) => new("f64", value);
}
