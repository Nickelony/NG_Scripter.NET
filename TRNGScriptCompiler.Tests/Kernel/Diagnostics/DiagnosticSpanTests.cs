using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Diagnostics;

/// <summary>
/// The span of a diagnostic: what the sink records from a location, and the exact (LineNumber, Column, Length)
/// triple of a diagnostic that a real compilation run produces. No assertion is weaker than exact equality.
/// </summary>
[TestClass]
public sealed class DiagnosticSpanTests
{
	private const string UnknownOptionsScript =
		"[Language]\nFile= 0, English.txt\n[Options]\nLoadSave= ENABLED\nFooBar= 123\n[Level]\nName= Test\nLoadCamera= 0,0,0,0,0,0,0\n";

	private const string PastTheWordMessage =
		"Argument 2 of TriggerGroup= is out of range: 70000 lies outside -32768 to 65535, the values its word holds; TriggerGroup= keeps its long form only for a $ literal with a plugin ID, so write the value as one";

	private static string NopeMessage(string command, int argument) =>
		$"Argument {argument} of {command} is invalid: nope is neither a number nor a known name";

	private static CompilerDiagnostic AnalyzeToError(string optionsLines, int line, string message)
	{
		using var workspace = TestWorkspace.Create(
			"[Language]\nFile= 0, English.txt\n[Options]\n"
				+ optionsLines
				+ "\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n"
		);

		workspace.WithEnglishLanguageFile().WithBinaryFile("a.bin", [1]).WithBinaryFile(Path.Combine("extras", "a.bin"), [2]);

		AnalysisResult result = new TRNGCompiler().Analyze(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);
		return DiagnosticAssert.SingleError(result.Diagnostics, line, message);
	}

