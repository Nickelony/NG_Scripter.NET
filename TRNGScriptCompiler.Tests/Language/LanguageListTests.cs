using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Language;

[TestClass]
public sealed class LanguageListTests
{
	private const string TwoLanguageScript = TestScripts.Language + "File= 1, French.txt\n" + TestScripts.Level;

	[TestMethod]
	[TestCategory("IO")]
	public void ListedLanguageFileThatIsMissing_Fails()
	{
		// PD-50: VB6 warns and publishes without the language (Module1.bas:8891-8894).
		using var workspace = TestWorkspace.CreateCompilable(TwoLanguageScript);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		// The error points at the file name on the File= line that lists the file.
		CompilerDiagnostic diagnostic = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "Missing language file: French.txt",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.AreEqual(("File= 1, ".Length, "French.txt".Length), (diagnostic.Column, diagnostic.Length));
		Assert.IsFalse(File.Exists(workspace.PathOf("English.dat")), "A failed compile publishes nothing.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ListedLanguageFileThatDoesNotParse_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(TwoLanguageScript);
		workspace.WithLanguageFile("French.txt", "[Strings]\nTest Level\n[Strings]\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.AreEqual("The section header [Strings] appears twice; merge the two sections into one", diagnostic.Message);
		Assert.AreEqual((3, "French.txt"), (diagnostic.LineNumber, Path.GetFileName(diagnostic.SourceFile)));
		Assert.IsFalse(File.Exists(workspace.PathOf("English.dat")), "A failed compile publishes nothing.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void StringCountThatDiffersInTheMainLanguage_Fails()
	{
		// PD-58: VB6 warns (Module1.bas:8941-8951), while Script.dat takes its indexes from the first language.
		using var workspace = TestWorkspace.CreateCompilable(TwoLanguageScript);
		workspace.WithLanguageFile("French.txt", "[Strings]\nTest Level\nExtra\n");

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage: "French"));

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.AreEqual(
			"French.txt holds 2 [Strings] lines and English.txt holds 1, so the indexes Script.dat takes from the first language point at other strings in this one; give both files the same [Strings] lines in the same order",
			diagnostic.Message
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void StringCountThatDiffersInAnotherLanguage_Warns()
	{
		using var workspace = TestWorkspace.CreateCompilable(TwoLanguageScript);
		workspace.WithLanguageFile("French.txt", "[Strings]\nTest Level\nExtra\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			diagnostics: result.Diagnostics,
			line: null,
			message: "French.txt holds 2 [Strings] lines and English.txt holds 1, so the indexes Script.dat takes from the first language point at other strings in this one; give both files the same [Strings] lines in the same order",
			file: workspace.PathOf(Path.Combine("script", "French.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ScriptWithoutLanguageFiles_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A Script.txt without a [Language] section has no language files and must fail.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: null,
			message: "No language files were listed in Script.txt",
			file: workspace.ScriptFile
		);
	}
}
