using CUDAPython.Dotnet.Python;

namespace CUDAPython.Dotnet.Tests;

[TestClass]
public sealed class CudaPythonRuntimeOptionsTests
{
    [TestMethod]
    public void LoadFromFile_ReadsGeneratedRuntimeConfiguration()
    {
        var configurationPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(configurationPath, """
                {
                  "pythonDllPath": "C:\\Python311\\python311.dll",
                  "pythonHome": "C:\\Python311",
                  "pythonPath": "C:\\Python311\\Lib",
                  "environmentPath": "C:\\AppData\\python"
                }
                """);

            var options = CudaPythonRuntimeOptions.LoadFromFile(configurationPath);

            Assert.AreEqual("C:\\Python311\\python311.dll", options.PythonDllPath);
            Assert.AreEqual("C:\\Python311", options.PythonHome);
            Assert.AreEqual("C:\\Python311\\Lib", options.PythonPath);
            Assert.AreEqual("cuda", options.PackageRootModule);
        }
        finally
        {
            File.Delete(configurationPath);
        }
    }

    [TestMethod]
    public void LoadFromFile_ResolvesPathsRelativeToRuntimeConfiguration()
    {
        var directory = Directory.CreateTempSubdirectory();
        var configurationPath = Path.Combine(directory.FullName, "runtime.json");
        try
        {
            File.WriteAllText(configurationPath, """
                {
                  "pythonDllPath": "python/python311.dll",
                  "pythonHome": "python",
                  "pythonPath": "python/Lib;python/DLLs",
                  "runtimeId": "win-x64"
                }
                """);

            var options = CudaPythonRuntimeOptions.LoadFromFile(configurationPath);

            Assert.AreEqual(Path.GetFullPath(Path.Combine(directory.FullName, "python", "python311.dll")), options.PythonDllPath);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(directory.FullName, "python")), options.PythonHome);
            Assert.AreEqual(
                string.Join(
                    Path.PathSeparator,
                    Path.GetFullPath(Path.Combine(directory.FullName, "python", "Lib")),
                    Path.GetFullPath(Path.Combine(directory.FullName, "python", "DLLs"))),
                options.PythonPath);
            Assert.AreEqual("win-x64", options.RuntimeId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
