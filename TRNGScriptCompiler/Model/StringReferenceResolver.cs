using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>
/// Resolves a string reference - an ExtraNG index <c>!NNN</c>, a binary string <c>&amp;HEX</c> or a text -
/// to its language index.
/// </summary>
/// <remarks>
/// Classic and NG commands share it as VB6's <c>GetIndiceStringa</c> (Module1.bas:6518-6617) does; an NG parser reads
/// <c>!NNN</c> first.
/// </remarks>
internal sealed class StringReferenceResolver
{
	private readonly LanguageData _language;
	private readonly DiagnosticSink _diagnostics;

	/// <param name="language">The first language, whose indexes the references resolve to.</param>
	/// <param name="diagnostics">
	/// The per-run sink that receives a diagnostic for each reference that does not resolve.
	/// </param>
	internal StringReferenceResolver(LanguageData language, DiagnosticSink diagnostics)
	{
		_language = language;
		_diagnostics = diagnostics;
	}

	/// <summary>Finds the index a reference resolves to, without reporting one that does not resolve.</summary>
	/// <returns>The index, or <c>-1</c> when the reference does not resolve.</returns>
	/// <remarks>
	/// <paramref name="allowExtraNg"/> is <see langword="true"/> for an NG command, which may name an ExtraNG string.
	/// </remarks>
	internal int Find(string text, bool allowExtraNg)
	{
		int index = _language.ResolveStringIndex(text, allowExtraNg);

		if (index >= 0)
			return index;

		List<(int Index, string Line)> matches = _language.FindNormalizedMatches(text, allowExtraNg);
		return matches.Count == 1 ? matches[0].Index : -1;
	}

	/// <summary>Resolves a reference to its index, reporting an error that says why it does not resolve.</summary>
	/// <remarks>
	/// A text the exact lookup misses resolves, with a warning, through the one language line it matches
	/// once both are normalized (PD-63).
	/// </remarks>
	internal bool TryResolve(string text, bool allowExtraNg, string notFoundMessage, SourceLocation location, out int index)
	{
		index = -1;

		if (text.StartsWith('!'))
		{
			if (!allowExtraNg)
			{
				_diagnostics.Error($"You cannot use an index for extra NG strings in a standard (old) script command: {text}", location);
				return false;
			}

			index = _language.ResolveStringIndex(text, allowExtraNgText: true);

			if (index >= 0)
				return true;

			_diagnostics.Error($"Cannot find the extra NG string with index: {text}", location);
			return false;
		}

		// VB6 checks a binary string before it looks the text up, for both command formats (Module1.bas:6579-6585).
		if (text.StartsWith('&') && !IsValidBinaryString(text))
		{
			_diagnostics.Error(
				$"{text} is no binary string; write & and pairs of the upper-case hexadecimal digits 0-9 and A-F, such as &0A1B, in the reference "
					+ "and its language line",

				location
			);

			return false;
		}

		index = _language.ResolveStringIndex(text, allowExtraNg);

		if (index >= 0)
			return true;

		// The exact key misses, so VB6 fails here (Module1.bas:6611); a language line that matches once both are normalized is found instead.
		List<(int Index, string Line)> matches = _language.FindNormalizedMatches(text, allowExtraNg);

		if (matches.Count == 1)
		{
			_diagnostics.Warning(
				$"The string '{text}' matches the language line '{matches[0].Line}' only when spaces and comments are ignored",
				location
			);

			index = matches[0].Index;
			return true;
		}

		if (matches.Count > 1)
		{
			_diagnostics.Error(
				$"The string '{text}' matches the language lines {string.Join(", ", matches.Select(match => $"'{match.Line}'"))} when spaces and "
					+ "comments are ignored; write the language line it names without extra spaces or a comment, so it matches exactly",

				location
			);

			return false;
		}

		_diagnostics.Error(notFoundMessage, location);
		return false;
	}

	/// <summary>
	/// Validates a binary string: an <c>&amp;</c> and an even number, at least two, of upper-case hexadecimal digits.
	/// </summary>
	/// <remarks>
	/// VB6's <c>IsStringaBinariaOk</c> also takes a lone <c>&amp;</c> and a <c>-</c> among the digits, which the port
	/// rejects (PD-92).
	/// </remarks>
	internal static bool IsValidBinaryString(string text)
	{
		if (!text.StartsWith('&'))
			return false;

		ReadOnlySpan<char> hex = text.AsSpan(1);

		if (hex.Length == 0 || (hex.Length & 1) != 0)
			return false;

		foreach (char ch in hex)
		{
			if (!char.IsAsciiHexDigitUpper(ch))
				return false;
		}

		return true;
	}
}
