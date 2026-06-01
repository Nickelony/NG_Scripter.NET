using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

/// <summary>
/// PD-23: VB6 deletes every <c>.dat</c> but <c>script.dat</c> in the engine folder before it reads
/// anything (Module1.bas:8829-8836).
/// </summary>
/// <remarks>
/// The port deletes, after publication, only the language files the old or the new <c>Script.dat</c> names,
/// and never a file the run read.
/// </remarks>
[TestClass]
public sealed class StaleLanguageCleanupTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void LanguageTheOldScriptDatNames_IsRemoved()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "File= 1, German.txt\n" + TestScripts.Level);
		workspace.WithLanguageFile("German.txt", "[Strings]\nTest Level\n");

		Assert.IsTrue(workspace.Compile(workspace.CreateOptions(mainLanguage: "German")).Success);
		Assert.IsTrue(File.Exists(workspace.PathOf("German.dat")), "The first compile ships German.dat.");

		workspace.WithScript(TestScripts.Language + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsFalse(File.Exists(workspace.PathOf("German.dat")), "The old Script.dat names German.DAT, so it is stale.");
		Assert.IsTrue(File.Exists(workspace.PathOf("English.dat")));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageAnEncryptedOldScriptDatNames_IsRemoved()
	{
		// SET_CRYPT_SCRIPT scrambles the first 64 bytes of Script.dat, which hold the counts that locate its language names.
		const string options = "[Options]\nSettings= SET_CRYPT_SCRIPT\n";

		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "File= 1, German.txt\n" + options + TestScripts.Level);
		workspace.WithLanguageFile("German.txt", "[Strings]\nTest Level\n");

		TestCompileResult first = workspace.Compile(workspace.CreateOptions(mainLanguage: "German"));

		Assert.IsTrue(first.Success, first.Log);
		Assert.IsTrue(File.Exists(workspace.PathOf("German.dat")), "The first compile ships German.dat.");

		workspace.WithScript(TestScripts.Language + options + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsFalse(File.Exists(workspace.PathOf("German.dat")), "The encrypted Script.dat names German.DAT, so it is stale.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileInTheEngineFolder_IsKept()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nImportFile= 1,custom7.dat,FTYPE_USERFILE,IMPORT_TEMPORARY\n" + TestScripts.Level
		);

		workspace.WithBinaryFile("custom7.dat", [1, 2, 3, 4]);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.AreSequenceEqual(new byte[] { 1, 2, 3, 4 }, workspace.ReadOutput("custom7.dat"), "An import the run read is never deleted.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageFileTheRunReads_IsKeptAlthoughTheOldScriptDatNamesIt()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "File= 1, Custom7.txt\n" + TestScripts.Level);
		workspace.WithLanguageFile("Custom7.txt", "[Strings]\nTest Level\n");

		Assert.IsTrue(workspace.Compile(workspace.CreateOptions(mainLanguage: "Custom7")).Success);
		byte[] custom7 = workspace.ReadOutput("Custom7.dat");

		workspace.WithScript(TestScripts.Language + "[Options]\nImportFile= 1,Custom7.dat,FTYPE_USERFILE,IMPORT_TEMPORARY\n" + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.AreSequenceEqual(custom7, workspace.ReadOutput("Custom7.dat"), "The run imports Custom7.dat, so it stays.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OldScriptDatThatDoesNotParse_WarnsAndTheLanguagesOfTheScriptAreStillRemoved()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "File= 1, German.txt\n" + TestScripts.Level);
		workspace.WithLanguageFile("German.txt", "[Strings]\nTest Level\n");

		workspace.WithBinaryFile("Script.dat", [1, 2, 3]);
		workspace.WithBinaryFile("German.dat", [1, 2, 3]);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			result.Diagnostics,
			line: null,
			"The existing Script.dat lists no language files the compiler can read, so only the languages of this script are removed as stale"
		);

		Assert.IsFalse(File.Exists(workspace.PathOf("German.dat")), "The new Script.dat names German.DAT, so it is stale.");
	}
}
