namespace TRNGScriptCompiler.Kernel.Diagnostics;

/// <summary>
/// Points a diagnostic at a source file by full path, a 1-based line, or a span of UTF-16 units from a 0-based column.
/// </summary>
/// <remarks>
/// A <see langword="null"/> line, or a line not above 0, ties the diagnostic to no line, a 0 length
/// marks the line from the column to its end, and a <see langword="null"/> file names none. The column and the length
/// count in the line as its file holds it, up to its comment, or in the first physical line of a line
/// a <c>&gt;</c> continues.
/// </remarks>
internal readonly record struct SourceLocation(int? Line, int Column = 0, int Length = 0, string? File = null)
{
	/// <summary>
	/// Creates a location that marks the first occurrence of a term in a line, or the whole line when the line lacks
	/// the term.
	/// </summary>
	/// <remarks>
	/// The line is 1-based, the text is the source line up to its comment, the term matches without regard to case,
	/// and a <see langword="null"/> file names no file.
	/// </remarks>
	internal static SourceLocation ForTerm(int? line, string text, string term, string? file = null)
	{
		int column = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
		return column >= 0 ? new SourceLocation(line, column, Length: term.Length, file) : new SourceLocation(line, File: file);
	}

	/// <summary>
	/// Creates a location that marks one word of a line, counted from 0 among the words that spaces and tabs separate,
	/// or the whole line when that word is not the one given, matched without regard to case.
	/// </summary>
	/// <remarks>
	/// A word that repeats part of an earlier word marks its own place, which <see cref="ForTerm"/> cannot do.
	/// </remarks>
	internal static SourceLocation ForWord(int? line, string text, int wordIndex, string word, string? file = null)
	{
		int position = 0;

		for (int index = 0; ; index++)
		{
			while (position < text.Length && text[position] is ' ' or '\t')
				position++;

			if (position == text.Length)
				return new SourceLocation(line, File: file);

			int start = position;

			while (position < text.Length && text[position] is not (' ' or '\t'))
				position++;

			if (index != wordIndex)
				continue;

			return text.AsSpan(start, position - start).Equals(word, comparisonType: StringComparison.OrdinalIgnoreCase)
				? new SourceLocation(line, Column: start, Length: position - start, file)
				: new SourceLocation(line, File: file);
		}
	}

	/// <summary>Creates a location that names a file but no line in it.</summary>
	/// <param name="file">The full path of the source file.</param>
	internal static SourceLocation InFile(string file) => new(Line: null, File: file);

	/// <summary>Names an earlier line: <c>line N</c> in the current file, else <c>line N of</c> its file.</summary>
	/// <param name="earlier">The location of the earlier line, which holds a line.</param>
	/// <param name="currentFile">The full path of the file the diagnostic points at.</param>
	internal static string DescribeLine(SourceLocation earlier, string? currentFile)
	{
		return string.Equals(earlier.File, currentFile, StringComparison.OrdinalIgnoreCase)
			? $"line {earlier.Line}"
			: $"line {earlier.Line} of {Path.GetFileName(earlier.File)}";
	}
}
