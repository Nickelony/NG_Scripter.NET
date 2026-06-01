using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

/// <summary>
/// The grammar of the NG command definitions in the <c>&lt;START_NEW_COMMANDS&gt;</c> section of a constants
/// file (PD-62).
/// </summary>
[TestClass]
public sealed class NgCommandDefinitionGrammarTests
{
	private const string FilePath = @"C:\Engine\my_constants.txt";

	private static (NgCommandCatalog Catalog, IReadOnlyList<CompilerDiagnostic> Diagnostics) Parse(params string[] definitionLines)
	{
		var diagnostics = new DiagnosticSink(new CompilerLog(conciseLogging: true));
		string[] lines = ["2#1#OutsideTheSection=:Word", "<START_NEW_COMMANDS>", .. definitionLines, "<END>"];

		return (NgCommandCatalog.Parse(lines, FilePath, diagnostics), diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void CodeAndLimitInHexadecimal_AreRead()
	{
		// VB6 reads the code, the limit and the flag values with GetCampoNumero, which takes $ (Module1.bas:2676-2700, 4557-4615).
		(NgCommandCatalog catalog, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse("$2#$10#Snow=:Word", "0#1#*CRS=:Bool#$0008");

		DiagnosticAssert.NoDiagnostics(diagnostics);

		Assert.AreEqual((NgCommandCode.Snow, 16), (catalog.Definitions["Snow="].Code, catalog.Definitions["Snow="].MaxOccurrences));
		Assert.AreEqual(8, catalog.Definitions["CRS="].BoolEnabledValue);
		Assert.IsFalse(catalog.Definitions.ContainsKey("OutsideTheSection="), "A line outside the section is no definition.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void IsFlag_HoldsForTheOneBoolCommandAlone()
	{
		(NgCommandCatalog catalog, _) = Parse("0#1#*CRS=:Bool#$0008", "2#1#Snow=:Word", "3#1#Wide=:Word:Word");

		Assert.IsTrue(catalog.Definitions["CRS="].IsFlag);
		Assert.IsFalse(catalog.Definitions["Snow="].IsFlag);
		Assert.IsFalse(catalog.Definitions["Wide="].IsFlag);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TriggerGroupPair_KeepsBothFormsAndReservedIsSkipped()
	{
		(NgCommandCatalog catalog, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse(
			"21#999#TriggerGroup=:Word:Long:long:Long:ArrayLong",
			"46#999#TriggerGroup=:Word:Word:Word:Word:Array",
			"200#1#RESERVED"
		);

		DiagnosticAssert.NoDiagnostics(diagnostics);

		Assert.AreEqual(NgCommandCode.TriggerGroup, catalog.Definitions["TriggerGroup="].Code);
		Assert.AreEqual(NgCommandCode.TriggerGroupWord, catalog.Definitions["TriggerGroupWord="].Code);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow(
		"2#1#Snow=:Word|3#1#Snow=:Word",
		"The definition of Snow= repeats line 3, and a command takes one definition; remove one of the two lines"
	)]
	[DataRow(
		"2#1#Snow=:Wrod",
		"The definition of Snow= has the unknown argument type 'Wrod'; write Slot, String, Word, Integer, Long, Bool, Import, Array, ArrayByte, ArrayNybble or ArrayLong"
	)]
	[DataRow("2#1#Snow=:Word:", "The definition of Snow= has an empty argument type; remove the extra ':'")]
	[DataRow("0#1#Odd=:Bool:Word#$0001", "The definition of Odd= gives a Bool with other arguments, but a Bool command takes one argument")]
	[DataRow(
		"0#1#Flag=:Bool#0#0",
		"The definition of Flag= gives 0 for both values of its Bool, so the flag sets nothing; write the flag after the next '#'"
	)]
	[DataRow(
		"0#1#Flag=:Bool",
		"The definition of Flag= gives 0 for both values of its Bool, so the flag sets nothing; write the flag after the next '#'"
	)]
	[DataRow("2x#1#Snow=:Word", "The code of the definition '2x#1#Snow=:Word' is invalid: 2x is not a number; a decimal number holds digits only")]
	[DataRow(
		"2#one#Snow=:Word",
		"The limit of the definition '2#one#Snow=:Word' is invalid: one is not a number; write decimal digits with an optional sign, or a $, &H or &O value"
	)]
	[DataRow(
		"256#1#Snow=:Word",
		"The code 256 of the definition of Snow= lies outside 0 to 255, the values the high byte of a command word holds; write a code in that range"
	)]
	[DataRow(
		"-1#1#Snow=:Word",
		"The code -1 of the definition of Snow= lies outside 0 to 255, the values the high byte of a command word holds; write a code in that range"
	)]
	[DataRow(
		"2#-2#Snow=:Word",
		"The limit -2 of the definition of Snow= is below -1; write -1 for no limit, or the most times a section holds the command"
	)]
	[DataRow(
		"2#1#Snow=:Array:Word",
		"The definition of Snow= gives an array type before its last argument type, but an array takes the remaining values; move the array to the end"
	)]
	[DataRow("2#1#Snow=:Word:ArrayByte:ArrayLong", "The definition of Snow= gives 2 array types, but a command takes one array; keep one")]
	[DataRow("2#1", "The definition '2#1' does not have the form CODE#LIMIT#Name=:Type:...#ENABLED#DISABLED, with the last two fields optional")]
	[DataRow("2#1#Snow:Word", "The definition '2#1#Snow:Word' names no command; write the name with its '=', such as Snow=")]
	public void MalformedDefinition_FailsNamingTheLine(string definitionLines, string expectedMessage)
	{
		// VB6 keeps the first of two definitions (Module1.bas:5320-5322), reads an unknown or empty type as a word after a message box
		// (:4668-4673), and makes a Bool a flag only when one of its values is not 0 (:7121); a code or a limit that is no number stops
		// NG Center with a message box (:4557-4615).
		string[] lines = definitionLines.Split('|');
		(_, IReadOnlyList<CompilerDiagnostic> diagnostics) = Parse(lines);

		CompilerDiagnostic diagnostic = diagnostics.Single();

		Assert.AreEqual(
			(true, expectedMessage, 2 + lines.Length, FilePath),
			(diagnostic.IsError, diagnostic.Message, diagnostic.LineNumber, diagnostic.SourceFile)
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CustomConstantsFileWithTwoDefinitionsOfOneName_FailsTheCompile()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		string stockConstants = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripter_constants.txt"));
		string customConstants = stockConstants + "\r\n<START_NEW_COMMANDS>\r\n2#1#Snow=:Word\r\n<END>\r\n";
		string constantsPath = workspace.WithTextFile("custom_constants.txt", customConstants).PathOf("custom_constants.txt");

		TestCompileResult result = workspace.Compile(workspace.CreateOptions() with { ConstantsFilePath = constantsPath });

		string[] lines = customConstants.Split("\r\n");

		int stockLine = Array.IndexOf(lines, "2#1#Snow=:Word") + 1;
		int addedLine = Array.LastIndexOf(lines, "2#1#Snow=:Word") + 1;

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: addedLine,
			message: $"The definition of Snow= repeats line {stockLine}, and a command takes one definition; remove one of the two lines",
			file: constantsPath
		);
	}
}
