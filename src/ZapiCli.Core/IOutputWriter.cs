namespace ZapiCli.Core;

public interface IOutputWriter
{
    void WriteSuccess(object data);
    void WriteError(string message, string code, int exitCode, object? detail = null);
}
