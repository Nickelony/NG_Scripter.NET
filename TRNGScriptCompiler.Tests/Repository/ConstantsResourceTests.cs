using System.Buffers.Binary;
using System.Text;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>The shipped <c>scripter_constants.txt</c> against the constants VB6 builds into NG Center.</summary>
[TestClass]
[TestCategory("Repository")]
public sealed class ConstantsResourceTests
{
	// The XOR key VB6 decrypts resource 101 with (Module1.bas:2447).
	private static readonly byte[] s_key = [0x37, 0x78, 0x77, 0x5C, 0x87, 0x73, 0x55, 0x0F, 0xC5];

	[TestMethod]
	public void ShippedConstantsFile_IsTheResourceVb6BuildsIn()
	{
		// VB6 loads its constants from resource 101 of type CUSTOM (Module1.bas:2497) and XORs each byte with the key in turn
		// (:2499-2503), so the stock file the port ships must hold the same bytes (PD-28).
		string resourcePath = RepositoryPaths.Below("Original Compiler", "Progetti", "NG_Scripter", "NG_Scripter.RES");

		if (!File.Exists(resourcePath))
			Assert.Inconclusive($"The VB6 sources are not checked in, so {resourcePath} is absent and the stock constants file is not compared.");

		byte[] resource = FindResource(File.ReadAllBytes(resourcePath), "CUSTOM", 101);

		for (int i = 0; i < resource.Length; i++)
			resource[i] ^= s_key[i % s_key.Length];

		byte[] shipped = File.ReadAllBytes(RepositoryPaths.Below("TRNGScriptCompiler", "scripter_constants.txt"));

		Assert.HasCount(resource.Length, shipped, "The shipped constants file must be as long as the resource.");
		Assert.AreSequenceEqual(resource, shipped);
	}

	// A .RES file is a run of entries: the data size and the header size (two 32-bit words), the type and the name, each a 0xFFFF
	// ordinal or a zero-ended UTF-16 string, then the rest of the header and the data, each entry aligned to 4 bytes.
	private static byte[] FindResource(byte[] file, string type, int name)
	{
		int offset = 0;

		while (offset + 8 <= file.Length)
		{
			int dataSize = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(offset));
			int headerSize = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(offset + 4));
			int cursor = offset + 8;

			(string? entryType, _) = ReadNameOrOrdinal(file, ref cursor);
			(_, int nameId) = ReadNameOrOrdinal(file, ref cursor);

			if (entryType == type && nameId == name)
				return file.AsSpan(offset + headerSize, dataSize).ToArray();

			offset = (offset + headerSize + dataSize + 3) & ~3;
		}

		Assert.Fail($"The resource file holds no resource {name} of type {type}.");
		return [];
	}

	private static (string? Text, int Ordinal) ReadNameOrOrdinal(byte[] file, ref int cursor)
	{
		if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(cursor)) == 0xFFFF)
		{
			int ordinal = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(cursor + 2));
			cursor += 4;
			return (null, ordinal);
		}

		var text = new StringBuilder();

		for (
			char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(cursor));
			c != '\0';
			c = (char)BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(cursor))
		)
		{
			text.Append(c);
			cursor += 2;
		}

		cursor += 2;
		return (text.ToString(), -1);
	}
}
