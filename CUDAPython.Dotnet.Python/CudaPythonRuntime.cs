using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Python.Runtime;

namespace CUDAPython.Dotnet.Python;

/// <summary>Hosts Python.NET and discovers or invokes CUDA Python callables.</summary>
/// <remarks>
/// Python.NET initializes CPython process-wide. The runtime cannot be reconfigured or shut down safely;
/// initialize it once and keep the configured Python installation and package set stable for the process lifetime.
/// </remarks>
public sealed class CudaPythonRuntime
{
    private const string DiscoveryScript = """
        import importlib
        import inspect
        import enum
        import base64
        import json
        import operator
        import pkgutil

        def _cuda_dotnet_discover(root_name, include_submodules):
            root = importlib.import_module(root_name)
            module_names = [root.__name__]
            if include_submodules and hasattr(root, "__path__"):
                module_names.extend(
                    item.name
                    for item in pkgutil.walk_packages(root.__path__, root.__name__ + ".")
                    if not any(part.startswith("_") for part in item.name.split(".")[1:])
                )

            functions = []

            def add_function(module, name, function):
                try:
                    signature = str(inspect.signature(function))
                except (TypeError, ValueError):
                    signature = None

                functions.append({
                    "moduleName": module.__name__,
                    "name": name,
                    "signature": signature,
                    "documentation": inspect.getdoc(function)
                })

            for module_name in sorted(set(module_names)):
                module = importlib.import_module(module_name)
                for name in dir(module):
                    if name.startswith("_"):
                        continue

                    member = getattr(module, name)
                    if inspect.isroutine(member):
                        add_function(module, name, member)
                    elif inspect.isclass(member) and getattr(member, "__module__", None) == module.__name__:
                        for method_name in dir(member):
                            if method_name.startswith("_"):
                                continue
                            method = getattr(member, method_name)
                            if inspect.isroutine(method):
                                add_function(module, name + "." + method_name, method)

            return json.dumps(functions, ensure_ascii=True)

        def _cuda_dotnet_json_value(value):
            if isinstance(value, enum.IntEnum):
                return int(value)
            if isinstance(value, enum.Enum):
                return {"$type": type(value).__qualname__, "name": value.name, "value": _cuda_dotnet_json_value(value.value)}
            if value is None or isinstance(value, (bool, int, float, str)):
                return value
            if isinstance(value, bytes):
                return {"$type": "bytes", "base64": base64.b64encode(value).decode("ascii")}
            if isinstance(value, (list, tuple)):
                return [_cuda_dotnet_json_value(item) for item in value]
            if isinstance(value, dict):
                return {str(key): _cuda_dotnet_json_value(item) for key, item in value.items()}
            if hasattr(value, "item") and callable(value.item):
                scalar = value.item()
                if scalar is not value:
                    return _cuda_dotnet_json_value(scalar)
            return {"$type": type(value).__module__ + "." + type(value).__qualname__, "repr": repr(value)}

        def _cuda_dotnet_invoke_json(module_name, attribute_path, arguments_json, keyword_arguments_json):
            if module_name != "cuda" and not module_name.startswith("cuda."):
                raise ValueError("Only modules below the cuda package can be invoked.")
            if any(part.startswith("_") for part in module_name.split(".") + attribute_path.split(".")):
                raise ValueError("Private Python members cannot be invoked through the API.")

            module = importlib.import_module(module_name)
            function = operator.attrgetter(attribute_path)(module)
            if not callable(function):
                raise TypeError("The requested CUDA Python member is not callable.")

            arguments = json.loads(arguments_json)
            keyword_arguments = json.loads(keyword_arguments_json)
            if not isinstance(arguments, list) or not isinstance(keyword_arguments, dict):
                raise ValueError("Arguments must be a JSON array and keyword arguments must be a JSON object.")

            result = function(*arguments, **keyword_arguments)
            return json.dumps(_cuda_dotnet_json_value(result), ensure_ascii=True, allow_nan=False)

        def _cuda_dotnet_cuda_driver_info():
            from cuda.bindings import driver

            version_status, version = driver.cuDriverGetVersion()
            initialization_status, = driver.cuInit(0)
            version_value = int(version) if int(version_status) == 0 else None
            return json.dumps({
                "driverVersionStatus": int(version_status),
                "driverVersion": version_value,
                "driverVersionMajor": version_value // 1000 if version_value is not None else None,
                "driverVersionMinor": (version_value % 1000) // 10 if version_value is not None else None,
                "initializationStatus": int(initialization_status),
                "available": int(initialization_status) == 0
            })

        def _cuda_dotnet_cuda_devices():
            from cuda.bindings import driver

            initialization_status, = driver.cuInit(0)
            initialization_status = int(initialization_status)
            if initialization_status != 0:
                return json.dumps({"status": initialization_status, "devices": []})

            count_status, count = driver.cuDeviceGetCount()
            count_status = int(count_status)
            if count_status != 0:
                return json.dumps({"status": count_status, "devices": []})

            devices = []
            for ordinal in range(int(count)):
                device_status, device = driver.cuDeviceGet(ordinal)
                device_status = int(device_status)
                if device_status != 0:
                    devices.append({"ordinal": ordinal, "status": device_status})
                    continue

                name_status, name = driver.cuDeviceGetName(256, device)
                compute_status, major, minor = driver.cuDeviceComputeCapability(device)
                memory_status, memory_bytes = driver.cuDeviceTotalMem(device)
                uuid_status, uuid = driver.cuDeviceGetUuid(device)

                def get_attribute(name):
                    attribute = getattr(driver.CUdevice_attribute, name)
                    result_status, value = driver.cuDeviceGetAttribute(attribute, device)
                    return int(result_status), int(value) if int(result_status) == 0 else None

                multiprocessor_status, multiprocessor_count = get_attribute("CU_DEVICE_ATTRIBUTE_MULTIPROCESSOR_COUNT")
                max_threads_status, max_threads_per_block = get_attribute("CU_DEVICE_ATTRIBUTE_MAX_THREADS_PER_BLOCK")
                clock_status, clock_rate_khz = get_attribute("CU_DEVICE_ATTRIBUTE_CLOCK_RATE")

                devices.append({
                    "ordinal": ordinal,
                    "status": device_status,
                    "name": name.decode("utf-8", "replace").split("\x00", 1)[0].strip() if int(name_status) == 0 else None,
                    "nameStatus": int(name_status),
                    "computeCapabilityMajor": int(major) if int(compute_status) == 0 else None,
                    "computeCapabilityMinor": int(minor) if int(compute_status) == 0 else None,
                    "computeCapabilityStatus": int(compute_status),
                    "memoryBytes": int(memory_bytes) if int(memory_status) == 0 else None,
                    "memoryStatus": int(memory_status),
                    "uuid": uuid.bytes.hex() if int(uuid_status) == 0 else None,
                    "uuidStatus": int(uuid_status),
                    "multiprocessorCount": multiprocessor_count,
                    "multiprocessorStatus": multiprocessor_status,
                    "maxThreadsPerBlock": max_threads_per_block,
                    "maxThreadsStatus": max_threads_status,
                    "clockRateKHz": clock_rate_khz,
                    "clockRateStatus": clock_status
                })

            return json.dumps({"status": 0, "devices": devices}, ensure_ascii=True)
        """;

