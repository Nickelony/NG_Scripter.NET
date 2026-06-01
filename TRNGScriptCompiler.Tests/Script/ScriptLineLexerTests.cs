using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class ScriptLineLexerTests
{
	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_SplitsCommandAndCommaSeparatedArguments()
	{
		(string? command, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("Key= 1,Key Label,2");

		Assert.AreEqual("Key=", command);
		string[] expected = ["1", "Key Label", "2"];
		Assert.AreSequenceEqual(expected, arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_TrimsSpaceBeforeEquals()
	{
		(string? command, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("InputTimeOut = 30");

		Assert.AreEqual("InputTimeOut=", command);
		string[] expected = ["30"];
		Assert.AreSequenceEqual(expected, arguments);
	}

	// VB6's EstraiArgomenti (Module1.bas:4729-4761) splits at every comma and trims each piece; a double quote has no meaning.

	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_KeepsQuotesAsText()
	{
		(string _, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("Key= 1,The \"Eye\" of Horus,2");

		string[] expected = ["1", "The \"Eye\" of Horus", "2"];
		Assert.AreSequenceEqual(expected, arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_UnpairedQuote_DoesNotSwallowLaterCommas()
	{
		(string _, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("Key= 1,12\" Ruler,1,2,3,4,5,6");

		string[] expected = ["1", "12\" Ruler", "1", "2", "3", "4", "5", "6"];
		Assert.AreSequenceEqual(expected, arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_SplitsAtACommaInsideQuotes()
	{
		(string _, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("Key= 1,\"Key, Gold\",2");

		string[] expected = ["1", "\"Key", "Gold\"", "2"];
		Assert.AreSequenceEqual(expected, arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_WithoutEquals_ReturnsEmptyCommand()
	{
		(string? command, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("NoEqualsHere");

		Assert.AreEqual(string.Empty, command);
		Assert.IsEmpty(arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ParseCommandLine_TrailingComma_AddsAnEmptyLastArgument()
	{
		// VB6 splits at every comma, so a trailing comma leaves an empty last argument (Module1.bas:4754-4755).
		(string _, List<string>? arguments) = ScriptLineLexer.ParseCommandLine("A=1,2,");

		string[] expected = ["1", "2", ""];
		Assert.AreSequenceEqual(expected, arguments);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void GetRawRemainder_PreservesCommasInText()
	{
		Assert.AreEqual("My Level, Part 2", ScriptLineLexer.GetRawRemainder("Name= My Level, Part 2"));

		Assert.AreEqual(
			"spaced",
			ScriptLineLexer.GetRawRemainder("Legend=   spaced   "),
			"Only spaces around the remainder are trimmed; inner spacing is preserved."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Key= 1,The \"Eye\" of Horus,1,2,3,4,5,6", "The \"Eye\" of Horus")]
	[DataRow("Key= 1,12\" Ruler,1,2,3,4,5,6", "12\" Ruler")]
	public void QuoteInAnItemName_IsPartOfTheName(string keyLine, string itemName)
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + keyLine + "\n", $"[Strings]\nTest Level\n{itemName}\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CommaInsideQuotes_SplitsTheArgument()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel + "Key= 1,\"Key, Gold\",1,2,3,4,5,6\n",
			"[Strings]\nTest Level\nKey, Gold\n\"Key, Gold\"\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "The quoted comma splits the name, so Key= has nine arguments.");
		DiagnosticAssert.SingleError(result.Diagnostics, 7, "Wrong number of arguments for 'Key=': expected 8, got 9");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void SpaceBeforeTheEquals_IsTheSameCommand()
	{
		// PD-36: VB6 keeps the space in the command name, so Fog = is an unknown command (Module1.bas:4749-4750).
		using var spacedWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Fog = 1,2,3\n");
		using var plainWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Fog= 1,2,3\n");

		TestCompileResult spacedResult = spacedWorkspace.Compile();
		TestCompileResult plainResult = plainWorkspace.Compile();

		Assert.IsTrue(spacedResult.Success, spacedResult.Log);
		Assert.IsTrue(plainResult.Success, plainResult.Log);

		Assert.AreSequenceEqual(plainWorkspace.ReadScriptDat(), spacedWorkspace.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= 1,2,3")]
	[DataRow("Fog= 1,\v2,3")]
	[DataRow("Fog= 1,2,3\f")]
	[DataRow("Fog = 1  ,2,3")]
	public void WhitespaceOtherThanSpacesAtTheEdgesOfAnArgument_IsTrimmed(string fogLine)
	{
		// PD-63: VB6's Trim$ strips spaces only (MioModulo.bas:5782, Module1.bas:4749-4758), so a no-break space, VT or FF next to '=' or a
		// comma stays in the argument and VB6 fails on a number that is not one; the port trims them.
		using var edgeWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + fogLine + "\n");
		using var plainWorkspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Fog= 1,2,3\n");

		TestCompileResult edgeResult = edgeWorkspace.Compile();
		TestCompileResult plainResult = plainWorkspace.Compile();

		Assert.IsTrue(edgeResult.Success, edgeResult.Log);
		Assert.IsTrue(plainResult.Success, plainResult.Log);

		Assert.AreSequenceEqual(plainWorkspace.ReadScriptDat(), edgeWorkspace.ReadScriptDat());
	}
}
