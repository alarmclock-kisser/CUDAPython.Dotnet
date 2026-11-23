using System.Text;
using CUDAPython.Dotnet.Bindings;
using CUDAPython.Dotnet.Bindings.Driver;
using CUDAPython.Dotnet.Bindings.Nvml;
using CUDAPython.Dotnet.Bindings.Nvrtc;
using CUDAPython.Dotnet.Python;
using global::Python.Runtime;

namespace CUDAPython.Dotnet.Tests;

/// <summary>
/// End-to-end integration tests for the curated <c>CUDAPython.Dotnet.Bindings</c> API.
/// These exercise the real embedded CPython + cuda-bindings stack and require a CUDA-capable GPU
/// with a working driver. Tests are skipped (via <see cref="Assert.Inconclusive(string)"/>) when no
/// device is available, so the suite still passes in CPU-only CI environments.
/// </summary>
[TestClass]
public sealed class CudaBindingsIntegrationTests
{
    private static bool TryInitDriver(out int deviceCount)
    {
        CudaPython.Initialize();
        CudaDriver.Init();
        var status = CudaDriver.DeviceGetCount(out deviceCount);
        return status == CudaError.Success && deviceCount > 0;
    }

    [TestMethod]
    public void DriverGetVersion_ReturnsPlausibleVersion()
    {
        CudaPython.Initialize();
        var status = CudaDriver.Init();
        Assert.AreEqual(CudaError.Success, status);

        status = CudaDriver.DriverGetVersion(out var version);
        Assert.AreEqual(CudaError.Success, status);
        Assert.IsTrue(version >= 11000, $"Expected a CUDA 11+ driver version, got {version}.");
    }

    [TestMethod]
    public void DeviceQuery_ReturnsNameAndMemory()
    {
        if (!TryInitDriver(out var deviceCount))
        {
            Assert.Inconclusive("No CUDA-capable device available in this environment.");
            return;
        }

        Assert.IsTrue(deviceCount > 0);

        // PyObject handles (device, etc.) must be disposed while holding the GIL;
        // wrap the whole scope so the `using` disposals at block-exit are GIL-safe.
        using (Py.GIL())
        {
            var status = CudaDriver.DeviceGet(0, out var device);
            Assert.AreEqual(CudaError.Success, status);
            using (device)
            {
                status = CudaDriver.DeviceGetName(device, out var name);
                Assert.AreEqual(CudaError.Success, status);
                Assert.IsFalse(string.IsNullOrWhiteSpace(name));

                status = CudaDriver.DeviceTotalMem(device, out var totalMemory);
                Assert.AreEqual(CudaError.Success, status);
                Assert.IsTrue(totalMemory > 0);
            }
        }
    }

    [TestMethod]
    public void MemAllocFreeAndMemcpyRoundTrip_PreservesData()
    {
        if (!TryInitDriver(out _))
        {
            Assert.Inconclusive("No CUDA-capable device available in this environment.");
            return;
        }

        var status = CudaDriver.DeviceGet(0, out var device);
        Assert.AreEqual(CudaError.Success, status);
        using (Py.GIL())
        using (device)
        {
            status = CudaDriver.CtxCreate(0, device, out var context);
            Assert.AreEqual(CudaError.Success, status);
            using (context)
            {
                var hostIn = Encoding.UTF8.GetBytes("CUDAPython.Dotnet round-trip!!");
                status = CudaDriver.MemAlloc((ulong)hostIn.Length, out var devicePointer);
                Assert.AreEqual(CudaError.Success, status);
                using (devicePointer)
                {
                    status = CudaDriver.MemcpyHtoD(devicePointer, hostIn, (ulong)hostIn.Length);
                    Assert.AreEqual(CudaError.Success, status);

                    var hostOut = new byte[hostIn.Length];
                    status = CudaDriver.MemcpyDtoH(hostOut, devicePointer, (ulong)hostIn.Length);
                    Assert.AreEqual(CudaError.Success, status);

                    CollectionAssert.AreEqual(hostIn, hostOut);

                    status = CudaDriver.MemFree(devicePointer);
                    Assert.AreEqual(CudaError.Success, status);
                }

                status = CudaDriver.CtxDestroy(context);
                Assert.AreEqual(CudaError.Success, status);
            }
        }
    }