    private static readonly object InitializationLock = new();
    private static CudaPythonRuntime? _current;

    private readonly CudaPythonRuntimeOptions _options;
    private readonly PyDict _pythonGlobals;
    private IReadOnlyList<CudaPythonFunctionDescriptor>? _discoveredFunctions;
    private Dictionary<string, CudaPythonFunctionDescriptor>? _functionsByQualifiedName;

    private CudaPythonRuntime(CudaPythonRuntimeOptions options, PyDict pythonGlobals)
    {
        _options = options;
        _pythonGlobals = pythonGlobals;
    }

    /// <summary>Initializes or returns the process-wide bundled Python runtime.</summary>
    /// <param name="options">Runtime, discovery, and invocation settings. The bundled runtime configuration is loaded when omitted.</param>
    /// <returns>The initialized process-wide runtime.</returns>
    /// <exception cref="FileNotFoundException">The bundled Python runtime configuration is missing.</exception>
    /// <exception cref="InvalidOperationException">CPython is unavailable or was initialized with different settings.</exception>
    public static CudaPythonRuntime Initialize(CudaPythonRuntimeOptions? options = null)
    {
        options ??= CudaPythonRuntimeOptions.LoadFromFile(
            Path.Combine(AppContext.BaseDirectory, "python-env", "runtime.json"));
        ValidateOptions(options);

        lock (InitializationLock)
        {
            if (_current is not null)
            {
                if (_current._options != options)
                {
                    throw new InvalidOperationException("The process-wide Python runtime is already initialized with different settings.");
                }

                return _current;
            }

            if (PythonEngine.IsInitialized)
            {
                throw new InvalidOperationException("CPython was initialized outside this library and cannot be configured safely.");
            }

            var pythonDllPath = options.PythonDllPath ?? Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
            if (string.IsNullOrWhiteSpace(pythonDllPath))
            {
                throw new InvalidOperationException("Set PythonDllPath or the PYTHONNET_PYDLL environment variable before initializing CPython.");
            }

            Runtime.PythonDLL = pythonDllPath;
            if (!string.IsNullOrWhiteSpace(options.PythonHome))
            {
                PythonEngine.PythonHome = options.PythonHome;
            }

            if (!string.IsNullOrWhiteSpace(options.PythonPath))
            {
                PythonEngine.PythonPath = options.PythonPath;
            }

            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
            PyDict pythonGlobals;
            using (Py.GIL())
            {
                pythonGlobals = new PyDict();
                try
                {
                    PythonEngine.Exec(DiscoveryScript, pythonGlobals, pythonGlobals);
                }
                catch (PythonException)
                {
                    pythonGlobals.Dispose();
                    throw;
                }
            }

            _current = new CudaPythonRuntime(options, pythonGlobals);
            return _current;
        }
    }

