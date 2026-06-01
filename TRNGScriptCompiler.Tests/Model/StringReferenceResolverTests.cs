using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Model;

[TestClass]
public sealed class StringReferenceResolverTests
{
	private const string BigRoomScript = TestScripts.Language + "[Level]\nName= Big Room\nLevel= data\\test,0\n" + TestScripts.LoadCamera + "\n";

	private static readonly SourceLocation s_location = SourceLocation.InFile("Script.txt");

	private static string BinaryStringMessage(string reference) =>
		$"{reference} is no binary string; write & and pairs of the upper-case hexadecimal digits 0-9 and A-F, such as &0A1B, in the reference and its language line";

	private static (StringReferenceResolver Resolver, CompilationRun Run) CreateResolver()
	{
		var language = new LanguageData
		{
			TotalSectionStrings = 3,
			TotalPsxStrings = 0,
			TotalPcStrings = 0,
			Strings = ["Test Level", "Big Room ", "&12ab"],
			ExtraStrings = [new ExtraNgString { Index = 31, Text = "Plugin Text" }],
			LanguageFile = "English.txt",
		};

		var run = new CompilationRun(conciseLogging: true);
		return (new StringReferenceResolver(language, run.Diagnostics), run);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void IsValidBinaryString_RequiresUppercaseEvenLengthHex()
	{
		// VB6's IsStringaBinariaOk takes no space, an even digit count and no lower-case digit (Module1.bas:14113-14150).
		Assert.IsTrue(StringReferenceResolver.IsValidBinaryString("&12AB"));
		Assert.IsTrue(StringReferenceResolver.IsValidBinaryString("&00"));

		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("&12ab"), "Lowercase hex digits are rejected.");
		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("&123"), "An odd digit count is rejected.");
		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("&"), "The literal must contain at least one byte.");
		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("12AB"), "The '&' prefix is required.");
		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("&1 2"), "Whitespace is not a hex digit.");

		// VB6 takes a lone & and a - among the digits (Module1.bas:14127-14154, MioModulo.bas:5003); the port rejects both (PD-92).
		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("&-1"), "A minus sign is not a hex digit.");
		Assert.IsFalse(StringReferenceResolver.IsValidBinaryString("&1-"), "A minus sign is not a hex digit.");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("Legend= {0}", "&")]
	[DataRow("Legend= {0}", "&-1")]
	[DataRow("Damage= 1,2,3,4,{0},5", "&")]
	[DataRow("Damage= 1,2,3,4,{0},5", "&-1")]
	public void BinaryStringVb6TakesWithoutAByteOrWithAMinus_FailsTheCompile(string commandFormat, string reference)
	{
		// VB6's IsStringaBinariaOk takes an empty digit string and checks each digit alone with isNumericHex, which takes a leading -
		// (Module1.bas:14127-14154, MioModulo.bas:5003), so a classic and an NG command name the language line "&" or "&-1" (PD-92).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + TestScripts.Level + commandFormat.Replace("{0}", reference, StringComparison.Ordinal) + "\n",
			"[Strings]\nTest Level\n" + reference + "\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(BinaryStringMessage(reference), diagnostic.Message);
		Assert.AreEqual(7, diagnostic.LineNumber);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow(false)]
	[DataRow(true)]
	public void InvalidBinaryString_IsRejectedForEitherFormat_EvenWhenTheLanguageHoldsIt(bool allowExtraNg)
	{
		// VB6 checks a binary string before the lookup, for a classic and an NG command alike (Module1.bas:6579-6585, 7529).
		(StringReferenceResolver resolver, CompilationRun run) = CreateResolver();

		Assert.IsFalse(resolver.TryResolve(text: "&12ab", allowExtraNg, notFoundMessage: "Not found", location: s_location, out int index));
		Assert.AreEqual(-1, index);
		Assert.AreEqual(BinaryStringMessage("&12ab"), run.Diagnostics.Recorded.Single(diagnostic => diagnostic.IsError).Message);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgIndex_ResolvesForAnNgCommand_AndIsRejectedForAClassicOne()
	{
		// VB6 ORs the found index with $8000 (Module1.bas:6574) and rejects the form in a classic command (:6548-6553).
		(StringReferenceResolver resolver, CompilationRun run) = CreateResolver();

		Assert.IsTrue(resolver.TryResolve(text: "!31", allowExtraNg: true, notFoundMessage: "Not found", location: s_location, out int index));
		Assert.AreEqual(LanguageData.ExtraNgBaseOffset + 31, index);

		Assert.IsFalse(resolver.TryResolve(text: "!31", allowExtraNg: false, notFoundMessage: "Not found", location: s_location, index: out _));

		DiagnosticAssert.SingleError(
			run.Diagnostics.Recorded,
			line: null,
			"You cannot use an index for extra NG strings in a standard (old) script command: !31"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgIndexTheLanguageLacks_NamesTheIndex()
	{
		// VB6 reports the index it cannot find (Module1.bas:6566).
		(StringReferenceResolver resolver, CompilationRun run) = CreateResolver();

		Assert.IsFalse(resolver.TryResolve(text: "!5", allowExtraNg: true, notFoundMessage: "Not found", location: s_location, index: out _));
		Assert.AreEqual("Cannot find the extra NG string with index: !5", run.Diagnostics.Recorded.Single(diagnostic => diagnostic.IsError).Message);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Find_GivesTheSingleNormalizedMatch_WithoutADiagnostic()
	{
		(StringReferenceResolver resolver, CompilationRun run) = CreateResolver();

		Assert.AreEqual(1, resolver.Find("Big Room", allowExtraNg: true));
		Assert.AreEqual(-1, resolver.Find("Missing", allowExtraNg: true));
		Assert.AreEqual(LanguageData.ExtraNgBaseOffset + 31, resolver.Find("Plugin Text", allowExtraNg: true));
		Assert.AreEqual(-1, resolver.Find("Plugin Text", allowExtraNg: false), "A classic command looks up the classic strings alone.");

		DiagnosticAssert.NoDiagnostics(run.Diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Find_KeepsOneMatchForLinesThatDifferOnlyInCase_AndFailsForLinesThatDifferInSpacing()
	{
		var language = new LanguageData
		{
			TotalSectionStrings = 5,
			TotalPsxStrings = 0,
			TotalPcStrings = 0,
			Strings = ["Big Room ", "BIG ROOM ", "Wide  Hall", "Wide Hall ; note", "Plain"],
			ExtraStrings = [new ExtraNgString { Index = 7, Text = "big room " }, new ExtraNgString { Index = 8, Text = "Wide Hall  " }],
			LanguageFile = "English.txt",
		};

		var resolver = new StringReferenceResolver(language, new CompilationRun(conciseLogging: true).Diagnostics);

		Assert.AreEqual(0, resolver.Find("Big Room", allowExtraNg: true), "The first of the lines that differ only in case is the one match.");
		Assert.AreEqual(0, resolver.Find("big ROOM", allowExtraNg: false), "The index is built once, and a classic command reads the same lines.");
		Assert.AreEqual(-1, resolver.Find("Wide Hall", allowExtraNg: true), "Lines that differ in spacing or in a comment are several matches.");

		Assert.AreSequenceEqual(
			[(2, "Wide  Hall"), (3, "Wide Hall ; note")],
			language.FindNormalizedMatches("Wide Hall", allowExtraNgText: false),
			"A classic command is offered the classic lines alone."
		);

		Assert.AreSequenceEqual(
			[(2, "Wide  Hall"), (3, "Wide Hall ; note"), (LanguageData.ExtraNgBaseOffset + 8, "Wide Hall  ")],
			language.FindNormalizedMatches("Wide Hall", allowExtraNgText: true),
			"An NG command is offered the ExtraNG lines after the classic ones."
		);

		Assert.HasCount(
			1,
			language.FindNormalizedMatches("big room", allowExtraNgText: true),
			"An ExtraNG line that repeats a classic one in other case adds nothing."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgCommandWithAnInvalidBinaryString_FailsTheCompile()
	{
		// An NG STRING argument goes through GetIndiceStringa too (Module1.bas:7529), so its binary string is checked as a classic one is.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + TestScripts.Level + "Damage= 1,2,3,4,&12ab,5\n",
			"[Strings]\nTest Level\n&12ab\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.IsError);

		Assert.AreEqual(BinaryStringMessage("&12ab"), diagnostic.Message);
		Assert.AreEqual(7, diagnostic.LineNumber);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ClassicInventoryString_DoesNotResolvePlainTextFromExtraNg()
	{
		const string script = """
			[Language]
			File= 0,English.txt

			[Options]
			Title= DISABLED
			Security= 0

			[Level]
			Name= Shared Level
			Level= data\shared,0
			LoadCamera= 0,0,0,0,0,0,0
			Key= 1,Key Extra Label,1,2,3,4,5,6
			""";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFileWithExtraNg(["Shared Level"], (120, "Key Extra Label")));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "Classic inventory commands should not resolve plain-text strings from [ExtraNG].");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 12,
			message: "Cannot find string 'Key Extra Label' in language file",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ClassicInventoryBinaryString_IsNotImmediateIndex_AndMustExistInLanguageTable()
	{
		const string script = """
			[Language]
			File= 0,English.txt

			[Options]
			Title= DISABLED
			Security= 0

			[Level]
			Name= Shared Level
			Level= data\shared,0
			LoadCamera= 0,0,0,0,0,0,0
			Puzzle= 1,&1234,1,2,3,4,5,6
			""";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Shared Level"));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "Binary-string tokens should still resolve through the standard language table.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 12,
			message: "Cannot find string '&1234' in language file",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ClassicLegend_DoesNotTreatHashValueAsImmediateStringIndex()
	{
		const string script = """
			[Language]
			File= 0,English.txt

			[Options]
			Title= DISABLED
			Security= 0

			[Level]
			Name= Shared Level
			Level= data\shared,0
			LoadCamera= 0,0,0,0,0,0,0
			Legend= #3
			""";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Shared Level", "Legend Line"));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "Classic legend text should not accept '#'-prefixed immediate indexes.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 12,
			message: "Cannot find legend text in language file: #3",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ExtraNgIndexInClassicCommand_Fails()
	{
		const string script = TestScripts.LanguageAndLevel + "Key= 1, !0, 1,2,3,4,5,6\n";
		const string language = "[Strings]\nTest Level\n[ExtraNG]\n0: hello\nTHE LAST STRING ENTRY\n";

		using var workspace = TestWorkspace.CreateCompilable(script, language);
		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "VB6 rejects !NNN indexes in standard (old) script commands.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 7,
			message: "You cannot use an index for extra NG strings in a standard (old) script command: !0",
			file: workspace.ScriptFile
		);
	}

	// A workspace that draws no warning, so a test can assert that a lookup draws none.
	private static TestWorkspace CreateCleanWorkspace(string script, string language) =>
		TestWorkspace.CreateCompilable(TestWorkspace.ExtensionSectionsContent + script, language).WithObjectsH(TestWorkspace.CleanObjectsHContent);

	[TestMethod]
	[TestCategory("IO")]
	public void NoBreakSpaceAtTheEdgeOfANameText_IsTrimmedAsAnArgumentEdgeIs()
	{
		// PD-63: VB6 takes the Name= text with Trim$ (Module1.bas:5995-6010), so a no-break space after '=' stays and the lookup misses.
		using TestWorkspace edgeWorkspace = CreateCleanWorkspace(BigRoomScript.Replace("Name= ", "Name= "), "[Strings]\nBig Room\n");
		using TestWorkspace plainWorkspace = CreateCleanWorkspace(BigRoomScript, "[Strings]\nBig Room\n");

		TestCompileResult edgeResult = edgeWorkspace.Compile();
		TestCompileResult plainResult = plainWorkspace.Compile();

		Assert.IsTrue(edgeResult.Success, edgeResult.Log);
		Assert.IsTrue(plainResult.Success, plainResult.Log);

		DiagnosticAssert.NoDiagnostics(edgeResult.Diagnostics);

		Assert.AreSequenceEqual(plainWorkspace.ReadScriptDat(), edgeWorkspace.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageLineWithATrailingSpace_IsFoundByItsNormalizedTextWithAWarning()
	{
		// PD-63: VB6 looks a string up by its raw language line (Module1.bas:2055, 6591-6596), which keeps the trailing space a
		// script line cannot carry, so Name= Big Room fails there. The port falls back to the normalized line and warns.
		using var spacedWorkspace = TestWorkspace.CreateCompilable(BigRoomScript, "[Strings]\nFiller\nBig Room \n");
		using var exactWorkspace = TestWorkspace.CreateCompilable(BigRoomScript, "[Strings]\nFiller\nBig Room\n");

		TestCompileResult spacedResult = spacedWorkspace.Compile();
		TestCompileResult exactResult = exactWorkspace.Compile();

		Assert.IsTrue(spacedResult.Success, spacedResult.Log);
		Assert.IsTrue(exactResult.Success, exactResult.Log);

		DiagnosticAssert.HasWarning(
			spacedResult.Diagnostics,
			4,
			"The string 'Big Room' matches the language line 'Big Room ' only when spaces and comments are ignored"
		);

		Assert.AreSequenceEqual(exactWorkspace.ReadScriptDat(), spacedWorkspace.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ExactLanguageLine_WinsOverANormalizedMatch()
	{
		// The exact lookup runs first (Module1.bas:6591-6596), so every string VB6 finds keeps its index and no warning is given.
		using TestWorkspace workspace = CreateCleanWorkspace(BigRoomScript, "[Strings]\nBig Room \nBig Room\n");
		using TestWorkspace exactWorkspace = CreateCleanWorkspace(BigRoomScript, "[Strings]\nFiller\nBig Room\n");

		TestCompileResult result = workspace.Compile();
		TestCompileResult exactResult = exactWorkspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(exactResult.Success, exactResult.Log);

		DiagnosticAssert.NoDiagnostics(result.Diagnostics);

		Assert.AreSequenceEqual(exactWorkspace.ReadScriptDat(), workspace.ReadScriptDat(), "Both name index 1.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void SeveralNormalizedMatches_Fail()
	{
		using var workspace = TestWorkspace.CreateCompilable(BigRoomScript, "[Strings]\nBig Room ; one\nBig  Room\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,

			message: "The string 'Big Room' matches the language lines 'Big Room ; one', 'Big  Room' when spaces and comments are ignored; "
				+ "write the language line it names without extra spaces or a comment, so it matches exactly",

			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void NgStringArgument_FindsALanguageLineByItsNormalizedTextWithAWarning()
	{
		// PD-63: an NG STRING argument is looked up as written (Module1.bas:7522-7529) against the raw language lines.
		const string script = TestScripts.Language + "[Options]\nTitle= DISABLED\nSecurity= 0\n" + TestScripts.Level + "WindowTitle= My Game\n";

		using var spacedWorkspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\nMy  Game\n");
		using var exactWorkspace = TestWorkspace.CreateCompilable(script, "[Strings]\nTest Level\nMy Game\n");

		TestCompileResult spacedResult = spacedWorkspace.Compile();
		TestCompileResult exactResult = exactWorkspace.Compile();

		Assert.IsTrue(spacedResult.Success, spacedResult.Log);
		Assert.IsTrue(exactResult.Success, exactResult.Log);

		DiagnosticAssert.HasWarning(
			spacedResult.Diagnostics,
			10,
			"The string 'My Game' matches the language line 'My  Game' only when spaces and comments are ignored"
		);

		Assert.AreSequenceEqual(exactWorkspace.ReadScriptDat(), spacedWorkspace.ReadScriptDat());
	}
}
