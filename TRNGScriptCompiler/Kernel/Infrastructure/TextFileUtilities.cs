using System.Text;
using System.Text.Unicode;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Kernel.Infrastructure;

/// <summary>Reads TRNG text inputs as Windows-1252 unless a Unicode byte-order mark names another encoding.</summary>
internal static class TextFileUtilities
{
	/// <summary>
	/// Reads a text file without its byte-order mark, decoded as that mark names or as Windows-1252 without one.
	/// </summary>
	/// <exception cref="IOException">
	/// The file could not be read, or it holds more than the limit of one input file.
	/// </exception>
	/// <remarks>
	/// <paramref name="encodingNote"/> is a note on a found mark, a warning naming the fix
	/// for unmarked non-ASCII UTF-8, or <see langword="null"/>.
	/// </remarks>
	internal static string ReadAllText(string path, out string? encodingNote)
	{
		byte[] bytes = InputFileReader.ReadAllBytes(path);

		encodingNote = null;

		if (GetByteOrderMark(bytes) is ({ } encoding, int markLength, string markName, string codecName))
		{
			encodingNote = $"'{Path.GetFileName(path)}' has a {markName} byte-order mark; the file will be decoded as {codecName}";
			return encoding.GetString(bytes, markLength, bytes.Length - markLength);
		}

		if (!Ascii.IsValid(bytes) && Utf8.IsValid(bytes))
		{
			// VB6 reads every byte through the ANSI code page (Module1.bas:5150, 1992), as this file is read.
			encodingNote =
				$"'{Path.GetFileName(path)}' looks like UTF-8 without a byte-order mark, so it is read as Windows-1252 and each character outside "
				+ "ASCII becomes several wrong ones; save the file as ANSI (Windows-1252), or as UTF-8 with a byte-order mark";
		}

		return Vb6Ansi.GetString(bytes);
	}

	/// <summary>
	/// Splits text into lines as <see cref="File.ReadAllLines(string)"/> does, so a trailing terminator adds no line.
	/// </summary>
	/// <remarks>
	/// CRLF, LF and CR each end a line, and every text input, from disk or a host's overlay,
	/// is split here, so both give the same lines.
	/// </remarks>
	internal static string[] SplitLines(string text)
	{
		if (text.Length == 0)
			return [];

		string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
		string[] parts = normalized.Split('\n');
		int count = parts.Length;

		if (parts[count - 1].Length == 0)
			count--;

		string[] lines = new string[count];
		Array.Copy(parts, lines, count);

		return lines;
	}

	private static (Encoding Encoding, int MarkLength, string MarkName, string CodecName)? GetByteOrderMark(ReadOnlySpan<byte> bytes) =>
		// Each mark reads in either byte order, and the UTF-32 little-endian mark is tested before the UTF-16 one it starts with (PD-27).
		bytes switch
		{
			[0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false), 3, "UTF-8", "UTF-8"),
			[0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(bigEndian: false, byteOrderMark: false), 4, "UTF-32 (little-endian)", "UTF-32"),
			[0xFF, 0xFE, ..] => (Encoding.Unicode, 2, "UTF-16 (little-endian)", "UTF-16"),
			[0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, 2, "UTF-16 (big-endian)", "UTF-16"),
			[0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(bigEndian: true, byteOrderMark: false), 4, "UTF-32 (big-endian)", "UTF-32"),
			_ => null,
		};

	/// <summary>The error for text after a Ctrl-Z, which <see cref="TryCutAtEndOfFileMark"/> rejects.</summary>
	/// <remarks>
	/// Only <c>script.txt</c>, its includes and the language files are cut this way; <c>Objects.h</c> and <c>@file</c>
	/// texts keep the byte.
	/// </remarks>
	internal const string TextAfterEndOfFileMarkError =
		"Text after a Ctrl-Z byte would be dropped, because the Ctrl-Z ends the file; remove the Ctrl-Z byte";

	/// <summary>Cuts text at its first Ctrl-Z, which VB6's <c>Line Input</c> reads as the end of the file.</summary>
	/// <remarks>
	/// The mark's line is 1-based, or 0 when the text has none, and the cut fails when anything but white space follows
	/// the mark.
	/// </remarks>
	internal static bool TryCutAtEndOfFileMark(string text, out string content, out int markLineNumber)
	{
		int markIndex = text.IndexOf('\u001A');

		if (markIndex < 0)
		{
			content = text;
			markLineNumber = 0;
			return true;
		}

		content = text[..markIndex];
		markLineNumber = content.Replace("\r\n", "\n").Split('\n', '\r').Length;

		return string.IsNullOrWhiteSpace(text[(markIndex + 1)..]);
	}
}
