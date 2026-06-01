using System.Globalization;
using System.Text;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Language;

/// <summary>
/// Converts the C-style escapes of a language string, <c>\xNN</c>, <c>\t</c> and <c>\n</c>,
/// as VB6's <c>StringaC_To_Abs</c> does.
/// </summary>
/// <remarks>
/// Decoding trims (<c>Trim$</c>), cuts at the first <c>;</c> and rescans, as each <c>\x</c> replacement can form
/// an escape (Module1.bas:2164-2231).
/// </remarks>
internal static class LanguageEscapeCodec
{
	/// <summary>
	/// Decodes a classic table string, splitting off a leading <c>Tag:</c>; it fails when a <c>\x</c> escape
	/// has no hex value.
	/// </summary>
	/// <remarks>
	/// The tag keeps its colon, or is empty, and is the text before the first colon when that text holds
	/// no space (Module1.bas:2244-2263).
	/// </remarks>
	internal static bool TryDecodeClassic(string text, out string decoded, out string specialTag)
	{
		specialTag = string.Empty;

		if (!TryDecode(text, out decoded))
			return false;

		if (decoded.Length > 0)
			decoded = ExtractSpecialTag(decoded, out specialTag);

		return true;
	}

	/// <summary>
	/// Decodes a string of the <c>[ExtraNG]</c> section as the language file
	/// or its <c>@file</c> writes it, keeping a leading <c>Tag:</c> as text (<c>Module1.bas:2235-2240</c>).
	/// </summary>
	/// <returns><see langword="false"/> when a <c>\x</c> escape has no hexadecimal value.</returns>
	internal static bool TryDecodeExtraNg(string text, out string decoded) => TryDecode(text, out decoded);

	/// <summary>
	/// Writes the control characters a language line cannot hold as the escapes that decode back to them.
	/// </summary>
	/// <returns>The line with each newline, tab, <c>0x17</c> and <c>0x14</c> written as its escape.</returns>
	internal static string Encode(string text)
	{
		// Build lazily and only when an escape is actually present;
		// text without escapes is returned unchanged (no StringBuilder round-trip).
		StringBuilder? result = null;
		int copyStart = 0;

		for (int i = 0; i < text.Length; i++)
		{
			string? replacement = text[i] switch
			{
				'\n' => "\\n",
				'\t' => "\\t",
				(char)0x17 => "\\x17",
				(char)0x14 => "\\x14",
				_ => null,
			};

			if (replacement is null)
				continue;

			result ??= new StringBuilder(text.Length + 8);
			result.Append(text, copyStart, i - copyStart).Append(replacement);
			copyStart = i + 1;
		}

		if (result is null)
			return text;

		return result.Append(text, copyStart, text.Length - copyStart).ToString();
	}

	/// <summary>
	/// Describes the first character the game reads as the end of a decoded string, or returns <see langword="null"/>
	/// for none.
	/// </summary>
	/// <remarks>
	/// The game ends a string at a zero byte; the 0xA5 XOR (Mod_ScriptDat.bas:246-288) zeroes byte 0xA5 in the file
	/// and a zero character once read.
	/// </remarks>
	internal static string? FindStringEnd(string decoded)
	{
		foreach (char character in decoded)
		{
			if (character == '\0')
				return "a zero byte";

			// U+00A5 is the only character Windows-1252 writes as byte 0xA5.
			if (character == '¥')
				return "byte 0xA5 ('¥' in Windows-1252)";
		}

		return null;
	}

	/// <summary>Formats the error for a text that holds a character <see cref="FindStringEnd"/> reports.</summary>
	/// <param name="text">The line or the file as the author wrote it.</param>
	/// <param name="description">The description <see cref="FindStringEnd"/> returned.</param>
	internal static string FormatStringEndError(string text, string description) =>
		$"'{text}' holds {description}, where the game would end the string; remove the character";

	/// <summary>Formats the error for a text with a <c>\x</c> escape that has no two hexadecimal digits.</summary>
	/// <param name="subject">The words that name the text, such as <c>The line 'Hello \x-4'</c>.</param>
	internal static string FormatInvalidEscapeError(string subject) =>
		$"{subject} holds an invalid hexadecimal escape; follow each \\x with two hexadecimal digits, such as \\x41";

	private static bool TryDecode(string text, out string decoded)
	{
		// Trim(' ') matches VB6's Trim$, which strips spaces only, and the comment is cut after the trim.
		decoded = text.Trim(' ');

		int commentIndex = decoded.IndexOf(';');

		if (commentIndex >= 0)
			decoded = decoded[..commentIndex];

		return decoded.Length == 0 || TryReplaceEscapes(ref decoded);
	}

	/// <summary>
	/// Replaces the <c>\x</c>, <c>\t</c> and <c>\n</c> escapes, keeping a backslash before any other character.
	/// </summary>
	/// <remarks>
	/// As in VB6, it finds the first <c>\x</c> without regard to case, checks the next
	/// one or two characters, replaces it everywhere and scans again, so one pass cannot stand for the rescan.
	/// A chain such as <c>\x5cx5cx5c</c> removes one escape per pass, and its cost grows with the square of its length.
	/// </remarks>
	private static bool TryReplaceEscapes(ref string text)
	{
		// Without a backslash no escape can match.
		if (!text.Contains('\\'))
			return true;

		while (true)
		{
			int escapeIndex = text.IndexOf("\\x", StringComparison.OrdinalIgnoreCase);

			if (escapeIndex < 0)
				break;

			int hexStart = escapeIndex + 2;
			int hexLength = Math.Min(2, text.Length - hexStart);

			if (hexLength <= 0)
				return false;

			string hexValue = text.Substring(hexStart, hexLength);

			if (!TryParseHexByte(hexValue, out int byteValue))
				return false;

			// VB6's Chr() decodes through the ANSI code page (Module1.bas:2222), so \x93 is the Windows-1252 quote that writes back as 0x93.
			string decoded = Vb6Ansi.ToChar((byte)byteValue).ToString();

			text = text.Replace("\\x" + hexValue, decoded, StringComparison.OrdinalIgnoreCase);
		}

		text = text.Replace("\\t", "\t", StringComparison.OrdinalIgnoreCase);
		text = text.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase);

		return true;
	}

	/// <summary>
	/// Parses one or two hexadecimal digits, because VB6 isNumericHex accepts a single character before the end
	/// of the text.
	/// </summary>
	private static bool TryParseHexByte(string hexValue, out int value)
	{
		value = 0;

		return hexValue.Length is 1 or 2
			&& int.TryParse(s: hexValue, style: NumberStyles.AllowHexSpecifier, provider: CultureInfo.InvariantCulture, result: out value);
	}

	/// <summary>
	/// Splits off the special tag, where a colon at position 0 is the empty tag, which VB6 removes
	/// too (Module1.bas:2244-2263).
	/// </summary>
	/// <remarks>
	/// Trim(' ') matches VB6's Trim$.
	/// </remarks>
	private static string ExtractSpecialTag(string text, out string specialTag)
	{
		specialTag = string.Empty;

		int colonIndex = text.IndexOf(':');

		if (colonIndex >= 0 && text.AsSpan(0, colonIndex).IndexOf(' ') < 0)
		{
			specialTag = text[..(colonIndex + 1)];
			text = text[(colonIndex + 1)..];
		}

		return text.Trim(' ');
	}
}
