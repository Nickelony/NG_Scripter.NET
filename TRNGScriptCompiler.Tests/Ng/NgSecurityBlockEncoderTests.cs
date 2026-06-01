using TRNGScriptCompiler.Kernel.Numerics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgSecurityBlockEncoderTests
{
	private static readonly ScriptModel s_script = new(
		new ParsedScript
		{
			Options = new ScriptOptions { Flags = ScriptMainFlags.LoadSave | ScriptMainFlags.Title },
			Sections = [new ParsedSection { LevelPath = @"data\title" }, new ParsedSection(), new ParsedSection { LevelPath = @"data\test" }],
		},

		new NgScriptModel { NgSettings = 9 }
	);

	private static List<short> Encode(double seed)
	{
		var words = new List<short>();
		NgSecurityBlockEncoder.Encode(words, s_script, new Vb6Random(seed));
		return words;
	}

	private static List<short> EncodeUnseeded(double timerSeconds)
	{
		var words = new List<short>();
		NgSecurityBlockEncoder.Encode(words, s_script, new Vb6Random(seed: null, timerSeconds: () => timerSeconds));
		return words;
	}

	[TestMethod]
	[DataRow(12345.0)]
	[DataRow(54321.0)]
	[DataRow(99999.0)]
	public void Block_CarriesTheLevelCountTheOptionsAndTheSettings_UnderItsChecksum(double seed)
	{
		// VB6 fills 20 to 28 words with random bytes, 4 or 6 at even places and below 16 at odd ones (Module1.bas:7984-8001). It stores the
		// level count at byte 5, the options at 12, the settings at 19 and the checksum of the other bytes at 0, then XORs every byte after the
		// first with a table of 13 (:8002-8042).
		List<short> words = Encode(seed);
		int dataWords = words[0] - 2;
		byte[] bytes = SecurityBlockDecoder.Decode([.. words.Skip(2).SelectMany(word => new[] { (byte)word, (byte)(word >> 8) })]);

		Assert.IsInRange(minValue: 20, maxValue: 28, value: dataWords, message: $"{dataWords} data words");
		Assert.AreEqual((dataWords + 2, unchecked((short)NgTagType.Security)), (words.Count, words[1]));
		Assert.AreEqual((2, 6, 9), ((int)bytes[5], (int)bytes[12], (int)bytes[19]));
		Assert.AreEqual((byte)bytes.Skip(1).Sum(value => value), bytes[0]);

		for (int i = 2; i < bytes.Length; i++)
		{
			if (i is 5 or 12 or 19)
				continue;

			Assert.IsTrue(i % 2 == 0 ? bytes[i] is 4 or 6 : bytes[i] < 16, $"Byte {i} is {bytes[i]}");
		}
	}

	[TestMethod]
	public void Block_DependsOnlyOnTheSeed()
	{
		// VB6 draws every padding byte from Rnd (Casuale, Module1.bas:7984-7999), so one seed writes one block.
		Assert.AreSequenceEqual(Encode(99999), Encode(99999));
		Assert.AreNotSequenceEqual(Encode(99999), Encode(12345));
	}

	[TestMethod]
	public void Block_WithoutASeed_TakesItsPaddingFromTheTimer()
	{
		// Pristine VB6 calls Randomize with no argument before each draw (PD-07), which seeds from Timer, so the padding follows the clock.
		Assert.AreSequenceEqual(EncodeUnseeded(40000.25), EncodeUnseeded(40000.25));
		Assert.AreNotSequenceEqual(EncodeUnseeded(40000.25), EncodeUnseeded(61234.5));
	}
}
