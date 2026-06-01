using System.Text;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class MainLanguagePublicationTests
{
	private const string TwoLanguageScript = TestScripts.Language + "File= 1, French.txt\n" + TestScripts.Level;

	[TestMethod]
	[TestCategory("IO")]
	public void OnlyTheMainLanguageDat_IsPublished_AndAnotherLanguageDatIsRemoved()
	{
		// VB6 writes every language .dat into script\, then copies script.dat and the main language .dat into the engine
		// folder (Module1.bas:8961-8973). The port writes no script\ copies and publishes the same two files.
		using var workspace = TestWorkspace.CreateCompilable(TwoLanguageScript);
		workspace.WithLanguageFile("French.txt", "[Strings]\nTest Level\n");
		workspace.WithBinaryFile("English.dat", Encoding.ASCII.GetBytes("stale-root-language"));

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "French"));

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(File.Exists(workspace.PathOf("Script.dat")), "Script.dat ships.");
		Assert.IsTrue(File.Exists(workspace.PathOf("French.dat")), "The main language's .dat ships.");
		Assert.IsFalse(File.Exists(workspace.PathOf("English.dat")), "Another language's .dat is removed from the engine folder.");
		Assert.IsEmpty(Directory.GetFiles(workspace.ScriptDirectory, "*.dat"), "Nothing is written into the script folder.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageThatIsNotTheMainOne_IsStillCompiledAndChecked()
	{
		// VB6 compiles every listed language (Module1.bas:8885-8938), so an error in one that does not ship still fails the run.
		using var workspace = TestWorkspace.CreateCompilable(TwoLanguageScript);
		workspace.WithLanguageFile("French.txt", "[Strings]\nTest Level\n[ExtraNG]\n5: Bad \\xZZ\n");

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "English"));

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,
			message: "ExtraNG string 5 holds an invalid hexadecimal escape; follow each \\x with two hexadecimal digits, such as \\x41",
			file: workspace.PathOf(Path.Combine("script", "French.txt"))
		);

		Assert.IsFalse(File.Exists(workspace.PathOf("English.dat")), "A failed compile publishes nothing.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void WithoutAMainLanguage_TheFirstListedLanguageShipsAndTheWarningNamesIt()
	{
		// PD-64: NG Center selects the last listed language whose .dat already sits in the engine folder (Module1.bas:9406-9436);
		// the port ships the first listed one, whatever the folder holds.
		const string script = "[Language]\nFile= 0, Italian.txt\nFile= 1, English.txt\n" + TestScripts.Level;

		using var workspace = TestWorkspace.CreateCompilable(script);
		workspace.WithLanguageFile("Italian.txt", "[Strings]\nTest Level\n");
		workspace.WithBinaryFile("English.dat", [1, 2, 3]);

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: null));

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			result.Diagnostics,
			line: null,
			"No main language is selected, so the first listed language, Italian.txt, ships with the game"
		);

		Assert.IsTrue(File.Exists(workspace.PathOf("Italian.dat")), "The first listed language ships.");
		Assert.IsFalse(File.Exists(workspace.PathOf("English.dat")), "A language that does not ship is removed.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FirstLanguage_DrivesClassicScriptStringIndexes_WhenMainLanguageDiffers()
	{
		const string script = """
			[Language]
			File= 0,English.txt
			File= 1,French.txt

			[Options]
			LoadSave= ENABLED
			Title= DISABLED
			Security= 0

			[PSXExtensions]
			Level= .PSX
			FMV= .FMV
			Cut= .CUT

			[PCExtensions]
			Level= .TR4
			FMV= .BIK
			Cut= .TR4

			[Level]
			Name= Shared Level
			Level= data\shared,7
			LoadCamera= 0,0,0,0,0,0,0
			""";

		using var workspace = TestWorkspace.Create(script);

		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("English Only", "Shared Level"));
		workspace.WithLanguageFile("French.txt", TestWorkspace.LanguageFile("Shared Level", "Francais Seulement"));

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "French"));

		Assert.IsTrue(result.Success, result.Log);

		byte[] classicSections = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat")).GetAllSections().ToArray();
		int levelTagIndex = Array.LastIndexOf(classicSections, (byte)0x81);

		Assert.IsGreaterThanOrEqualTo(0, levelTagIndex, "Compiled classic section should contain the final level tag.");

		Assert.AreEqual(
			1,
			classicSections[levelTagIndex + 1],
			"Classic Script.dat level-name indexes must come from the first listed language, not the selected root language."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void MainLanguageMissingExtraNgIndex_Fails()
	{
		const string script = """
			[Language]
			File= 0,English.txt
			File= 1,German.txt

			[Options]
			LoadSave= ENABLED
			Title= DISABLED
			Security= 0

			[Level]
			Name= Shared Level
			Level= data\shared,0
			LoadCamera= 0,0,0,0,0,0,0
			""";

		using var workspace = TestWorkspace.Create(script);

		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFileWithExtraNg(["Shared Level"], (100, "English extra")));
		workspace.WithLanguageFile("German.txt", TestWorkspace.LanguageFile("Shared Level"));

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "German"));

		Assert.IsFalse(
			result.Success,
			"Compilation should fail when the selected main language is missing ExtraNG indexes present in the first language."
		);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: null,
			message: "Main language German.txt is missing ExtraNG string index 100",
			file: workspace.PathOf(Path.Combine("script", "German.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void MainLanguageNamedEnglishThatIsNotFirst_MissingExtraNgIndex_Fails()
	{
		// VB6 skips the check for a main language whose file is named english (Module1.bas:8903); the port skips it only for the
		// first language, so an English main language listed after French must hold French's ExtraNG indexes.
		const string script = """
			[Language]
			File= 0,French.txt
			File= 1,English.txt

			[Options]
			LoadSave= ENABLED
			Title= DISABLED
			Security= 0

			[Level]
			Name= Shared Level
			Level= data\shared,0
			LoadCamera= 0,0,0,0,0,0,0
			""";

		using var workspace = TestWorkspace.Create(script);

		workspace.WithLanguageFile("French.txt", TestWorkspace.LanguageFileWithExtraNg(["Shared Level"], (100, "French extra")));
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Shared Level"));

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "English"));

		Assert.IsFalse(result.Success, "An English main language that is not the first must hold every ExtraNG index of the first language.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: null,
			message: "Main language English.txt is missing ExtraNG string index 100",
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_BaseNameSelector_SelectsListedLanguageCaseInsensitively()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions(mainLanguage: null) with { MainLanguage = MainLanguageSelector.FromText("ENGLISH") };
		AnalysisResult analysis = workspace.Analyze(options);

		Assert.IsTrue(analysis.Success, $"Analysis must succeed: {analysis.Log}");
		Assert.IsNotNull(analysis.MainLanguageFile, "A matching base name must select the language file.");
		Assert.AreEqual("English.txt", Path.GetFileName(analysis.MainLanguageFile), "Base-name selection must match case-insensitively.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_IndexSelectorOutOfRange_FailsWithRangeMessage()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions(mainLanguage: null) with { MainLanguage = MainLanguageSelector.FromIndex(99) };
		AnalysisResult analysis = workspace.Analyze(options);

		Assert.IsFalse(analysis.Success, "An out-of-range index must fail the analysis.");

		string expected = "The main language index 99 lies outside 0 to 0, the indexes of the listed languages English.txt";

		DiagnosticAssert.SingleError(analysis.Diagnostics, line: null, expected);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Analyze_NameSelectorNotListed_FailsNamingTheListedLanguages()
	{
		using var workspace = TestWorkspace.Create();
		workspace.WithEnglishLanguageFile();

		TRNGCompilerOptions options = workspace.CreateOptions(mainLanguage: null) with { MainLanguage = MainLanguageSelector.FromText("French") };
		AnalysisResult analysis = workspace.Analyze(options);

		Assert.IsFalse(analysis.Success, "A language the script does not list must fail the analysis.");

		string expected = "The main language French is not one of the languages Script.txt lists, which are English.txt";

		DiagnosticAssert.SingleError(analysis.Diagnostics, line: null, expected);
	}
}
