namespace TRNGScriptCompiler.Cli;

/// <summary>Reads the command-line arguments one after another, and takes the value that follows an option.</summary>
/// <param name="arguments">The arguments to read.</param>
internal sealed class ArgumentCursor(string[] arguments)
{
	/// <summary>Gets the position of <see cref="Current"/> in the arguments.</summary>
	internal int Position { get; private set; } = -1;

	/// <summary>Gets the argument at the cursor.</summary>
	internal string Current => arguments[Position];

	/// <summary>Moves to the next argument.</summary>
	/// <returns><see langword="false"/> when no argument is left.</returns>
	internal bool MoveNext() => ++Position < arguments.Length;

	/// <summary>Takes the argument after the current one as the value of an option and moves to it.</summary>
	/// <returns>The value, or <see langword="null"/> when no argument follows.</returns>
	internal string? TakeValue() => Position + 1 < arguments.Length ? arguments[++Position] : null;
}
