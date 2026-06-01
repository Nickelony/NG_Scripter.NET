using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

/// <summary>
/// Compiles each classic command into the script of <see cref="CoverageWorkspace"/> and reads back the bytes it writes.
/// </summary>
/// <remarks>
/// The bytes follow VB6, as <see cref="ClassicCommandEncoderTests"/> derives them. The language file of the workspace
/// numbers its strings from 0: <c>Test Level</c>, <c>Level Name</c>, <c>Legend Line</c>, <c>Key Label</c>,
/// <c>Puzzle Label</c>, <c>Combo Label</c>, <c>Pickup Label</c> and <c>Examine Label</c>.
/// </remarks>
[TestClass]
public sealed class ClassicCommandCoverageTests
{
	// LoadCamera= 0,0,0,0,0,0,0 -> tag 0x91, six int32 zeros, then the final byte 0x00.
	private static readonly byte[] s_baselineLoadCamera = [0x91, .. new byte[25]];

	// The workspace lists English.txt as file 0, so the line adds the second language file.
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("File= 1,French.txt", "French.DAT")]
	public void LanguageCommand_WritesItsLanguageFileName(string commandLine, string expectedName)
	{
		var dat = ScriptDatReader.Read(CoverageWorkspace.CompileScriptDat(commandLine, ScenarioSection.Language, commandLine));
		Assert.AreSequenceEqual(["English.DAT", expectedName], dat.LanguageFiles);
	}

	// The workspace opens [Options] with Title= ENABLED, which sets the title bit, and Security= 0.
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("LoadSave= ENABLED", (int)(ScriptMainFlags.Title | ScriptMainFlags.LoadSave), 0, 0)]
	[DataRow("Title= DISABLED", (int)ScriptMainFlags.None, 0, 0)]
	[DataRow("PlayAnyLevel= ENABLED", (int)(ScriptMainFlags.Title | ScriptMainFlags.PlayAnyLevel), 0, 0)]
	[DataRow("FlyCheat= ENABLED", (int)(ScriptMainFlags.Title | ScriptMainFlags.FlyCheat), 0, 0)]
	[DataRow("DemoDisc= ENABLED", (int)(ScriptMainFlags.Title | ScriptMainFlags.DemoDisc), 0, 0)]
	[DataRow("InputTimeOut= 30", (int)ScriptMainFlags.Title, 30, 0)]
	[DataRow("Security= 7", (int)ScriptMainFlags.Title, 0, 7)]
	public void ClassicOptionCommand_WritesItsHeaderFields(string commandLine, int expectedFlags, int expectedInputTimeOut, int expectedSecurity)
	{
		var dat = ScriptDatReader.Read(CoverageWorkspace.CompileScriptDat(commandLine, ScenarioSection.Options, commandLine));
		Assert.AreEqual((expectedFlags, expectedInputTimeOut, expectedSecurity), (dat.Flags, dat.InputTimeOut, dat.Security));
	}

	// The workspace gives each table the extensions .PSX, .FMV and .CUT, or .TR4, .BIK and .TR4, in the slots of Level=, FMV= and Cut=.
	// A line gives its slot an extension unlike the one it holds, so the table shows that the line was applied.
	[TestMethod]
	[TestCategory("IO")]
	[DataRow(ScenarioSection.PsxExtensions, "Level= .PS2", 0, ".PS2")]
	[DataRow(ScenarioSection.PsxExtensions, "FMV= .FM2", 1, ".FM2")]
	[DataRow(ScenarioSection.PsxExtensions, "Cut= .CU2", 2, ".CU2")]
	[DataRow(ScenarioSection.PcExtensions, "Level= .TR5", 0, ".TR5")]
	[DataRow(ScenarioSection.PcExtensions, "FMV= .BK2", 1, ".BK2")]
	[DataRow(ScenarioSection.PcExtensions, "Cut= .TR6", 2, ".TR6")]
	public void ExtensionCommand_WritesItsSlotOfItsTable(ScenarioSection section, string commandLine, int slot, string extension)
	{
		var dat = ScriptDatReader.Read(CoverageWorkspace.CompileScriptDat(commandLine, section, commandLine));

		string[] psx = [".PSX", ".FMV", ".CUT"];
		string[] pc = [".TR4", ".BIK", ".TR4"];

		(section == ScenarioSection.PsxExtensions ? psx : pc)[slot] = extension;

		Assert.AreSequenceEqual(psx, dat.PsxExtensions);
		Assert.AreSequenceEqual(pc, dat.PcExtensions);
	}

	// The workspace gives [Title] the path data\title and LoadCamera= 0,0,0,0,0,0,0, and [Level] the name Test Level, the path data\test and the same
	// LoadCamera=. A row replaces or adds its line in [Level]. The section holds the bytes of its commands in VB6's order, then 0x81, the name index,
	// the flags word, the section index and the CD slot, and 0x83 (Module1.bas:5711-5733, 5839-5864); LoadCamera= follows every command but ResetHUB=.
	[TestMethod]
	[TestCategory("IO")]
	[DynamicData(
		nameof(GetClassicLevelRows),
		DynamicDataDisplayName = nameof(CoverageWorkspace.GetScenarioDisplayName),
		DynamicDataDisplayNameDeclaringType = typeof(CoverageWorkspace)
	)]
	public void ClassicLevelCommand_WritesItsBytesAndFlagsIntoTheLevelSection(
		string command,
		string commandLine,
		byte[] commandBytes,
		int flags,
		int nameIndex,
		int cdSlot,
		bool followsLoadCamera
	)
	{
		var dat = ScriptDatReader.Read(CoverageWorkspace.CompileScriptDat(command, ScenarioSection.Level, commandLine));

		byte[] title = [.. s_baselineLoadCamera, 0x82, 0x00, 0x00, 0x00, 0x00, 0x83];

		// A LoadCamera= row replaces the baseline line instead of joining it.
		byte[] baselineLoadCamera = command == "LoadCamera=" ? [] : s_baselineLoadCamera;
		byte[] header = [0x81, (byte)nameIndex, (byte)flags, (byte)(flags >> 8), 0x01, (byte)cdSlot, 0x83];

		byte[] level = [.. followsLoadCamera ? [] : commandBytes, .. baselineLoadCamera, .. followsLoadCamera ? commandBytes : [], .. header];

		Assert.AreSequenceEqual(["data\\title", "data\\test"], dat.LevelPaths);
		Assert.AreSequenceEqual(title, dat.GetSectionBytes(0));
		Assert.AreSequenceEqual(level, dat.GetSectionBytes(1));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ClassicLevelRows_CoverEveryClassicLevelCommand()
	{
		string[] covered = [.. GetClassicLevelRows().Select(row => (string)row[0])];

		Assert.AreSequenceEqual(
			ClassicCommandCatalog.Names.Order(StringComparer.OrdinalIgnoreCase),
			covered.Order(StringComparer.OrdinalIgnoreCase),
			"Every classic level command has one row."
		);
	}

	/// <summary>Gets one row per classic level command.</summary>
	/// <remarks>
	/// A row holds the command's name, its sample line, the bytes the line writes, the level flags it sets,
	/// the name index the header writes, and whether the bytes follow those of <c>LoadCamera=</c>.
	/// </remarks>
	public static IEnumerable<object[]> GetClassicLevelRows() => GetSamples().Select(sample => sample.ToRow());

	private static IEnumerable<Sample> GetSamples()
	{
		yield return new("Name=", "Name= Level Name", []) { NameIndex = 1 };
		yield return new("FMV=", "FMV= 1,0", [0x80, 0x01]);
		yield return new("Level=", @"Level= data\test,3", []) { CdSlot = 3 };
		yield return new("Cut=", "Cut= 2", [0x84, 0x02]);
		yield return new("ResidentCut=", "ResidentCut= 1,3", [0x85, 0x03]);
		yield return new("Layer1=", "Layer1= 1,2,3,4", [0x89, 0x01, 0x02, 0x03, 0x04]) { Flags = ScriptLevelFlags.Layer1 };
		yield return new("Layer2=", "Layer2= 5,6,7,8", [0x8A, 0x05, 0x06, 0x07, 0x08]) { Flags = ScriptLevelFlags.Layer2 };
		yield return new("UVRotate=", "UVRotate= 9", [0x8B, 0x09]);
		yield return new("Legend=", "Legend= Legend Line", [0x8C, 0x02]);

		// Each coordinate is written divided by 256 as a word.
		yield return new("LensFlare=", "LensFlare= 256,512,768,10,11,12", [0x8D, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x0A, 0x0B, 0x0C])
		{
			Flags = ScriptLevelFlags.LensFlare,
		};

		yield return new("Mirror=", "Mirror= 3,1024", [0x8E, 0x03, 0x00, 0x04, 0x00, 0x00]) { Flags = ScriptLevelFlags.Mirror };
		yield return new("Fog=", "Fog= 13,14,15", [0x8F, 0x0D, 0x0E, 0x0F]);

		// The byte holds the distance times 16 plus the slot less 1.
		yield return new("AnimatingMIP=", "AnimatingMIP= 2,5", [0x90, 0x51]);

		// LoadCamera= writes six dwords and the room as a byte.
		yield return new("LoadCamera=", "LoadCamera= 1,2,3,4,5,6,7", [0x91, .. Dwords(1, 2, 3, 4, 5, 6), 0x07]);

		yield return new("ResetHUB=", "ResetHub= 2", [0x92, 0x02]) { Flags = ScriptLevelFlags.ResetHub, FollowsLoadCamera = true };

		// An item is its tag plus its number less 1, the string index and six words; a combo adds the piece less 1 to its tag.
		yield return new("Key=", "Key= 1,Key Label,1,2,3,4,5,6", [0x93, .. Words(3, 1, 2, 3, 4, 5, 6)]);
		yield return new("Puzzle=", "Puzzle= 1,Puzzle Label,7,8,9,10,11,12", [0x9F, .. Words(4, 7, 8, 9, 10, 11, 12)]);
		yield return new("Pickup=", "Pickup= 1,Pickup Label,13,14,15,16,17,18", [0xAB, .. Words(6, 13, 14, 15, 16, 17, 18)]);
		yield return new("Examine=", "Examine= 1,Examine Label,19,20,21,22,23,24", [0xAF, .. Words(7, 19, 20, 21, 22, 23, 24)]);
		yield return new("KeyCombo=", "KeyCombo= 1,2,Combo Label,25,26,27,28,29,30", [0xB3, .. Words(5, 25, 26, 27, 28, 29, 30)]);
		yield return new("PuzzleCombo=", "PuzzleCombo= 1,1,Combo Label,31,32,33,34,35,36", [0xC2, .. Words(5, 31, 32, 33, 34, 35, 36)]);
		yield return new("PickupCombo=", "PickupCombo= 1,2,Combo Label,37,38,39,40,41,42", [0xD3, .. Words(5, 37, 38, 39, 40, 41, 42)]);

		// A switch writes no byte and sets its flag.
		yield return Switch("YoungLara=", ScriptLevelFlags.YoungLara);
		yield return Switch("Horizon=", ScriptLevelFlags.Horizon);
		yield return Switch("StarField=", ScriptLevelFlags.StarField);
		yield return Switch("Lightning=", ScriptLevelFlags.Lightning);
		yield return Switch("Train=", ScriptLevelFlags.Train);
		yield return Switch("Pulse=", ScriptLevelFlags.Pulse);
		yield return Switch("Timer=", ScriptLevelFlags.Timer);
		yield return Switch("RemoveAmulet=", ScriptLevelFlags.RemoveAmulet);
		yield return Switch("NoLevel=", ScriptLevelFlags.NoLevel);
		yield return Switch("ColAddHorizon=", ScriptLevelFlags.ColAddHorizon);
	}

	private static Sample Switch(string command, ScriptLevelFlags flags) => new(command, command + " ENABLED", []) { Flags = flags };

	// The little-endian bytes of VB6 Integer and Long values, the layout the level section holds them in.
	private static byte[] Words(params short[] values) => [.. values.SelectMany(value => new[] { (byte)value, (byte)(value >> 8) })];

	private static byte[] Dwords(params int[] values) => [.. values.SelectMany(value => Words((short)value, (short)(value >> 16)))];

	/// <summary>
	/// A classic level command, a line that gives it, and what the line writes into the level section.
	/// </summary>
	/// <param name="Command">The command with its <c>=</c>, as the catalog spells it.</param>
	/// <param name="CommandLine">The line that replaces or joins the baseline lines of the level.</param>
	/// <param name="CommandBytes">The bytes the line writes, none for a command that writes no byte.</param>
	private sealed record Sample(string Command, string CommandLine, byte[] CommandBytes)
	{
		/// <summary>Gets the level flags the line sets.</summary>
		public ScriptLevelFlags Flags { get; init; }

		/// <summary>Gets the name index that the section header writes.</summary>
		public int NameIndex { get; init; }

		/// <summary>Gets the CD slot that the section header writes.</summary>
		public int CdSlot { get; init; }

		/// <summary>
		/// Gets a value indicating whether the bytes follow those of <c>LoadCamera=</c> in the section.
		/// </summary>
		public bool FollowsLoadCamera { get; init; }

		/// <summary>Returns the arguments of the test that reads the row.</summary>
		public object[] ToRow() => [Command, CommandLine, CommandBytes, (int)Flags, NameIndex, CdSlot, FollowsLoadCamera];
	}
}
