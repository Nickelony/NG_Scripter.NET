using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class ScriptDatEncoderTests
{
	private static ParsedSection Section(UnitRun unit, int headerLine, string levelPath)
	{
		return new()
		{
			SectionType = ScriptSectionType.Level,
			LevelPath = levelPath,
			HeaderLocation = unit.At(headerLine),
		};
	}

	private static ScriptModel Model(ParsedScript parsed) => new(parsed, new NgScriptModel());

	private static (bool Encoded, byte[] Bytes, UnitRun Unit) Encode(Func<UnitRun, ScriptModel> scriptModel, IReadOnlyList<byte[]> sectionBytes)
	{
		var unit = new UnitRun();
		bool encoded = new ScriptDatEncoder(unit.Run.Logger, unit.Run.Diagnostics).TryEncode(scriptModel(unit), sectionBytes, out byte[] bytes);
		return (encoded, bytes, unit);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ClassicPayload_FollowsVb6sLayout()
	{
		// VB6 writes the options, the counts and sizes, the two 20-byte extension lists, the level path offsets and paths, the section offsets
		// and bodies, and each language name with the extension .DAT (Module1.bas:6661-6773).
		(bool encoded, byte[] bytes, UnitRun unit) = Encode(
			unit =>
				Model(
					new ParsedScript
					{
						Options = new ScriptOptions
						{
							Flags = ScriptMainFlags.LoadSave | ScriptMainFlags.Title,
							InputTimeOut = 300,
							Security = 7,
						},

						Sections = [Section(unit, 5, @"data\title"), Section(unit, 9, @"data\test")],
						PsxExtensions = ["PSX", "FMV", "CUT"],
						PsxExtensionCount = 3,
						PcExtensions = ["TR4", "BIK", "TR4"],
						PcExtensionCount = 3,
						LanguageFiles = ["English.txt", "Italiano.txt"],
					}
				),

			[
				[0x82, 0, 0, 0, 0, 0x83],
				[0x81, 0, 0, 0, 1, 0, 0x83],
			]
		);

		byte[] expected =
		[
			0x06,
			0x00,
			0x00,
			0x00,
			0x2C,
			0x01,
			0x00,
			0x00,
			7,
			2,
			2,
			0,
			21,
			0,
			13,
			0,
			.. "PSX\0FMV\0CUT\0"u8,
			.. new byte[8],
			.. "TR4\0BIK\0TR4\0"u8,
			.. new byte[8],
			0,
			0,
			11,
			0,
			.. "data\\title\0data\\test\0"u8,
			0,
			0,
			6,
			0,
			0x82,
			0,
			0,
			0,
			0,
			0x83,
			0x81,
			0,
			0,
			0,
			1,
			0,
			0x83,
			.. "English.DAT\0Italiano.DAT\0"u8,
		];

		Assert.IsTrue(encoded);
		Assert.AreSequenceEqual(expected, bytes);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtensionList_WritesTheSlotsBelowItsCount()
	{
		// VB6 writes the slots 0 to its count less 1, each with a terminator, and pads the field with zeros (Module1.bas:6697-6725).
		(bool encoded, byte[] bytes, _) = Encode(
			unit =>
				Model(
					new ParsedScript
					{
						Sections = [Section(unit, 1, "a")],
						PsxExtensions = ["PSX", null, null],
						PsxExtensionCount = 1,
						PcExtensionCount = 0,
					}
				),

			[
				[0x83],
			]
		);

		byte[] expectedExtensions = [.. "PSX\0"u8, .. new byte[16], .. new byte[20]];

		Assert.IsTrue(encoded);
		Assert.AreSequenceEqual(expectedExtensions, bytes[16..56]);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtensionListWithAGap_Fails()
	{
		// VB6 counts the slots a section fills and writes that many from slot 0, so a gap drops the highest slot (Module1.bas:5545-5562,
		// 6714-6718) (PD-57).
		// The error points at the Cut= line, whose slot would be dropped.
		(bool encoded, _, UnitRun unit) = Encode(
			unit =>
				Model(
					new ParsedScript
					{
						Sections = [Section(unit, 1, "a")],
						PcExtensions = ["TR4", null, "TR4"],
						PcExtensionCount = 2,
						PcExtensionLocations = [unit.At(3), default, unit.At(4)],
					}
				),

			[
				[0x83],
			]
		);

		Assert.IsFalse(encoded);

		DiagnosticAssert.SingleError(
			diagnostics: unit.Diagnostics,
			line: 4,
			message: "The [PCExtensions] section declares 'Cut=' but no 'FMV=', so the slot of 'Cut=' would be dropped; add a 'FMV=' line",
			file: unit.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtensionListPastItsField_Fails()
	{
		// VB6 writes a list past 20 bytes unchecked, which moves every field after it (Module1.bas:6697-6710) (PD-26).
		// The first two slots take 16 bytes, so the error points at the Cut= line, whose slot carries the list past its field.
		(bool encoded, _, UnitRun unit) = Encode(
			unit =>
				Model(
					new ParsedScript
					{
						Sections = [Section(unit, 1, "a")],
						PsxExtensions = ["ABCDEFG", "ABCDEFG", "ABCD"],
						PsxExtensionCount = 3,
						PsxExtensionLocations = [unit.At(3), unit.At(4), unit.At(5)],
					}
				),

			[
				[0x83],
			]
		);

		Assert.IsFalse(encoded);

		DiagnosticAssert.SingleError(
			diagnostics: unit.Diagnostics,
			line: 5,
			message: "The [PSXExtensions] extension list takes 21 bytes with its terminators, but its field holds 20; shorten the extensions",
			file: unit.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TooManySections_FailAtTheFirstSectionPastTheByte()
	{
		// VB6 stores the section count in a Byte, which overflows at 256 sections (Module1.bas:6675-6676).
		(bool encoded, _, UnitRun unit) = Encode(
			unit => Model(new ParsedScript { Sections = [.. Enumerable.Range(0, 256).Select(index => Section(unit, index + 1, "a"))] }),
			[.. Enumerable.Range(0, 256).Select(_ => new byte[] { 0x83 })]
		);

		Assert.IsFalse(encoded);
		DiagnosticAssert.SingleError(unit.Diagnostics, 256, "Too many script sections (256); the format supports at most 255");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LevelPathsPastTheWord_FailAtTheSectionThatOverflows()
	{
		// VB6 stores the size of the level paths in an Integer, which overflows past 32767 (Module1.bas:6681-6687).
		string path = new('a', 16384);

		(bool encoded, _, UnitRun unit) = Encode(
			unit => Model(new ParsedScript { Sections = [Section(unit, 1, path), Section(unit, 7, path)] }),

			[
				[0x83],
				[0x83],
			]
		);

		Assert.IsFalse(encoded);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			7,
			"Level names table is too large (32770 bytes); the format supports at most 32767 (VB6 errored on overflow)"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void SectionDataPastTheWord_FailAtTheSectionThatOverflows()
	{
		// VB6 stores the size of the section data in an Integer, which overflows past 32767.
		(bool encoded, _, UnitRun unit) = Encode(
			unit => Model(new ParsedScript { Sections = [Section(unit, 1, "a"), Section(unit, 7, "a")] }),
			[new byte[16384], new byte[16384]]
		);

		Assert.IsFalse(encoded);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			7,
			"Section data is too large (32768 bytes); the format supports at most 32767 (VB6 errored on overflow)"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DuplicateLevelSections_ProduceTwoLevelEntries()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var header = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));

		Assert.AreEqual(2, header.SectionCount, "Both [Level] sections must be stored.");
		Assert.AreEqual(2, header.LevelCount, "Both level paths must be listed.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TwoLevelsThatShareAFile_CountTwiceInTheSecurityBlock()
	{
		// VB6 adds one entry to TotLivelli per Level= line (Module1.bas:5811-5814) and writes that count into byte 5 of the
		// security block (Module1.bas:8004), which the XOR pass scrambles with the table's fifth entry, 58.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var header = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));
		ScriptDatReader.NgField security = header.GetNgField(ScriptDatReader.SecurityTag);

		Assert.AreEqual(2, header.Bytes[security.DataOffset + 5] ^ 58);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OverlongExtensionList_Fails()
	{
		// PD-26: VB6 writes an extension list longer than its 20-byte field unchecked (Module1.bas:6697-6710), which moves every
		// field after it.
		string longExtension = "." + new string('A', 24);

		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[PSXExtensions]\nLevel= " + longExtension + "\n" + TestScripts.Level
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "The list does not fit its field.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,
			message: "The [PSXExtensions] extension list takes 26 bytes with its terminators, but its field holds 20; shorten the extensions",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);
	}
}