    [TestMethod]
    public void NvrtcCompileAndLaunchKernel_ComputesVectorAdd()
    {
        if (!TryInitDriver(out _))
        {
            Assert.Inconclusive("No CUDA-capable device available in this environment.");
            return;
        }

        const string source = """
            extern "C" __global__ void vector_add(float *a, float *b, float *c, int n)
            {
                int i = threadIdx.x;
                if (i < n) c[i] = a[i] + b[i];
            }
            """;

        var nvrtcStatus = Nvrtc.CreateProgram("vector_add.cu", source, out var program);
        Assert.AreEqual(NvrtcResult.Success, nvrtcStatus);
        using (Py.GIL())
        using (program)
        {
            nvrtcStatus = Nvrtc.CompileProgram(program);
            if (nvrtcStatus != NvrtcResult.Success)
            {
                Nvrtc.GetProgramLog(program, out var log);
                Assert.Fail($"NVRTC compilation failed ({nvrtcStatus}): {log}");
            }

            nvrtcStatus = Nvrtc.GetPtx(program, out var ptx);
            Assert.AreEqual(NvrtcResult.Success, nvrtcStatus);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ptx));

            var driverStatus = CudaDriver.DeviceGet(0, out var device);
            Assert.AreEqual(CudaError.Success, driverStatus);
            using (device)
            {
                driverStatus = CudaDriver.CtxCreate(0, device, out var context);
                Assert.AreEqual(CudaError.Success, driverStatus);
                using (context)
                {
                    driverStatus = CudaDriver.ModuleLoadData(ptx, out var module);
                    Assert.AreEqual(CudaError.Success, driverStatus);
                    using (module)
                    {
                        driverStatus = CudaDriver.ModuleGetFunction(module, "vector_add", out var function);
                        Assert.AreEqual(CudaError.Success, driverStatus);
                        using (function)
                        {
                            const int n = 16;
                            var a = new float[n];
                            var b = new float[n];
                            for (var i = 0; i < n; i++) { a[i] = 1f; b[i] = 2f; }
                            var aBytes = new byte[n * sizeof(float)];
                            var bBytes = new byte[n * sizeof(float)];
                            Buffer.BlockCopy(a, 0, aBytes, 0, aBytes.Length);
                            Buffer.BlockCopy(b, 0, bBytes, 0, bBytes.Length);

                            CudaDriver.MemAlloc((ulong)aBytes.Length, out var da);
                            CudaDriver.MemAlloc((ulong)bBytes.Length, out var db);
                            CudaDriver.MemAlloc((ulong)aBytes.Length, out var dc);
                            using (da) using (db) using (dc)
                            {
                                CudaDriver.MemcpyHtoD(da, aBytes, (ulong)aBytes.Length);
                                CudaDriver.MemcpyHtoD(db, bBytes, (ulong)bBytes.Length);

                                driverStatus = CudaDriver.LaunchKernel(
                                    function,
                                    1, 1, 1,
                                    n, 1, 1,
                                    0,
                                    null,
                                    CudaKernelArg.DevicePointer(da),
                                    CudaKernelArg.DevicePointer(db),
                                    CudaKernelArg.DevicePointer(dc),
                                    CudaKernelArg.Int32(n));
                                Assert.AreEqual(CudaError.Success, driverStatus);

                                driverStatus = CudaDriver.CtxSynchronize();
                                Assert.AreEqual(CudaError.Success, driverStatus);

                                var cBytes = new byte[aBytes.Length];
                                driverStatus = CudaDriver.MemcpyDtoH(cBytes, dc, (ulong)cBytes.Length);
                                Assert.AreEqual(CudaError.Success, driverStatus);

                                var c = new float[n];
                                Buffer.BlockCopy(cBytes, 0, c, 0, cBytes.Length);
                                for (var i = 0; i < n; i++)
                                {
                                    Assert.AreEqual(3f, c[i], 1e-6f, $"Mismatch at index {i}.");
                                }
                            }
                        }
                    }

                    driverStatus = CudaDriver.CtxDestroy(context);
                    Assert.AreEqual(CudaError.Success, driverStatus);
                }
            }
        }
    }

    [TestMethod]
    public void Nvml_ReportsDeviceInfo()
    {
        try
        {
            CudaPython.Initialize();
            Nvml.Init();
        }
        catch (CudaPythonException)
        {
            Assert.Inconclusive("NVML is not available in this environment.");
            return;
        }

        try
        {
            var count = Nvml.GetDeviceCount();
            if (count == 0)
            {
                Assert.Inconclusive("No NVML-visible devices in this environment.");
                return;
            }

            var handle = Nvml.GetHandleByIndex(0);
            var name = Nvml.GetName(handle);
            Assert.IsFalse(string.IsNullOrWhiteSpace(name));

            var (total, free, used) = Nvml.GetMemoryInfo(handle);
            Assert.IsTrue(total > 0);
            Assert.IsTrue(free + used <= total + 1024); // small slack for rounding

            var driverVersion = Nvml.GetDriverVersion();
            Assert.IsFalse(string.IsNullOrWhiteSpace(driverVersion));
        }
        finally
        {
            Nvml.Shutdown();
        }
    }
}
