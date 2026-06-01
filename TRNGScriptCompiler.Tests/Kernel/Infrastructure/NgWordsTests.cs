using TRNGScriptCompiler.Kernel.Infrastructure;

namespace TRNGScriptCompiler.Tests.Kernel.Infrastructure;

[TestClass]
[TestCategory("Fast")]
public sealed class NgWordsTests
{
	[TestMethod]
	public void AddDWord_AppendsTheLowWordThenTheHighWord()
	{
		var words = new List<short>();

		words.AddDWord(0x12345678);
		words.AddDWord(-2);

		Assert.AreSequenceEqual(new short[] { 0x5678, 0x1234, -2, -1 }, words);
	}

	[TestMethod]
	public void AddPackedBytes_PutsTheFirstByteLow_AndPadsAnOddLastByteWithZero()
	{
		// VB6 packs a byte array's count and bytes two to a word and pads an odd count with a zero byte (Module1.bas:7277-7281).
		var words = new List<short>();

		words.AddPackedBytes([0x01, 0x02, 0xFF]);

		Assert.AreSequenceEqual(new short[] { 0x0201, 0x00FF }, words);
	}

	[TestMethod]
	public void AddFixedLengthText_PadsRemainingBytesWithSpaces()
	{
		var words = new List<short>();

		words.AddFixedLengthText("A\0", totalWords: 2);

		Assert.AreSequenceEqual(new short[] { 0x0041, 0x2020 }, words, "The copied bytes stay and the rest of the field is spaces.");
	}

	[TestMethod]
	public void AddFixedLengthText_AlwaysFillsTheFullFieldWidth()
	{
		var words = new List<short>();

		words.AddFixedLengthText("ABCDE", totalWords: 2);

		Assert.AreSequenceEqual(new short[] { 0x4241, 0x4443 }, words, "'ABCDE' is cut to two words ('AB', 'CD').");
	}

	[TestMethod]
	public void AddFixedLengthText_PadsOddByteCountWithSpace()
	{
		var words = new List<short>();

		words.AddFixedLengthText("ABC", totalWords: 3);

		Assert.AreSequenceEqual(new short[] { 0x4241, 0x2043, 0x2020 }, words, "'ABC' fills one and a half words; the rest is spaces (0x20).");
	}

	[TestMethod]
	public void PatchCount_WritesTheWordsFromTheCountToTheEnd_ItselfIncluded()
	{
		var words = new List<short> { 7 };

		int countIndex = words.ReserveCount();

		words.Add(unchecked((short)0x800C));
		words.Add(5);

		Assert.AreEqual(1, countIndex);
		Assert.AreEqual(3, words.CountFrom(countIndex));

		words.PatchCount(countIndex);

		Assert.AreSequenceEqual(new short[] { 7, 3, unchecked((short)0x800C), 5 }, words);
	}

	[TestMethod]
	public void AddChunk_WritesShortSizeAndTag()
	{
		var words = new List<short>();

		words.AddChunk(unchecked((short)0x800A), [0x1234, 0x5678]);

		Assert.AreSequenceEqual(
			new short[] { 4, unchecked((short)0x800A), 0x1234, 0x5678 },
			words,
			"The size counts the size word, the tag and the payload."
		);
	}

	[TestMethod]
	public void AddChunk_LongFormSize_IsUsedWhenWordCountExceeds15Bits()
	{
		// 0x7FFE payload words plus the size and the tag make 0x8000, which does not fit 15 bits, so VB6 counts the second
		// size word as well (Module1.bas:1895-1898) and writes (size >> 16 | 0x8000) first, then the low word.
		var words = new List<short>();

		words.AddChunk(unchecked((short)0x8001), new short[0x7FFE]);

		Assert.AreSequenceEqual([unchecked((short)0x8000), unchecked((short)0x8001), unchecked((short)0x8001)], words[..3]);
		Assert.HasCount(3 + 0x7FFE, words, "A long-form chunk holds both size words, the tag and the payload.");
	}

	[TestMethod]
	public void AddChunk_ShortFormSize_AtThe15BitBoundary()
	{
		// 0x7FFD payload words -> 0x7FFF total words: still short form.
		var words = new List<short>();

		words.AddChunk(unchecked((short)0x8002), new short[0x7FFD]);

		Assert.AreSequenceEqual(new short[] { 0x7FFF, unchecked((short)0x8002) }, words[..2]);
		Assert.HasCount(2 + 0x7FFD, words);
	}

	[TestMethod]
	public void AddChunk_LongFormRequested_WritesTwoSizeWordsForASmallChunk()
	{
		var words = new List<short>();

		words.AddChunk(unchecked((short)0x800B), [7], longForm: true);

		Assert.AreSequenceEqual(
			new short[] { unchecked((short)0x8000), 4, unchecked((short)0x800B), 7 },
			words,
			"The long size counts both size words."
		);
	}

	[TestMethod]
	public void BuildHeaderBytes_WritesMarkerFooterAndSize()
	{
		byte[] bytes = NgWords.BuildHeaderBytes([0x1111, 0x2222, 0x3333]);

		Assert.AreSequenceEqual("NG"u8.ToArray(), bytes[..2], "Header should begin with the NG marker.");
		Assert.AreSequenceEqual(new byte[] { 0x11, 0x11, 0x22, 0x22, 0x33, 0x33 }, bytes[2..8], "The words follow the marker in order.");
		Assert.AreSequenceEqual(new byte[] { 0, 0, 0, 0 }, bytes[8..12], "Two zero words precede the footer.");
		Assert.AreSequenceEqual("NGLE"u8.ToArray(), bytes[^8..^4], "Header footer should write NGLE in little endian.");
		Assert.AreSequenceEqual(BitConverter.GetBytes(20), bytes[^4..], "Footer size should match the encoded header word count.");
	}
}
