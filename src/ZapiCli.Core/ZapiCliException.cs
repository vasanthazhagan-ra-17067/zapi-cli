namespace ZapiCli.Core;

public sealed class ZapiCliException : Exception
{
    public string Code { get; }
    public int ExitCode { get; }

    /// <summary>Optional machine-readable detail (e.g. response body on API_ERROR).</summary>
    public string? Detail { get; init; }

    public ZapiCliException(string message, string code, int exitCode = 1)
        : base(message)
    {
        Code = code;
        ExitCode = exitCode;
    }

    public ZapiCliException(string message, string code, int exitCode, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
        ExitCode = exitCode;
    }
}
