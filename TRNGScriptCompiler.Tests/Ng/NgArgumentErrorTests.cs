using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// An NG argument that does not parse fails the compile,
/// as it does in VB6 (<c>ErroreSintassi</c>, <c>Module1.bas:7668-7670</c>).
/// </summary>
/// <remarks>
/// The command is never dropped from a run that reports success.
/// </remarks>
[TestClass]
public sealed class NgArgumentErrorTests
{
	// The command is the seventh line of the script.
	private const int CommandLine = 7;

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(
		"Customize= CUST_TYPO_DOES_NOT_EXIST,1",
		"Argument 1 of Customize= is invalid: CUST_TYPO_DOES_NOT_EXIST is neither a number nor a known name",
		DisplayName = nameof(ArgumentThatDoesNotParse_Fails) + " (Unknown constant)"
	)]
	[DataRow(
		"CutScene= MAYBE",
		"Argument 1 of CutScene= is invalid: MAYBE is neither ENABLED nor DISABLED",
		DisplayName = nameof(ArgumentThatDoesNotParse_Fails) + " (Not ENABLED or DISABLED)"
	)]
	[DataRow(
		"Snow= 1,2",
		"Wrong number of arguments for 'Snow=': expected 1, got 2",
		DisplayName = nameof(ArgumentThatDoesNotParse_Fails) + " (Wrong argument count)"
	)]
	public void ArgumentThatDoesNotParse_Fails(string command, string expectedMessage)
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + command + "\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(expectedMessage, diagnostic.Message);
		Assert.AreEqual(CommandLine, diagnostic.LineNumber);
		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "A failed compile must not publish Script.dat.");
	}
}
