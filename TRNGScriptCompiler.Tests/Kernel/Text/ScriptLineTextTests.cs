using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Tests.Kernel.Text;

[TestClass]
[TestCategory("Fast")]
public sealed class ScriptLineTextTests
{
	[TestMethod]
	public void Normalize_StripsCommentsAndCollapsesWhitespace()
	{
		Assert.AreEqual(string.Empty, ScriptLineText.Normalize("; pure comment"));
		Assert.AreEqual("a", ScriptLineText.Normalize("a // trailing"));
		Assert.AreEqual("a b", ScriptLineText.Normalize("  a   b  "));
		Assert.AreEqual("a b", ScriptLineText.Normalize("a\t\tb"));
		Assert.AreEqual("a b", ScriptLineText.Normalize("a\r\nb"));
	}

	[TestMethod]
	[DataRow("a \t\r\n  b", "a b")]
	[DataRow("a\r\r\nb", "a\r b")]
	[DataRow("a\rb\nc", "a\rb\nc")]
	[DataRow("\t a  b \t", "a b")]
	public void Normalize_TurnsEachCrLfAndTabIntoASpace_ThenCollapsesTheRun(string line, string expected)
	{
		// VB6 replaces each vbCrLf and each tab with a space, then keeps one space of each run (MioModulo.bas:5765-5779); a lone CR or LF
		// stays.
		Assert.AreEqual(expected, ScriptLineText.Normalize(line));
	}

	[TestMethod]
	public void Normalize_ALineLongerThanTheStackBuffer_CollapsesTheSameWay()
	{
		string words = string.Concat(Enumerable.Repeat("word \t ", 100));
		Assert.AreEqual(string.Join(' ', Enumerable.Repeat("word", 100)), ScriptLineText.Normalize(words));
	}

	[TestMethod]
	[DataRow("Fog=\t1 ; a // b", "Fog=\t1 ")]
	[DataRow("Fog= 1 // a ; b", "Fog= 1 // a ")]
	[DataRow("Fog= 1 // a", "Fog= 1 ")]
	[DataRow("Fog= 1", "Fog= 1")]
	[DataRow("; only a comment", "")]
	public void WithoutComment_CutsWhereNormalizeDoes_AndKeepsTheRestAsWritten(string line, string expected) =>
		Assert.AreEqual(expected, ScriptLineText.WithoutComment(line));

	[TestMethod]
	public void Normalize_SemicolonWinsOverSlashSlash_EvenWhenItAppearsLater()
	{
		// VB6 looks for ';' first and, when the line has one, does not look for '//', so a '//' before the ';' stays in the text.
		Assert.AreEqual("a // b", ScriptLineText.Normalize("a // b ; c"));
	}
}
