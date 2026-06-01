using System.Buffers.Binary;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Kernel.Infrastructure;

/// <summary>
/// Appends the values of the NG payload to its word list: dwords, bytes packed two to a word, fixed-width text,
/// section counts and chunks, and frames the list as an NG header.
/// </summary>
internal static class NgWords
{
	private const short NgMarker = 0x474E;

	/// <summary>The <c>NGLE</c> dword that ends an NG header, ahead of its size.</summary>
	internal const int NgLeMarker = 0x454C474E;

	private const int ShortFormLimit = 0x7FFF;

	/// <summary>Appends a dword as its low word, then its high word.</summary>
	internal static void AddDWord(this List<short> words, int value)
	{
		words.Add(unchecked((short)value));
		words.Add(unchecked((short)(value >> 16)));
	}

	/// <summary>
	/// Appends bytes packed two to a word, the first byte of each pair in the low byte; an odd last byte gets a zero
	/// high byte.
	/// </summary>
	internal static void AddPackedBytes(this List<short> words, ReadOnlySpan<byte> bytes)
	{
		for (int i = 0; i < bytes.Length; i += 2)
		{
			int high = i + 1 < bytes.Length ? bytes[i + 1] : 0;
			words.Add((short)(bytes[i] | (high << 8)));
		}
	}

	/// <summary>
	/// Appends a text field of <paramref name="totalWords"/> words in Windows-1252, cut to the width or padded
	/// with spaces.
	/// </summary>
	internal static void AddFixedLengthText(this List<short> words, string text, int totalWords)
	{
		byte[] bytes = Vb6Ansi.GetBytes(text);
		byte[] field = new byte[totalWords * 2];

		Array.Fill(field, (byte)0x20);
		bytes.AsSpan(0, Math.Min(bytes.Length, field.Length)).CopyTo(field);

		words.AddPackedBytes(field);
	}

	/// <summary>
	/// Appends a placeholder for a section's word count, which <see cref="PatchCount"/> writes once the section
	/// is complete.
	/// </summary>
	/// <returns>The index of the count word.</returns>
	internal static int ReserveCount(this List<short> words)
	{
		words.Add(0);
		return words.Count - 1;
	}

	/// <summary>
	/// Gets the word count of the section whose count word <see cref="ReserveCount"/>
	/// reserved at <paramref name="countIndex"/>: every word from it to the end, itself included.
	/// </summary>
	internal static int CountFrom(this List<short> words, int countIndex) => words.Count - countIndex;

	/// <summary>
	/// Writes the word count of the section into the count word <see cref="ReserveCount"/> reserved
	/// at <paramref name="countIndex"/>.
	/// </summary>
	/// <remarks>
	/// The caller checks the count against <see cref="short.MaxValue"/> with <see cref="CountFrom"/> first,
	/// because VB6 does.
	/// </remarks>
	internal static void PatchCount(this List<short> words, int countIndex) => words[countIndex] = (short)words.CountFrom(countIndex);

	/// <summary>Appends a chunk: its size, its tag, then its payload words.</summary>
	/// <remarks>
	/// The size counts every word of the chunk (Module1.bas:1895-1898). The long form has two size words,
	/// the first the high half with bit 15 set, and its size counts both. A chunk of more than 0x7FFF words,
	/// or one with <paramref name="longForm"/> set, takes the long form.
	/// </remarks>
	internal static void AddChunk(this List<short> words, short tag, ReadOnlySpan<short> payload, bool longForm = false)
	{
		int chunkWords = payload.Length + 2;

		if (longForm || chunkWords > ShortFormLimit)
		{
			chunkWords++;

			words.Add(unchecked((short)((chunkWords >> 16) | 0x8000)));
			words.Add(unchecked((short)chunkWords));
		}
		else
		{
			words.Add((short)chunkWords);
		}

		words.Add(tag);
		words.AddRange(payload);
	}

	/// <summary>Builds an NG header around the words of its payload.</summary>
	/// <remarks>
	/// The header holds the marker, the words, two zero words and the <c>NGLE</c> footer with its size.
	/// The payload holds whole words, so the word count cannot come out cut short.
	/// </remarks>
	internal static byte[] BuildHeaderBytes(ReadOnlySpan<short> words)
	{
		// The marker, the two zero words, and the footer's marker and size make 14 bytes beside the words.
		byte[] bytes = new byte[(words.Length * 2) + 14];
		int offset = 0;

		BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), NgMarker);
		offset += 2;

		foreach (short word in words)
		{
			BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), word);
			offset += 2;
		}

		// The two zero words stay as allocated.
		offset += 4;
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), NgLeMarker);
		offset += 4;

		int totalWords = 1 + words.Length + 2;
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), (totalWords * 2) + 8);

		return bytes;
	}
}
