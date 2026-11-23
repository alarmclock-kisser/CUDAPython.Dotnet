using global::Python.Runtime;
using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Bindings.Nvml;

/// <summary>
/// Curated bindings for the NVIDIA Management Library (NVML) via <c>cuda.bindings.nvml</c>.
/// </summary>
/// <remarks>
/// <para>
/// NVML's Python surface differs from the CUDA Driver/NVRTC bindings: instead of returning
/// <c>(status, ...)</c> tuples, NVML functions return bare values directly and raise a Python
/// exception (e.g. <c>UninitializedError</c>) on failure. Those exceptions are surfaced here as
/// <see cref="CudaPythonException"/> by the underlying <see cref="CudaPythonRuntime"/>.
/// Device handles are raw pointer values (<c>intptr_t</c>), represented as <see cref="long"/>.
/// Call <see cref="Init"/> once before using any other method, and <see cref="Shutdown"/> when done.
/// </para>
/// <para>
/// Unlike the driver/NVRTC bindings, NVML's Cython wrapper performs strict <c>isinstance</c>
/// checks on its arguments, so every numeric argument here is routed through
/// <see cref="CudaInvoke.ToPyInt"/> to guarantee a genuine Python <c>int</c> (not a CLR-wrapped
/// <c>System.Int64</c>) is sent across the boundary. See that helper's remarks for details.
/// </para>
/// </remarks>
public static class Nvml
{
    private const string Module = "cuda.bindings.nvml";

    /// <summary>Initializes the NVML library. Must be called before any other NVML function.</summary>
    public static void Init()
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "init_v2", [], null);
        }
    }

    /// <summary>Shuts down NVML and releases allocated resources.</summary>
    public static void Shutdown()
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "shutdown", [], null);
        }
    }

    /// <summary>Returns the number of NVML-compatible devices in the system.</summary>
    public static int GetDeviceCount()
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_count_v2", [], null);
            return result.As<int>();
        }
    }

    /// <summary>Returns an opaque device handle for the given zero-based device index.</summary>
    public static long GetHandleByIndex(int index)
    {
        using (Py.GIL())
        {
            using var pyIndex = CudaInvoke.ToPyInt(index);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_handle_by_index_v2", [pyIndex], null);
            return result.As<long>();
        }
    }

    /// <summary>Returns the product name of the device (e.g. "NVIDIA GeForce RTX 4090").</summary>
    public static string GetName(long deviceHandle)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_name", [pyHandle], null);
            return result.As<string>();
        }
    }

    /// <summary>Returns the device's globally unique UUID string (e.g. "GPU-xxxxxxxx-...").</summary>
    public static string GetUuid(long deviceHandle)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_uuid", [pyHandle], null);
            return result.As<string>();
        }
    }

    /// <summary>Returns the device's total, free and used memory, in bytes.</summary>
    public static (ulong TotalBytes, ulong FreeBytes, ulong UsedBytes) GetMemoryInfo(long deviceHandle)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_memory_info_v2", [pyHandle], null);
            using var total = result.GetAttr("total");
            using var free = result.GetAttr("free");
            using var used = result.GetAttr("used");
            return (total.As<ulong>(), free.As<ulong>(), used.As<ulong>());
        }
    }

    /// <summary>Returns the device's current GPU and memory-controller utilization, in percent (0-100).</summary>
    public static (uint GpuPercent, uint MemoryPercent) GetUtilizationRates(long deviceHandle)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_utilization_rates", [pyHandle], null);
            using var gpu = result.GetAttr("gpu");
            using var memory = result.GetAttr("memory");
            return (gpu.As<uint>(), memory.As<uint>());
        }
    }

    /// <summary>Returns the device's current temperature reading, in degrees Celsius.</summary>
    public static int GetTemperatureCelsius(long deviceHandle, NvmlTemperatureSensor sensor = NvmlTemperatureSensor.Gpu)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var pySensor = CudaInvoke.ToPyInt((int)sensor);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_temperature_v", [pyHandle, pySensor], null);
            return result.As<int>();
        }
    }

    /// <summary>Returns the device's current power draw, in milliwatts.</summary>
    public static uint GetPowerUsageMilliwatts(long deviceHandle)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_power_usage", [pyHandle], null);
            return result.As<uint>();
        }
    }

    /// <summary>Returns a device clock domain reading, in MHz.</summary>
    public static uint GetClockMHz(long deviceHandle, NvmlClockType clockType, NvmlClockId clockId = NvmlClockId.Current)
    {
        using (Py.GIL())
        {
            using var pyHandle = CudaInvoke.ToPyInt(deviceHandle);
            using var pyClockType = CudaInvoke.ToPyInt((int)clockType);
            using var pyClockId = CudaInvoke.ToPyInt((int)clockId);
            using var result = CudaInvoke.Runtime.Invoke(Module, "device_get_clock", [pyHandle, pyClockType, pyClockId], null);
            return result.As<uint>();
        }
    }

    /// <summary>Returns the installed NVIDIA display driver version string (e.g. "582.70").</summary>
    public static string GetDriverVersion()
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "system_get_driver_version", [], null);
            return result.As<string>();
        }
    }

    /// <summary>Returns the max CUDA version supported by the installed driver (e.g. 13000 for CUDA 13.0).</summary>
    public static int GetCudaDriverVersion()
    {
        using (Py.GIL())
        {
            using var result = CudaInvoke.Runtime.Invoke(Module, "system_get_cuda_driver_version", [], null);
            return result.As<int>();
        }
    }
}
