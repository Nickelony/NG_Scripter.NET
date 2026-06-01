using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
public sealed class NgParserTests
{
	private static readonly LanguageData s_language = UnitRun.Language(["Test Level"], [new ExtraNgString { Index = 31, Text = "Plugin_X" }]);

	private sealed class Parser
	{
		private readonly NgParser _parser;

		internal Parser(UnitRun? unit = null)
		{
			Unit = unit ?? new UnitRun();

			_parser = new NgParser(
				run: Unit.Run,
				commandCatalog: UnitRun.CommandCatalog,
				evaluator: Unit.Evaluator,
				objectsH: Unit.ObjectsH,
				plugins: Unit.Plugins,
				languageData: s_language
			);
		}

		internal UnitRun Unit { get; }

		internal NgCommand? Level(int line, string command, string arguments, bool isTitle = false) =>
			_parser.Parse(Line(line, command, arguments), isOptionsCommand: false, isTitleCommand: isTitle);

		internal NgCommand? Options(int line, string command, string arguments) =>
			_parser.Parse(Line(line, command, arguments), isOptionsCommand: true);

		internal void NewSection() => _parser.ResetOccurrenceCounts();

		private ScriptCommandLine Line(int line, string command, string arguments)
		{
			string text = $"{command} {arguments}";
			List<string> split = [.. arguments.Split(',').Select(argument => argument.Trim())];

			return new ScriptCommandLine(text, SourceText: text, command, Arguments: split, Location: Unit.At(line));
		}
	}

