using System.Diagnostics.CodeAnalysis;

namespace TRNGScriptCompiler.Kernel.Text;

/// <summary>
/// Reads a script line as VB6 reads it before it looks at the line's words: its normalized text and its directive word.
/// </summary>
/// <remarks>
/// The define scan, the plugin registry and the language lookup read lines this way too, so the type sits
/// below the formats.
/// </remarks>
internal static class ScriptLineText
{
	/// <summary>The directive that defines a constant, or names a plugin, given with its <c>#</c>.</summary>
	internal const string DefineDirective = "#DEFINE";

	/// <summary>
	/// Cuts a line before its comment, as <see cref="Normalize"/> does, and keeps the rest as written.
	/// </summary>
	/// <remarks>
	/// A <c>;</c> starts a comment anywhere and <c>//</c> only in a line without <c>;</c>.
	/// </remarks>
	internal static string WithoutComment(string line) => line[..CommentStart(line)];

	/// <summary>
	/// Normalizes a line as <c>NormalizzaLinea</c> does (<c>MioModulo.bas:5745</c>), trimming it and collapsing runs
	/// of spaces.
	/// </summary>
	/// <remarks>
	/// It drops the comment as <see cref="WithoutComment"/> cuts it, and a CRLF or a tab counts as a space.
	/// </remarks>
	internal static string Normalize(string line)
	{
		ReadOnlySpan<char> text = line.AsSpan(0, CommentStart(line));
		Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
		int length = 0;

		for (int i = 0; i < text.Length; i++)
		{
			char character = text[i];

			if (character == '\t')
			{
				character = ' ';
			}
			else if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
			{
				character = ' ';
				i++;
			}

			if (character != ' ' || length == 0 || buffer[length - 1] != ' ')
				buffer[length++] = character;
		}

		return buffer[..length].Trim().ToString();
	}

	private static int CommentStart(string line)
	{
		int commentIndex = line.IndexOf(';');

		if (commentIndex < 0)
			commentIndex = line.IndexOf("//", StringComparison.Ordinal);

		return commentIndex >= 0 ? commentIndex : line.Length;
	}

	/// <summary>
	/// Tests whether a normalized line's first word, in any case, is a directive such as <c>#DEFINE</c>,
	/// given with its <c>#</c>.
	/// </summary>
	/// <remarks>
	/// The word ends at a space, a <c>"</c> or the line end, so <c>#INCLUDEX</c> fails. VB6 ends it at a space
	/// only (<c>Module1.bas:4966-4974</c>, <c>5001</c>, <c>13658</c>), so the quote is an extension (PD-37).
	/// </remarks>
	internal static bool StartsWithDirective(string line, string directive)
	{
		return line.StartsWith(directive, StringComparison.OrdinalIgnoreCase)
			&& (line.Length == directive.Length || line[directive.Length] is ' ' or '"');
	}

	/// <summary>
	/// Splits a <c>#DEFINE</c> line into its words, and reports the count error when the line lacks a name or a value.
	/// </summary>
	/// <remarks>
	/// The words always come back, <c>#DEFINE</c> first, so a caller can still name the first argument after a failure.
	/// </remarks>
	internal static bool TrySplitDefine(string line, out string[] words, [NotNullWhen(false)] out string? countError)
	{
		words = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
		countError = words.Length == 3 ? null : ArgumentCountMessage.Wrong(DefineDirective, expected: 2, actual: words.Length - 1);

		return countError is null;
	}
}
