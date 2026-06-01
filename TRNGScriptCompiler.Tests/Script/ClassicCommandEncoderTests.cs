using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class ClassicCommandEncoderTests
{
	private const int Line = 7;

	private static readonly string[] s_strings = ["Test Level", "Big Key", "Egypt, present day."];

	private sealed record Compiled(bool Success, byte[] Bytes, ClassicCompileContext Context, UnitRun Unit);

	private static Compiled Compile(string line) => Compile(line, s_strings);

	private static Compiled Compile(string line, string[] strings)
	{
		var unit = new UnitRun();

		var encoder = new ClassicCommandEncoder(
			unit.Run.Diagnostics,
			new ScriptArgumentResolver(unit.Run.Diagnostics, unit.Evaluator, UnitRun.Language(strings))
		);

		ScriptCommandLine commandLine = unit.CommandLine(Line, line);

		var bytes = new List<byte>();
		var context = new ClassicCompileContext(bytes, cdSlot: 0, commandLine);

		bool success = encoder.TryEncode(context, commandLine.Arguments);
		return new Compiled(success, Bytes: [.. bytes], context, unit);
	}

	private static void AssertCompiles(Compiled compiled, byte[] expectedBytes, ScriptLevelFlags expectedFlags = ScriptLevelFlags.None)
	{
		Assert.IsTrue(compiled.Success);
		Assert.AreSequenceEqual(expectedBytes, compiled.Bytes);
		Assert.AreEqual(expectedFlags, compiled.Context.Flags);

		DiagnosticAssert.NoDiagnostics(compiled.Unit.Diagnostics);
	}

	private static void AssertFails(Compiled compiled, string expectedError)
	{
		Assert.IsFalse(compiled.Success);
		DiagnosticAssert.SingleError(compiled.Unit.Diagnostics, Line, expectedError);
	}

	[TestMethod]
	public void Cut_WritesItsTagAndTheCutsceneByte()
	{
		// VB6 writes Cut= as the tag 0x84 and one byte from 0 to 31 (Module1.bas:5865-5891).
		AssertCompiles(Compile("Cut= 6"), [0x84, 0x06]);
		AssertFails(Compile("Cut= 32"), "Argument 1 of Cut= is out of range: 32 lies outside 0 to 31, the values a cutscene number holds");
	}

	[TestMethod]
	public void ResidentCut_AddsItsSlotToTheTag()
	{
		// VB6 writes the tag 0x85 plus the slot less 1, then the room byte (Module1.bas:5892-5921).
		AssertCompiles(Compile("ResidentCut= 3,30"), [0x87, 30]);

		AssertFails(
			Compile("ResidentCut= 5,30"),
			"Argument 1 of ResidentCut= is out of range: 5 lies outside 1 to 4, the values a cutscene number holds"
		);
	}

	[TestMethod]
	public void Mirror_WritesTheRoomByteAndTheSurfaceDwordAndSetsItsFlag()
	{
		// VB6 writes the tag 0x8E, the room as a byte and the surface as a dword, and sets slf_Mirror (Module1.bas:6049-6081).
		AssertCompiles(Compile("Mirror= 69,$7400"), [0x8E, 69, 0x00, 0x74, 0x00, 0x00], ScriptLevelFlags.Mirror);
	}

	[TestMethod]
	public void Layer_WritesFourBytesAndSetsItsLayerFlag()
	{
		// VB6 writes Layer1= as the tag 0x89 and Layer2= as 0x8A, then four bytes, and sets the layer's flag (Module1.bas:5923-5953); a negative
		// speed reverses the movement.
		AssertCompiles(Compile("Layer1= 160,160,192,-7"), [0x89, 160, 160, 192, 0xF9], ScriptLevelFlags.Layer1);
		AssertCompiles(Compile("Layer2= 1,2,3,4"), [0x8A, 1, 2, 3, 4], ScriptLevelFlags.Layer2);
	}

	[TestMethod]
	public void FogAndUvRotate_WriteTheirBytesWithoutAFlag()
	{
		// VB6 writes Fog= as the tag 0x8F and three bytes, and UVRotate= as the tag 0x8B and one byte (Module1.bas:5955-5974, 6082-6106).
		AssertCompiles(Compile("Fog= 210,176,99"), [0x8F, 210, 176, 99]);
		AssertCompiles(Compile("UVRotate= -8"), [0x8B, 0xF8]);
	}

	[TestMethod]
	public void LoadCamera_WritesSixDwordsAndAByte()
	{
		// VB6 writes the tag 0x91, six dwords and a last byte (Module1.bas:6149-6180).
		AssertCompiles(
			Compile("LoadCamera= 87075,-14188,26581,89090,-15443,25670,29"),

			[
				0x91,
				0x23,
				0x54,
				0x01,
				0x00,
				0x94,
				0xC8,
				0xFF,
				0xFF,
				0xD5,
				0x67,
				0x00,
				0x00,
				0x02,
				0x5C,
				0x01,
				0x00,
				0xAD,
				0xC3,
				0xFF,
				0xFF,
				0x46,
				0x64,
				0x00,
				0x00,
				29,
			]
		);
	}

	[TestMethod]
	public void ResetHub_WritesItsByteAndSetsItsFlag()
	{
		// VB6 writes the tag 0x92 and one byte, and sets slf_ResetHub (Module1.bas:6182-6204).
		AssertCompiles(Compile("ResetHUB= 3"), [0x92, 3], ScriptLevelFlags.ResetHub);
	}

	[TestMethod]
	[DataRow("Lightning= ENABLED", (int)ScriptLevelFlags.Lightning)]
	[DataRow("lightning= enabled", (int)ScriptLevelFlags.Lightning)]
	[DataRow("YoungLara= ENABLED", (int)ScriptLevelFlags.YoungLara)]
	[DataRow("NoLevel= ENABLED", (int)ScriptLevelFlags.NoLevel)]
	[DataRow("Lightning= DISABLED", (int)ScriptLevelFlags.None)]
	public void Switch_SetsItsFlagOnlyWhenEnabled_AndWritesNoByte(string line, int expectedFlags)
	{
		// VB6 reads ENABLED or DISABLED without regard to case and sets the command's flag only for ENABLED (Module1.bas:4778-4788,
		// 6359-6389).
		AssertCompiles(Compile(line), [], (ScriptLevelFlags)expectedFlags);
	}

	[TestMethod]
	public void Switch_WithAnotherValue_Fails()
	{
		// VB6's ControllaEnabled fails any value other than ENABLED or DISABLED (Module1.bas:4783-4787).
		AssertFails(Compile("Train= ON"), "Argument 1 of Train= is invalid: ON is neither ENABLED nor DISABLED");
	}

	[TestMethod]
	public void Switch_ErrorNamesTheCatalogSpelling()
	{
		// VB6 writes the command as StarField= (Module1.bas:3939), and the port matches it without regard to case.
		AssertFails(Compile("starfield= on"), "Argument 1 of StarField= is invalid: on is neither ENABLED nor DISABLED");
	}

	[TestMethod]
	public void GenericCommand_ChecksTheArgumentCountFirst()
	{
		// VB6 checks the count before it reads an argument (Fog=, Module1.bas:6086-6092).
		AssertFails(Compile("Fog= 1,2"), "Wrong number of arguments for 'Fog=': expected 3, got 2");
	}

	[TestMethod]
	public void GenericCommand_NamesTheFirstArgumentThatFails()
	{
		// VB6 stops at the first argument that does not read (Module1.bas:6098-6105).
		AssertFails(Compile("Fog= 1,BLUE,300"), "Argument 2 of Fog= is invalid: BLUE is neither a number nor a known name");
	}

	[TestMethod]
	public void Command_TakesItsCanonicalSpelling()
	{
		// Module1.bas compares text without regard to case (Option Compare Text, :4), so fog= is Fog= (:6082).
		Compiled compiled = Compile("fog= 1,2,3");

		AssertCompiles(compiled, [0x8F, 1, 2, 3]);
		Assert.AreEqual("Fog=", compiled.Context.Command);
	}

	[TestMethod]
	public void Fmv_SetsBit7ForTheTrigger()
	{
		// VB6 writes the tag 0x80 and the movie index, with bit 7 set when the trigger is 1 (Module1.bas:5763-5794).
		AssertCompiles(Compile("FMV= 43,1"), [0x80, 43 | 0x80]);
		AssertCompiles(Compile("FMV= 43,0"), [0x80, 43]);
	}

	[TestMethod]
	[DataRow("FMV= 128,0", "The FMV= index must be between 0 and 127, because bit 7 of its byte is the trigger flag: 128")]
	[DataRow("FMV= -1,0", "The FMV= index must be between 0 and 127, because bit 7 of its byte is the trigger flag: -1")]
	[DataRow("FMV= 1,2", "The FMV= trigger must be 0 or 1: 2")]
	[DataRow("FMV= 1", "Wrong number of arguments for 'FMV=': expected 2, got 1")]
	public void Fmv_ValueOutsideItsBits_Fails(string line, string expectedError)
	{
		// VB6 writes FMV= 200,0 as movie 72 with the trigger set, because the index shares the byte with the trigger bit
		// (Module1.bas:5789-5790); the port fails (PD-56).
		AssertFails(Compile(line), expectedError);
	}

	[TestMethod]
	public void Level_RecordsTheCdSlotAndWritesNoByte()
	{
		// VB6 records the CD of Level= for the section header, which the header writes (Module1.bas:5816-5823, 5864).
		Compiled compiled = Compile(@"Level= data\tut1,107");

		AssertCompiles(compiled, []);
		Assert.AreEqual(107, compiled.Context.CdSlot);
	}

	[TestMethod]
	[DataRow(@"Level= data\tut1,256", "Argument 2 of Level= is out of range: 256 lies outside 0 to 255, the values a CD track holds")]
	[DataRow(@"Level= data\tut1", "Wrong number of arguments for 'Level=': expected 2, got 1")]
	public void Level_WithoutAValidCd_Fails(string line, string expectedError)
	{
		// VB6 checks the count and the CD range of Level= (Module1.bas:5801-5806, 5816-5822).
		AssertFails(Compile(line), expectedError);
	}

	[TestMethod]
	public void Name_WritesNoByteWhereItStands()
	{
		// VB6 records the name at its line and writes its index in the section header (Module1.bas:5995-6010, 5845-5851).
		AssertCompiles(Compile("Name= Big Key"), []);
	}

	[TestMethod]
	public void Legend_TakesTheRestOfTheLine_CommasIncluded()
	{
		// VB6 reads the text after Legend= as one string, because "there could be a comma in the string" (Module1.bas:5975-5994).
		AssertCompiles(Compile("Legend= Egypt, present day."), [0x8C, 2]);
		AssertFails(Compile("Legend= Missing text"), "Cannot find legend text in language file: Missing text");
	}

	[TestMethod]
	public void Legend_PastTheFirst256Strings_Fails()
	{
		// VB6 writes the index as one byte (Module1.bas:5994), so the game would show string 43 (PD-56).
		string[] strings = [.. Enumerable.Range(0, 300).Select(index => $"String {index}")];

		AssertFails(
			Compile("Legend= String 299", strings),
			"The Legend= string 'String 299' is entry 299 of the language file, but Legend= writes its index in one byte; move the string into the first 256 entries"
		);
	}

	[TestMethod]
	public void LensFlare_WritesEachCoordinateDividedBy256_ThenTheColorBytes()
	{
		// VB6 reads a coordinate as a dword, writes it divided by 256 as a word and sets slf_LensFlare (Module1.bas:6012-6046). The division
		// truncates toward zero, so -511 writes -1.
		AssertCompiles(
			Compile("LensFlare= 225792,-1600000,588544,255,255,255"),
			[0x8D, 0x72, 0x03, 0x96, 0xE7, 0xFB, 0x08, 255, 255, 255],
			ScriptLevelFlags.LensFlare
		);

		AssertCompiles(Compile("LensFlare= -511,255,-255,1,2,3"), [0x8D, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 1, 2, 3], ScriptLevelFlags.LensFlare);
	}

	[TestMethod]
	[DataRow(
		"LensFlare= 8388608,0,0,0,0,0",
		"Coordinate 1 of LensFlare= is out of range: 8388608 divided by 256 must fit -32768 to 32767, so the coordinate lies between -8388863 and 8388607"
	)]
	[DataRow("LensFlare= 0,0,0,-1,0,0", "Argument 4 of LensFlare= is out of range: -1 lies outside 0 to 255, the values a color holds")]
	[DataRow("LensFlare= 0,0,0,0,256,0", "Argument 5 of LensFlare= is out of range: 256 lies outside -128 to 255, the values its byte holds")]
	[DataRow("LensFlare= 0,0,0,0,0", "Wrong number of arguments for 'LensFlare=': expected 6, got 5")]
	public void LensFlare_ValueOutsideItsField_Fails(string line, string expectedError)
	{
		// VB6 writes a quotient outside the signed word wrapped (Module1.bas:6033-6034) and a color through its byte check (:6040); the port
		// fails (PD-56).
		AssertFails(Compile(line), expectedError);
	}

	[TestMethod]
	public void AnimatingMip_PacksTheDistanceAndTheSlotIntoOneByte()
	{
		// VB6 writes the tag 0x90 and distance * 16 + slot - 1 (Module1.bas:6107-6148).
		AssertCompiles(Compile("AnimatingMIP= 12,5"), [0x90, (5 * 16) + 11]);
		AssertCompiles(Compile("AnimatingMIP= 1,0"), [0x90, 0]);
	}

	[TestMethod]
	[DataRow("AnimatingMIP= 17,5", "Argument 1 of AnimatingMIP= is out of range: 17 lies outside 1 to 16, the values a MIP slot holds")]
	[DataRow("AnimatingMIP= 0,5", "Argument 1 of AnimatingMIP= is out of range: 0 lies outside 1 to 16, the values a MIP slot holds")]
	[DataRow("AnimatingMIP= 1,16", "Argument 2 of AnimatingMIP= is out of range: 16 lies outside 0 to 15, the values a distance holds")]
	[DataRow("AnimatingMIP= 1", "Wrong number of arguments for 'AnimatingMIP=': expected 2, got 1")]
	public void AnimatingMip_ValueOutsideItsNybble_Fails(string line, string expectedError)
	{
		// VB6 checks the slot range and, after it, the distance range (Module1.bas:6134-6144).
		AssertFails(Compile(line), expectedError);
	}

	[TestMethod]
	public void InventoryItem_AddsItsItemNumberToTheTag_ThenWritesTheStringAndSixWords()
	{
		// VB6 writes the first item's tag plus the item less 1, the string index as a word and six words (Module1.bas:6205-6274).
		AssertCompiles(
			Compile("Key= 2,Big Key,$0002,$0500,$0000,$4000,$0000,$0002"),
			[0x94, 0x01, 0x00, 0x02, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x02, 0x00]
		);

		AssertCompiles(Compile("Examine= 3,Big Key,0,0,0,0,0,-1"), [0xB1, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF]);
	}

	[TestMethod]
	[DataRow("Examine= 4,Big Key,0,0,0,0,0,0", "Argument 1 of Examine= is out of range: 4 lies outside 1 to 3, the values an item number holds")]
	[DataRow("Pickup= 5,Big Key,0,0,0,0,0,0", "Argument 1 of Pickup= is out of range: 5 lies outside 1 to 4, the values an item number holds")]
	[DataRow("puzzle= 13,Big Key,0,0,0,0,0,0", "Argument 1 of Puzzle= is out of range: 13 lies outside 1 to 12, the values an item number holds")]
	[DataRow("Key= 1,No Such Key,0,0,0,0,0,0", "Cannot find string 'No Such Key' in language file")]
	[DataRow("Key= 1,Big Key,0,0,0,0,0", "Wrong number of arguments for 'Key=': expected 8, got 7")]
	[DataRow("Key= 1,Big Key,65536,0,0,0,0,0", "Argument 3 of Key= is out of range: 65536 lies outside -32768 to 65535, the values its word holds")]
	public void InventoryItem_InvalidArgument_Fails(string line, string expectedError)
	{
		// VB6 checks the count, the item range and the string, and width-checks each word (Module1.bas:6210-6273); a word value that fits
		// only by wrapping fails in the port (PD-56).
		AssertFails(Compile(line), expectedError);
	}

	[TestMethod]
	public void InventoryCombo_AddsItsComboAndPieceToTheTag()
	{
		// VB6 writes the first combo's tag plus (combo - 1) * 2 plus the piece less 1 (Module1.bas:6276-6358).
		AssertCompiles(
			Compile("PuzzleCombo= 5,2,Big Key,$0002,$0500,$4000,$1000,$0000,$000a"),
			[0xC2 + 8 + 1, 0x01, 0x00, 0x02, 0x00, 0x00, 0x05, 0x00, 0x40, 0x00, 0x10, 0x00, 0x00, 0x0A, 0x00]
		);

		AssertCompiles(Compile("KeyCombo= 1,1,Big Key,0,0,0,0,0,0"), [0xB2, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
	}

	[TestMethod]
	[DataRow(
		"PuzzleCombo= 9,1,Big Key,0,0,0,0,0,0",
		"Argument 1 of PuzzleCombo= is out of range: 9 lies outside 1 to 8, the values a combo number holds"
	)]
	[DataRow(
		"PickupCombo= 5,1,Big Key,0,0,0,0,0,0",
		"Argument 1 of PickupCombo= is out of range: 5 lies outside 1 to 4, the values a combo number holds"
	)]
	[DataRow("KeyCombo= 1,3,Big Key,0,0,0,0,0,0", "Argument 2 of KeyCombo= is out of range: 3 lies outside 1 to 2, the values a piece number holds")]
	[DataRow("KeyCombo= 1,1,Big Key,0,0,0,0,0", "Wrong number of arguments for 'KeyCombo=': expected 9, got 8")]
	public void InventoryCombo_InvalidArgument_Fails(string line, string expectedError)
	{
		// VB6 checks the count, the combo range and then the piece range (Module1.bas:6282-6335).
		AssertFails(Compile(line), expectedError);
	}
}
