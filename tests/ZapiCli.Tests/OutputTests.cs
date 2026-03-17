using System.Text.Json;
using Xunit;
using ZapiCli.Core;

namespace ZapiCli.Tests;

public sealed class JsonOutputWriterTests
{
    [Fact]
    public void WriteSuccess_ProducesCorrectJsonEnvelope()
    {
        // We redirect stdout to capture the output
        using var sw = new System.IO.StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var writer = new JsonOutputWriter();
            writer.WriteSuccess(new { id = 42, name = "test" });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = sw.ToString().Trim();
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("data", out var data));
        Assert.Equal(42, data.GetProperty("id").GetInt32());
        Assert.Equal("test", data.GetProperty("name").GetString());
    }

    [Fact]
    public void WriteSuccess_UsesSnakeCaseLowerForDataProperties()
    {
        using var sw = new System.IO.StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var writer = new JsonOutputWriter();
            writer.WriteSuccess(new { accountName = "work", isDefault = true });
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = sw.ToString().Trim();
        using var doc = JsonDocument.Parse(output);
        var data = doc.RootElement.GetProperty("data");

        // SnakeCaseLower: accountName → account_name, isDefault → is_default
        Assert.True(data.TryGetProperty("account_name", out _));
        Assert.True(data.TryGetProperty("is_default", out _));
    }

    [Fact]
    public void WriteError_ProducesCorrectEnvelopeOnStderr()
    {
        using var sw = new System.IO.StringWriter();
        var originalErr = Console.Error;
        Console.SetError(sw);
        try
        {
            var writer = new JsonOutputWriter();
            writer.WriteError("account not found", ErrorCodes.AccountNotFound, 1);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var output = sw.ToString().Trim();
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("account not found", root.GetProperty("error").GetString());
        Assert.Equal("ACCOUNT_NOT_FOUND", root.GetProperty("code").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.False(root.TryGetProperty("detail", out _));
    }

    [Fact]
    public void WriteError_WithDetail_IncludesDetailField()
    {
        using var sw = new System.IO.StringWriter();
        var originalErr = Console.Error;
        Console.SetError(sw);
        try
        {
            var writer = new JsonOutputWriter();
            writer.WriteError("HTTP 404 Not Found", ErrorCodes.ApiError, 1,
                detail: new { message = "resource not found" });
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var output = sw.ToString().Trim();
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("API_ERROR", root.GetProperty("code").GetString());
        Assert.True(root.TryGetProperty("detail", out var detail));
        Assert.Equal("resource not found", detail.GetProperty("message").GetString());
    }

    [Fact]
    public void WriteError_ErrorExitCodeIsNotSnakeCase()
    {
        // Verify that exitCode is camelCase, NOT exit_code
        using var sw = new System.IO.StringWriter();
        var originalErr = Console.Error;
        Console.SetError(sw);
        try
        {
            var writer = new JsonOutputWriter();
            writer.WriteError("test", ErrorCodes.InternalError, 1);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var output = sw.ToString().Trim();
        // The raw JSON should contain "exitCode" not "exit_code"
        Assert.Contains("\"exitCode\"", output);
        Assert.DoesNotContain("\"exit_code\"", output);
    }
}

public sealed class InMemoryOutputWriterTests
{
    [Fact]
    public void WriteSuccess_CapturesJsonInSuccessOutputs()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteSuccess(new { value = 1 });

        Assert.Single(writer.SuccessOutputs);
        using var doc = JsonDocument.Parse(writer.SuccessOutputs[0]);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WriteError_CapturesJsonInErrorOutputs()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteError("fail", ErrorCodes.InvalidArgs, 1);

        Assert.Single(writer.ErrorOutputs);
        using var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        Assert.Equal("INVALID_ARGS", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void LastSuccess_ReturnsLastWrittenEnvelope()
    {
        var writer = new InMemoryOutputWriter();
        writer.WriteSuccess(new { a = 1 });
        writer.WriteSuccess(new { a = 2 });

        Assert.NotNull(writer.LastSuccess);
        using var doc = JsonDocument.Parse(writer.LastSuccess!);
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("a").GetInt32());
    }

    [Fact]
    public void LastSuccess_IsNullWhenNothingWritten()
    {
        var writer = new InMemoryOutputWriter();
        Assert.Null(writer.LastSuccess);
        Assert.Null(writer.LastError);
    }
}

public sealed class ZapiCliExceptionTests
{
    [Fact]
    public void ZapiCliException_HasCorrectProperties()
    {
        var ex = new ZapiCliException("account not found", ErrorCodes.AccountNotFound, 1);
        Assert.Equal("account not found", ex.Message);
        Assert.Equal("ACCOUNT_NOT_FOUND", ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void ZapiCliException_DefaultExitCodeIsOne()
    {
        var ex = new ZapiCliException("error", ErrorCodes.InvalidArgs);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void ZapiCliException_CanCarryInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new ZapiCliException("wrapped", ErrorCodes.IoError, 1, inner);
        Assert.Same(inner, ex.InnerException);
    }
}

public sealed class InMemoryOutputWriter_ExceptionHandlerTests
{
    /// <summary>
    /// Simulates what Program.cs exception handler does when a ZapiCliException is thrown.
    /// Verifies the resulting stderr JSON is well-formed.
    /// </summary>
    [Fact]
    public void ExceptionHandler_ZapiCliException_ProducesCorrectEnvelope()
    {
        var writer = new InMemoryOutputWriter();
        var ex = new ZapiCliException("account not found", ErrorCodes.AccountNotFound, 1);

        // Simulate exception handler logic from Program.cs
        writer.WriteError(ex.Message, ex.Code, ex.ExitCode);

        Assert.Single(writer.ErrorOutputs);
        using var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        var root = doc.RootElement;
        Assert.Equal("account not found", root.GetProperty("error").GetString());
        Assert.Equal("ACCOUNT_NOT_FOUND", root.GetProperty("code").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void ExceptionHandler_GenericException_ProducesInternalErrorEnvelope()
    {
        var writer = new InMemoryOutputWriter();
        var ex = new InvalidOperationException("something went wrong");

        // Simulate exception handler logic from Program.cs
        writer.WriteError(ex.Message, ErrorCodes.InternalError, 1);

        Assert.Single(writer.ErrorOutputs);
        using var doc = JsonDocument.Parse(writer.ErrorOutputs[0]);
        Assert.Equal("INTERNAL_ERROR", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
    }
}
