namespace TRNGScriptCompiler.Tests.Support;

[TestClass]
public sealed class GoldenAssertTests(TestContext testContext)
{
	[TestMethod]
	[TestCategory("IO")]
	public void FileMatches_ReportsTheFirstDifferenceAndItsRegion()
	{
		byte[] golden = File.ReadAllBytes(Path.Combine(TestFixtures.Vb6OutputDirectory(12345), "Script.dat"));
		var reader = ScriptDatReader.Read(golden);
		int offset = reader.Sections[0].Offset + 3;

		using var workspace = TestWorkspace.CreateEmpty();
		byte[] changed = (byte[])golden.Clone();

		changed[offset] ^= 0xFF;
		workspace.WithBinaryFile("Script.dat", changed);

		AssertFailedException failure = Assert.ThrowsExactly<AssertFailedException>(() =>
			GoldenAssert.FileMatches(golden, workspace.PathOf("Script.dat"), testContext)
		);

		Assert.Contains($"Script.dat differs from the golden output at offset 0x{offset:X} ({offset}): classic section 0, byte 3.", failure.Message);
	}
}
