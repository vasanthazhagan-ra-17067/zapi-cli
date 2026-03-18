namespace ZapiCli.Core;

/// <summary>
/// Routes all CLI output. Commands must never write directly to <see cref="System.Console"/>.
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// Serializes <paramref name="data"/> with SnakeCaseLower naming and writes to stdout.
    /// No wrapper envelope — the object is serialized directly.
    /// </summary>
    void WriteJson(object data);

    /// <summary>
    /// Writes <paramref name="rawJson"/> to stdout verbatim with no re-serialization.
    /// Used to pass Zoho API response bodies byte-for-byte to the caller.
    /// </summary>
    void WriteRaw(string rawJson);

    /// <summary>
    /// Writes <c>{"error":"...","code":"...","exitCode":N}</c> to stderr.
    /// </summary>
    void WriteError(string message, string code, int exitCode = 1);
}
