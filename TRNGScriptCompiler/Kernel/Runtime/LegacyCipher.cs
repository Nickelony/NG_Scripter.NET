namespace TRNGScriptCompiler.Kernel.Runtime;

/// <summary>
/// The binary transforms of the VB6 compiler: the <c>Script.dat</c> header scramble and the encrypted-image transform.
/// </summary>
/// <remarks>
/// The transforms change a byte buffer in place and touch no file, so an output is encrypted in memory
/// before the publication writes it.
/// </remarks>
internal static class LegacyCipher
{
	/// <summary>The number of leading <c>Script.dat</c> bytes the header scramble operates on.</summary>
	internal const int ScriptDatHeaderLength = 64;

	/// <summary>The number of leading image bytes <see cref="EncryptImage"/> reverses.</summary>
	internal const int ImageReversedLength = 256;

	private static readonly byte[] s_imageXorMaskData =
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

	private static readonly byte[] s_scriptDatEncryptOrderData =
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

	private static readonly byte[] s_scriptDatXorTableData = [239, 85, 225, 248, 61, 111, 214, 25, 218, 151, 29, 139, 133, 15, 180, 10, 196];

	private static ReadOnlySpan<byte> ImageXorMask => s_imageXorMaskData;
	private static ReadOnlySpan<byte> ScriptDatEncryptOrder => s_scriptDatEncryptOrderData;
	private static ReadOnlySpan<byte> ScriptDatXorTable => s_scriptDatXorTableData;

	/// <summary>
	/// Applies the VB6 header scramble (byte permutation + XOR) to the first 64 bytes of a composed
	/// <c>Script.dat</c> image.
	/// </summary>
	/// <param name="header">The composed <c>Script.dat</c> bytes; at least 64 bytes are required.</param>
	internal static bool TryScrambleScriptDatHeader(Span<byte> header)
	{
		if (header.Length < ScriptDatEncryptOrder.Length)
			return false;

		Span<byte> scrambled = stackalloc byte[ScriptDatHeaderLength];

		for (int i = 0; i < scrambled.Length; i++)
			scrambled[i] = header[ScriptDatEncryptOrder[i]];

		for (int i = 0; i < scrambled.Length; i++)
			scrambled[i] ^= ScriptDatXorTable[i % ScriptDatXorTable.Length];

		scrambled.CopyTo(header);
		return true;
	}

	/// <summary>
	/// Undoes <see cref="TryScrambleScriptDatHeader"/> on the first 64 bytes of an encrypted <c>Script.dat</c>.
	/// </summary>
	/// <param name="header">The encrypted <c>Script.dat</c> bytes; at least 64 bytes are required.</param>
	internal static bool TryUnscrambleScriptDatHeader(Span<byte> header)
	{
		if (header.Length < ScriptDatEncryptOrder.Length)
			return false;

		Span<byte> plain = stackalloc byte[ScriptDatHeaderLength];

		for (int i = 0; i < plain.Length; i++)
			plain[ScriptDatEncryptOrder[i]] = (byte)(header[i] ^ ScriptDatXorTable[i % ScriptDatXorTable.Length]);

		plain.CopyTo(header);
		return true;
	}

	/// <summary>Applies the VB6 encrypted-image transformation to bitmap bytes in place.</summary>
	/// <remarks>
	/// The first <see cref="ImageReversedLength"/> bytes, or the whole image when smaller, are reversed,
	/// then every byte is XORed with the repeating mask.
	/// </remarks>
	internal static void EncryptImage(Span<byte> imageBytes)
	{
		int reversedByteCount = Math.Min(ImageReversedLength, imageBytes.Length);

		for (int index = 0; index < reversedByteCount / 2; index++)
			(imageBytes[index], imageBytes[reversedByteCount - 1 - index]) = (imageBytes[reversedByteCount - 1 - index], imageBytes[index]);

		for (int index = 0; index < imageBytes.Length; index++)
			imageBytes[index] ^= ImageXorMask[index % ImageXorMask.Length];
	}
}
