using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Tests.Support;

[TestClass]
public sealed class ScriptDatReaderTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow(12345)]
	[DataRow(54321)]
	[DataRow(99999)]
	public void Read_ParsesEveryGoldenScriptDat(int seed)
	{
		var reader = ScriptDatReader.ReadFile(Path.Combine(TestFixtures.Vb6OutputDirectory(seed), "Script.dat"));

		Assert.IsTrue(reader.HeaderScrambled, "The fixture enables script encryption, so the golden header is scrambled.");
		Assert.HasCount(reader.SectionCount, reader.Sections);
		Assert.HasCount(reader.LevelCount, reader.LevelPaths);

		Assert.AreSequenceEqual(["English.DAT"], reader.LanguageFiles);

		Assert.AreEqual((ushort)NgTagType.ScriptOptions, reader.NgFields[0].Tag, "The options section is the first NG field.");
		Assert.Contains(field => field.Tag == ScriptDatReader.SecurityTag, reader.NgFields, "Every NG header holds a security block.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Read_ListsTheExtensionsOfTheGoldenScriptDat()
	{
		// The fixture's Script.txt gives each table the three extensions of its [PSXExtensions] and [PCExtensions] sections.
		var reader = ScriptDatReader.ReadFile(Path.Combine(TestFixtures.Vb6OutputDirectory(99999), "Script.dat"));

		Assert.AreSequenceEqual([".PSX", ".FMV", ".CUT"], reader.PsxExtensions);
		Assert.AreSequenceEqual([".TR4", ".BIK", ".TR4"], reader.PcExtensions);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Describe_NamesTheRegionOfAnOffset()
	{
		var reader = ScriptDatReader.ReadFile(Path.Combine(TestFixtures.Vb6OutputDirectory(12345), "Script.dat"));

		ScriptDatReader.NgField security = reader.GetNgField(ScriptDatReader.SecurityTag);

		Assert.AreEqual("classic header, byte 9 (scrambled)", reader.Describe(9));
		Assert.AreEqual("classic section 1, byte 0", reader.Describe(reader.Sections[1].Offset));
		Assert.AreEqual("NG marker", reader.Describe(reader.NgOffset));

		Assert.StartsWith("NG field 0x8016 (index ", reader.Describe(security.DataOffset + 2));
		Assert.EndsWith("), data word 1", reader.Describe(security.DataOffset + 2));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void GetMiniTags_ListsTheCommandsOfALevelFieldWithoutItsEndingZeroWord()
	{
		// TextureSequence= is code 17 and FogRange= is code 4; the level then ends with its flags mini-tag, code 201, and a zero word.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "TextureSequence= 1,2,3\nFogRange= 1024,16384\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));
		IReadOnlyList<ScriptDatReader.MiniTag> tags = reader.GetMiniTags(reader.GetNgField((ushort)NgTagType.ScriptLevel));

		Assert.AreSequenceEqual([4, 17, 201], tags.Select(tag => tag.Code).Order());
		Assert.AreSequenceEqual(new short[] { 1, 2, 3 }, tags.Single(tag => tag.Code == 17).Words);
		Assert.AreSequenceEqual(new byte[] { 0x02, 0x04, 0x00, 0x04, 0x00, 0x40 }, tags.Single(tag => tag.Code == 4).ToBytes());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void GetImportedBytes_ReadsThePayloadTheChunkDeclares()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nImportFile= 1,data.bin,FTYPE_USERFILE,IMPORT_TEMPORARY\n" + TestScripts.Level
		);

		byte[] payload = [0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7];

		workspace.WithBinaryFile("data.bin", payload);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));

		Assert.AreSequenceEqual(
			payload,
			reader.GetImportedBytes(reader.GetNgField((ushort)NgTagType.ImportFile)),
			"A size of 3 mod 4 gains a word past the chunk."
		);
	}
}