    /// <summary>Discovers callable functions in the configured root module and its included submodules.</summary>
    /// <returns>A stable, read-only snapshot of discovered functions.</returns>
    /// <exception cref="CudaPythonException">A Python module failed to import or enumerate.</exception>
    public IReadOnlyList<CudaPythonFunctionDescriptor> DiscoverFunctions()
    {
        lock (InitializationLock)
        {
            if (_discoveredFunctions is not null)
            {
                return _discoveredFunctions;
            }

            try
            {
                using (Py.GIL())
                using (var discover = _pythonGlobals.GetItem("_cuda_dotnet_discover"))
                using (var rootName = new PyString(_options.PackageRootModule))
                using (var includeSubmodules = PyObject.FromManagedObject(_options.IncludeSubmodules))
                using (var jsonResult = discover.Invoke(new[] { rootName, includeSubmodules }))
                {
                    var metadata = JsonSerializer.Deserialize<FunctionMetadata[]>(jsonResult.As<string>()) ?? [];
                    var functions = metadata
                        .Select(item =>
                        {
                            return new CudaPythonFunctionDescriptor(
                                item.ModuleName,
                                item.Name,
                                item.Signature,
                                item.Documentation);
                        })
                        .OrderBy(item => item.PythonQualifiedName, StringComparer.Ordinal)
                        .ToArray();

                    _functionsByQualifiedName = functions.ToDictionary(
                        item => item.PythonQualifiedName,
                        StringComparer.Ordinal);
                    _discoveredFunctions = Array.AsReadOnly(functions);
                    return _discoveredFunctions;
                }
            }
            catch (PythonException exception)
            {
                throw new CudaPythonException(
                    $"Failed to discover functions from Python module '{_options.PackageRootModule}': {exception.Message}",
                    exception);
            }
        }
    }

