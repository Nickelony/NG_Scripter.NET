using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// The <c>_</c> discard of a numeric NG argument, which compiles as <c>IGNORE</c> does, and the hint that <c>IGNORE</c>
/// earns (PD-97).
/// </summary>
[TestClass]
[TestCategory("IO")]
public sealed class DiscardArgumentTests
{
	private const string HintMessage = "Replace IGNORE with the _ discard";

	// The command follows the eight lines of the extension sections, the two of [Language] and the four of [Level].
	private const int CommandLine = 15;

	[TestMethod]
	[DataRow("KeyPad= IGNORE,0,0,0", "KeyPad= _,0,0,0")]
	[DataRow("KeyPad= 1,ignore,IGNORE,2", "KeyPad= 1,_,_,2")]
	[DataRow("ItemGroup= 1,2,3,IGNORE", "ItemGroup= 1,2,3,_")]
	public void Underscore_CompilesToTheBytesOfIgnore(string withIgnore, string withUnderscore)
	{
		// VB6 fails _ as an unknown constant (Module1.bas:4887-4896); the port reads it as the -1 that IGNORE names (PD-97).
		byte[] expected = TestWorkspace.CompileScriptDatOnStockObjectsH(TestScripts.LanguageAndLevel + withIgnore + "\n");
		byte[] actual = TestWorkspace.CompileScriptDatOnStockObjectsH(TestScripts.LanguageAndLevel + withUnderscore + "\n");

		Assert.AreSequenceEqual(expected, actual);
	}

	[TestMethod]
	public void Ignore_HintsInTheDiagnostics_AndSucceeds()
	{
		using var workspace = TestWorkspace.CreateClean("KeyPad= IGNORE,0,0,0\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasHint(diagnostics: result.Diagnostics, line: CommandLine, message: HintMessage, file: workspace.ScriptFile);
		Assert.HasCount(1, result.Diagnostics);
	}

	[TestMethod]
	public void Underscore_GivesNoDiagnostic()
	{
		using var workspace = TestWorkspace.CreateClean("KeyPad= _,0,0,0\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}

	[TestMethod]
	public void Hint_HasNoLogLine_AndIsNotInTheFailureSummary()
	{
		// A hint writes no log line, and the summary counts the errors and the warnings alone.
		using var workspace = TestWorkspace.CreateClean("KeyPad= IGNORE,0,0,0\nFog= 1,2\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.HasHint(diagnostics: result.Diagnostics, line: CommandLine, message: HintMessage, file: workspace.ScriptFile);

		Assert.IsFalse(result.Entries.Any(entry => entry.Message == HintMessage), "A hint writes no log line.");
		Assert.IsTrue(result.Entries.Any(entry => entry.Message == "1 error(s) and 0 warning(s) found"));
	}
}
