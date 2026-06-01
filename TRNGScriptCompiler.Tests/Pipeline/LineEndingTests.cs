using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

/// <summary>The line ends of the script and the language files.</summary>
[TestClass]
public sealed class LineEndingTests
{
	private const string Script =
		"[Language]\r\nFile= 0, English.txt\r\n[Level]\r\nName= Test Level\r\nLevel= data\\test,0\r\nLoadCamera= 0,0,0,0,0,0,0\r\n";

	[TestMethod]
	[TestCategory("IO")]
	public void BareLineFeedInTheScript_EndsTheLine()
	{
		// PD-43: VB6's Line Input ends a line only at a carriage return (Module1.bas:5150), so the comment swallows Fog=.
		using var workspace = TestWorkspace.CreateCompilable(Script + "; comment\nFog= 1,2,3\r\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		Assert.AreSequenceEqual(
			new byte[] { 0x8F, 0x01, 0x02, 0x03 },
			ScriptDatReader.Read(workspace.ReadScriptDat()).GetSectionBytes(0).Take(4),
			"Fog= writes tag 0x8F and its three colors."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageFileWithBareLineFeeds_CompilesLikeCrlf()
	{
		// PD-44: VB6 reads a file without a carriage return as one line (Module1.bas:1992), so it holds almost no strings.
		using var lineFeedWorkspace = TestWorkspace.CreateCompilable(
			Script,
			"[Strings]\nTest Level\nSecond\n[ExtraNG]\n5: Extra\nTHE LAST STRING ENTRY\n"
		);

		using var crlfWorkspace = TestWorkspace.CreateCompilable(
			Script,
			"[Strings]\r\nTest Level\r\nSecond\r\n[ExtraNG]\r\n5: Extra\r\nTHE LAST STRING ENTRY\r\n"
		);

		TestCompileResult lineFeedResult = lineFeedWorkspace.Compile();
		TestCompileResult crlfResult = crlfWorkspace.Compile();

		Assert.IsTrue(lineFeedResult.Success, lineFeedResult.Log);
		Assert.IsTrue(crlfResult.Success, crlfResult.Log);

		Assert.AreSequenceEqual(crlfWorkspace.ReadOutput("English.dat"), lineFeedWorkspace.ReadOutput("English.dat"));
	}
}
