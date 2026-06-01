namespace TRNGScriptCompiler.Tests.Support;

/// <summary>Decodes the data bytes of the NG security block, as VB6 encodes them.</summary>
/// <remarks>
/// The key is the table VB6 XORs every byte of the block but the first with, in turn (Module1.bas:7978, 8021-8026).
/// </remarks>
internal static class SecurityBlockDecoder
{
	private static readonly byte[] s_key = [27, 48, 38, 153, 58, 77, 42, 58, 66, 45, 55, 22, 55];

	/// <summary>Returns the plain bytes of a block.</summary>
	/// <param name="encoded">The data bytes of the block, which follow its size word and its tag.</param>
	internal static byte[] Decode(ReadOnlySpan<byte> encoded)
	{
		byte[] plain = encoded.ToArray();

		for (int i = 1; i < plain.Length; i++)
			plain[i] ^= s_key[(i - 1) % s_key.Length];

		return plain;
	}
}