    /// <summary>Invokes a discovered Python function.</summary>
    /// <remarks>Dispose the returned object and access it only while holding <c>Py.GIL()</c>.</remarks>
    /// <param name="function">The exact descriptor returned by <see cref="DiscoverFunctions"/>.</param>
    /// <param name="positionalArguments">Positional values. Use <see cref="PyObject"/> instances to pass Python objects returned by earlier calls.</param>
    /// <param name="keywordArguments">Optional keyword values.</param>
    /// <returns>An owned Python object. The caller must dispose it after use.</returns>
    /// <exception cref="ArgumentException">The descriptor was not returned by this runtime's discovery snapshot.</exception>
    /// <exception cref="CudaPythonException">Python raised an exception while the function was invoked.</exception>
    public PyObject Invoke(
        CudaPythonFunctionDescriptor function,
        IReadOnlyList<object?>? positionalArguments = null,
        IReadOnlyDictionary<string, object?>? keywordArguments = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        DiscoverFunctions();

        if (_functionsByQualifiedName is null ||
            !_functionsByQualifiedName.TryGetValue(function.PythonQualifiedName, out var discoveredFunction) ||
            !ReferenceEquals(discoveredFunction, function))
        {
            throw new ArgumentException("The function descriptor was not returned by this runtime's discovery snapshot.", nameof(function));
        }

        return Invoke(function.ModuleName, function.Name, positionalArguments, keywordArguments);
    }

    /// <summary>Invokes a CUDA Python callable using JSON-compatible arguments and returns a JSON result.</summary>
    /// <param name="moduleName">A module name under the <c>cuda</c> package.</param>
    /// <param name="attributePath">The public module-relative function or class-method path.</param>
    /// <param name="argumentsJson">A JSON array of positional arguments.</param>
    /// <param name="keywordArgumentsJson">A JSON object of keyword arguments.</param>
    /// <returns>A JSON result. Python bytes and non-primitive objects are represented with type metadata.</returns>
    /// <exception cref="ArgumentException">The module or attribute path is outside the public CUDA package surface.</exception>
    /// <exception cref="CudaPythonException">Python raised an exception while the callable was invoked.</exception>
    public string InvokeJson(
        string moduleName,
        string attributePath,
        string argumentsJson,
        string keywordArgumentsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributePath);
        ArgumentNullException.ThrowIfNull(argumentsJson);
        ArgumentNullException.ThrowIfNull(keywordArgumentsJson);

