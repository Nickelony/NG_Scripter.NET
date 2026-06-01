using System.Buffers.Binary;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Reads the language <c>.DAT</c> names an existing <c>Script.dat</c> lists, so a compile can find the language files
/// it replaces.
/// </summary>
/// <remarks>
/// An encrypted script scrambles its first 64 bytes, so when the plain header leads to no valid name list,
/// the reader unscrambles it and reads again.
/// </remarks>
internal static class ScriptDatLanguageNameReader
{
	private const int CountsOffset = 9;
	private const int LevelOffsetsOffset = 16 + (2 * ScriptDatEncoder.ExtensionFieldSize);

	/// <summary>Reads the language names of a <c>Script.dat</c>, such as <c>ENGLISH.DAT</c>, in file order.</summary>
	/// <returns>
	/// <see langword="true"/> when the bytes hold a valid name list,
	/// scrambled or not; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool TryRead(byte[] bytes, out IReadOnlyList<string> names)
	{
		if (bytes.Length < LegacyCipher.ScriptDatHeaderLength)
		{
			names = [];
			return false;
		}

		int end = FindClassicEnd(bytes);

		if (TryReadNames(bytes, end, out names))
			return true;

		byte[] unscrambled = (byte[])bytes.Clone();
		return LegacyCipher.TryUnscrambleScriptDatHeader(unscrambled) && TryReadNames(unscrambled, end, out names);
	}

	/// <summary>
	/// Finds the classic part's end: the NG header's start, or the file's end without an NG trailer, as in a stock
	/// TR4 script.
	/// </summary>
	private static int FindClassicEnd(byte[] bytes)
	{
		int trailer = bytes.Length - 8;

		if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(trailer)) != NgWords.NgLeMarker)
			return bytes.Length;

		int ngHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(trailer + 4));
		return ngHeaderSize > 0 && ngHeaderSize <= bytes.Length - LevelOffsetsOffset ? bytes.Length - ngHeaderSize : bytes.Length;
	}

	private static bool TryReadNames(byte[] bytes, int end, out IReadOnlyList<string> names)
	{
		names = [];

		int sectionCount = bytes[CountsOffset];
		int levelCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(CountsOffset + 1));
		int levelNamesSize = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(CountsOffset + 3));
		int sectionsSize = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(CountsOffset + 5));

		if (levelCount < 0 || levelNamesSize < 0 || sectionsSize < 0)
			return false;

		int position = LevelOffsetsOffset + (2 * levelCount) + levelNamesSize + (2 * sectionCount) + sectionsSize;

		if (position >= end)
			return false;

		var list = new List<string>();

		while (position < end)
		{
			int terminator = Array.IndexOf(array: bytes, value: (byte)0, startIndex: position, count: end - position);

			if (terminator <= position)
				return false;

			string name = Vb6Ansi.GetString(bytes.AsSpan(position, terminator - position));

			if (!Path.GetExtension(name).IgnoreCaseEquals(".dat"))
				return false;

			list.Add(name);
			position = terminator + 1;
		}

		names = list;
		return true;
	}
}
