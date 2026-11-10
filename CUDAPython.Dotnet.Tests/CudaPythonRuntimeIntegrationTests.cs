using CUDAPython.Dotnet.Python;
using Python.Runtime;

namespace CUDAPython.Dotnet.Tests;

[TestClass]
public sealed class CudaPythonRuntimeIntegrationTests
{
    [TestMethod]
    public void DiscoverFunctionsAndInvoke_UsesEmbeddedPythonRuntime()
    {
        var runtime = CudaPythonRuntime.Initialize();
        var functions = runtime.DiscoverFunctions();

        Assert.IsTrue(functions.Any(item => item.PythonQualifiedName == "cuda.bindings.driver.cuInit"));
        using (Py.GIL())
        using (var result = runtime.Invoke("math", "sqrt", [9d]))
        {
            Assert.AreEqual(3d, result.As<double>(), 1e-12);
        }
    }
}
