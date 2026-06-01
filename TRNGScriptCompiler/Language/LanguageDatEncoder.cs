using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Language;

/// <summary>Encodes a parsed language into the bytes of its <c>.dat</c> file.</summary>
/// <remarks>
/// The bytes hold the classic string tables, their offsets and the ExtraNG header, and an error reaches
/// the run's diagnostics.
/// </remarks>
internal sealed class LanguageDatEncoder
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;

	internal LanguageDatEncoder(CompilerLog logger, DiagnosticSink diagnostics)
	{
		_logger = logger;
		_diagnostics = diagnostics;
	}

	/// <summary>
	/// Encodes a language payload with any ExtraNG header, reading <c>@file</c> texts beside the language file.
	/// </summary>
	/// <remarks>
	/// The loader reads an <c>@file</c> text from its folder and the name the string writes,
	/// and returns <see langword="null"/> for a missing file.
	/// </remarks>
	internal bool TryEncode(LanguageData languageData, Func<string, string, string?> extraStringFileLoader, out byte[] bytes)
	{
		bytes = [];

		if (!TryEncodeStrings(languageData, out byte[][] encodedStrings))
			return false;

		// One size per table and one offset per string, each the decoded byte count plus the terminating zero, as VB6 counts them
		// (Module1.bas:2059-2063).
		int[] tableCounts = [languageData.TotalSectionStrings, languageData.TotalPsxStrings, languageData.TotalPcStrings];

		int[] sectionSizes = new int[tableCounts.Length];
		int[] offsets = new int[encodedStrings.Length];

		int offset = 0;

		for (int table = 0, index = 0; table < tableCounts.Length; table++)
		{
			for (int end = index + tableCounts[table]; index < end; index++)
			{
				offsets[index] = offset;
				offset += encodedStrings[index].Length + 1;
				sectionSizes[table] += encodedStrings[index].Length + 1;
			}
		}

		// The .dat holds each section size and string offset in a 16-bit word (Module1.bas:1784-1798), which VB6 cuts without a word.
		if (Math.Max(sectionSizes.Max(), offsets.Length > 0 ? offsets[^1] : 0) > ushort.MaxValue)
		{
			_diagnostics.Error(
				$"The classic strings take {offset} bytes, so a section size or a string offset passes the 65,535 a language .dat can hold; shorten or remove strings",
				SourceLocation.InFile(languageData.LanguageFile)
			);

			return false;
		}

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Vb6Ansi.Encoding, leaveOpen: true);

		foreach (int count in tableCounts)
			writer.Write((short)count);

		foreach (int size in sectionSizes)
			writer.Write((short)size);

		foreach (int stringOffset in offsets)
			writer.Write((short)stringOffset);

		foreach (byte[] encodedString in encodedStrings)
		{
			writer.Write(encodedString);
			writer.Write((byte)0);
		}

		if (languageData.ExtraStrings.Count > 0)
		{
			_logger.LogVerbose($"Writing extra NG header ({languageData.ExtraStrings.Count} strings)", indent: 1);

			if (!TryEncodeExtraNgHeader(languageData, extraStringFileLoader, bytes: out byte[] extraHeaderBytes))
				return false;

			writer.Write(extraHeaderBytes);
		}

		writer.Flush();
		bytes = stream.ToArray();

		return true;
	}

	/// <summary>
	/// Decodes and encrypts each classic string, which the language file holds with its escapes intact.
	/// </summary>
	private bool TryEncodeStrings(LanguageData languageData, out byte[][] encodedStrings)
	{
		encodedStrings = new byte[languageData.Strings.Count][];

		for (int i = 0; i < encodedStrings.Length; i++)
		{
			var location = new SourceLocation(
				i < languageData.StringLines.Count ? languageData.StringLines[i] : null,
				File: languageData.LanguageFile
			);

			if (!LanguageEscapeCodec.TryDecodeClassic(languageData.Strings[i], out string text, out _))
			{
				_diagnostics.Error(LanguageEscapeCodec.FormatInvalidEscapeError($"Language string {i}, '{languageData.Strings[i]}',"), location);
				return false;
			}

			// Such a character needs a byte-order mark (see Vb6Ansi.IsLossy).
			if (Vb6Ansi.IsLossy(text))
			{
				_diagnostics.Error(Vb6Ansi.FormatLossyError(languageData.Strings[i]), location);
				return false;
			}

			encodedStrings[i] = EncryptString(text);
		}

		return true;
	}

	private bool TryEncodeExtraNgHeader(LanguageData languageData, Func<string, string, string?> extraStringFileLoader, out byte[] bytes)
	{
		bytes = [];
		List<short> extraWords = [(short)languageData.ExtraStrings.Count];

		foreach (ExtraNgString extraString in languageData.ExtraStrings)
		{
			extraWords.Add((short)extraString.Index);

			string text = extraString.Text;

			// An error about the text, or about the file it names, points at the entry's line.
			SourceLocation location = extraString.Location;

			if (text.StartsWith('@'))
			{
				string fileName = text[1..];

				string scriptDir = Path.GetDirectoryName(languageData.LanguageFile) ?? string.Empty;
				string? loadedText = extraStringFileLoader(scriptDir, fileName);

				if (loadedText is null)
				{
					_diagnostics.Error($"Could not find referenced file: {fileName}", location);
					return false;
				}

				// VB6 trims only the spaces at the two ends of the whole text, in the decode below, and keeps every line's own spaces
				// (Module1.bas:1865-1867, 2182).
				text = loadedText.Replace("\r\n", "\n", StringComparison.Ordinal);

				// The text runs through the comment strip below, which drops everything from its first ';' (Module1.bas:1874).
				if (text.Contains(';'))
				{
					_diagnostics.Error(
						$"The file '{fileName}' of ExtraNG string {extraString.Index} holds a ';', where its text would end; write \\x3B in its place",
						location
					);

					return false;
				}
			}

			if (!LanguageEscapeCodec.TryDecodeExtraNg(text, out text))
			{
				_diagnostics.Error(LanguageEscapeCodec.FormatInvalidEscapeError($"ExtraNG string {extraString.Index}"), location);
				return false;
			}

			if (LanguageEscapeCodec.FindStringEnd(text) is { } stringEnd)
			{
				_diagnostics.Error(LanguageEscapeCodec.FormatStringEndError(extraString.Text, stringEnd), location);
				return false;
			}

			if (Vb6Ansi.IsLossy(text))
			{
				_diagnostics.Error(Vb6Ansi.FormatLossyError($"{extraString.Index}: {extraString.Text}"), location);
				return false;
			}

			short[] words = ConvertStringToWords(text);

			// The word count is a 16-bit value, which VB6 cuts without a word (Module1.bas:1879).
			if (words.Length > ushort.MaxValue)
			{
				_diagnostics.Error(
					$"ExtraNG string {extraString.Index} takes {words.Length} words, so its length passes the 65,535 a string can hold; shorten it or split it into two strings",
					location
				);

				return false;
			}

			extraWords.Add((short)words.Length);
			extraWords.AddRange(words);
		}

		const short languageStringsTag = unchecked((short)0x800A);
		var chunkWords = new List<short>();
		chunkWords.AddChunk(languageStringsTag, CollectionsMarshal.AsSpan(extraWords));
		bytes = NgWords.BuildHeaderBytes(CollectionsMarshal.AsSpan(chunkWords));

		return true;
	}

	/// <summary>
	/// Encodes a decoded classic string as the Windows-1252 bytes the <c>.dat</c> file stores,
	/// each one XOR <c>0xA5</c>.
	/// </summary>
	/// <returns>The stored bytes, without the zero terminator.</returns>
	internal static byte[] EncryptString(string text)
	{
		byte[] bytes = Vb6Ansi.GetBytes(text);

		for (int i = 0; i < bytes.Length; i++)
			bytes[i] ^= 0xA5;

		return bytes;
	}

	/// <summary>
	/// Converts text to little-endian words with the ExtraNG XOR obfuscation applied to non-zero bytes
	/// (VB6's ExtraNG string encoding).
	/// </summary>
	private static short[] ConvertStringToWords(string text)
	{
		byte[] bytes = Vb6Ansi.GetBytes(text);

		// The string ends at a zero byte, and an even length takes a second zero byte to fill the last word (TR_Explorer/Mod_NextGeneration.bas:2073-2080).
		short[] words = new short[(bytes.Length / 2) + 1];

		for (int i = 0; i < words.Length; i++)
			words[i] = (short)(ObfuscatedByteAt(bytes, i * 2) | (ObfuscatedByteAt(bytes, (i * 2) + 1) << 8));

		return words;
	}

	/// <summary>
	/// Obfuscates the byte at an index with 0xA5, keeping a zero byte and the padding past the text zero.
	/// </summary>
	private static int ObfuscatedByteAt(byte[] bytes, int index) => index < bytes.Length && bytes[index] != 0 ? bytes[index] ^ 0xA5 : 0;
}
