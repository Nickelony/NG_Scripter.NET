using System.Text;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Infrastructure;

[TestClass]
public sealed class TextFileUtilitiesTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void Utf8FileWithoutAByteOrderMark_IsReadAsWindows1252WithAWarning()
	{
		// VB6 reads every byte through the ANSI code page (Module1.bas:5150, 1992), so the two bytes of a UTF-8 'é' read as 'Ã©'.
		using var workspace = TestWorkspace.CreateEmpty();
		string path = workspace.PathOf("English.txt");

		File.WriteAllBytes(path, Encoding.UTF8.GetBytes("Café"));

		string text = TextFileUtilities.ReadAllText(path, out string? encodingNote);

		Assert.AreEqual("CafÃ©", text);

		Assert.AreEqual(
			"'English.txt' looks like UTF-8 without a byte-order mark, so it is read as Windows-1252 and each character outside ASCII becomes several wrong ones; save the file as ANSI (Windows-1252), or as UTF-8 with a byte-order mark",
			encodingNote
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(new byte[] { 0x43, 0x61, 0x66, 0xE9 })]
	[DataRow(new byte[] { 0x43, 0x61, 0x66, 0x65 })]
	public void AnsiOrAsciiFile_IsReadWithoutANote(byte[] bytes)
	{
		using var workspace = TestWorkspace.CreateEmpty();
		string path = workspace.PathOf("English.txt");

		File.WriteAllBytes(path, bytes);

		TextFileUtilities.ReadAllText(path, out string? encodingNote);

		Assert.IsNull(encodingNote);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("utf-8", "UTF-8", "UTF-8")]
	[DataRow("utf-16", "UTF-16 (little-endian)", "UTF-16")]
	[DataRow("utf-16BE", "UTF-16 (big-endian)", "UTF-16")]
	[DataRow("utf-32", "UTF-32 (little-endian)", "UTF-32")]
	[DataRow("utf-32BE", "UTF-32 (big-endian)", "UTF-32")]
	public void FileWithAByteOrderMark_IsDecodedByItsMark(string encodingName, string markName, string codecName)
	{
		// VB6 reads the mark as text (Module1.bas:5150, 1992); the port decodes the file by it (PD-27). A UTF-32 little-endian mark starts
		// with the UTF-16 one, so it is tested first.
		var encoding = Encoding.GetEncoding(encodingName);
		using var workspace = TestWorkspace.CreateEmpty();

		string path = workspace.PathOf("English.txt");

		File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes("Café")]);

		string text = TextFileUtilities.ReadAllText(path, out string? encodingNote);

		Assert.AreEqual("Café", text);
		Assert.AreEqual($"'English.txt' has a {markName} byte-order mark; the file will be decoded as {codecName}", encodingNote);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TextFileUtilities_SplitsLines()
	{
		string[] expectedLines = ["a", "b"];
		string[] expectedCrLines = ["a", "b", "c"];

		Assert.AreSequenceEqual(expectedLines, TextFileUtilities.SplitLines("a\nb\n"));
		Assert.AreSequenceEqual(expectedCrLines, TextFileUtilities.SplitLines("a\r\nb\rc"));

		Assert.IsEmpty(TextFileUtilities.SplitLines(string.Empty));
	}

	// VB6 reads script.txt, its includes and the language files with Line Input (Module1.bas:5150, 1992), which stops at a Ctrl-Z.

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("\u001A")]
	[DataRow("\u001A\r\n  \r\n")]
	public void CtrlZAtTheEndOfTheScriptAndTheLanguageFile_EndsTheFile(string ending)
	{
		const string script = TestScripts.Language + TestScripts.Level;

		using var markedWorkspace = TestWorkspace.CreateCompilable(script + ending, TestWorkspace.CompilableLanguageContent + ending);
		using var plainWorkspace = TestWorkspace.CreateCompilable(script);

		TestCompileResult markedResult = markedWorkspace.Compile();
		TestCompileResult plainResult = plainWorkspace.Compile();

		Assert.IsTrue(markedResult.Success, markedResult.Log);
		Assert.IsTrue(plainResult.Success, plainResult.Log);

		Assert.AreSequenceEqual(plainWorkspace.ReadScriptDat(), markedWorkspace.ReadScriptDat());
		Assert.AreSequenceEqual(plainWorkspace.ReadOutput("English.dat"), markedWorkspace.ReadOutput("English.dat"));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TextAfterACtrlZInTheScript_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "\u001A\nFog= 1,2,3\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 drops the Fog= line after the Ctrl-Z.");

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			7,
			"Text after a Ctrl-Z byte would be dropped, because the Ctrl-Z ends the file; remove the Ctrl-Z byte"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TextAfterACtrlZInTheLanguageFile_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + TestScripts.Level,
			TestWorkspace.CompilableLanguageContent + "\u001AMore\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "Text after a Ctrl-Z byte would be dropped, because the Ctrl-Z ends the file; remove the Ctrl-Z byte",
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);
	}
}
