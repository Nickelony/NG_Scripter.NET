using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class ClassicSectionCompilerTests
{
	private const int HeaderLine = 1;

	private static readonly string[] s_strings = ["Test Level", "Big Key", "Title Name"];
	private static readonly byte[] s_zeroCamera = new byte[24];

	private static ParsedSection Section(UnitRun unit, ScriptSectionType type, params string[] lines)
	{
		return new()
		{
			SectionType = type,

			LevelName =
				lines.Where(line => line.StartsWith("Name=", StringComparison.Ordinal)).Select(ScriptLineLexer.GetRawRemainder).LastOrDefault()
				?? string.Empty,

			LevelPath =
				lines
					.Where(line => line.StartsWith("Level=", StringComparison.Ordinal))
					.Select(line => ScriptLineLexer.ParseCommandLine(line).Arguments[0])
					.LastOrDefault()
				?? string.Empty,

			HeaderLocation = unit.At(HeaderLine),
			ClassicLines = [.. lines.Select((text, index) => unit.CommandLine(HeaderLine + 1 + index, text))],
		};
	}

	private static (IReadOnlyList<byte[]>? Sections, UnitRun Unit) Compile(params string[] levelLines)
	{
		var unit = new UnitRun();
		return (Compile(unit, Section(unit, ScriptSectionType.Level, levelLines)), unit);
	}

	private static IReadOnlyList<byte[]>? Compile(UnitRun unit, params ParsedSection[] sections) =>
		new ClassicSectionCompiler(unit.Run, unit.Evaluator, UnitRun.Language(s_strings)).Compile(sections);

	[TestMethod]
	[TestCategory("Fast")]
	public void Lines_AreWrittenInVb6sCommandOrder_ThenTheHeaderAndTheEndTag()
	{
		// VB6 keys each line on its command's place in VetOrdineTag times 1000, plus the item number times 10 for an inventory command, sorts
		// the lines (Module1.bas:5068-5074, 5685-5733), and writes the header at Level=, the last in that order, and 0x83 after it
		// (:5830-5864, 6405-6406).
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile(
			"LoadCamera= 0,0,0,0,0,0,1",
			"Fog= 1,2,3",
			"Key= 2,Big Key,0,0,0,0,0,0",
			"Name= Test Level",
			"Key= 1,Big Key,0,0,0,0,0,0",
			@"Level= data\test,5",
			"Lightning= ENABLED"
		);

		Assert.IsNotNull(sections);

		byte[] expected =
		[
			0x93,
			0x01,
			0x00,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0x94,
			0x01,
			0x00,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0x8F,
			1,
			2,
			3,
			0x91,
			.. s_zeroCamera,
			1,
			0x81,
			0,
			(byte)ScriptLevelFlags.Lightning,
			0x00,
			0,
			5,
			0x83,
		];

		Assert.AreSequenceEqual(expected, sections.Single());
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ComboPiece_RefinesTheSortKey()
	{
		// A combo adds its piece number to the key (Module1.bas:5700-5704), so piece 1 of a combo comes before piece 2, and an item, whose
		// command stands earlier in VetOrdineTag, before both.
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			"KeyCombo= 1,2,Big Key,0,0,0,0,0,0",
			"KeyCombo= 1,1,Big Key,0,0,0,0,0,0",
			"Key= 3,Big Key,0,0,0,0,0,0",
			TestScripts.LoadCamera
		);

		Assert.IsNotNull(sections);

		byte[] expected =
		[
			0x95,
			0x01,
			0x00,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0xB2,
			0x01,
			0x00,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0xB3,
			0x01,
			0x00,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0x91,
			.. s_zeroCamera,
			0,
			0x81,
			0,
			0x00,
			0x00,
			0,
			0,
			0x83,
		];

		Assert.AreSequenceEqual(expected, sections.Single());
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LinesThatShareAKey_ComeOutReversed_WhenALowerKeyFollowsThem()
	{
		// VB6's exchange sort swaps only on a strictly greater key (Module1.bas:5711-5733), so two LoadCamera= lines that a Fog= follows come
		// out in reverse, and the engine uses the first one the script gives. VB6 warns that it uses the last (:6409-6419).
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			"LoadCamera= 0,0,0,0,0,0,1",
			"LoadCamera= 0,0,0,0,0,0,2",
			"Fog= 1,2,3"
		);

		Assert.IsNotNull(sections);
		byte[] expected = [0x8F, 1, 2, 3, 0x91, .. s_zeroCamera, 2, 0x91, .. s_zeroCamera, 1, 0x81, 0, 0x00, 0x00, 0, 0, 0x83];

		Assert.AreSequenceEqual(expected, sections.Single());

		DiagnosticAssert.HasWarning(
			unit.Diagnostics,
			5,
			"There are 2 'LoadCamera=' lines in level 'Test Level'; the engine uses the last one written"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void MoviesThatShareAKey_ComeOutReversed_WhenAnItemFollowsThem()
	{
		// Every FMV= line keys on 8000 (Module1.bas:5693), so the exchange sort reverses two of them when a Key= line follows (:5711-5733).
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			TestScripts.LoadCamera,
			"FMV= 1,0",
			"FMV= 2,0",
			"Key= 1,Big Key,0,0,0,0,0,0"
		);

		Assert.IsNotNull(sections);

		byte[] expected =
		[
			0x93,
			0x01,
			0x00,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0x80,
			2,
			0x80,
			1,
			0x91,
			.. s_zeroCamera,
			0,
			0x81,
			0,
			0x00,
			0x00,
			0,
			0,
			0x83,
		];

		Assert.AreSequenceEqual(expected, sections.Single());
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TitleHeader_WritesNoNameIndex_AndEachSectionWritesItsIndex()
	{
		// VB6 writes 0x82 for a [Title] and 0x81 with the name index for a [Level], then the flags word, the section's index and the CD
		// (Module1.bas:5832-5864).
		var unit = new UnitRun();

		IReadOnlyList<byte[]>? sections = Compile(
			unit,
			Section(unit, ScriptSectionType.Title, @"Level= data\title,3", TestScripts.LoadCamera),
			Section(unit, ScriptSectionType.Level, "Name= Test Level", @"Level= data\test,4", TestScripts.LoadCamera, "Horizon= ENABLED")
		);

		Assert.IsNotNull(sections);

		byte[] expectedTitle = [0x91, .. s_zeroCamera, 0, 0x82, 0x00, 0x00, 0, 3, 0x83];
		byte[] expectedLevel = [0x91, .. s_zeroCamera, 0, 0x81, 0, (byte)ScriptLevelFlags.Horizon, 0x00, 1, 4, 0x83];

		Assert.AreSequenceEqual(expectedTitle, sections[0]);
		Assert.AreSequenceEqual(expectedLevel, sections[1]);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TitleName_IsLookedUpThoughTheHeaderDoesNotWriteIt()
	{
		// VB6 looks the Name= of a [Title] up at its line and fails a missing string (Module1.bas:6001-6009).
		var unit = new UnitRun();

		IReadOnlyList<byte[]>? sections = Compile(
			unit,
			Section(unit, ScriptSectionType.Title, "Name= Missing Name", @"Level= data\title,0", TestScripts.LoadCamera)
		);

		Assert.IsNull(sections);
		DiagnosticAssert.SingleError(unit.Diagnostics, 2, "Cannot find level name in language file: Missing Name");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("Name= Test Level", @"Level= data\test,0", "", 1, "LoadCamera= command is required in level section 'Test Level'")]
	[DataRow("Name= Test Level", TestScripts.LoadCamera, "", 1, "Missing 'Level=' command in [Level] section")]
	[DataRow(@"Level= data\test,0", TestScripts.LoadCamera, "", 1, "Missing 'Name=' command in [Level] section")]
	[DataRow("Name= No Such Level", @"Level= data\test,0", TestScripts.LoadCamera, 2, "Cannot find level name in language file: No Such Level")]
	public void SectionWithoutARequiredCommand_Fails(string first, string second, string third, int expectedLine, string expectedError)
	{
		// VB6 requires LoadCamera= and Level= in each section (Module1.bas:6421-6434), and a Name= whose string the language holds in a [Level]
		// (:5839-5850).
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile([.. new[] { first, second, third }.Where(line => line.Length > 0)]);

		Assert.IsNull(sections);
		DiagnosticAssert.SingleError(unit.Diagnostics, expectedLine, expectedError);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TitleWithoutANameOrALoadCamera_NamesTheSectionByItsHeader()
	{
		// VB6 closes the message with the section's name in quotes (Module1.bas:6421-6426), which a [Title] need not have, so the port names
		// such a section by its header.
		var unit = new UnitRun();

		IReadOnlyList<byte[]>? sections = Compile(unit, Section(unit, ScriptSectionType.Title, @"Level= data\title,0"));

		Assert.IsNull(sections);
		DiagnosticAssert.SingleError(unit.Diagnostics, HeaderLine, "LoadCamera= command is required in the [Title] section");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void FailingLine_DoesNotStopTheLinesAfterIt()
	{
		// VB6 stops the section at its first error (Module1.bas:6402); the port reports every failing line of the section and writes none of it.
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			TestScripts.LoadCamera,
			"Fog= 1,2,BLUE",
			"Cut= 40"
		);

		Assert.IsNull(sections);

		DiagnosticAssert.Errors(
			unit.Diagnostics,
			(5, "Argument 3 of Fog= is invalid: BLUE is neither a number nor a known name"),
			(6, "Argument 1 of Cut= is out of range: 40 lies outside 0 to 31, the values a cutscene number holds")
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void RepeatedSetting_FailsOnADifferentValue_AndWarnsOnTheSameOne()
	{
		// VB6 writes each repeat of a tag and warns that the engine uses the last (Module1.bas:6409-6419); the port fails a repeat with a
		// different value and warns on an identical one, keyed on the resolved tag, so Key= 1 and Key= $1 are one setting (PD-02).
		(IReadOnlyList<byte[]>? different, UnitRun differentUnit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			TestScripts.LoadCamera,
			"Fog= 1,2,3",
			"Fog= 1,2,4"
		);

		(IReadOnlyList<byte[]>? same, UnitRun sameUnit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			TestScripts.LoadCamera,
			"Key= 1,Big Key,0,0,0,0,0,0",
			"Key= $1,Big Key,0,0,0,0,0,0"
		);

		Assert.IsNull(different);

		DiagnosticAssert.SingleError(
			differentUnit.Diagnostics,
			6,
			"'Fog=' gives a value different from line 5, and the setting holds one value; keep one of the two lines"
		);

		Assert.IsNotNull(same);
		Assert.HasCount((2 * 15) + 26 + 6 + 1, same.Single(), "Both Key= lines are written, then LoadCamera=, the header and the end tag.");

		DiagnosticAssert.HasWarning(sameUnit.Diagnostics, 6, "'Key= $1' repeats line 5 with the same value; remove one of the two lines");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void SlotCommands_RepeatPerSlot()
	{
		// VB6 counts a ResidentCut= repeat per slot, because each slot writes its own tag (Module1.bas:5912-5914); AnimatingMIP= writes one tag
		// for every slot (:6119), so the port keys it on the slot it packs (PD-02).
		(IReadOnlyList<byte[]>? distinct, UnitRun distinctUnit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			TestScripts.LoadCamera,
			"ResidentCut= 1,3",
			"ResidentCut= 2,3",
			"AnimatingMIP= 1,5",
			"AnimatingMIP= 2,5"
		);

		(IReadOnlyList<byte[]>? repeated, UnitRun repeatedUnit) = Compile(
			"Name= Test Level",
			@"Level= data\test,0",
			TestScripts.LoadCamera,
			"AnimatingMIP= 1,5",
			"AnimatingMIP= 1,6"
		);

		Assert.IsNotNull(distinct);
		DiagnosticAssert.NoDiagnostics(distinctUnit.Diagnostics);
		Assert.IsNull(repeated);

		DiagnosticAssert.SingleError(
			repeatedUnit.Diagnostics,
			6,
			"'AnimatingMIP= 1' gives a value different from line 5, and the setting holds one value; keep one of the two lines"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void SecondLevelLine_Fails()
	{
		// VB6 adds a level path for each Level= (Module1.bas:5809-5814), so a second one points every later level at the wrong file (PD-31).
		(IReadOnlyList<byte[]>? sections, UnitRun unit) = Compile("Name= Test Level", @"Level= data\a,0", @"Level= data\a,0", TestScripts.LoadCamera);

		Assert.IsNull(sections);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			4,
			"The section gives 'Level=' at line 3 already, and a second level path would point every later level at the wrong file; keep one of the two lines"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void MissingNameCommand_Fails()
	{
		// PD-05: VB6 starts the name as 0 (Module1.bas:5250) and looks that string up (:5845), so it compiles the level when the
		// first language holds the string 0; the port fails whatever the language holds.
		const string script = TestScripts.Language + "[Level]\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\n0\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A [Level] without Name= fails.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "Missing 'Name=' command in [Level] section",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TwoLoadCameraLines_AreBothWrittenWithVb6sWarning()
	{
		// The help's own [Level] example gives two LoadCamera= lines, and the engine uses the last one written; VB6 warns (Module1.bas:6163,
		// 6409-6419).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,255\nLoadCamera= 1,0,0,0,0,0,0\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			result.Diagnostics,
			7,
			"There are 2 'LoadCamera=' lines in level 'Test Level'; the engine uses the last one written"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NameWithComma_LooksUpRawRemainder()
	{
		const string script = TestScripts.Language + "[Level]\nName= My Level, Part 2\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.CreateCompilable(script, "[Strings]\nMy Level, Part 2\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LegendWithComma_LooksUpRawRemainder()
	{
		const string script = TestScripts.LanguageAndLevel + "Legend= Egypt,1600BC\n";

		using var workspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\nEgypt,1600BC\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("data\\second")]
	[DataRow("data\\first")]
	public void RepeatedLevelCommand_Fails(string secondPath)
	{
		// PD-42: VB6 writes the section index in each level header (Module1.bas:5861) but adds one path per Level= line (:5811-5814),
		// so the extra path makes every later level point one path early. An identical repeat fails too.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + $"[Level]\nName= Test Level\nLevel= data\\first,0\nLevel= {secondPath},0\n{TestScripts.LoadCamera}\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A section takes one Level= line.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 6,
			message: "The section gives 'Level=' at line 5 already, and a second level path would point every later level at the wrong file; keep one of the two lines",
			file: workspace.ScriptFile
		);
	}
}
