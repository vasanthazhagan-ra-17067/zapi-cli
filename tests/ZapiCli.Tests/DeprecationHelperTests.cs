using ZapiCli.Commands;

namespace ZapiCli.Tests;

public sealed class DeprecationHelperTests
{
    [Fact]
    public void Warn_WritesToStderr_NotStdout()
    {
        // Arrange
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        using var stdoutCapture = new StringWriter();
        using var stderrCapture = new StringWriter();

        Console.SetOut(stdoutCapture);
        Console.SetError(stderrCapture);

        try
        {
            // Act
            DeprecationHelper.Warn("old-command", "new-command");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        // Assert — warning must appear on stderr, not stdout
        var stderr = stderrCapture.ToString();
        var stdout = stdoutCapture.ToString();

        Assert.False(string.IsNullOrEmpty(stderr), "Expected deprecation warning on stderr but got nothing.");
        Assert.True(string.IsNullOrEmpty(stdout), $"Expected nothing on stdout but got: {stdout}");
    }

    [Fact]
    public void Warn_MessageContainsOldCommandName()
    {
        var originalErr = Console.Error;
        using var stderrCapture = new StringWriter();
        Console.SetError(stderrCapture);

        try
        {
            DeprecationHelper.Warn("api-call", "api request");
        }
        finally
        {
            Console.SetError(originalErr);
        }

        Assert.Contains("api-call", stderrCapture.ToString());
    }

    [Fact]
    public void Warn_MessageContainsNewCommandName()
    {
        var originalErr = Console.Error;
        using var stderrCapture = new StringWriter();
        Console.SetError(stderrCapture);

        try
        {
            DeprecationHelper.Warn("api-call", "api request");
        }
        finally
        {
            Console.SetError(originalErr);
        }

        Assert.Contains("api request", stderrCapture.ToString());
    }

    [Fact]
    public void Warn_ContainsBothNamesInSingleMessage()
    {
        const string OldName = "account-login";
        const string NewName = "account add";

        var originalErr = Console.Error;
        using var stderrCapture = new StringWriter();
        Console.SetError(stderrCapture);

        try
        {
            DeprecationHelper.Warn(OldName, NewName);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var message = stderrCapture.ToString();
        Assert.Contains(OldName, message);
        Assert.Contains(NewName, message);
    }
}
