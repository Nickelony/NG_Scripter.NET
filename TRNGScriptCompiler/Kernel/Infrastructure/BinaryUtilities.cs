using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Kernel.Infrastructure;

/// <summary>Provides low-level helpers for TRNG binary encoding.</summary>
internal static class BinaryUtilities
{
	internal static void WriteNullTerminatedString(BinaryWriter writer, string text) => writer.Write(GetNullTerminatedBytes(text));

	/// <summary>Returns the Windows-1252 bytes for a string followed by a terminating zero byte.</summary>
	internal static byte[] GetNullTerminatedBytes(string text)
	{
		// Encode directly into the exactly-sized result (single allocation).
		byte[] result = GC.AllocateUninitializedArray<byte>(Vb6Ansi.GetByteCount(text) + 1);
		Vb6Ansi.GetBytes(text, result);
		result[^1] = 0;
		return result;
	}

	/// <summary>Appends a word as two little-endian bytes.</summary>
	internal static void WriteWord(List<byte> target, short value)
	{
		target.Add((byte)(value & 0xFF));
		target.Add((byte)((value >> 8) & 0xFF));
	}

	/// <summary>Appends a dword as four little-endian bytes.</summary>
	internal static void WriteDWord(List<byte> target, int value)
	{
		target.Add((byte)(value & 0xFF));
		target.Add((byte)((value >> 8) & 0xFF));
		target.Add((byte)((value >> 16) & 0xFF));
		target.Add((byte)((value >> 24) & 0xFF));
	}
}
