using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Parity;

/// <summary>Input the VB6 compiler rejects, which the port rejects too.</summary>
[TestClass]
public sealed class Vb6RejectedInputTests
{
	private static readonly byte[] s_importBytes = [0xA1, 0xA2, 0xA3, 0xA4];

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= 1,2,3,", "Wrong number of arguments for 'Fog=': expected 3, got 4")]
	[DataRow("KeyPad= 1,2,3,4,", "Wrong number of arguments for 'KeyPad=': expected 4, got 5")]
	[DataRow("Fog= 1,2,,3", "Wrong number of arguments for 'Fog=': expected 3, got 4")]
	public void TrailingOrInnerEmptyArgument_Fails(string command, string expectedMessage)
	{
		// VB6 splits at every comma and keeps the empty piece (Module1.bas:4754-4755), which fails the argument count
		// (:7071-7106) or the empty-value check (:4816-4823).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + command + "\n");

		TestCompileResult result = workspace.Compile();

		DiagnosticAssert.SingleError(result.Diagnostics, 7, expectedMessage);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("#INCLUDEX \"a.txt\"", "#INCLUDEX", "#INCLUDE")]
	[DataRow("#DEFINEX FOO 1", "#DEFINEX", "#DEFINE")]
	[DataRow("#FIRST_IDX 5", "#FIRST_IDX", "#FIRST_ID")]
	public void DirectiveWordWithExtraLetters_Fails(string line, string word, string directive)
	{
		// VB6 compares the whole first word with the directive (Module1.bas:4966-4974, 5001, 13664), so a longer word is a command
		// without '=', which fails inside a section (:5293-5299); the port fails above the first section too (PD-57).
		string[] scripts = [line + "\n" + TestScripts.LanguageAndLevel, TestScripts.LanguageAndLevel + line + "\n"];

		foreach (string script in scripts)
		{
			using var workspace = TestWorkspace.CreateCompilable(script);

			TestCompileResult result = workspace.Compile();

			Assert.IsFalse(result.Success, result.Log);

			Assert.AreEqual(
				$"{word} is not a directive; write {directive} followed by a space",
				result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	public void UnquotedIncludeNameWithASpace_Fails()
	{
		// VB6 takes the second space-separated piece and requires quotes around it (Module1.bas:5012-5022).
		using var workspace = TestWorkspace.CreateCompilable("#INCLUDE my levels.txt\n" + TestScripts.Language + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		string expected = "The #INCLUDE file name 'my levels.txt' holds a space; write the name in double quotes";

		DiagnosticAssert.SingleError(diagnostics: result.Diagnostics, line: 1, message: expected, file: workspace.ScriptFile);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TitleNameMissingFromTheLanguageFile_Fails()
	{
		// VB6 looks up the Name= of every stored section, the [Title] too, and fails on a missing string (Module1.bas:5995-6009).
		const string title = "[Title]\nName= Missing Title\nLevel= data\\title,0\nLoadCamera= 0,0,0,0,0,0,0\n";
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + title + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 4, "Cannot find level name in language file: Missing Title");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("")]
	[DataRow(TestScripts.Language + "[Options]\nTitle= DISABLED\n")]
	public void ScriptWithoutATitleOrLevelSection_Fails(string script)
	{
		// VB6 sizes the level-name offsets with ReDim VetOffset(TotLivelli - 1), which fails with no stored section (Module1.bas:6728).
		using var workspace = TestWorkspace.CreateCompilable(script);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.AreEqual(
			"The script has no [Title] or [Level] section, so the game has no level to start; add a [Level] section",
			result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void StarAsAnNgNumber_Fails()
	{
		// VB6 looks * up as a constant and fails, because it is none (Module1.bas:4887-4896). It fails _ too, which the port reads as -1 (PD-97).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "KeyPad= *,0,0,0\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.AreEqual(
			"Argument 1 of KeyPad= is invalid: * is neither a number nor a known name",
			result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("ImportFile= 1,data.bin,FTYPE_USERFILE", 3)]
	[DataRow("ImportFile= 1,data.bin,FTYPE_USERFILE,IMPORT_MEMORY,", 5)]
	public void ImportFileWithoutFourArguments_Fails(string command, int argumentCount)
	{
		// VB6 reads ImportFile= as an NG command of four arguments (Module1.bas:7071-7106).
		TestCompileResult result = CompileWithImport(command);

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			4,
			$"Wrong number of arguments for 'ImportFile=': expected 4 (ID, Path, Type, Mode), got {argumentCount}"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileIdRepeatedInAnotherOptionsSection_Fails()
	{
		// VB6 checks the IDs of every [Options] section as one list (Module1.bas:8306, 1212-1300).
		TestCompileResult result = CompileWithImport(
			"ImportFile= 1,data.bin,FTYPE_USERFILE,IMPORT_MEMORY\n"
				+ TestScripts.Level
				+ "[Options]\nImportFile= 1,data.bin,FTYPE_USERFILE,IMPORT_TEMPORARY"
		);

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 10, "ImportFile= ID 1 is already declared for data.bin; give each ImportFile= its own ID");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("ImportFile= 65536,data.bin,FTYPE_USERFILE,0", "The ImportFile= ID 65536 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("ImportFile= 1,data.bin,-32769,0", "The ImportFile= file type -32769 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("ImportFile= 1,data.bin,FTYPE_USERFILE,3", "The ImportFile= mode 3 lies outside 0 to 2; write 0, IMPORT_MEMORY or IMPORT_TEMPORARY")]
	public void ImportFileValueOutsideItsField_Fails(string command, string expectedMessage)
	{
		// VB6 width-checks each word (Module1.bas:4915-4937) and names the mode through a three-entry array (:8282, 8600-8602).
		TestCompileResult result = CompileWithImport(command);

		Assert.IsFalse(result.Success, result.Log);
		Assert.AreEqual(expectedMessage, result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("ImportUser= 1", true)]
	[DataRow("ImportUser= IGNORE", true)]
	[DataRow("ImportUser= 2", false)]
	public void ImportArgument_NamesADeclaredId(string command, bool compiles)
	{
		// VB6 fails an IMPORT argument whose ID no ImportFile= declares, unless it is -1 (Module1.bas:7464-7484). No stock command
		// takes the type, so the test declares one.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nImportFile= 1,data.bin,FTYPE_USERFILE,0\n" + TestScripts.Level + command + "\n"
		);

		workspace.WithBinaryFile("data.bin", s_importBytes);

		string constantsPath = workspace.PathOf("custom_constants.txt");
		string stockConstants = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, CompilerFileNames.ScripterConstants));

		File.WriteAllText(constantsPath, stockConstants + "\r\n<START_NEW_COMMANDS>\r\n240#99#ImportUser=:Import\r\n<END>\r\n");

		TestCompileResult result = workspace.Compile(workspace.CreateOptions() with { ConstantsFilePath = constantsPath });

		Assert.AreEqual(compiles, result.Success, result.Log);

		if (!compiles)
		{
			Assert.AreEqual(
				"Argument 1 of ImportUser= names import ID 2, which no ImportFile= declares; declare it in [Options] or write a declared ID",
				result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
			);
		}
	}

	private static TestCompileResult CompileWithImport(string options)
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Options]\n" + options + "\n" + TestScripts.Level);
		workspace.WithBinaryFile("data.bin", s_importBytes);

		return workspace.Compile();
	}
}
