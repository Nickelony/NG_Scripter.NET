namespace TRNGScriptCompiler.Kernel.Text;

/// <summary>
/// Words the error for a command or directive that gets a different number of arguments than it takes, so every command
/// that counts its arguments reports a mismatch alike.
/// </summary>
internal static class ArgumentCountMessage
{
	/// <summary>Words the error for a count that differs from the one expected.</summary>
	/// <remarks>
	/// The command is as the author wrote it or the definition names it, and it appears in quotes.
	/// </remarks>
	internal static string Wrong(string command, int expected, int actual) => Wrong(command, FormattableString.Invariant($"{expected}"), actual);

	/// <summary>Words the error for a count that differs from the expected one, which the caller words.</summary>
	/// <remarks>
	/// The expected text is, for example, <c>at least 5</c> or <c>6 or 7</c>.
	/// </remarks>
	internal static string Wrong(string command, string expected, int actual) =>
		FormattableString.Invariant($"Wrong number of arguments for '{command}': expected {expected}, got {actual}");
}
