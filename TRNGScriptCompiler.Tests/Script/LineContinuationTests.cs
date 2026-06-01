using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

/// <summary>The <c>&gt;</c> line continuation.</summary>
/// <remarks>
/// VB6 cuts each piece at the first <c>&gt;</c> before a <c>;</c>, trims it and joins it to the next
/// line with nothing between (<c>Module1.bas:5149-5167</c>), and reports the joined line at its first physical line
/// (<c>:5147-5148</c>).
/// </remarks>
[TestClass]
public sealed class LineContinuationTests
{
	private const string Head = "[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

	private const string SplitsAValueOrAWord =
		"A '>' continuation joins the next line with nothing between, so here it splits a value or a word; break the line next to a comma or right after the '='";

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Fog= 1,2,>\n3\n", "Fog= 1,2,3\n")]
	[DataRow("Fog= 1,2>\n,3\n", "Fog= 1,2,3\n")]
	[DataRow("Fog= 1,>\n2,>\n3\n", "Fog= 1,2,3\n")]
	[DataRow("Fog=>\n1,2,3\n", "Fog= 1,2,3\n")]
	[DataRow("Fog= 1,2,> ; the blue\n3\n", "Fog= 1,2,3\n")]
	[DataRow("Legend=>\nTest Level\n", "Legend= Test Level\n")]
	public void ContinuationNextToACommaOrTheEquals_WritesTheBytesOfOneLine(string continued, string single)
	{
		using var continuedWorkspace = TestWorkspace.CreateCompilable(Head + continued);
		using var singleWorkspace = TestWorkspace.CreateCompilable(Head + single);

		TestCompileResult continuedResult = continuedWorkspace.Compile();
		TestCompileResult singleResult = singleWorkspace.Compile();

		Assert.IsTrue(continuedResult.Success, continuedResult.Log);
		Assert.IsTrue(singleResult.Success, singleResult.Log);

		Assert.AreSequenceEqual(singleWorkspace.ReadScriptDat(), continuedWorkspace.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("LoadCamera= 0,0,0,0,0,1>\n2,0\n")]
	[DataRow("Name= Test>\nLevel\n")]
	[DataRow("Fog= 1,2>\n\n")]
	public void ContinuationThatSplitsAValueOrAWord_Fails(string lines)
	{
		// VB6 reads the first case's sixth value as 12, and looks the second case up as TestLevel.
		AssertErrorAtLine(lines, SplitsAValueOrAWord, expectedLine: 7);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TextAfterTheContinuation_Fails()
	{
		// VB6 keeps only the text before the '>' (Module1.bas:5160), so " to go on" is dropped.
		AssertErrorAtLine(
			"Legend= Press,> to go on\nTest Level\n",
			"The text 'to go on' after a '>' continuation would be dropped; move it before the '>' or onto the next line",
			expectedLine: 7
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ContinuationInsideASlashComment_Fails()
	{
		// Only a ';' comment stops a continuation (Module1.bas:5158), so VB6 joins the next line into the comment.
		AssertErrorAtLine(
			"Fog= 1,2,3 // fog -> red\nLightning= ENABLED\n",
			"A '>' inside a '//' comment continues the line and swallows the next one; start the comment with ';' or remove the '>'",
			expectedLine: 7
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void JoinedLine_IsReportedAtItsFirstPhysicalLine()
	{
		using var workspace = TestWorkspace.CreateCompilable(Head + "TriggerGroup= 1,$2000,>\n406,>\nNOT_A_NAME\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success);

		// VB6 reports the joined line at its first physical line, not at line 9.
		DiagnosticAssert.SingleError(
			result.Diagnostics,
			7,
			"Argument 4 of TriggerGroup= is invalid: NOT_A_NAME is neither a number nor a known name"
		);
	}

	private static void AssertErrorAtLine(string lines, string expectedMessage, int expectedLine)
	{
		using var workspace = TestWorkspace.CreateCompilable(Head + lines);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, expectedLine, expectedMessage);
	}
}
