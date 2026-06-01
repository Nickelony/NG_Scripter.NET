using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>
/// A command line of a section, split at every comma into its command name, which ends in <c>=</c> and loses
/// the whitespace before it, and its arguments.
/// </summary>
/// <remarks>
/// <see cref="Text"/> is the normalized line the arguments come from, and <see cref="SourceText"/> is the first
/// physical line as the file holds it, up to its comment, which a diagnostic's span measures.
/// The line of <see cref="Location"/> is 1-based and its file is a full path.
/// </remarks>
internal sealed record ScriptCommandLine(string Text, string SourceText, string Command, IReadOnlyList<string> Arguments, SourceLocation Location)
{
	/// <summary>
	/// Gets the location of the command name in the source line, or of the whole line when it is not written as is.
	/// </summary>
	internal SourceLocation CommandLocation => SourceLocation.ForTerm(line: Location.Line, text: SourceText, term: Command, file: Location.File);

	/// <summary>
	/// Returns the location of an argument in the source line, or of the whole line when it is not written as is.
	/// </summary>
	/// <param name="index">The zero-based index of the argument.</param>
	/// <remarks>
	/// The source text splits at every comma as the parse does, so a repeated value marks its own argument.
	/// A line that a <c>&gt;</c> continues holds only the arguments of its first physical line.
	/// </remarks>
	internal SourceLocation ArgumentLocation(int index)
	{
		if ((uint)index < (uint)Arguments.Count && TryFindArgument(index, out int column, out int length))
			return new SourceLocation(Line: Location.Line, column, length, File: Location.File);

		return new SourceLocation(Line: Location.Line, File: Location.File);
	}

	private bool TryFindArgument(int index, out int column, out int length)
	{
		column = 0;
		length = 0;

		int equalsIndex = SourceText.IndexOf('=');

		if (equalsIndex < 0)
			return false;

		int start = equalsIndex + 1;

		for (int segment = 0; segment < index; segment++)
		{
			int comma = SourceText.IndexOf(',', start);

			if (comma < 0)
				return false;

			start = comma + 1;
		}

		int end = SourceText.IndexOf(',', start);

		ReadOnlySpan<char> written = SourceText.AsSpan(start, (end < 0 ? SourceText.Length : end) - start);
		ReadOnlySpan<char> trimmed = written.Trim();

		if (!ReadsAs(trimmed, Arguments[index]))
			return false;

		column = start + (written.Length - written.TrimStart().Length);
		length = trimmed.Length;
		return true;
	}

	/// <summary>
	/// Tests whether an argument as written reads as the normalized one, which collapses its runs of spaces and tabs.
	/// </summary>
	private static bool ReadsAs(ReadOnlySpan<char> written, string argument)
	{
		int position = 0;

		for (int i = 0; i < written.Length; i++)
		{
			if (i > 0 && (written[i] is ' ' or '\t') && (written[i - 1] is ' ' or '\t'))
				continue;

			char character = written[i] == '\t' ? ' ' : written[i];

			if (position >= argument.Length || argument[position] != character)
				return false;

			position++;
		}

		return position == argument.Length;
	}
}
