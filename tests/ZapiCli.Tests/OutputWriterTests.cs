using ZapiCli.Core;

namespace ZapiCli.Tests;

public sealed class OutputWriterTests
{
    // ── InMemoryOutputWriter ────────────────────────────────────────────────

    [Fact]
    public void InMemoryOutputWriter_WriteJson_SerializesWithSnakeCase()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteJson(new { CamelCase = 1 });

        var json = writer.LastSuccessJson;
        Assert.NotNull(json);
        Assert.Contains("camel_case", json);
        Assert.DoesNotContain("CamelCase", json);
    }

    [Fact]
    public void InMemoryOutputWriter_WriteRaw_PassesThroughUnchanged()
    {
        const string raw = "{\"raw\":1}";
        var writer = new InMemoryOutputWriter();
        writer.WriteRaw(raw);

        Assert.Equal(raw, writer.LastSuccessJson);
    }

    [Fact]
    public void InMemoryOutputWriter_WriteError_RecordsToErrors()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteError("msg", "CODE", 1);

        var err = writer.LastError;
        Assert.NotNull(err);
        Assert.Equal("msg", err.Message);
        Assert.Equal("CODE", err.Code);
        Assert.Equal(1, err.ExitCode);
    }

    [Fact]
    public void InMemoryOutputWriter_WriteJson_RecordsToSuccessOutput()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteJson(new { A = 1 });
        writer.WriteRaw("{\"b\":2}");

        Assert.Equal(2, writer.SuccessOutput.Count);
        Assert.Equal(2, writer.AllSuccessJson.Count);
    }

    [Fact]
    public void InMemoryOutputWriter_MultipleErrors_AllCaptured()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteError("e1", "C1", 1);
        writer.WriteError("e2", "C2", 2);

        Assert.Equal(2, writer.Errors.Count);
        Assert.Equal(2, writer.AllErrors.Count);
        Assert.Equal("C2", writer.LastError!.Code);
        Assert.Equal(2, writer.LastError.ExitCode);
    }

    // ── JsonOutputWriter (via Console redirection) ──────────────────────────

    [Fact]
    public void JsonOutputWriter_WriteJson_SerializesWithSnakeCase()
    {
        var writer = new JsonOutputWriter();
        using var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            writer.WriteJson(new { CamelCase = 1 });
        }
        finally
        {
            Console.SetOut(orig);
        }

        var output = sw.ToString().Trim();
        Assert.Contains("camel_case", output);
        Assert.DoesNotContain("CamelCase", output);
    }

    [Fact]
    public void JsonOutputWriter_WriteRaw_PassesThroughUnchanged()
    {
        const string raw = "{\"raw\":1}";
        var writer = new JsonOutputWriter();
        using var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            writer.WriteRaw(raw);
        }
        finally
        {
            Console.SetOut(orig);
        }

        Assert.Equal(raw, sw.ToString().Trim());
    }

    [Fact]
    public void JsonOutputWriter_WriteError_WritesToStderrWithCamelCaseExitCode()
    {
        var writer = new JsonOutputWriter();
        using var sw = new StringWriter();
        var orig = Console.Error;
        Console.SetError(sw);
        try
        {
            writer.WriteError("msg", "CODE", 1);
        }
        finally
        {
            Console.SetError(orig);
        }

        var json = sw.ToString().Trim();
        Assert.Contains("\"error\":\"msg\"", json);
        Assert.Contains("\"code\":\"CODE\"", json);
        Assert.Contains("\"exitCode\":1", json);
        // exitCode must be camelCase, not exit_code (ADR-0008)
        Assert.DoesNotContain("exit_code", json);
    }

    // ── ErrorCodes ──────────────────────────────────────────────────────────

    [Fact]
    public void ErrorCodes_AreNonNullNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(ErrorCodes.ACCOUNT_NOT_FOUND));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.ACCOUNT_ALREADY_EXISTS));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.NO_DEFAULT_ACCOUNT));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.AUTH_FAILURE));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.NEEDS_REAUTH));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.API_ERROR));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.INVALID_ARGS));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.IO_ERROR));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.KEYCHAIN_ERROR));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.ACCOUNT_DOMAIN_BLOCKED));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.EMAIL_REQUIRED));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.HOST_NOT_ALLOWED));
        Assert.False(string.IsNullOrEmpty(ErrorCodes.INTERNAL_ERROR));
    }

    // ── ZapiCliException ────────────────────────────────────────────────────

    [Fact]
    public void ZapiCliException_ExposesCodeAndExitCode()
    {
        var ex = new ZapiCliException("test message", "MY_CODE", 2);

        Assert.Equal("MY_CODE", ex.Code);
        Assert.Equal(2, ex.ExitCode);
        Assert.Equal("test message", ex.Message);
    }

    [Fact]
    public void ZapiCliException_DefaultExitCodeIsOne()
    {
        var ex = new ZapiCliException("msg", "SOME_CODE");

        Assert.Equal(1, ex.ExitCode);
    }
}