	private static CompilerDiagnostic AnalyzeLevelToError(string levelLines, int line, string message)
	{
		using var workspace = TestWorkspace.Create(
			"[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n" + levelLines + "\n"
		);

		workspace.WithEnglishLanguageFile();

		AnalysisResult result = new TRNGCompiler().Analyze(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);
		return DiagnosticAssert.SingleError(result.Diagnostics, line, message);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("Fog= 1,FOGGY,3", "foggy", 7, 5, DisplayName = nameof(ForTerm_MarksTheTermOrTheWholeLine) + " (Found, ignoring case)")]
	[DataRow("Fog= 1,2,3", "FOGGY", 0, 0, DisplayName = nameof(ForTerm_MarksTheTermOrTheWholeLine) + " (Missing: whole line)")]
	public void ForTerm_MarksTheTermOrTheWholeLine(string line, string term, int expectedColumn, int expectedLength)
	{
		var logger = new CompilerLog(conciseLogging: true);
		var diagnostics = new DiagnosticSink(logger);

		string file = Path.GetFullPath("Script.txt");

		diagnostics.Error("Message", SourceLocation.ForTerm(line: 4, text: line, term, file));

		CompilerDiagnostic diagnostic = diagnostics.Recorded.Single();

		Assert.AreEqual(
			(4, expectedColumn, expectedLength, file),
			(diagnostic.LineNumber, diagnostic.Column, diagnostic.Length, diagnostic.SourceFile)
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("#DEFINE DEF 1", 1, "DEF", 8, 3, DisplayName = nameof(ForWord_MarksTheWordAtItsIndexOrTheWholeLine) + " (Inside an earlier word)")]
	[DataRow("#DEFINE A5 5", 2, "5", 11, 1, DisplayName = nameof(ForWord_MarksTheWordAtItsIndexOrTheWholeLine) + " (Repeating an earlier word)")]
	[DataRow("#DEFINE\t\tX \t Y", 2, "y", 13, 1, DisplayName = nameof(ForWord_MarksTheWordAtItsIndexOrTheWholeLine) + " (Tabs and case)")]
	[DataRow("#DEFINE A5 5", 2, "6", 0, 0, DisplayName = nameof(ForWord_MarksTheWordAtItsIndexOrTheWholeLine) + " (Other word: whole line)")]
	[DataRow("#DEFINE A5", 2, "5", 0, 0, DisplayName = nameof(ForWord_MarksTheWordAtItsIndexOrTheWholeLine) + " (Past the end: whole line)")]
	public void ForWord_MarksTheWordAtItsIndexOrTheWholeLine(string line, int wordIndex, string word, int expectedColumn, int expectedLength)
	{
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		string file = Path.GetFullPath("Script.txt");

		diagnostics.Error("Message", SourceLocation.ForWord(line: 4, text: line, wordIndex, word, file));

		CompilerDiagnostic diagnostic = diagnostics.Recorded.Single();

		Assert.AreEqual(
			(4, expectedColumn, expectedLength, file),
			(diagnostic.LineNumber, diagnostic.Column, diagnostic.Length, diagnostic.SourceFile)
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnknownOptionsCommand_SpanPointsAtCommandName()
	{
		using var workspace = TestWorkspace.Create(UnknownOptionsScript);
		workspace.WithEnglishLanguageFile();

		// The unknown command is on line 5 (1-indexed) and starts at column 0.
		const int expectedLine = 5;
		const int expectedColumn = 0;

		var compiler = new TRNGCompiler { ConciseLogging = false };
		AnalysisResult result = compiler.Analyze(workspace.CreateOptions());

		// Unknown commands are fatal in VB6, but the diagnostic must still carry
		// the command-name span (see PD-04 in docs/PARITY-DECISIONS.md).
		Assert.IsFalse(result.Success, $"Analysis should fail (unknown command is fatal like VB6): {result.Log}");

		CompilerDiagnostic unknownDiag = DiagnosticAssert.SingleError(
			result.Diagnostics,
			expectedLine,
			"Unknown command for [Options] section: FooBar=; correct its name or remove the line"
		);

		Assert.AreEqual(expectedColumn, unknownDiag.Column);
		Assert.AreEqual("FooBar=".Length, unknownDiag.Length, "Length must be exactly the command name length.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnknownOptionsCommandAfterTabs_SpanCountsTheTabs()
	{
		CompilerDiagnostic error = AnalyzeToError(
			"\t\tFooBar= 123",
			line: 4,
			message: "Unknown command for [Options] section: FooBar=; correct its name or remove the line"
		);

		Assert.AreEqual((2, "FooBar=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void RepeatedImportIdAfterSpaces_SpanCountsTheSpaces()
	{
		CompilerDiagnostic error = AnalyzeToError(
			"ImportFile= 7, a.bin, FTYPE_USERFILE, IMPORT_MEMORY\nImportFile=      7, extras\\a.bin, FTYPE_USERFILE, IMPORT_TEMPORARY",
			line: 5,
			message: "ImportFile= ID 7 is already declared for a.bin; give each ImportFile= its own ID"
		);

		Assert.AreEqual(("ImportFile=      ".Length, "7".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileModeThatRepeatsTheId_SpanMarksTheModeNotTheId()
	{
		// The first 3 in the line is the ID, which is valid, and the mode is the last argument.
		const string command = "ImportFile= 3, a.bin, 0, 3";

		CompilerDiagnostic error = AnalyzeToError(
			command,
			line: 4,
			message: "The ImportFile= mode 3 lies outside 0 to 2; write 0, IMPORT_MEMORY or IMPORT_TEMPORARY"
		);

		Assert.AreEqual((command.LastIndexOf('3'), "3".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void PluginNameThatIsItsIdText_SpanMarksTheIdArgument()
	{
		// The ID text is also the start of the command name, so a search for it by text would mark the command.
		CompilerDiagnostic error = AnalyzeToError(
			"Plugin= Plugin, Plugin, IGNORE",
			line: 4,
			message: "The plugin ID 'Plugin' of Plugin= is not a number; write the ID as a decimal or $ number"
		);

		Assert.AreEqual(("Plugin= ".Length, "Plugin".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgCommandIndentedWithSpaces_SpanCountsTheSpaces()
	{
		CompilerDiagnostic error = AnalyzeToError("    Settings= 256", line: 4, message: "The value of 'Settings=' must be between 0 and 255: 256");
		Assert.AreEqual((4, "Settings=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgCommandInTheWrongSectionAfterATab_SpanCountsTheTab()
	{
		CompilerDiagnostic error = AnalyzeLevelToError("\tSettings= 0", line: 7, message: "Settings= can only be used in [Options] section");
		Assert.AreEqual((1, "Settings=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DuplicatedNgCommandAfterATab_SpanCountsTheTab()
	{
		CompilerDiagnostic error = AnalyzeLevelToError(
			"\tColorRGB= 1,1,1,1\n\tColorRGB= 1,2,3,4",
			line: 8,
			message: "Duplicated command [ColorRGB= 1, ...] was already present in same [level/title/option] section"
		);

		Assert.AreEqual((1, "ColorRGB=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgArgumentThatDoesNotEvaluate_SpanMarksTheArgument()
	{
		const string command = "\tColorRGB= 1, 2, nope, 4";

		CompilerDiagnostic error = AnalyzeLevelToError(command, line: 7, message: NopeMessage("ColorRGB=", argument: 3));

		Assert.AreEqual((command.IndexOf("nope", StringComparison.Ordinal), "nope".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgArgumentOfAnArrayThatDoesNotEvaluate_SpanMarksTheArgument()
	{
		const string command = "\tTriggerGroup= 1, $32000, 406, 1, $32000, nope, 2";

		CompilerDiagnostic error = AnalyzeLevelToError(command, line: 7, message: NopeMessage("TriggerGroup=", argument: 6));

		Assert.AreEqual((command.IndexOf("nope", StringComparison.Ordinal), "nope".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TriggerGroupValuePastTheWord_SpanMarksTheValue()
	{
		const string command = "\tTriggerGroup= 1, 70000, 406, 1";

		CompilerDiagnostic error = AnalyzeLevelToError(command, line: 7, message: PastTheWordMessage);

		Assert.AreEqual((command.IndexOf("70000", StringComparison.Ordinal), "70000".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgArgumentCount_SpanMarksTheWholeLine()
	{
		CompilerDiagnostic error = AnalyzeLevelToError(
			"\tColorRGB= 1, 2",
			line: 7,
			message: "Wrong number of arguments for 'ColorRGB=': expected 4, got 2"
		);

		Assert.AreEqual((0, 0), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ClassicArgumentThatDoesNotEvaluate_SpanMarksTheArgument()
	{
		const string command = "\tFog= 1, 2, nope";

		CompilerDiagnostic error = AnalyzeLevelToError(command, line: 7, message: NopeMessage("Fog=", argument: 3));

		Assert.AreEqual((command.IndexOf("nope", StringComparison.Ordinal), "nope".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ClassicArgumentOutOfRange_SpanMarksTheArgument()
	{
		const string command = "\tAnimatingMIP=  17 , 3";

		CompilerDiagnostic error = AnalyzeLevelToError(
			command,
			line: 7,
			message: "Argument 1 of AnimatingMIP= is out of range: 17 lies outside 1 to 16, the values a MIP slot holds"
		);

		Assert.AreEqual((command.IndexOf("17", StringComparison.Ordinal), "17".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ClassicArgumentCount_SpanMarksTheWholeLine()
	{
		CompilerDiagnostic error = AnalyzeLevelToError("\tFog= 1, 2", line: 7, message: "Wrong number of arguments for 'Fog=': expected 3, got 2");
		Assert.AreEqual((0, 0), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TermOnTheSecondPhysicalLineOfAContinuedLine_MarksTheWholeFirstLine()
	{
		// The 5 stands on the second physical line, so the first one cannot hold it and the diagnostic marks the line it starts on.
		CompilerDiagnostic error = AnalyzeToError(
			"ImportFile= 7, a.bin, FTYPE_USERFILE,>\n    5",
			line: 4,
			message: "The ImportFile= mode 5 lies outside 0 to 2; write 0, IMPORT_MEMORY or IMPORT_TEMPORARY"
		);

		Assert.AreEqual((0, 0), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TermThatOnlyTheTrailingCommentHolds_MarksTheWholeLine()
	{
		// The 5 stands in the comment of the first physical line and on the second one, and the source line ends where its comment starts.
		CompilerDiagnostic error = AnalyzeToError(
			"ImportFile= 7, a.bin, FTYPE_USERFILE,> ; the mode 5 follows\n    5",
			line: 4,
			message: "The ImportFile= mode 5 lies outside 0 to 2; write 0, IMPORT_MEMORY or IMPORT_TEMPORARY"
		);

		Assert.AreEqual((0, 0), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LineAboveTheFirstSection_SpanMarksItsFirstWordInTheSourceLine()
	{
		using var workspace = TestWorkspace.Create("\t  Fog= 1 ; a comment\n[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\n");
		workspace.WithEnglishLanguageFile();

		AnalysisResult result = new TRNGCompiler().Analyze(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			result.Diagnostics,
			line: 1,
			message: "The line 'Fog= 1' stands above the first section, where it has no effect; move it into the section it belongs to"
		);

		Assert.AreEqual((3, "Fog=".Length), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LineWithoutACommand_MarksTheWholeLine()
	{
		CompilerDiagnostic error = AnalyzeToError(
			"\tNoEquals",
			line: 4,
			message: "Syntax error: missing command, because the line has no '=' character"
		);

		Assert.AreEqual((0, 0), (error.Column, error.Length));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void PluginDuplicateId_SpanPointsAtIdValue()
	{
		// Two different plugin names with the same ID (42) → duplicate ID error on line 5.
		const string script =
			"[Language]\nFile= 0, English.txt\n[Level]\n#DEFINE @Plugin_A 42\n#DEFINE @Plugin_B 42\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithEnglishLanguageFile();

		// Line 5: "#DEFINE @Plugin_B 42" - the ID value starts after "#DEFINE @Plugin_B ".
		const int expectedLine = 5;
		const int expectedColumn = 18;

		var compiler = new TRNGCompiler { ConciseLogging = false };
		AnalysisResult result = compiler.Analyze(workspace.CreateOptions());

		Assert.IsFalse(result.Success, $"Analysis should fail due to duplicate plugin ID: {result.Log}");

		CompilerDiagnostic dupDiag = DiagnosticAssert.SingleError(
			result.Diagnostics,
			expectedLine,
			"The plugin ID 42 is already defined for another plugin; add a [#DEFINE @plugins clear] line to clear the previous definitions"
		);

		Assert.AreEqual(expectedColumn, dupDiag.Column);
		Assert.AreEqual("42".Length, dupDiag.Length, "Length must be the duplicate ID value length.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DefineValueError_SpanPointsAtBadValue()
	{
		const string script =
			"#DEFINE MY_CONST notanumber\n[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithEnglishLanguageFile();

		// Line 1: "#DEFINE MY_CONST notanumber" - the bad value starts after "#DEFINE MY_CONST ".
		const int expectedLine = 1;
		const int expectedColumn = 17;

		var compiler = new TRNGCompiler { ConciseLogging = false };
		AnalysisResult result = compiler.Analyze(workspace.CreateOptions());

		Assert.IsFalse(result.Success, $"Analysis should fail due to invalid define value: {result.Log}");

		CompilerDiagnostic defineDiag = DiagnosticAssert.SingleError(
			result.Diagnostics,
			expectedLine,
			"The #DEFINE value [notanumber] is invalid: notanumber is neither a number nor a known name"
		);

		Assert.AreEqual(expectedColumn, defineDiag.Column);
		Assert.AreEqual("notanumber".Length, defineDiag.Length, "Length must be exactly the bad value length.");
	}
}
