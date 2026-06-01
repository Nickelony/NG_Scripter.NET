using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Diagnostics;

/// <summary>
/// A diagnostic names the full path of the file that holds its line, so an error inside an <c>#INCLUDE</c> file
/// can be found.
/// </summary>
[TestClass]
public sealed class DiagnosticLocationTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow("UVRotate= 999")]
	[DataRow("Snow= 1,2")]
	[DataRow("#DEFINE ONLY_TWO")]
	public void ErrorInAnIncludedFile_NamesTheIncludeAndItsLine(string line)
	{
		// VB6 records the file it is reading with every error (FileNow = Glob.NomeFileNow, Module1.bas:9455).
		// The rows reach the error at three stages: a classic command fails when Script.dat is written, an NG command when the
		// NG lines compile, and a directive when the file is loaded.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "#INCLUDE \"sub\\Inc.txt\"\n");
		workspace.WithTextFile(Path.Combine("script", "sub", "Inc.txt"), "; The second line holds the error\n" + line + "\n");

		TestCompileResult result = workspace.Compile();

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		Assert.AreEqual((2, workspace.PathOf(Path.Combine("script", "sub", "Inc.txt"))), (diagnostic.LineNumber, diagnostic.SourceFile), result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ErrorsInAnIncludedFile_EachNameTheInclude()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "#INCLUDE \"extra.txt\"\n");
		workspace.WithTextFile(Path.Combine("script", "extra.txt"), "#DEFINE FIRST\n#DEFINE SECOND\n");

		TestCompileResult result = workspace.Compile();

		string message = "Wrong number of arguments for '#DEFINE': expected 2, got 1";

		DiagnosticAssert.Errors(result.Diagnostics, workspace.PathOf(Path.Combine("script", "extra.txt")), (1, message), (2, message));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ErrorInAnIncludedFile_LogLineNamesTheFileAndItsLine()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "#INCLUDE \"extra.txt\"\n");
		workspace.WithTextFile(Path.Combine("script", "extra.txt"), "; one\n; two\nUVRotate= 999\n");

		TestCompileResult result = workspace.Compile();

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		LogEntry entry = result.Entries.Single(logEntry => logEntry.Severity is LogSeverity.Error);

		Assert.AreEqual($"ERROR: Line 3 of extra.txt: {diagnostic.Message}", entry.FormattedMessage);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ErrorInTheScript_NamesTheFullPathOfScriptTxt()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "UVRotate= 999\n");

		TestCompileResult result = workspace.Compile();

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);
		Assert.AreEqual((7, workspace.PathOf(Path.Combine("script", "Script.txt"))), (diagnostic.LineNumber, diagnostic.SourceFile), result.Log);
	}
}
