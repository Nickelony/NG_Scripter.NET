using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler.Tests.Kernel.Runtime;

/// <summary>Pins the pure VB6 cipher transforms.</summary>
/// <remarks>
/// The expected values are computed here from independent copies of the VB6 order/XOR tables,
/// so tampering with the production tables or reordering the transform steps fails these tests.
/// </remarks>
[TestClass]
[TestCategory("Fast")]
public sealed class LegacyCipherTests
{
	// Independent copies of the VB6 goldens (Module1.bas encryption tables).
	private static readonly int[] s_expectedScrambleOrder =
	[
		57,
		49,
		1,
		7,
		36,
		37,
		0,
		17,
		45,
		13,
		40,
		44,
		46,
		33,
		30,
		34,
		20,
		41,
		26,
		19,
		59,
		53,
		43,
		2,
		22,
		6,
		23,
		9,
		31,
		10,
		21,
		15,
		5,
		8,
		42,
		24,
		55,
		14,
		48,
		56,
		47,
		60,
		12,
		39,
		28,
		32,
		16,
		27,
		52,
		35,
		62,
		58,
		63,
		11,
		18,
		38,
		4,
		54,
		50,
		61,
		51,
		25,
		29,
		3,
	];

	private static readonly byte[] s_expectedXorTable = [239, 85, 225, 248, 61, 111, 214, 25, 218, 151, 29, 139, 133, 15, 180, 10, 196];

	private static readonly byte[] s_expectedImageMask =
	[
		143,
		201,
		42,
		255,
		43,
		173,
		99,
		243,
		3,
		196,
		51,
		200,
		188,
		213,
		85,
		143,
		201,
		66,
		45,
		215,
		213,
		17,
		51,
		200,
		188,
		213,
		85,
		143,
		99,
		243,
		3,
		196,
	];

	private static byte[] CreateSequence(int length)
	{
		byte[] bytes = new byte[length];

		for (int i = 0; i < length; i++)
			bytes[i] = (byte)i;

		return bytes;
	}

	[TestMethod]
	public void TryScrambleScriptDatHeader_ShorterThan64Bytes_FailsAndLeavesInputUntouched()
	{
		byte[] header = CreateSequence(63);

		Assert.IsFalse(LegacyCipher.TryScrambleScriptDatHeader(header), "Headers shorter than the 64-byte scramble window must be rejected.");
		Assert.AreSequenceEqual(CreateSequence(63), header, "A rejected scramble must not modify the input.");
	}

	[TestMethod]
	public void TryScrambleScriptDatHeader_ReordersThenXorsFirst64Bytes()
	{
		byte[] header = CreateSequence(80);
		byte[] expected = new byte[80];

		for (int i = 0; i < LegacyCipher.ScriptDatHeaderLength; i++)
			expected[i] = (byte)(header[s_expectedScrambleOrder[i]] ^ s_expectedXorTable[i % s_expectedXorTable.Length]);

		// Bytes beyond the 64-byte window are never touched.
		for (int i = LegacyCipher.ScriptDatHeaderLength; i < header.Length; i++)
			expected[i] = header[i];

		Assert.IsTrue(LegacyCipher.TryScrambleScriptDatHeader(header));

		Assert.AreSequenceEqual(
			expected,
			header,
			"The scramble must apply the VB6 byte order first, then the repeating XOR table, over the first 64 bytes only."
		);
	}

	[TestMethod]
	public void TryUnscrambleScriptDatHeader_RestoresTheScrambledBytes()
	{
		byte[] original = CreateSequence(80);
		byte[] header = (byte[])original.Clone();

		Assert.IsTrue(LegacyCipher.TryScrambleScriptDatHeader(header));
		Assert.IsTrue(LegacyCipher.TryUnscrambleScriptDatHeader(header));

		Assert.AreSequenceEqual(original, header);
	}

	[TestMethod]
	public void EncryptImage_ReversesFirst256BytesAndMasksEveryByte()
	{
		byte[] image = CreateSequence(300);
		byte[] reversed = new byte[300];

		for (int i = 0; i < 256; i++)
			reversed[i] = image[255 - i];

		for (int i = 256; i < image.Length; i++)
			reversed[i] = image[i];

		byte[] expected = new byte[300];

		for (int i = 0; i < image.Length; i++)
			expected[i] = (byte)(reversed[i] ^ s_expectedImageMask[i % s_expectedImageMask.Length]);

		LegacyCipher.EncryptImage(image);

		Assert.AreSequenceEqual(
			expected,
			image,
			"The image transform must reverse the first 256 bytes, then XOR every byte with the repeating mask."
		);
	}

	[TestMethod]
	public void EncryptImage_ShorterThan256Bytes_ReversesWholeImageThenMasks()
	{
		byte[] image = CreateSequence(100);
		byte[] expected = new byte[100];

		for (int i = 0; i < image.Length; i++)
			expected[i] = (byte)(image[99 - i] ^ s_expectedImageMask[i % s_expectedImageMask.Length]);

		LegacyCipher.EncryptImage(image);
		Assert.AreSequenceEqual(expected, image, "Small images must be reversed in full before the mask is applied.");
	}
}
