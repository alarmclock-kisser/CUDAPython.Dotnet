namespace CUDAPython.Dotnet.Python;

/// <summary>Reports a failure while discovering or invoking a CUDA Python function.</summary>
public sealed class CudaPythonException : InvalidOperationException
{
    /// <summary>Initializes an exception with a message and the originating Python exception.</summary>
    /// <param name="message">The .NET-side failure description.</param>
    /// <param name="innerException">The originating Python.NET exception.</param>
    public CudaPythonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
