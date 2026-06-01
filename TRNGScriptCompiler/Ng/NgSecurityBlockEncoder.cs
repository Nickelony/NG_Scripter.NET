using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Numerics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Encodes the NG security header block, a randomized preamble that carries the level count,
/// option flags and NG settings.
/// </summary>
/// <remarks>
/// The encoder takes the VB6-compatible PRNG explicitly, so the block can be reproduced from a seed in isolation.
/// </remarks>
internal static class NgSecurityBlockEncoder
{
	private static readonly byte[] s_securityXorTableData = [27, 48, 38, 153, 58, 77, 42, 58, 66, 45, 55, 22, 55];

	private static ReadOnlySpan<byte> SecurityXorTable => s_securityXorTableData;

	/// <summary>Appends the security header block for the script.</summary>
	/// <remarks>
	/// The block's size and padding come from <paramref name="random"/>, the run's VB6 generator,
	/// which repeats its draws only when the options set a seed.
	/// </remarks>
	internal static void Encode(List<short> allWords, ScriptModel scriptModel, Vb6Random random)
	{
		NgScriptModel ngModel = scriptModel.NgModel;

		int totalWords = random.Next(9) + 20;
		int totalBytes = totalWords * 2;

		byte[] dataBytes = new byte[totalBytes];

		for (int i = 0; i < totalBytes; i++)
			dataBytes[i] = i % 2 == 0 ? (byte)(random.Next(2) == 1 ? 6 : 4) : (byte)random.Next(16);

		// One per Level=, so two levels that share a file count twice (Module1.bas:8004).
		dataBytes[5] = (byte)scriptModel.Parsed.LevelPaths.Count;

		dataBytes[12] = (byte)scriptModel.Parsed.Options.Flags;
		dataBytes[19] = (byte)ngModel.NgSettings;

		int checksum = 0;

		for (int i = 1; i < totalBytes; i++)
			checksum += dataBytes[i];

		dataBytes[0] = (byte)(checksum & 0xFF);

		int xorIndex = 0;

		for (int i = 1; i < totalBytes; i++)
		{
			if (xorIndex == SecurityXorTable.Length)
				xorIndex = 0;

			dataBytes[i] ^= SecurityXorTable[xorIndex];
			xorIndex++;
		}

		List<short> payload = new(totalWords);
		payload.AddPackedBytes(dataBytes);

		allWords.AddChunk(unchecked((short)NgTagType.Security), CollectionsMarshal.AsSpan(payload));
	}
}
