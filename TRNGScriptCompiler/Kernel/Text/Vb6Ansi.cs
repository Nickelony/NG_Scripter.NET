using System.Text;

namespace TRNGScriptCompiler.Kernel.Text;

/// <summary>
/// Converts between text and the Windows-1252 bytes VB6 reads and writes through the ANSI code page of the systems
/// TRNG targets.
/// </summary>
/// <remarks>
/// VB6 reads an unmarked file, decodes <c>Chr()</c> and writes every output string through that code
/// page (<c>Module1.bas:1992</c>, <c>2222</c>).
/// </remarks>
internal static class Vb6Ansi
{
	private static readonly string s_byteCharacters = BuildByteCharacters();

	/// <summary>
	/// Gets the Windows-1252 encoding, for a reader or writer that takes an <see cref="System.Text.Encoding"/>.
	/// </summary>
	internal static Encoding Encoding { get; } = GetCodePage();

	/// <summary>
	/// Returns the character a byte decodes to, as VB6's <c>Chr()</c> gives it for a <c>\xNN</c> escape.
	/// </summary>
	/// <returns>The character, which encodes back to <paramref name="value"/>.</returns>
	internal static char ToChar(byte value) => s_byteCharacters[value];

	/// <summary>Encodes text as Windows-1252 bytes.</summary>
	/// <returns>
	/// The bytes, with another character, such as <c>A</c> for <c>&#x0100;</c> or <c>?</c>, for a character
	/// <see cref="IsLossy"/> reports.
	/// </returns>
	internal static byte[] GetBytes(string text) => Encoding.GetBytes(text);

	/// <summary>
	/// Encodes text as Windows-1252 bytes into a buffer of at least <see cref="GetByteCount"/> bytes.
	/// </summary>
	/// <returns>The number of bytes written.</returns>
	internal static int GetBytes(string text, Span<byte> bytes) => Encoding.GetBytes(text, bytes);

	/// <summary>Counts the Windows-1252 bytes of text, one per character.</summary>
	internal static int GetByteCount(string text) => Encoding.GetByteCount(text);

	/// <summary>Decodes Windows-1252 bytes.</summary>
	/// <returns>The text, one character per byte.</returns>
	internal static string GetString(ReadOnlySpan<byte> bytes) => Encoding.GetString(bytes);

	/// <summary>
	/// Tests whether text holds a character Windows-1252 cannot represent, which an output would replace.
	/// </summary>
	/// <returns><see langword="true"/> when the text does not survive a Windows-1252 round trip.</returns>
	/// <remarks>
	/// Only a file with a byte-order mark can carry such a character, because VB6 reads every other byte
	/// through the ANSI code page.
	/// </remarks>
	internal static bool IsLossy(string text) =>
		text.Length > 0 && !Encoding.GetString(Encoding.GetBytes(text)).Equals(text, StringComparison.Ordinal);

	/// <summary>Formats the error for a value that holds a character <see cref="IsLossy"/> reports.</summary>
	/// <param name="value">The value as the author wrote it.</param>
	internal static string FormatLossyError(string value) =>
		$"'{value}' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character";

	/// <summary>
	/// Builds the byte table from the code page itself, so it does not depend on the order of the static initializers.
	/// </summary>
	private static string BuildByteCharacters()
	{
		byte[] bytes = new byte[256];

		for (int i = 0; i < bytes.Length; i++)
			bytes[i] = (byte)i;

		return GetCodePage().GetString(bytes);
	}

	/// <summary>
	/// Gets Windows-1252 from <see cref="CodePagesEncodingProvider.Instance"/>, so the process registers no provider.
	/// </summary>
	private static Encoding GetCodePage() =>
		CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? throw new InvalidOperationException("The Windows-1252 code page is not available.");
}
