using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class RepeatedSettingTests
{
	[TestMethod]
	[TestCategory("IO")]
	[DataRow(
		"LoadSave= ENABLED\nLoadSave= DISABLED\n",
		5,
		"'LoadSave=' gives a value different from line 4, and the setting holds one value; keep one of the two lines"
	)]
	[DataRow(
		"Title= DISABLED\nTitle= ENABLED\n",
		5,
		"'Title=' gives a value different from line 4, and the setting holds one value; keep one of the two lines"
	)]
	[DataRow(
		"InputTimeOut= 30\nInputTimeOut= 31\n",
		5,
		"'InputTimeOut=' gives a value different from line 4, and the setting holds one value; keep one of the two lines"
	)]
	[DataRow(
		"Security= 5\nSecurity= 6\n",
		5,
		"'Security=' gives a value different from line 4, and the setting holds one value; keep one of the two lines"
	)]
	[DataRow(
		"FlyCheat= ENABLED\n[Options]\nFlyCheat= DISABLED\n",
		6,
		"'FlyCheat=' gives a value different from line 4, and the setting holds one value; keep one of the two lines"
	)]
	public void OptionsSettingWithADifferentValue_Fails(string lines, int expectedLine, string expectedMessage)
	{
		// PD-31: VB6 only ever sets an [Options] flag (Module1.bas:5402-5425), so ENABLED then DISABLED leaves it set, and keeps the last
		// InputTimeOut= and Security= (:5426-5430). Every [Options] section adds to one list (:5448-5456).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Options]\n" + lines + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A setting given two different values is an error.");
		DiagnosticAssert.SingleError(diagnostics: result.Diagnostics, line: expectedLine, message: expectedMessage, file: workspace.ScriptFile);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void OptionsSettingRepeatedWithTheSameResolvedValue_WarnsAndWritesOnce()
	{
		// $1E is 30, so the second line repeats the first; the classic header holds one InputTimeOut= field (Module1.bas:6668-6669).
		using var single = TestWorkspace.CreateCompilable(TestScripts.Language + "[Options]\nInputTimeOut= 30\n" + TestScripts.Level);

		using var repeated = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nInputTimeOut= 30\nInputTimeOut= $1E\n" + TestScripts.Level
		);

		TestCompileResult singleResult = single.Compile();
		TestCompileResult repeatedResult = repeated.Compile();

		Assert.IsTrue(singleResult.Success, singleResult.Log);
		Assert.IsTrue(repeatedResult.Success, repeatedResult.Log);

		DiagnosticAssert.HasWarning(repeatedResult.Diagnostics, 5, "'InputTimeOut=' repeats line 4 with the same value; remove one of the two lines");
		Assert.AreSequenceEqual(single.ReadScriptDat(), repeated.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ExtensionLineWithADifferentValue_Fails()
	{
		// PD-31: VB6 keeps the last extension with a warning (Module1.bas:5545-5556).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[PCExtensions]\nLevel= .TR4\nLevel= .TR5\nFMV= .BIK\nCut= .TR4\n" + TestScripts.Level
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "An extension given two different values is an error.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 5,
			message: "'Level=' gives a value different from line 4, and the setting holds one value; keep one of the two lines",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ExtensionLineRepeatedWithTheSameValue_WarnsAndWritesOnce()
	{
		// VB6 counts a slot once (Module1.bas:5546-5553), so the repeat does not change the extension table.
		const string head = TestScripts.Language + "[PCExtensions]\nLevel= .TR4\n";
		const string extensions = "FMV= .BIK\nCut= .TR4\n";

		using var single = TestWorkspace.CreateCompilable(head + extensions + TestScripts.Level);
		using var repeated = TestWorkspace.CreateCompilable(head + "Level= .TR4\n" + extensions + TestScripts.Level);

		TestCompileResult singleResult = single.Compile();
		TestCompileResult repeatedResult = repeated.Compile();

		Assert.IsTrue(singleResult.Success, singleResult.Log);
		Assert.IsTrue(repeatedResult.Success, repeatedResult.Log);

		DiagnosticAssert.HasWarning(repeatedResult.Diagnostics, 5, "'Level=' repeats line 4 with the same value; remove one of the two lines");
		Assert.AreSequenceEqual(single.ReadScriptDat(), repeated.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void DefineWithADifferentValueInOneFile_Fails()
	{
		// PD-31: VB6 keeps the first value with a warning (Module1.bas:13691-13698).
		using var workspace = TestWorkspace.CreateCompilable(
			"#DEFINE SPEED 1\n#DEFINE SPEED 2\n" + TestScripts.Language + TestScripts.Level + "UVRotate= SPEED\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A define given two different values is an error.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 2,
			message: "#DEFINE SPEED gives a value different from line 1, and a define holds one value; keep one of the two lines",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IdenticalClassicCommand_IsEmittedTwiceAndWarned()
	{
		// VB6 writes every repeat and warns (Module1.bas:6409-6419); an identical repeat keeps those bytes.
		const string header = TestScripts.Language + "[Level]\nName= Test Level\nLoadCamera= 0,0,0,0,0,0,0\nLevel= data\\test,0\n";

		using var single = TestWorkspace.CreateCompilable(header + "Fog= 1,2,3\n", TestWorkspace.CompilableLanguageContent);
		using var duplicate = TestWorkspace.CreateCompilable(header + "Fog= 1,2,3\nFog= 1,2,3\n", TestWorkspace.CompilableLanguageContent);

		TestCompileResult singleResult = single.Compile();
		TestCompileResult duplicateResult = duplicate.Compile();

		Assert.IsTrue(singleResult.Success, singleResult.Log);
		Assert.IsTrue(duplicateResult.Success, duplicateResult.Log);

		// Fog= writes tag 0x8F and three color bytes (Module1.bas:6094-6105); the repeat adds them again.
		byte[] level = ScriptDatReader.Read(duplicate.ReadScriptDat()).GetSectionBytes(0);

		Assert.HasCount(single.ReadScriptDat().Length + 4, duplicate.ReadScriptDat(), "VB6 writes every repeat.");
		Assert.AreSequenceEqual(new byte[] { 0x8F, 0x01, 0x02, 0x03, 0x8F, 0x01, 0x02, 0x03 }, level.Take(8));

		DiagnosticAssert.HasWarning(duplicateResult.Diagnostics, 8, "'Fog=' repeats line 7 with the same value; remove one of the two lines");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Name= Test Level\nFog= 1,2,3\nFog= 4,5,6\n", 8, "'Fog=' gives a value different from line 7")]
	[DataRow("Name= Test Level\nKey= 1,Test Level,1,2,3,4,5,6\nKey= $1,Test Level,9,2,3,4,5,6\n", 8, "'Key= $1' gives a value different from line 7")]
	[DataRow("Name= Test Level\nResidentCut= 2,5\nResidentCut= 2,6\n", 8, "'ResidentCut= 2' gives a value different from line 7")]
	[DataRow("Name= Test Level\nAnimatingMIP= 3,1\nAnimatingMIP= 3,2\n", 8, "'AnimatingMIP= 3' gives a value different from line 7")]
	[DataRow(
		"Name= Test Level\nPuzzleCombo= 1,2,Test Level,1,2,3,4,5,6\nPuzzleCombo= 1,2,Test Level,0,2,3,4,5,6\n",
		8,
		"'PuzzleCombo= 1,2' gives a value different from line 7"
	)]
	[DataRow("Name= Test Level\nHorizon= ENABLED\nHorizon= DISABLED\n", 8, "'Horizon=' gives a value different from line 7")]
	[DataRow("Name= Test Level\nName= Other Level\n", 7, "'Name=' gives a value different from line 6")]
	public void ClassicCommandRepeatedWithADifferentValue_Fails(string lines, int expectedLine, string expectedRepeat)
	{
		// VB6 counts a repeat by the tag it writes (Module1.bas:8781-8790), per ResidentCut= slot and per inventory item and piece,
		// writes both lines and warns (:6409-6419); a level flag is only ever set (:6370-6377), and Name= keeps the last (:5995-6010).
		const string header = TestScripts.Language + "[Level]\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.CreateCompilable(header + lines, "[Strings]\nTest Level\nOther Level\n");
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A setting given two different values is an error.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: expectedLine,
			message: expectedRepeat + ", and the setting holds one value; keep one of the two lines",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("ResidentCut= 1,5\nResidentCut= 2,6\n")]
	[DataRow("AnimatingMIP= 1,1\nAnimatingMIP= 2,2\n")]
	[DataRow("FMV= 1,0\nFMV= 2,1\n")]
	[DataRow("Key= 1,Test Level,1,2,3,4,5,6\nKey= 2,Test Level,1,2,3,4,5,6\n")]
	public void ClassicCommandsOnDifferentSlots_AreNoRepeat(string lines)
	{
		// Two slots write two tags (Module1.bas:5912, 6253), AnimatingMIP= packs its slot into its byte (:6146), and each FMV= is a
		// movie of its own.
		using var workspace = TestWorkspace.CreateClean(lines);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		DiagnosticAssert.NoDiagnostics(result.Diagnostics);
	}
}
