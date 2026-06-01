using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class ScriptArgumentResolverTests
{
	private static (ScriptArgumentResolver Resolver, UnitRun Unit) CreateResolver(params string[] strings)
	{
		var unit = new UnitRun();
		return (new ScriptArgumentResolver(unit.Run.Diagnostics, unit.Evaluator, UnitRun.Language(strings)), unit);
	}

	[TestMethod]
	[DataRow("12", 12)]
	[DataRow("$1F", 31)]
	[DataRow("#1F", 31)]
	[DataRow("$FFFFFFFF", -1)]
	[DataRow("&H10", 16)]
	[DataRow("&O17", 15)]
	[DataRow("-5", -5)]
	[DataRow("$10+2-1", 17)]
	[DataRow(" 3 + 4 ", 7)]
	public void TryEvaluateArgument_ReadsTheVb6NumericForms(string argument, int expected)
	{
		// VB6 splits an argument at + and each piece at -, reads a $ or # piece as a 32-bit hexadecimal value and any other number with Val,
		// and adds or subtracts the pieces in order (Module1.bas:4857-4908).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsTrue(resolver.TryEvaluateArgument(args: [argument], index: 0, command: "Cut=", location: unit.At(7), out int value));
		Assert.AreEqual(expected, value);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[DataRow("ENABLED", 1)]
	[DataRow("CL_RED", 3)]
	[DataRow("cl_red", 3)]
	[DataRow("IGNORE", -1)]
	[DataRow("SLOT_TEST", 2)]
	[DataRow("STATIC_TEST", 1)]
	[DataRow("CL_RED+SLOT_TEST+$10", 21)]
	public void TryEvaluateArgument_ResolvesConstantsSlotAndStaticNames(string argument, int expected)
	{
		// A piece that is no number is looked up, without regard to case (Option Compare Text, Module1.bas:4), among the constants, then the
		// slot names, then the static names (:4887-4897, 10116-10171). IGNORE is the constant -1 of scripter_constants.txt.
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsTrue(resolver.TryEvaluateArgument(args: [argument], index: 0, command: "Cut=", location: unit.At(7), out int value));
		Assert.AreEqual(expected, value);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void TryEvaluateArgument_ReadsTheDefinesOfTheCommandsFileOnly()
	{
		// VB6 finds a #DEFINE only when the file of the command declares it (TrovaValoreDefine, Module1.bas:10085-10096).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();
		string include = Path.Combine(unit.Root, "script", "Rooms.txt");
		unit.Define(unit.ScriptFile, "#DEFINE MY_ROOM 7");

		Assert.IsTrue(resolver.TryEvaluateArgument(args: ["MY_ROOM+1"], index: 0, command: "Cut=", location: unit.At(7), out int value));
		Assert.AreEqual(8, value);

		Assert.IsFalse(
			resolver.TryEvaluateArgument(args: ["MY_ROOM"], index: 0, command: "Cut=", location: new SourceLocation(3, File: include), value: out _)
		);

		DiagnosticAssert.SingleError(
			diagnostics: unit.Diagnostics,
			line: 3,
			message: "Argument 1 of Cut= is invalid: MY_ROOM is neither a number nor a known name",
			file: include
		);
	}

	[TestMethod]
	public void TryEvaluateArgument_AFailedDefineOfAConstantsName_LeavesTheConstantInUse()
	{
		// VB6 looks the constants up before the defines (Module1.bas:10116-10128, 10148-10150), so a define of a constant's name that gives no
		// value changes nothing, while a failed define of a new name names its own line when a command uses it.
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();
		unit.Define(unit.ScriptFile, "#DEFINE CL_RED NOT_A_NAME", "#DEFINE MY_ROOM NOT_A_NAME");

		Assert.IsTrue(resolver.TryEvaluateArgument(args: ["CL_RED"], index: 0, command: "Cut=", location: unit.At(7), out int value));
		Assert.AreEqual(3, value);
		Assert.IsFalse(resolver.TryEvaluateArgument(args: ["MY_ROOM"], index: 0, command: "Cut=", location: unit.At(8), value: out _));

		DiagnosticAssert.Errors(
			unit.Diagnostics,
			(1, "The #DEFINE value [NOT_A_NAME] is invalid: NOT_A_NAME is neither a number nor a known name"),
			(2, "The #DEFINE value [NOT_A_NAME] is invalid: NOT_A_NAME is neither a number nor a known name"),
			(8, "Argument 1 of Cut= is invalid: MY_ROOM names the #DEFINE of line 2, which gives no value; fix that line")
		);
	}

	[TestMethod]
	public void TryEvaluateArgument_NamesTheArgumentCountingFromOne()
	{
		// VB6 numbers an argument from 1 in its messages (Module1.bas:4816-4822).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsFalse(resolver.TryEvaluateArgument(args: ["1", "2", "BLUE_FOG"], index: 2, command: "Fog=", location: unit.At(7), value: out _));
		DiagnosticAssert.SingleError(unit.Diagnostics, 7, "Argument 3 of Fog= is invalid: BLUE_FOG is neither a number nor a known name");
	}

	[TestMethod]
	public void TryEvaluate_FailsWithoutReporting()
	{
		// The sort key reads the item number with GetArgNumerico's TestNoShow, which reports nothing (Module1.bas:4806-4822, 4876-4896, 5697).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsFalse(resolver.TryEvaluate("BLUE_FOG", unit.ScriptFile, out _));
		Assert.IsTrue(resolver.TryEvaluate("$0C", unit.ScriptFile, out int value));
		Assert.AreEqual(12, value);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void ResolveStringIndex_FindsTheClassicIndex()
	{
		// VB6 looks a classic string up in the first language without regard to case (Module1.bas:6590-6596).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver("Test Level", "Big Key");

		Assert.AreEqual(1, resolver.ResolveStringIndex("big key", "Cannot find string 'big key' in language file", unit.At(7)));
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	public void ResolveStringIndex_RejectsAnExtraNgReference()
	{
		// A classic command may not name an ExtraNG string by its index (Module1.bas:6547-6553).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver("Test Level");

		Assert.AreEqual(-1, resolver.ResolveStringIndex("!5", "Cannot find string '!5' in language file", unit.At(7)));
		DiagnosticAssert.SingleError(unit.Diagnostics, 7, "You cannot use an index for extra NG strings in a standard (old) script command: !5");
	}

	[TestMethod]
	public void ResolveStringIndex_MissingString_ReportsTheMessageOfTheCaller()
	{
		// VB6 fails a string the language file lacks (Module1.bas:6611-6615).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver("Test Level");

		Assert.AreEqual(-1, resolver.ResolveStringIndex("Big Key", "Cannot find string 'Big Key' in language file", unit.At(7)));
		DiagnosticAssert.SingleError(unit.Diagnostics, 7, "Cannot find string 'Big Key' in language file");
	}

	[TestMethod]
	public void ValidateOneByteStringIndex_TakesTheFirst256Entries()
	{
		// Name= and Legend= write the index as one byte (Module1.bas:5851, 5994), so VB6 writes entry 256 as entry 0 (PD-56).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsTrue(resolver.ValidateOneByteStringIndex(index: 255, text: "Last", command: "Legend=", location: unit.At(7)));
		Assert.IsFalse(resolver.ValidateOneByteStringIndex(index: 256, text: "Past", command: "Legend=", location: unit.At(8)));

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			8,
			"The Legend= string 'Past' is entry 256 of the language file, but Legend= writes its index in one byte; move the string into the first 256 entries"
		);
	}

	[TestMethod]
	public void CheckArgumentCount_NamesTheExpectedAndTheGivenCount()
	{
		// VB6 checks the argument count of each classic command before it reads one (Fog=, Module1.bas:6086-6092).
		var unit = new UnitRun();
		DiagnosticSink sink = unit.Run.Diagnostics;

		Assert.IsTrue(
			ScriptArgumentResolver.CheckArgumentCount(diagnostics: sink, args: ["1", "2", "3"], expected: 3, command: "Fog=", location: unit.At(7))
		);

		Assert.IsFalse(
			ScriptArgumentResolver.CheckArgumentCount(diagnostics: sink, args: ["1", "2"], expected: 3, command: "Fog=", location: unit.At(8))
		);

		DiagnosticAssert.SingleError(unit.Diagnostics, 8, "Wrong number of arguments for 'Fog=': expected 3, got 2");
	}

	[TestMethod]
	[DataRow(-128, 1)]
	[DataRow(255, 1)]
	[DataRow(-32768, 2)]
	[DataRow(65535, 2)]
	[DataRow(int.MinValue, 4)]
	[DataRow(-1, 4)]
	public void ValidateWidth_AcceptsTheSignedAndTheUnsignedReadingOfTheField(int value, int width)
	{
		// VB6 checks a byte against -256 to 255 and a word against -65536 to 65535, and leaves 4 bytes unchecked (Module1.bas:4915-4937); the
		// port takes the values the field holds read signed or unsigned (PD-56).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsTrue(resolver.ValidateWidth(value, widthInBytes: width, command: "Cut=", argumentIndex: 0, location: unit.At(7)));
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[DataRow(-129, 1, "Argument 1 of Cut= is out of range: -129 lies outside -128 to 255, the values its byte holds")]
	[DataRow(256, 1, "Argument 1 of Cut= is out of range: 256 lies outside -128 to 255, the values its byte holds")]
	[DataRow(-200, 1, "Argument 1 of Cut= is out of range: -200 lies outside -128 to 255, the values its byte holds")]
	[DataRow(-32769, 2, "Argument 1 of Cut= is out of range: -32769 lies outside -32768 to 65535, the values its word holds")]
	[DataRow(65536, 2, "Argument 1 of Cut= is out of range: 65536 lies outside -32768 to 65535, the values its word holds")]
	public void ValidateWidth_ValueOutsideItsField_Fails(int value, int width, string expectedError)
	{
		// VB6 writes -200 as the byte 56, because its width check takes -256 to 255 (Module1.bas:4915-4937), and rejects 256
		// and 65536; the port fails the values VB6 wraps as well (PD-56).
		(ScriptArgumentResolver resolver, UnitRun unit) = CreateResolver();

		Assert.IsFalse(resolver.ValidateWidth(value, widthInBytes: width, command: "Cut=", argumentIndex: 0, location: unit.At(7)));
		DiagnosticAssert.SingleError(unit.Diagnostics, 7, expectedError);
	}
}
