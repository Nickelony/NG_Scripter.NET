namespace TRNGScriptCompiler.Kernel.Text;

/// <summary>
/// Reads the <c>ENABLED</c> or <c>DISABLED</c> argument of a switch command and words the error for any other value,
/// so a classic command and an NG flag command report it alike.
/// </summary>
internal static class EnabledState
{
	/// <summary>
	/// Reads an <c>ENABLED</c> or <c>DISABLED</c> argument without regard to case,
	/// as VB6's <c>ControllaEnabled</c> does.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> for any other value, which the caller reports with <see cref="InvalidValueError"/>.
	/// </returns>
	internal static bool TryParse(string value, out bool enabled)
	{
		enabled = string.Equals(value, "ENABLED", StringComparison.OrdinalIgnoreCase);
		return enabled || string.Equals(value, "DISABLED", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Words the error for a switch value that is neither <c>ENABLED</c> nor <c>DISABLED</c>.</summary>
	/// <remarks>
	/// The argument number is 1-based, and the command is as the author wrote it or the definition names it.
	/// </remarks>
	internal static string InvalidValueError(int argumentNumber, string command, string value) =>
		FormattableString.Invariant($"Argument {argumentNumber} of {command} is invalid: {value} is neither ENABLED nor DISABLED");
}
