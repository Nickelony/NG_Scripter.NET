using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Pipeline;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class StaleLanguageFilesTests
{
	private const string Warning =
		"The existing Script.dat lists no language files the compiler can read, so only the languages of this script are removed as stale";

	[TestMethod]
	[TestCategory("IO")]
	public void NoOldScriptDat_NamesTheLanguagesOfTheScriptExceptThePublishedOnes()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		HashSet<string> stale = new StaleLanguageFiles(diagnostics).Collect(
			workspace.PathOf("Script.dat"),
			["English.txt", "German.txt"],
			publishedFileNames: ["english.DAT", "Script.dat"]
		);

		Assert.AreSequenceEqual(["German.DAT"], stale);
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OldScriptDat_AddsTheLanguagesItNames()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "File= 1, German.txt\n" + TestScripts.Level);
		workspace.WithLanguageFile("German.txt", "[Strings]\nTest Level\n");

		Assert.IsTrue(workspace.Compile().Success);

		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		HashSet<string> stale = new StaleLanguageFiles(diagnostics).Collect(
			workspace.PathOf("Script.dat"),
			["French.txt"],
			publishedFileNames: ["English.dat"]
		);

		Assert.AreSequenceEqual(new[] { "German.DAT", "French.DAT" }.Order(), stale.Order());
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OldScriptDatThatListsNoLanguage_WarnsAndNamesTheLanguagesOfTheScript()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		workspace.WithBinaryFile("Script.dat", [1, 2, 3]);

		HashSet<string> stale = new StaleLanguageFiles(diagnostics).Collect(workspace.PathOf("Script.dat"), ["German.txt"], publishedFileNames: []);

		Assert.AreSequenceEqual(["German.DAT"], stale);
		DiagnosticAssert.HasWarning(diagnostics.Recorded, line: null, Warning);
	}
}
