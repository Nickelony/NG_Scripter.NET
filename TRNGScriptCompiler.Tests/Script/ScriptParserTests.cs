using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class ScriptParserTests
{
	private const string Language = "[Strings]\nTest Level\n";

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("[Level]", TestScripts.Level + "FooBar= 1\n")]
	[DataRow("[Title]", "[Title]\nLevel= data\\title,0\nLoadCamera= 0,0,0,0,0,0,0\nFooBar= 1\n[Level]\nName= Test Level\nLevel= data\\test,0\n")]
	public void UnknownLevelOrTitleCommand_FailsTheAnalysisWhereItStands(string sectionName, string sections)
	{
		// VB6 looks a [Level] or [Title] command up in its classic table and then in the NG table while it reads the line, and fails on one
		// that is in neither (Module1.bas:5302-5332), so the analysis a host runs reports it.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + sections, Language);

		AnalysisResult result = workspace.Analyze();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual($"Unknown command for {sectionName} section: FooBar=; correct its name or remove the line", diagnostic.Message);
		Assert.AreEqual(sectionName == "[Level]" ? 7 : 6, diagnostic.LineNumber);
		Assert.AreEqual(0, diagnostic.Column);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Commands_AreClassifiedWithoutRegardToCase()
	{
		// Module1.bas compares text without regard to case (Option Compare Text, :4), so each table finds a command however it is spelled
		// (:5302-5332, 5401-5463), and a handler that tests the name, such as Layer1= against Layer2= (:5923, 5934), matches it too (PD-01).
		const string canonical =
			"[Options]\nLoadSave= ENABLED\nDiagnostic= ENABLED\n"
			+ TestScripts.Level
			+ "Fog= 1,2,3\nSnow= 1\n"
			+ "Layer1= 1,2,3,4\nKey= 1,Test Level,1,2,3,4,5,6\nFMV= 1,0\n";

		const string lowercase =
			"[options]\nloadsave= ENABLED\ndiagnostic= ENABLED\n[level]\nname= Test Level\nlevel= data\\test,0\nloadcamera= 0,0,0,0,0,0,0\nfog= 1,2,3\nsnow= 1\n"
			+ "layer1= 1,2,3,4\nkey= 1,Test Level,1,2,3,4,5,6\nfmv= 1,0\n";

		using var canonicalWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + canonical, Language);
		using var lowercaseWorkspace = TestWorkspace.CreateCompilable("[language]\nfile= 0, English.txt\n" + lowercase, Language);

		TestCompileResult canonicalResult = canonicalWorkspace.Compile();
		TestCompileResult lowercaseResult = lowercaseWorkspace.Compile();

		Assert.IsTrue(canonicalResult.Success, canonicalResult.Log);
		Assert.IsTrue(lowercaseResult.Success, lowercaseResult.Log);

		byte[] lowercaseLevel = ScriptDatReader.Read(lowercaseWorkspace.ReadScriptDat()).GetSectionBytes(0);

		Assert.AreSequenceEqual(canonicalWorkspace.ReadScriptDat(), lowercaseWorkspace.ReadScriptDat());
		Assert.IsTrue(ByteSearch.Contains(lowercaseLevel, [0x89, 1, 2, 3, 4]), "layer1= writes the Layer1= tag 0x89.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeThatFailsBelowAnEarlierError_IsNotReported()
	{
		// VB6 opens an include when it reaches the #INCLUDE line (Module1.bas:5173-5207), so an error on an earlier line stops the read
		// before the include is looked for.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Level]\nName= Test Level\nFooBar= 1\n#INCLUDE \"Missing.txt\"\n",
			Language
		);

		AnalysisResult result = workspace.Analyze();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 5,
			message: "Unknown command for [Level] section: FooBar=; correct its name or remove the line",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeThatFails_IsReportedAfterTheLinesAboveIt()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Level]\nName= Test Level\nLevel= data\\test,0\n#INCLUDE \"Missing.txt\"\n",
			Language
		);

		AnalysisResult result = workspace.Analyze();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 6,
			message: "The include file was not found: Missing.txt",
			file: workspace.ScriptFile
		);
	}

	private static void AssertParseFails(string script, int? expectedLine, string expectedError)
	{
		UnitRun.ScriptParse parse = UnitRun.ParseScript(script);

		Assert.IsNull(parse.Parsed);
		DiagnosticAssert.SingleError(parse.Unit.Diagnostics, expectedLine, expectedError);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ReadSource_WithoutTheScriptFile_Fails()
	{
		// VB6 opens script.txt before it reads a line (Module1.bas:5127).
		var unit = new UnitRun();

		ScriptParser parser = unit.CreateScriptParser();

		Assert.IsNull(parser.ReadSource(scriptPath: unit.ScriptFile, scriptDirectory: unit.Root, overlay: null, token: CancellationToken.None));
		DiagnosticAssert.SingleError(unit.Diagnostics, line: null, $"Cannot find script file: {unit.ScriptFile}");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_SortsEachLineIntoItsSection()
	{
		// VB6 keeps the classic lines of a [Title] or [Level] for the section compile and its NG lines for the NG header (Module1.bas:5302-5389),
		// reads the [Options] classics where they stand and keeps their NG lines (:5395-5470), and reads [Language] (:5475-5522).
		UnitRun.ScriptParse parse = UnitRun.ParseScript(
			TestScripts.Language
				+ "[Options]\nLoadSave= ENABLED\nDiagnostic= ENABLED\n[Title]\nLevel= data\\title,1\nLoadCamera= 0,0,0,0,0,0,0\n"
				+ TestScripts.Level
				+ "Fog= 1,2,3\nSnow= 1\n"
		);

		Assert.IsNotNull(parse.Parsed);

		ParsedScript parsed = parse.Parsed;

		Assert.AreSequenceEqual(["English.txt"], parsed.LanguageFiles);
		Assert.AreEqual(ScriptMainFlags.LoadSave, parsed.Options.Flags);

		Assert.AreSequenceEqual(
			[("Diagnostic=", 5)],
			parsed.OptionsNgLines.Select(line => (line.CommandLine.Command, line.CommandLine.Location.Line))
		);

		Assert.AreSequenceEqual([ScriptSectionType.Title, ScriptSectionType.Level], parsed.Sections.Select(section => section.SectionType));

		ParsedSection level = parsed.Sections[1];
		Assert.AreEqual(("Test Level", "data\\test", 9), (level.LevelName, level.LevelPath, level.HeaderLocation.Line));

		Assert.AreSequenceEqual(
			["Name= Test Level", "Level= data\\test,0", TestScripts.LoadCamera, "Fog= 1,2,3"],
			level.ClassicLines.Select(line => line.Text)
		);

		Assert.AreSequenceEqual([("Snow=", 14)], level.NgLines.Select(line => (line.CommandLine.Command, line.CommandLine.Location.Line)));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Include_IsExpandedWhereItStands()
	{
		// VB6 reads an include's lines at its #INCLUDE line, into the section that line stands in (Module1.bas:5173-5207).
		UnitRun.ScriptParse parse = UnitRun.ParseScript(
			TestScripts.Language + "[Level]\n#INCLUDE \"Rooms.txt\"\nName= Test Level\nLevel= data\\test,0\n",
			("Rooms.txt", "LoadCamera= 0,0,0,0,0,0,0\n")
		);

		Assert.IsNotNull(parse.Parsed);

		Assert.AreSequenceEqual(
			[
				(TestScripts.LoadCamera, 1, parse.Unit.PathOf("Rooms.txt")),
				("Name= Test Level", 5, parse.Unit.ScriptFile),
				("Level= data\\test,0", 6, parse.Unit.ScriptFile),
			],

			parse.Parsed.Sections.Single().ClassicLines.Select(line => (line.Text, line.Location.Line, line.Location.File))
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void PluginLines_AreCollectedFromEveryFileWhereverTheyStand()
	{
		// VB6 builds its plugin table from every Plugin= line before it compiles (Module1.bas:3868, 15024-15104); the port reads the lines of
		// included files too (PD-87).
		UnitRun.ScriptParse parse = UnitRun.ParseScript(
			TestScripts.Language + "[Options]\nPlugin= 1, Plugin_One, 0\n#INCLUDE \"More.txt\"\n" + TestScripts.Level,
			("More.txt", "Plugin= 2, Plugin_Two, 0\n")
		);

		Assert.IsNotNull(parse.Source);

		Assert.AreSequenceEqual(
			[("1", "Plugin_One", 4), ("2", "Plugin_Two", 1)],
			parse.Source.PluginLines.Select(line => (line.CommandLine.Arguments[0], line.CommandLine.Arguments[1], line.CommandLine.Location.Line))
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void DefinesOfAFile_AreRecordedBeforeItsFirstLineIsRead()
	{
		// VB6 reads the #DEFINE lines of script.txt before its first command (Module1.bas:5094), so a number may name a define of a later line.
		UnitRun.ScriptParse parse = UnitRun.ParseScript("[Language]\nFile= FIRST_FILE, English.txt\n" + TestScripts.Level + "#DEFINE FIRST_FILE 0\n");

		Assert.IsNotNull(parse.Parsed);
		Assert.AreSequenceEqual(["English.txt"], parse.Parsed.LanguageFiles);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void FirstIdDirective_IsPassedOver()
	{
		// VB6 reads #FIRST_ID as a directive and does nothing with it here (Module1.bas:4970, 5213).
		UnitRun.ScriptParse parse = UnitRun.ParseScript(TestScripts.Language + TestScripts.Level + "#FIRST_ID 5\n");

		Assert.IsNotNull(parse.Parsed);
		Assert.HasCount(3, parse.Parsed.Sections.Single().ClassicLines);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void MistypedDirective_Fails()
	{
		// VB6 compares the whole first word (Module1.bas:4966-4974), reads #INCLUDEX as a command and fails it inside a section (:5293-5299)
		// (PD-57).
		AssertParseFails(
			TestScripts.Language + TestScripts.Level + "#INCLUDEX \"Rooms.txt\"\n",
			7,
			"#INCLUDEX is not a directive; write #INCLUDE followed by a space"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LineAboveTheFirstSection_Fails()
	{
		// VB6 warns that the line is out of context and ignores it (Module1.bas:5272-5276); the port fails, because the line has no effect
		// (PD-55).
		AssertParseFails(
			"Snow= 1\n" + TestScripts.Language + TestScripts.Level,
			1,
			"The line 'Snow= 1' stands above the first section, where it has no effect; move it into the section it belongs to"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LineWithoutAnEqualsSign_Fails()
	{
		// VB6 fails a line without '=' as a missing command (Module1.bas:5292-5299).
		AssertParseFails(
			TestScripts.Language + TestScripts.Level + "Fog 1,2,3\n",
			7,
			"Syntax error: missing command, because the line has no '=' character"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void PluginDefineOutsideALevelOrTitle_Fails_AndInsideOneStandsAmongItsNgLines()
	{
		// VB6 reads a #define @plugin by its place among the NG commands of a [Level] or [Title] (Module1.bas:5283-5289), and as an unknown
		// command elsewhere (:5433-5440) (PD-55).
		AssertParseFails(
			TestScripts.Language + "[Options]\n#DEFINE @Plugin_One 1\n" + TestScripts.Level,
			4,
			"A #DEFINE @plugin line names the plugin of the exported triggers that follow it in a [Level] or [Title] section, and has no effect in [Options]; move it into the section of its triggers"
		);

		UnitRun.ScriptParse parse = UnitRun.ParseScript(TestScripts.Language + TestScripts.Level + "#DEFINE @Plugin_One 1\nSnow= 1\n");

		Assert.IsNotNull(parse.Parsed);

		Assert.AreSequenceEqual(
			[(true, 7), (false, 8)],
			parse.Parsed.Sections.Single().NgLines.Select(line => (line.IsPluginDefine, line.CommandLine.Location.Line))
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ReadError_IsReportedAfterTheLinesReadBeforeIt()
	{
		// VB6 reads and checks each line as it goes (Module1.bas:5138-5299), so a line that fails above a bad continuation is the one reported.
		UnitRun.ScriptParse parse = UnitRun.ParseScript(TestScripts.Language + TestScripts.Level + "Fog= 1,2,>");

		Assert.IsNotNull(parse.Source?.ReadError);
		Assert.HasCount(6, parse.Source.Lines);
		Assert.IsNull(parse.Parsed);

		DiagnosticAssert.SingleError(
			diagnostics: parse.Unit.Diagnostics,
			line: 7,
			message: "A '>' continuation on the last line of the file has no line to join; remove the '>'",
			file: parse.Unit.ScriptFile
		);

		AssertParseFails(
			TestScripts.Language + "[Level]\nFooBar= 1\nFog= 1,2,>",
			4,
			"Unknown command for [Level] section: FooBar=; correct its name or remove the line"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ScriptWithoutALevelOrTitle_Fails()
	{
		// VB6 sizes its level-name offsets with ReDim VetOffset(TotLivelli - 1), which fails without a stored section (Module1.bas:6728).
		AssertParseFails(
			TestScripts.Language,
			null,
			"The script has no [Title] or [Level] section, so the game has no level to start; add a [Level] section"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("[PSXExtensions]\nLevel= PSX\n[PCExtensions]\nLevel= TR4\nFMV= BIK\nCut= TR4\n[PSXExtensions]\nFMV= FMV\n", 3, 2)]
	[DataRow("[PCExtensions]\nLevel= TR4\nFMV= BIK\nCut= TR4\n", null, 0)]
	public void ExtensionSectionWithoutThreeExtensions_WarnsAtItsFirstHeader(string extensionSections, int? line, int count)
	{
		// VB6 warns at no line when an extension section fills other than three slots (Module1.bas:5626-5636); the port points at the first
		// header of the section, and at no line when the script has no such section.
		UnitRun.ScriptParse parse = UnitRun.ParseScript(TestScripts.Language + extensionSections + TestScripts.Level);

		Assert.IsNotNull(parse.Parsed);
		Assert.HasCount(1, parse.Unit.Diagnostics);

		CompilerDiagnostic warning = DiagnosticAssert.HasWarning(
			diagnostics: parse.Unit.Diagnostics,
			line,
			message: $"Unexpected number of extensions in [PSXExtensions] section: {count}",
			file: line is null ? null : parse.Unit.ScriptFile
		);

		if (line is null)
			Assert.IsNull(warning.SourceFile);

		Assert.AreSequenceEqual(new[] { "TR4", "BIK", "TR4" }, parse.Parsed.PcExtensions.ToArray());
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= 1,2,3")]
	[DataRow("#define @PluginDemo 9")]
	[DataRow("#DEFINE @plugins clear")]
	public void LineBeforeAnySection_Fails(string strayLine)
	{
		// PD-55: VB6 warns that the line is out of context and ignores it (Module1.bas:5272-5276); a #define @plugin is read by its
		// place among the commands (:4958-4963, 5283-5289), so it takes the same path.
		using var workspace = TestWorkspace.CreateCompilable("; A comment\n\n" + strayLine + "\n" + TestScripts.Language + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A line above the first section has no effect.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: $"The line '{strayLine}' stands above the first section, where it has no effect; move it into the section it belongs to",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DirectivesBeforeAnySection_StayLegal()
	{
		// VB6 reads #DEFINE, #INCLUDE and #FIRST_ID wherever they stand (Module1.bas:5176, 5213, 4970). The script gives no
		// [Language] section itself, so only the include supplies the language file, and the define sets the Fog= color.
		using var workspace = TestWorkspace.CreateCompilable(
			"; A comment\n\n#DEFINE CORPUS_SPEED 4\n#FIRST_ID 100\n#INCLUDE \"inc.txt\"\n" + TestScripts.Level + "Fog= CORPUS_SPEED,2,3\n"
		);

		workspace.WithTextFile(Path.Combine("script", "inc.txt"), "; Included above the first section\n" + TestScripts.Language);

		using var plainWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Fog= 4,2,3\n");

		TestCompileResult result = workspace.Compile();
		TestCompileResult plainResult = plainWorkspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(plainResult.Success, plainResult.Log);

		Assert.AreSequenceEqual(plainWorkspace.ReadScriptDat(), workspace.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnknownBracketSection_FailsWithASyntaxError()
	{
		// Inside an open section, '[Bogus]' is not a section header and has no '=' -> fatal.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "[Bogus]\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "An unknown [Section] is not a section: it fails as a missing '=' line.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 7,
			message: "Syntax error: missing command, because the line has no '=' character",
			file: workspace.ScriptFile
		);
	}
}
