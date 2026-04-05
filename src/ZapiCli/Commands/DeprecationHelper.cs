namespace ZapiCli.Commands;

/// <summary>
/// Shared deprecation-warning infrastructure for renamed commands.
/// </summary>
/// <remarks>
/// <para>
/// Spectre.Console.Cli does not support command-level aliases natively.
/// The chosen pattern for deprecated command aliases is:
/// </para>
/// <list type="number">
///   <item>
///     Register the deprecated command name as a separate, thin wrapper command class
///     (e.g. <c>ApiCallCommand</c> alongside <c>ApiRequestCommand</c>).
///   </item>
///   <item>
///     The wrapper's <c>ExecuteAsync</c> calls <see cref="Warn"/> at the top, then
///     delegates immediately to the shared implementation (or duplicates the same
///     <c>IOutputWriter</c> call if the logic is trivial).
///   </item>
///   <item>
///     No exit-code change occurs — the command exits 0 on success just as it always did.
///   </item>
/// </list>
/// <para>
/// This keeps each registration simple, avoids reflection or runtime inspection,
/// and makes the deprecation path explicit and grep-able.
/// </para>
/// </remarks>
internal static class DeprecationHelper
{
    /// <summary>
    /// Writes a deprecation warning to <see cref="Console.Error"/>.
    /// The warning contains both the old and new command names so the caller can
    /// immediately update their scripts. The command continues to execute normally
    /// after this call — the exit code is <em>not</em> changed by this method.
    /// </summary>
    /// <param name="oldCommand">The deprecated command/sub-command name as invoked by the user.</param>
    /// <param name="newCommand">The replacement command/sub-command name the user should switch to.</param>
    public static void Warn(string oldCommand, string newCommand)
    {
        Console.Error.WriteLine(
            $"Warning: '{oldCommand}' is deprecated and will be removed in a future version. " +
            $"Use '{newCommand}' instead.");
    }
}
