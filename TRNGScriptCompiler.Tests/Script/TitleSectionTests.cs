using System.Globalization;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class TitleSectionTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void TitleDisabled_CompilesTheTitleSectionAndClearsOnlyTheTitleFlag()
	{
		// PD-54: VB6 adds an NG block for an ignored [Title] (Module1.bas:5229-5236) but stores no section for it (:5239-5257),
		// so its NG writer reads one level name past the end (:8421-8427) and the compile fails. The port stores the
		// [Title] as written, so the output differs from Title= ENABLED only in the title flag, bit 2 of the options flags.
		const string scriptTemplate = """
			[Language]
			File= 0,English.txt

			[Options]
			LoadSave= ENABLED
			Title= {0}
			Security= 0

			[PSXExtensions]
			Level= .PSX
			FMV= .FMV
			Cut= .CUT

			[PCExtensions]
			Level= .TR4
			FMV= .BIK
			Cut= .TR4

			[Title]
			Name= Playable Level
			Level= data\title,0
			LoadCamera= 0,0,0,0,0,0,0

			[Level]
			Name= Playable Level
			Level= data\playable,0
			LoadCamera= 0,0,0,0,0,0,0
			""";

		using var enabled = TestWorkspace.Create(string.Format(CultureInfo.InvariantCulture, scriptTemplate, "ENABLED"));
		enabled.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Playable Level"));

		using var disabled = TestWorkspace.Create(string.Format(CultureInfo.InvariantCulture, scriptTemplate, "DISABLED"));
		disabled.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Playable Level"));

		TestCompileResult enabledResult = enabled.Compile();
		TestCompileResult disabledResult = disabled.Compile();

		Assert.IsTrue(enabledResult.Success, enabledResult.Log);
		Assert.IsTrue(disabledResult.Success, disabledResult.Log);

		var enabledDat = ScriptDatReader.ReadFile(enabled.PathOf("Script.dat"));
		var disabledDat = ScriptDatReader.ReadFile(disabled.PathOf("Script.dat"));

		Assert.AreEqual(2, disabledDat.SectionCount);
		Assert.AreEqual(2, disabledDat.LevelCount);
		Assert.AreEqual(0x04, enabledDat.Flags & 0x04);
		Assert.AreEqual(0, disabledDat.Flags & 0x04);

		// The NG security field encodes the options flags too, so the comparison leaves it out.
		Assert.AreSequenceEqual(WithoutTitleFlagAndSecurityField(enabledDat), WithoutTitleFlagAndSecurityField(disabledDat));
	}

	private static byte[] WithoutTitleFlagAndSecurityField(ScriptDatReader dat)
	{
		byte[] bytes = [.. dat.Bytes];
		bytes[0] &= unchecked((byte)~0x04);

		ScriptDatReader.NgField security = dat.GetNgField(ScriptDatReader.SecurityTag);
		Array.Clear(bytes, security.DataOffset, security.DataWordCount * 2);

		return bytes;
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("LoadCamra= 0,0,0,0,0,0,0", "Unknown command for [Title] section: LoadCamra=; correct its name or remove the line")]
	[DataRow("LoadCamera 0,0,0,0,0,0,0", "Syntax error: missing command, because the line has no '=' character")]
	public void TitleDisabled_TitleLinesAreStillChecked(string titleLine, string expectedMessage)
	{
		// PD-54: VB6 skips the lines of an ignored [Title] unparsed (Module1.bas:5269). The port compiles the [Title], so a
		// misspelled command or a line without = fails there as in any section.
		string script =
			"[Language]\nFile= 0,English.txt\n[Options]\nTitle= DISABLED\n[Title]\nLevel= data\\title,0\n"
			+ titleLine
			+ "\n[Level]\nName= Playable Level\nLevel= data\\playable,0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Playable Level"));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 7, expectedMessage);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TitleNamePastTheFirst256Strings_Compiles()
	{
		// A [Title] header writes no name index (Module1.bas:5833-5836), so its string may lie past the first 256.
		string strings = string.Concat(Enumerable.Range(0, 300).Select(index => $"String {index}\n"));

		const string script =
			TestScripts.Language + "[Title]\nName= String 299\nLevel= data\\title,0\nLoadCamera= 0,0,0,0,0,0,0\n" + TestScripts.Level;

		using var workspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\n" + strings);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DefaultWindowsFont_OutsideTitleSection_Fails()
	{
		const string script = """
			[Language]
			File= 0,English.txt

			[Options]
			Title= ENABLED
			Security= 0
			DefaultWindowsFont= 0,1,2,3,4,5,6,7,8,9,10,11,12,13

			[Title]
			Level= data\title,0
			LoadCamera= 0,0,0,0,0,0,0

			[Level]
			Name= Playable Level
			Level= data\playable,0
			LoadCamera= 0,0,0,0,0,0,0
			""";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Playable Level"));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "DefaultWindowsFont= should be rejected outside [Title].");
		DiagnosticAssert.SingleError(result.Diagnostics, 7, "DefaultWindowsFont= can only be used in [Title] section");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void TitleSectionWithoutLoadCamera_Fails()
	{
		// PD-24: VB6 checks every stored section, the [Title] included, for LoadCamera= (Module1.bas:6423-6429).
		const string script = TestScripts.Language + "[Title]\nName= Test Title\nLevel= data\\title,0\n" + TestScripts.Level;

		using var workspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\nTest Title\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 requires LoadCamera= in every stored Title/Level section.");
		DiagnosticAssert.SingleError(result.Diagnostics, 3, "LoadCamera= command is required in title section 'Test Title'");
	}
}