	private static NgCommand AssertParses(NgCommand? command, Parser parser)
	{
		Assert.IsNotNull(command, string.Join("; ", parser.Unit.Diagnostics.Select(diagnostic => diagnostic.Message)));
		return command;
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LevelCommand_CompilesToItsWords_WithItsLineAndFile()
	{
		// VB6 compiles each NG line of a level section into its words (Module1.bas:8485-8518).
		var parser = new Parser();

		NgCommand command = AssertParses(parser.Level(7, "Snow=", "1"), parser);

		Assert.AreEqual(
			("Snow=", NgCommandCode.Snow, 7, parser.Unit.ScriptFile),
			(command.CommandName, command.CommandCode, command.Location.Line, command.Location.File)
		);

		Assert.AreSequenceEqual(new short[] { (2 * 256) + 1, 1 }, command.CompiledWords);
		DiagnosticAssert.NoDiagnostics(parser.Unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("Settings=", "8", false, false, "Settings= can only be used in [Options] section")]
	[DataRow("DefaultWindowsFont=", "1,2,3,4,5,6,7,8,9,10,11,12,13,14", false, false, "DefaultWindowsFont= can only be used in [Title] section")]
	[DataRow("Snow=", "1", true, false, "Snow= can only be used in a [Level] or [Title] section")]
	public void CommandOutsideItsSection_FailsAtItsName(string command, string arguments, bool inOptions, bool inTitle, string expectedError)
	{
		// VB6 fails an [Options] command in a level section and DefaultWindowsFont= outside [Title] (Module1.bas:5333-5352), and a level command
		// in [Options] (:5441-5445).
		var parser = new Parser();

		Assert.IsNull(inOptions ? parser.Options(7, command, arguments) : parser.Level(line: 7, command, arguments, isTitle: inTitle));

		CompilerDiagnostic error = DiagnosticAssert.SingleError(parser.Unit.Diagnostics, 7, expectedError);
		Assert.AreEqual((0, command.Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TriggerGroupWord_IsNoScriptCommand()
	{
		// The constants file names the word form of TriggerGroup= (code 46) TriggerGroup= too, and VB6 takes it only from TriggerGroup=
		// (Module1.bas:7327-7353), so the name the port gives the form is no command.
		var parser = new Parser();

		Assert.IsNull(parser.Level(7, "TriggerGroupWord=", "1,2,3,4"));

		DiagnosticAssert.SingleError(
			parser.Unit.Diagnostics,
			7,
			"TriggerGroupWord= is not a script command; write TriggerGroup=, which takes the word form when no argument is a $ literal with a plugin ID"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void OccurrenceLimit_FailsTheLinePastIt_UntilTheNextSection()
	{
		// VB6 counts the lines of each command in a level section, fails one past the limit of its definition (Module1.bas:8480-8497) and
		// resets the counts for each section (:8423).
		var parser = new Parser();

		AssertParses(parser.Level(4, "Damage=", "1,2,3,4,Test Level,5"), parser);
		AssertParses(parser.Level(5, "Damage=", "2,2,3,4,Test Level,5"), parser);

		Assert.IsNull(parser.Level(6, "Damage=", "3,2,3,4,Test Level,5"));
		parser.NewSection();
		AssertParses(parser.Level(9, "Damage=", "3,2,3,4,Test Level,5"), parser);

		DiagnosticAssert.SingleError(parser.Unit.Diagnostics, 6, "Too many Damage= commands; a section holds at most 2");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TriggerGroup_TakesTheWordForm_UnlessADollarLiteralCarriesAPluginId()
	{
		// VB6 keeps the long form only when the first $ literal of an argument has bits 16-23 set, and writes the word form otherwise
		// (Module1.bas:7302-7353); the long form writes each value as a dword (:7549-7576).
		var parser = new Parser();

		NgCommand word = AssertParses(parser.Level(4, "TriggerGroup=", "1,$2000,406,1"), parser);
		NgCommand dword = AssertParses(parser.Level(5, "TriggerGroup=", "2,$32000,406,1"), parser);

		Assert.AreEqual(NgCommandCode.TriggerGroupWord, word.CommandCode);
		Assert.AreSequenceEqual(new short[] { (46 * 256) + 4, 1, 0x2000, 406, 1 }, word.CompiledWords);
		Assert.AreEqual(NgCommandCode.TriggerGroup, dword.CommandCode);
		Assert.AreSequenceEqual(new short[] { (21 * 256) + 7, 2, 0x2000, 3, 406, 0, 1, 0 }, dword.CompiledWords);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExportedTriggerPluginId_IsTranslatedToTheIdOfItsPluginLine()
	{
		// VB6 translates the high word of the first value of each triplet through the #DEFINE @plugin table into the ID of the plugin's
		// Plugin= line (Module1.bas:6969-7012, 7558-7574).
		var unit = new UnitRun(pluginScripts: [new PluginScript("Plugin_X", "Plugin_X.script", ["<START_CONSTANTS>", "<END>"])]);
		unit.Plugins.Declare(UnitRun.PluginLine(id: "2", name: "Plugin_X", location: unit.At(2)));
		unit.Plugins.Defines.Apply(UnitRun.PluginDefineLine("#DEFINE @Plugin_X 7", unit.At(3)));
		var parser = new Parser(unit);

		NgCommand command = AssertParses(parser.Level(4, "TriggerGroup=", "1,$72000,406,1,$72000,407,2"), parser);

		Assert.AreSequenceEqual(new short[] { (21 * 256) + 13, 1, 0x2000, 2, 406, 0, 1, 0, 0x2000, 2, 407, 0, 2, 0 }, command.CompiledWords);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ArgumentsPast255Words_Fail()
	{
		// The low byte of the first word holds the word count, and VB6 fails 256 or more (Module1.bas:7358-7362).
		var parser = new Parser();
		string fixedArguments = "1,2,3,4,5,6,7,";

		NgCommand largest = AssertParses(parser.Level(4, "Animation=", fixedArguments + string.Join(",", Enumerable.Repeat("0", 248))), parser);
		Assert.IsNull(parser.Level(5, "Animation=", fixedArguments + string.Join(",", Enumerable.Repeat("0", 249))));

		Assert.AreEqual(unchecked((short)((10 * 256) + 255)), largest.CompiledWords[0]);

		DiagnosticAssert.SingleError(
			parser.Unit.Diagnostics,
			5,
			"Animation= writes 256 words of arguments, but a command holds at most 255; give fewer values"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(TestScripts.Level + "TriggerGroupWord= 1,$2000,406,1\n", 7)]
	[DataRow("[Options]\nTriggerGroupWord= 1,$2000,406,1\n" + TestScripts.Level, 4)]
	public void WordFormNameOfTriggerGroup_Fails(string sections, int lineNumber)
	{
		// VB6 knows code 46 only as the form TriggerGroup= takes (Module1.bas:7328-7335), so TriggerGroupWord= is an unknown
		// command (:5320-5330).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + sections, TestWorkspace.CompilableLanguageContent);
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			lineNumber,
			"TriggerGroupWord= is not a script command; write TriggerGroup=, which takes the word form when no argument is a $ literal with a plugin ID"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Customize= 1,2,3", "Customize=")]
	[DataRow("CutScene= ENABLED", "CutScene=")]
	public void LevelOnlyNgCommandInOptions_Fails(string optionsLine, string command)
	{
		// VB6 error 284: an NG command whose constants-file name lacks the * prefix fails in [Options] (Module1.bas:4630-4639,
		// 5441-5445).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Options]\n" + optionsLine + "\n" + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 4, $"{command} can only be used in a [Level] or [Title] section");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OnceOnlyOptionsCommand_RepeatedWithOtherValues_Fails()
	{
		// PD-41: VB6 counts occurrences only in levels (Module1.bas:8480, 8491), so a once-only command that [Options] repeats
		// with other values compiles and writes both.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nDiagnosticType= 1,2\nDiagnosticType= 3,2\n" + TestScripts.Level
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 5, "Too many DiagnosticType= commands; a section holds at most 1");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void SeveralOptionsSections_ShareOneOccurrenceTable()
	{
		// VB6 checks the joined [Options] list against one duplicate table, cleared once (Module1.bas:8306-8311), and counts
		// no occurrences there. The port counts once-only commands across the sections (PD-41), so a command the second
		// section repeats counts against the first section's.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nWorldFarView= 127\n[Options]\nWorldFarView= 127\n" + TestScripts.Level
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 6, "Too many WorldFarView= commands; a section holds at most 1");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void SeveralOptionsSections_CompileAsOneList()
	{
		// VB6 appends the NG commands of every [Options] section to one list (Module1.bas:5448-5456) and compiles that list once
		// (Module1.bas:8306-8311), so splitting [Options] in two writes the same bytes as one section.
		using var single = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nDiagnostic= ENABLED\nWorldFarView= 127\nDiagnosticType= 1,2\n" + TestScripts.Level
		);

		using var split = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nDiagnostic= ENABLED\nWorldFarView= 127\n" + TestScripts.Level + "[Options]\nDiagnosticType= 1,2\n"
		);

		TestCompileResult singleResult = single.Compile();
		TestCompileResult splitResult = split.Compile();

		Assert.IsTrue(singleResult.Success, singleResult.Log);
		Assert.IsTrue(splitResult.Success, splitResult.Log);

		Assert.AreSequenceEqual(single.ReadScriptDat(), split.ReadScriptDat());
	}
}
