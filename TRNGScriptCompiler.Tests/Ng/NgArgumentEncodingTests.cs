using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>The words an NG level command writes for its arguments, read from the level's NG field.</summary>
[TestClass]
public sealed class NgArgumentEncodingTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void EmptyNybbleArray_WritesNoCountWord()
	{
		// VB6 packs a byte or nybble array only when the line gives an element (Module1.bas:7182-7186), so TextureSequence=
		// (code 17) with its three fixed arguments writes the header 0x1103 and three words.
		byte[] tag = CompileCommand("TextureSequence= 1,2,3", code: 17).ToBytes();
		Assert.AreSequenceEqual(new byte[] { 0x03, 0x11, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 }, tag, "No count word follows the fixed arguments.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NybbleArray_WritesItsCountThenTwoNybblesPerByte()
	{
		// Module1.bas:7255-7268: the count byte, then low nybble first, and an odd count padded with a zero nybble.
		byte[] tag = CompileCommand("TextureSequence= 1,2,3,4,5,6", code: 17).ToBytes();
		Assert.AreSequenceEqual(new byte[] { 0x05, 0x11, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x03, 0x54, 0x06, 0x00 }, tag);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DefineInANybbleArray_IsReadAsItsValue()
	{
		// PD-46: VB6 reads an array element as a literal only, so FRAMES fails as "not decimal numeric value" (Module1.bas:7201-7227).
		// The count byte 1 and the nybble 4 make the word 0x0401.
		byte[] tag = CompileCommand("#define FRAMES 4\nTextureSequence= 1,2,3,FRAMES", code: 17).ToBytes();
		Assert.AreSequenceEqual(new byte[] { 0x04, 0x11, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x01, 0x04 }, tag);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("TriggerGroup= 1,$2000,IGNORE,1", new byte[] { 0x04, 0x2E, 0x01, 0x00, 0x00, 0x20, 0xFF, 0xFF, 0x01, 0x00 })]
	[DataRow("TriggerGroup= 1,$2000,406,-2", new byte[] { 0x04, 0x2E, 0x01, 0x00, 0x00, 0x20, 0x96, 0x01, 0xFE, 0xFF })]
	[DataRow("TriggerGroup= 1,TGROUP_OR+$2000,406,2", new byte[] { 0x04, 0x2E, 0x01, 0x00, 0x10, 0x20, 0x96, 0x01, 0x02, 0x00 })]
	[DataRow("TriggerGroup= 1,$2000,406,1,2", new byte[] { 0x05, 0x2E, 0x01, 0x00, 0x00, 0x20, 0x96, 0x01, 0x01, 0x00, 0x02, 0x00 })]
	public void TriggerGroupWithoutAPluginIdLiteral_TakesTheWordForm(string command, byte[] expected)
	{
		// VB6 reads the first $ literal of each argument and keeps the long form only when bits 16-23 of one are set
		// (Module1.bas:7302-7327); IGNORE is -1, whose high word is set, but it is no $ literal. The word form is code 46 with one
		// word per value (:7328-7335), so the header is 0x2E04 for four values. Five values end in a partial triplet, which VB6 writes
		// as it stands (:7154-7172), so the header is 0x2E05 (PD-57).
		byte[] tag = CompileCommand(command, code: 46).ToBytes();
		Assert.AreSequenceEqual(expected, tag);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TriggerGroupWithAPluginIdLiteral_KeepsTheLongForm()
	{
		// $12345 sets bit 16, so the command stays code 21 with two words per value: the header 0x1507, then the ID word.
		byte[] tag = CompileCommand("TriggerGroup= 1,$12345,406,1", code: 21).ToBytes();
		Assert.AreSequenceEqual(new byte[] { 0x07, 0x15, 0x01, 0x00, 0x45, 0x23, 0x01, 0x00, 0x96, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }, tag);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(
		"TriggerGroup= 1,$2000,100001,1",
		"Argument 3 of TriggerGroup= is out of range: 100001 lies outside -32768 to 65535, the values its word holds; TriggerGroup= keeps its long form only for a $ literal with a plugin ID, so write the value as one"
	)]
	[DataRow(
		"TriggerGroup= 1,TRIGGER_IN_PLUGIN,406,1",
		"Argument 2 of TriggerGroup= is out of range: 458753 lies outside -32768 to 65535, the values its word holds; TriggerGroup= keeps its long form only for a $ literal with a plugin ID, so write the value as one"
	)]
	public void TriggerGroupWordFormValuePastTheWord_Fails(string command, string expectedMessage)
	{
		// Without a $ literal that holds a plugin ID, VB6 reads every value as a word and its width check fails a value past the
		// word (Module1.bas:7442, 7458-7462), a define that holds a plugin ID included.
		using var workspace = TestWorkspace.CreateCompilable("#define TRIGGER_IN_PLUGIN $70001\n" + TestScripts.LanguageAndLevel + command + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 8, expectedMessage);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TestPosition_WritesEachValueAsAWord()
	{
		// TestPosition= is code 30 with fifteen WORD and SLOT arguments, so its header is 0x1E0F (Module1.bas:7154-7177, 7369). Each value is
		// cut to its low word (:6811-6816): SLOT_TEST is its position 2 in object_types (:7490-7506), the flag sum is 3 (:4857-4910), IGNORE is
		// -1 and a negative distance is its two's complement. The level's flags mini-tag, 0xC901 and 0, and a zero word follow (:8549-8556).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel
				+ "TestPosition= 1,TPOS_DOUBLE_HORIENT+TPOS_FOUR_HORIENT,SLOT_TEST,-256,256,-512,0,-1024,1024,-8192,8192,0,0,IGNORE,$FFFF\n"
		);

		workspace.WithObjectsH(UnitRun.ObjectsHText);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));
		ScriptDatReader.NgField level = reader.GetNgField((ushort)NgTagType.ScriptLevel);

		Assert.AreSequenceEqual(
			new byte[]
			{
				0x0F,
				0x1E,
				0x01,
				0x00,
				0x03,
				0x00,
				0x02,
				0x00,
				0x00,
				0xFF,
				0x00,
				0x01,
				0x00,
				0xFE,
				0x00,
				0x00,
				0x00,
				0xFC,
				0x00,
				0x04,
				0x00,
				0xE0,
				0x00,
				0x20,
				0x00,
				0x00,
				0x00,
				0x00,
				0xFF,
				0xFF,
				0xFF,
				0xFF,
				0x01,
				0xC9,
				0x00,
				0x00,
				0x00,
				0x00,
			},

			reader.Bytes[level.DataOffset..(level.DataOffset + (2 * level.DataWordCount))]
		);
	}

	internal static IReadOnlyList<ScriptDatReader.MiniTag> CompileLevelCommand(string command)
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + command + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));
		ScriptDatReader.NgField level = reader.GetNgField((ushort)NgTagType.ScriptLevel);

		return reader.GetMiniTags(level);
	}

	private static ScriptDatReader.MiniTag CompileCommand(string command, int code) => CompileLevelCommand(command).Single(tag => tag.Code == code);
}