        if ((moduleName != "cuda" && !moduleName.StartsWith("cuda.", StringComparison.Ordinal)) ||
            moduleName.Split('.').Concat(attributePath.Split('.')).Any(part => string.IsNullOrWhiteSpace(part) || part.StartsWith("_", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Only public callables under the cuda package can be invoked.");
        }

        try
        {
            using (Py.GIL())
            using (var invoke = _pythonGlobals.GetItem("_cuda_dotnet_invoke_json"))
            using (var pythonModuleName = new PyString(moduleName))
            using (var pythonAttributePath = new PyString(attributePath))
            using (var pythonArguments = new PyString(argumentsJson))
            using (var pythonKeywordArguments = new PyString(keywordArgumentsJson))
            using (var result = invoke.Invoke(new[] { pythonModuleName, pythonAttributePath, pythonArguments, pythonKeywordArguments }))
            {
                return result.As<string>();
            }
        }
        catch (PythonException exception)
        {
            throw new CudaPythonException(
                $"Python callable '{moduleName}.{attributePath}' failed: {exception.Message}",
                exception);
        }
    }

    /// <summary>Returns the CUDA driver version and initialization status as JSON.</summary>
    /// <returns>A JSON object containing driver version and availability fields.</returns>
    public string GetCudaDriverInfoJson() => InvokePythonHelper("_cuda_dotnet_cuda_driver_info");

    /// <summary>Returns CUDA device specifications as JSON.</summary>
    /// <returns>A JSON object containing device query statuses and available device information.</returns>
    public string GetCudaDevicesJson() => InvokePythonHelper("_cuda_dotnet_cuda_devices");

    /// <summary>Invokes a Python callable by module name and attribute path.</summary>
    /// <remarks>Dispose the returned object and access it only while holding <c>Py.GIL()</c>.</remarks>
    /// <param name="moduleName">The fully qualified Python module name.</param>
    /// <param name="attributePath">The module-relative function or class-method path.</param>
    /// <param name="positionalArguments">Optional positional values.</param>
    /// <param name="keywordArguments">Optional keyword values.</param>
    /// <returns>An owned Python object. The caller must dispose it after use.</returns>
    /// <exception cref="CudaPythonException">Python raised an exception while the callable was invoked.</exception>
    public PyObject Invoke(
        string moduleName,
        string attributePath,
        IReadOnlyList<object?>? positionalArguments = null,
        IReadOnlyDictionary<string, object?>? keywordArguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributePath);

        try
        {
            using (Py.GIL())
            using (var module = Py.Import(moduleName))
            using (var operatorModule = Py.Import("operator"))
            using (var getAttribute = operatorModule.GetAttr("attrgetter"))
            using (var pythonAttributePath = new PyString(attributePath))
            using (var attributeGetter = getAttribute.Invoke(new[] { pythonAttributePath }))
            using (var callable = attributeGetter.Invoke(new[] { module }))
            using (var keywordArgs = new PyDict())
            {
                if (!callable.IsCallable())
                {
                    throw new InvalidOperationException($"'{moduleName}.{attributePath}' is not callable.");
                }

                foreach (var (name, value) in keywordArguments ?? new Dictionary<string, object?>())
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(name);
                    using var pythonValue = ConvertArgument(value);
                    keywordArgs.SetItem(name, pythonValue);
                }

                var sourceArguments = positionalArguments ?? Array.Empty<object?>();
                var pythonArguments = new PyObject[sourceArguments.Count];
                try
                {
                    for (var index = 0; index < sourceArguments.Count; index++)
                    {
                        pythonArguments[index] = ConvertArgument(sourceArguments[index]);
                    }

                    return callable.Invoke(pythonArguments, keywordArgs);
                }
                finally
                {
                    foreach (var pythonArgument in pythonArguments)
                    {
                        pythonArgument?.Dispose();
                    }
                }
            }
        }
        catch (PythonException exception)
        {
            throw new CudaPythonException(
                $"Python callable '{moduleName}.{attributePath}' failed: {exception.Message}",
                exception);
        }
    }

    private static PyObject ConvertArgument(object? value) => value switch
    {
        null => PyObject.FromManagedObject(null!),
        PyObject pythonObject => pythonObject.NewReference(),
        _ => PyObject.FromManagedObject(value)
    };

    private string InvokePythonHelper(string helperName)
    {
        try
        {
            using (Py.GIL())
            using (var helper = _pythonGlobals.GetItem(helperName))
            using (var result = helper.Invoke(Array.Empty<PyObject>()))
            {
                return result.As<string>();
            }
        }
        catch (PythonException exception)
        {
            throw new CudaPythonException($"Python helper '{helperName}' failed: {exception.Message}", exception);
        }
    }

    private static void ValidateOptions(CudaPythonRuntimeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PackageRootModule);
        if (options.PythonDllPath is not null && string.IsNullOrWhiteSpace(options.PythonDllPath))
        {
            throw new ArgumentException("PythonDllPath cannot be an empty string.", nameof(options));
        }

        if (string.Equals(options.RuntimeId, "win-x64", StringComparison.OrdinalIgnoreCase) &&
            (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            throw new PlatformNotSupportedException("The bundled CPython runtime supports Windows x64 only.");
        }
    }

    private sealed class FunctionMetadata
    {
        [JsonPropertyName("moduleName")]
        public string ModuleName { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("signature")]
        public string? Signature { get; init; }

        [JsonPropertyName("documentation")]
        public string? Documentation { get; init; }
    }
}
