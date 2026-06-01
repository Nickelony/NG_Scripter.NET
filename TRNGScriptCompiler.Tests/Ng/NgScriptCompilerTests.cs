using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgScriptCompilerTests
{
	private static readonly LanguageData s_language = UnitRun.Language(["Test Level"], [new ExtraNgString { Index = 31, Text = "Plugin_X" }]);

	private static NgLine Line(UnitRun unit, int line, string text)
	{
		if (NgLine.IsPluginDefineText(text))
			return UnitRun.PluginDefineLine(text, unit.At(line));

		return new NgLine(unit.CommandLine(line, text));
	}

	private static ParsedSection Section(UnitRun unit, int firstLine, params string[] lines) =>
		new() { SectionType = ScriptSectionType.Level, NgLines = [.. lines.Select((text, index) => Line(unit, firstLine + index, text))] };

	private static NgScriptModel Compile(UnitRun unit, ParsedScript parsed, NgCommandCatalog? catalog = null)
	{
		return new NgScriptCompiler(
			run: unit.Run,
			commandCatalog: catalog ?? UnitRun.CommandCatalog,
			evaluator: unit.Evaluator,
			objectsH: unit.ObjectsH,
			plugins: unit.Plugins,
			firstLanguage: s_language
		).Compile(parsed, CancellationToken.None);
	}

	private static short[] WordsOf(NgCommand command) => [.. command.CompiledWords];

	[TestMethod]
	public void OptionsBoolCommands_AddTheirValueToTheFlagsWord_AndWriteNoCommand()
	{
		// VB6 ORs the ENABLED or the DISABLED value of a Bool command into the [Options] flags (Module1.bas:8372-8383).
		var unit = new UnitRun();

		var parsed = new ParsedScript
		{
			OptionsNgLines =
			[
				Line(unit, 2, "Diagnostic= ENABLED"),
				Line(unit, 3, "ForceBumpMapping= DISABLED"),
				Line(unit, 4, "ShowLaraInTitle= DISABLED"),
			],
		};

		NgScriptModel compiled = Compile(unit, parsed);

		Assert.AreEqual((NgMainFlags)0x0021, compiled.OptionsCommands.OptionFlags);
		Assert.IsEmpty(compiled.OptionsCommands.Commands);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[DataRow(9, true)]
	[DataRow(4, false)]
	public void Settings_KeepsItsValue_AndBit3EncryptsTheScript(int settings, bool encrypts)
	{
		// VB6 keeps the Settings= value for the security block and encrypts the script when SET_CRYPT_SCRIPT, 8, is set (Module1.bas:7582-7586,
		// 8360-8366).
		var unit = new UnitRun();

		NgScriptModel compiled = Compile(unit, new ParsedScript { OptionsNgLines = [Line(unit, 2, $"Settings= {settings}")] });

		Assert.AreEqual((settings, encrypts), (compiled.NgSettings, compiled.EnableScriptEncryption));
		Assert.AreSequenceEqual(new short[] { (16 * 256) + 1, (short)settings }, WordsOf(compiled.OptionsCommands.Commands.Single()));

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void SettingsPastAByte_Fails()
	{
		// The security block stores Settings= in a VB6 Byte, which overflows outside 0 to 255 (Module1.bas:8008).
		var unit = new UnitRun();

		NgScriptModel compiled = Compile(unit, new ParsedScript { OptionsNgLines = [Line(unit, 2, "Settings= 256")] });

		Assert.IsEmpty(compiled.OptionsCommands.Commands);
		CompilerDiagnostic error = DiagnosticAssert.SingleError(unit.Diagnostics, 2, "The value of 'Settings=' must be between 0 and 255: 256");
		Assert.AreEqual((0, "Settings=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	public void LevelBoolCommands_AddTheirValueToTheSectionFlags()
	{
		// VB6 ORs the ENABLED or the DISABLED value of a Bool command into the level's flags and keeps the other commands (Module1.bas:8502-8532).
		var unit = new UnitRun();

		NgScriptModel compiled = Compile(
			unit,
			new ParsedScript { Sections = [Section(unit, 4, "CutScene= ENABLED", "Snow= 1", "ForceVolumetricFX= DISABLED")] }
		);

		NgCommandGroup level = compiled.LevelCommands.Single();

		Assert.AreEqual((NgLevelFlags)0x0048, level.LevelFlags);
		Assert.AreSequenceEqual(new short[] { (2 * 256) + 1, 1 }, WordsOf(level.Commands.Single()));
	}

	[TestMethod]
	public void BoolCommandOfAnyCode_IsAFlagInALevel()
	{
		// VB6 tells a flag by its one Bool argument, whatever its code (Module1.bas:7112).
		var unit = new UnitRun();

		var catalog = NgCommandCatalog.Parse(
			["<START_NEW_COMMANDS>", "99#1#ZzFlag=:Bool#$0001#$0000", "<END>"],
			"scripter_constants.txt",
			new DiagnosticSink(new CompilerLog(conciseLogging: true))
		);

		NgScriptModel compiled = Compile(unit, new ParsedScript { Sections = [Section(unit, 4, "ZzFlag= ENABLED")] }, catalog);

		NgCommandGroup level = compiled.LevelCommands.Single();

		Assert.AreEqual((NgLevelFlags)0x0001, level.LevelFlags);
		Assert.IsEmpty(level.Commands);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void BoolCommandOfAnyCode_RepeatedInALevel_IsADuplicateLikeTheStockFlag()
	{
		// VB6 passes -1 for the arguments of any Bool command and compares it by name, so a second line is a duplicate (Module1.bas:7112).
		// The definition allows any count (-1), so the count limit does not answer first.
		var unit = new UnitRun();

		var catalog = NgCommandCatalog.Parse(
			["<START_NEW_COMMANDS>", "99#-1#ZzFlag=:Bool#$0001#$0000", "<END>"],
			"scripter_constants.txt",
			new DiagnosticSink(new CompilerLog(conciseLogging: true))
		);

		Compile(unit, new ParsedScript { Sections = [Section(unit, 4, "ZzFlag= ENABLED", "ZzFlag= DISABLED")] }, catalog);

		DiagnosticAssert.SingleError(unit.Diagnostics, 5, "Duplicated command [ZzFlag=] was already present in same [level/title/option] section");
	}

	[TestMethod]
	public void BoolCommandOfAnyCode_IsAFlagInTheOptions()
	{
		var unit = new UnitRun();

		var catalog = NgCommandCatalog.Parse(
			["<START_NEW_COMMANDS>", "99#1#*ZzOption=:Bool#$0200#$0000", "<END>"],
			"scripter_constants.txt",
			new DiagnosticSink(new CompilerLog(conciseLogging: true))
		);

		NgScriptModel compiled = Compile(unit, new ParsedScript { OptionsNgLines = [Line(unit, 2, "ZzOption= ENABLED")] }, catalog);

		Assert.AreEqual((NgMainFlags)0x0200, compiled.OptionsCommands.OptionFlags);
		Assert.IsEmpty(compiled.OptionsCommands.Commands);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void EachSection_CountsItsOwnLines()
	{
		// VB6 resets the counts and the repeat lists for each level section (Module1.bas:8421-8423).
		var unit = new UnitRun();

		NgScriptModel compiled = Compile(unit, new ParsedScript { Sections = [Section(unit, 4, "Snow= 1"), Section(unit, 9, "Snow= 1")] });

		Assert.AreSequenceEqual([1, 1], compiled.LevelCommands.Select(level => level.Commands.Count));
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void FailingLine_LeavesItsErrorAndTheOtherLinesCompile()
	{
		// VB6 stops at the first line that fails (Module1.bas:8485-8489); the port reports every failing line and compiles the others.
		var unit = new UnitRun();

		NgScriptModel compiled = Compile(unit, new ParsedScript { Sections = [Section(unit, 4, "Snow= 70000", "Rain= 1", "LevelFarView= X")] });

		Assert.AreSequenceEqual(["Rain="], compiled.LevelCommands.Single().Commands.Select(command => command.CommandName));

		DiagnosticAssert.Errors(
			unit.Diagnostics,
			(4, "Argument 1 of Snow= is out of range: 70000 lies outside -32768 to 65535, the values its word holds"),
			(6, "Argument 1 of LevelFarView= is invalid: X is neither a number nor a known name")
		);
	}

	[TestMethod]
	public void PluginDefine_AppliesFromWhereItStands_AndHoldsInTheSectionsAfterIt()
	{
		// VB6 empties the @plugin table before the first level section and never between two (Module1.bas:8420), and applies each define where
		// it stands among the commands (:8455-8460), so an exported trigger above the define keeps the ID it is written with.
		var unit = new UnitRun(pluginScripts: [new PluginScript("Plugin_X", "Plugin_X.script", ["<START_CONSTANTS>", "<END>"])]);
		unit.Plugins.Declare(UnitRun.PluginLine(id: "2", name: "Plugin_X", location: unit.At(2)));
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_X 7", unit.At(3)));

		var parsed = new ParsedScript
		{
			Sections =
			[
				Section(unit, 5, "TriggerGroup= 1,$72000,406,1", "#DEFINE @Plugin_X 7", "TriggerGroup= 2,$72000,406,1"),
				Section(unit, 10, "TriggerGroup= 1,$72000,406,1"),
			],
		};

		NgScriptModel compiled = Compile(unit, parsed);

		Assert.AreSequenceEqual(new short[] { (21 * 256) + 7, 1, 0x2000, 7, 406, 0, 1, 0 }, WordsOf(compiled.LevelCommands[0].Commands[0]));
		Assert.AreSequenceEqual(new short[] { (21 * 256) + 7, 2, 0x2000, 2, 406, 0, 1, 0 }, WordsOf(compiled.LevelCommands[0].Commands[1]));
		Assert.AreSequenceEqual(new short[] { (21 * 256) + 7, 1, 0x2000, 2, 406, 0, 1, 0 }, WordsOf(compiled.LevelCommands[1].Commands[0]));

		Assert.AreEqual(("Plugin_X", 2), (unit.Plugins.Declarations.Single().Name, unit.Plugins.Declarations.Single().Id));
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void ImportArgument_MustNameADeclaredImport()
	{
		// VB6 fails an IMPORT argument whose ID no ImportFile= declares, unless it is -1 (Module1.bas:7464-7484).
		var unit = new UnitRun();

		var catalog = NgCommandCatalog.Parse(
			["<START_NEW_COMMANDS>", "120#-1#PlayImport=:Import", "<END>"],
			"scripter_constants.txt",
			new DiagnosticSink(new CompilerLog(conciseLogging: true))
		);

		var parsed = new ParsedScript
		{
			Sections = [Section(unit, 4, "PlayImport= 3", "PlayImport= IGNORE", "PlayImport= 5")],

			ImportFiles =
			[
				new NgImportFile
				{
					ImportId = 3,
					FileName = "data3.bin",
					ImportMode = NgImportMode.Memory,
					FileType = 1,
					Data = [0],
				},
			],
		};

		NgScriptModel compiled = Compile(unit, parsed, catalog);

		Assert.HasCount(3, compiled.LevelCommands.Single().Commands);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			6,
			"Argument 1 of PlayImport= names import ID 5, which no ImportFile= declares; declare it in [Options] or write a declared ID"
		);
	}

	[TestMethod]
	public void ImportArgument_NamesAnImportByTheWordItsChunkWrites()
	{
		// The chunk writes ID 65534 as the word -2, so an IMPORT argument of -2 names it.
		var unit = new UnitRun();

		var catalog = NgCommandCatalog.Parse(
			["<START_NEW_COMMANDS>", "120#-1#PlayImport=:Import", "<END>"],
			"scripter_constants.txt",
			new DiagnosticSink(new CompilerLog(conciseLogging: true))
		);

		var import = new NgImportFile
		{
			ImportId = 65534,
			FileName = "data.bin",
			ImportMode = NgImportMode.Memory,
			FileType = 1,
			Data = [0],
		};

		var parsed = new ParsedScript { Sections = [Section(unit, 4, "PlayImport= -2")], ImportFiles = [import] };

		Compile(unit, parsed, catalog);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[DataRow(65534)]
	[DataRow(-2)]
	public void ImportArgumentPastTheSignedWord_NamesTheImportOfItsWord(int importId)
	{
		// VB6 compares the argument as written with the signed word it stored, so 65534 names no import (PD-95).
		var unit = new UnitRun();

		var catalog = NgCommandCatalog.Parse(
			["<START_NEW_COMMANDS>", "120#-1#PlayImport=:Import", "<END>"],
			"scripter_constants.txt",
			new DiagnosticSink(new CompilerLog(conciseLogging: true))
		);

		var import = new NgImportFile
		{
			ImportId = importId,
			FileName = "data.bin",
			ImportMode = NgImportMode.Memory,
			FileType = 1,
			Data = [0],
		};

		var parsed = new ParsedScript { Sections = [Section(unit, 4, "PlayImport= 65534")], ImportFiles = [import] };

		Compile(unit, parsed, catalog);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}
}
