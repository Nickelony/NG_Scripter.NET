using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// An NG <c>STRING</c> argument resolves against the first language file, and one that does not resolve
/// fails the compile, as it does in VB6 (<c>Module1.bas:7525-7537</c>), instead of compiling to index 0.
/// </summary>
[TestClass]
public sealed class NgStringReferenceTests
{
	private const string Language = "[Strings]\nTest Level\nDamage Text\n\n[ExtraNG]\n100: PluginDemo\n";

	[TestMethod]
	[TestCategory("IO")]
	public void StringMissingFromTheLanguageFile_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Damage= 1,2,3,4,Missing Text,5\n", Language);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual("Cannot find the string 'Missing Text' in the first language file", diagnostic.Message);
		Assert.AreEqual(7, diagnostic.LineNumber);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("[Strings]\nTest Level\n_\n", true)]
	[DataRow("[Strings]\nTest Level\n", false)]
	public void UnderscoreInAStringArgument_IsLookedUpAsText(string language, bool compiles)
	{
		// In a STRING argument only * and IGNORE mean -1; VB6 looks _ up like any text (Module1.bas:7521-7529).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.LanguageAndLevel + "WindowsFont= 5,_,0,0,0,0\n", language);

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(compiles, result.Success, result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void PluginNameThatIsNotAnExtraNgString_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nPlugin= 2,Damage Text,IGNORE\n" + TestScripts.Level,
			Language
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		Assert.Contains(
			diagnostic =>
				diagnostic.IsError && diagnostic.LineNumber == 4 && diagnostic.Message == "The plugin name 'Damage Text' must be an ExtraNG string",

			result.Diagnostics,
			result.Log
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ScriptWithoutOptions_ResolvesLevelStringsAgainstTheFirstLanguage()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Damage= 1,2,3,4,Damage Text,5\n", Language);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		// Damage= 1,2,3,4,Damage Text,5 compiles to the words 1, 2, 3, the long 4 as two words, string index 1, and 5.
		var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));

		ScriptDatReader.NgField level = reader.GetNgField((ushort)NgTagType.ScriptLevel);

		Assert.AreSequenceEqual(
			new short[] { 1, 2, 3, 4, 0, 1, 5 },
			reader.GetMiniTags(level).Single(tag => tag.Code == 8).Words,
			"The Damage= string argument must compile to its index in the first language."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageSectionBelowTheLevels_ResolvesTheirStrings()
	{
		// VB6 reads the first language once the whole script is read (Module1.bas:5643-5658) and compiles the NG commands after it (:8283),
		// so a [Language] section below the levels serves their string arguments.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Level + "Damage= 1,2,3,4,Damage Text,5\n" + TestScripts.Language, Language);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		var reader = ScriptDatReader.ReadFile(workspace.PathOf("Script.dat"));

		ScriptDatReader.NgField level = reader.GetNgField((ushort)NgTagType.ScriptLevel);

		Assert.AreSequenceEqual(new short[] { 1, 2, 3, 4, 0, 1, 5 }, reader.GetMiniTags(level).Single(tag => tag.Code == 8).Words);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Edited Text", true)]
	[DataRow("Damage Text", false)]
	public void UnsavedLanguageEdit_ResolvesStringsAgainstTheUnsavedText(string text, bool resolves)
	{
		// The first language is read through the host's overlay, the same text its .dat is compiled from, so a string resolves exactly when
		// the unsaved text holds it.
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + $"Damage= 1,2,3,4,{text},5\n", Language);

		var overlay = new VirtualFileSystem(
			new Dictionary<string, string> { [Path.Combine(workspace.ScriptDirectory, "English.txt")] = "[Strings]\nTest Level\nEdited Text\n" }
		);

		AnalysisResult result = workspace.Analyze(overlay: overlay);

		Assert.AreEqual(resolves, result.Success, result.Log);

		if (!resolves)
		{
			Assert.AreEqual(
				$"Cannot find the string '{text}' in the first language file",
				result.Diagnostics.Single(diagnostic => diagnostic.IsError).Message
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	public void QuotedNgStringArgument_LooksUpTheQuotes()
	{
		const string script = TestScripts.LanguageAndLevel + "WindowTitle= \"My Game\"\n";

		using var workspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\nMy Game\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "The language file holds My Game, not \"My Game\".");
		DiagnosticAssert.SingleError(result.Diagnostics, 7, "Cannot find the string '\"My Game\"' in the first language file");
	}
}
