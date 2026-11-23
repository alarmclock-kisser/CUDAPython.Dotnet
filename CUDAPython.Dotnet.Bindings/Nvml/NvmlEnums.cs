namespace CUDAPython.Dotnet.Bindings.Nvml;

/// <summary>Temperature sensor locations (mirrors <c>cuda.bindings.nvml.TemperatureSensors</c>).</summary>
public enum NvmlTemperatureSensor : int
{
    Gpu = 0,
    GpuMax = 1,
}

/// <summary>Clock domains (mirrors <c>cuda.bindings.nvml.ClockType</c>).</summary>
public enum NvmlClockType : int
{
    Graphics = 0,
    Sm = 1,
    Memory = 2,
    Video = 3,
}

/// <summary>Clock reading kinds (mirrors <c>cuda.bindings.nvml.ClockId</c>).</summary>
public enum NvmlClockId : int
{
    Current = 0,
    AppClockTarget = 1,
    AppClockDefault = 2,
    CustomerBoostMax = 3,
}
