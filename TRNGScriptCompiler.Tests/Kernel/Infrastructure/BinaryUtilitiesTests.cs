using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Tests.Kernel.Infrastructure;

[TestClass]
[TestCategory("Fast")]
public sealed class BinaryUtilitiesTests
{
	[TestMethod]
	public void NullTerminatedString_IsWrittenInWindows1252WithATerminator()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Vb6Ansi.Encoding, leaveOpen: true);

		BinaryUtilities.WriteNullTerminatedString(writer, "Caf\u00E9");
		writer.Flush();

		byte[] expected = [0x43, 0x61, 0x66, 0xE9, 0x00];
		Assert.AreSequenceEqual(expected, stream.ToArray(), "Strings are written in Windows-1252 with a NUL terminator.");
	}

	[TestMethod]
	public void WriteWordAndDWord_WriteLittleEndian()
	{
		var bytes = new List<byte>();

		BinaryUtilities.WriteWord(bytes, unchecked((short)0x1234));
		BinaryUtilities.WriteDWord(bytes, 0x12345678);

		Assert.AreSequenceEqual(new byte[] { 0x34, 0x12, 0x78, 0x56, 0x34, 0x12 }, bytes, "Both words and dwords must be little-endian.");
	}
}
