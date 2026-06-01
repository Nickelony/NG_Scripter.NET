using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgArgumentParserTests
{
	private const int Line = 7;

	private sealed record Parsed(List<NgArgumentValue>? Arguments, UnitRun Unit);

	// A command of the constants file's grammar that no stock command has, such as a plugin command's IMPORT or ARRAYBYTE argument.
	private static NgCommandDefinition Custom(params NgArgumentType[] types)
	{
		return new()
		{
			Name = "PluginCommand=",
			Code = (NgCommandCode)250,
			ArgumentTypes = types,
		};
	}

	private static Parsed Parse(NgCommandDefinition definition, string arguments, UnitRun? unit = null)
	{
		unit ??= new UnitRun();

		var parser = new NgArgumentParser(unit.Run.Diagnostics, unit.ObjectsH, unit.Evaluator);

		List<string> split = arguments.Length == 0 ? [] : [.. arguments.Split(',').Select(argument => argument.Trim())];
		string text = $"{definition.Name} {arguments}";

		return new Parsed(
			parser.Parse(
				definition,
				new ScriptCommandLine(text, SourceText: text, Command: definition.Name, Arguments: split, Location: unit.At(Line))
			),

			unit
		);
	}

	private static List<NgArgumentValue> AssertParses(Parsed parsed)
	{
		Assert.IsNotNull(parsed.Arguments);
		DiagnosticAssert.NoErrorsOrWarnings(parsed.Unit.Diagnostics);
		return parsed.Arguments;
	}

	private static void AssertFails(Parsed parsed, string expectedError)
	{
		Assert.IsNull(parsed.Arguments);
		DiagnosticAssert.SingleError(parsed.Unit.Diagnostics, Line, expectedError);
	}

	private static int[] Values(IEnumerable<NgArgumentValue> arguments) => [.. arguments.Select(argument => argument.Int32Value)];

	private static int[] Elements(NgArgumentValue array)
	{
		Assert.IsNotNull(array.ArrayValue, $"The {array.Type} argument holds no array.");
		return Values(array.ArrayValue);
	}

	[TestMethod]
	[DataRow("ENABLED", true)]
	[DataRow("disabled", false)]
	public void Bool_ReadsEnabledOrDisabled(string argument, bool expected)
	{
		// VB6 reads the one argument of a Bool command as ENABLED or DISABLED, without regard to case (Module1.bas:7112-7132, Option Compare
		// Text at :4).
		NgArgumentValue value = AssertParses(Parse(UnitRun.Stock("Diagnostic="), argument)).Single();
		Assert.AreEqual((NgArgumentType.Bool, expected), (value.Type, value.BoolValue));
	}

	[TestMethod]
	[DataRow("ON", "Argument 1 of Diagnostic= is invalid: ON is neither ENABLED nor DISABLED")]
	[DataRow("ENABLED,1", "Wrong number of arguments for 'Diagnostic=': expected 1, got 2")]
	public void Bool_WithAnotherArgument_Fails(string arguments, string expectedError)
	{
		// VB6 fails a Bool argument other than ENABLED or DISABLED (Module1.bas:7135-7138) and a wrong count (:7071-7099).
		AssertFails(Parse(UnitRun.Stock("Diagnostic="), arguments), expectedError);
	}

	[TestMethod]
	[DataRow("$10+CL_RED", 19)]
	[DataRow("IGNORE", -1)]
	[DataRow("_", -1)]
	[DataRow("-32768", -32768)]
	[DataRow("65535", 65535)]
	public void Word_ReadsANumericExpression(string argument, int expected)
	{
		// VB6 reads a word with GetArgNumerico two bytes wide (Module1.bas:7442), and IGNORE is the constant -1;
		// the port reads _ as -1 too (PD-97).
		NgArgumentValue value = AssertParses(Parse(UnitRun.Stock("Snow="), argument)).Single();
		Assert.AreEqual((NgArgumentType.Word, expected), (value.Type, value.Int32Value));
	}

	[TestMethod]
	[DataRow("65536", "Argument 1 of Snow= is out of range: 65536 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("-32769", "Argument 1 of Snow= is out of range: -32769 lies outside -32768 to 65535, the values its word holds")]
	[DataRow("1,2", "Wrong number of arguments for 'Snow=': expected 1, got 2")]
	public void Word_ThatDoesNotReadAsAWord_Fails(string arguments, string expectedError)
	{
		// VB6's width check takes -65536 to 65535 (Module1.bas:4915-4937),
		// which the port narrows to the signed and the unsigned reading of the word (PD-56).
		AssertFails(Parse(UnitRun.Stock("Snow="), arguments), expectedError);
	}

	[TestMethod]
	public void IntegerAndImport_ReadAsAWord()
	{
		// VB6 writes an INTEGER and an IMPORT argument as one word through GetArgNumerico (Module1.bas:7414-7422, 7442).
		List<NgArgumentValue> values = AssertParses(Parse(Custom(NgArgumentType.Integer, NgArgumentType.Import), "-5, IGNORE"));

		Assert.AreSequenceEqual([NgArgumentType.Integer, NgArgumentType.Import], values.Select(value => value.Type));
		Assert.AreSequenceEqual(new[] { -5, -1 }, Values(values));

		AssertFails(
			Parse(Custom(NgArgumentType.Import), "70000"),
			"Argument 1 of PluginCommand= is out of range: 70000 lies outside -32768 to 65535, the values its word holds"
		);
	}

	[TestMethod]
	public void Long_TakesAnyThirtyTwoBitValue()
	{
		// VB6 reads a LONG four bytes wide, with no width check (Module1.bas:7405-7407, 4915).
		List<NgArgumentValue> values = AssertParses(Parse(UnitRun.Stock("AssignSlot="), "SLOT_TEST, $FFFFFFFF"));

		Assert.AreSequenceEqual([NgArgumentType.ItemSlot, NgArgumentType.Long], values.Select(value => value.Type));
		Assert.AreSequenceEqual(new[] { 2, -1 }, Values(values));
	}

	[TestMethod]
	[DataRow("Coastal Ruins", "Coastal Ruins")]
	[DataRow("*", "*")]
	[DataRow("IGNORE", "*")]
	[DataRow("_", "_")]
	[DataRow("!$1F", "!31")]
	[DataRow("!CL_RED+1", "!4")]
	public void String_KeepsItsReference(string argument, string expected)
	{
		// VB6 reads * and IGNORE as -1 and looks any other text up, _ included (Module1.bas:7518-7533),
		// and reads the index of a !NNN reference with GetArgNumerico (:6535-6540).
		NgArgumentValue value = AssertParses(Parse(UnitRun.Stock("WindowTitle="), argument)).Single();
		Assert.AreEqual((NgArgumentType.String, expected), (value.Type, value.StringValue));
	}

	[TestMethod]
	public void String_ExtraNgIndexThatDoesNotRead_Fails()
	{
		// VB6 fails a !NNN index that GetArgNumerico cannot read (Module1.bas:6540-6546).
		AssertFails(
			Parse(UnitRun.Stock("WindowTitle="), "!FIRST"),
			"Argument 1 of WindowTitle= is invalid: FIRST is neither a number nor a known name"
		);
	}

	[TestMethod]
	[DataRow("SLOT_TEST", 2)]
	[DataRow("lara", 0)]
	[DataRow("1", 1)]
	[DataRow("$2", 2)]
	[DataRow("PISTOLS_ANIM+1", 2)]
	[DataRow("MY_SLOT", 1)]
	public void Slot_ReadsASlotNameANumberOrADefine(string argument, int expected)
	{
		// VB6 takes a number or a $ literal (Module1.bas:7432-7436) or a slot name (:7491-7516);
		// the port also takes a #DEFINE and sums of them (PD-60).
		var unit = new UnitRun();
		unit.Define(unit.ScriptFile, "#DEFINE MY_SLOT 1");

		NgArgumentValue value = AssertParses(Parse(UnitRun.Stock("Equipment="), argument + ", 1", unit))[0];

		Assert.AreEqual((NgArgumentType.ItemSlot, expected), (value.Type, value.Int32Value));
	}

	[TestMethod]
	[DataRow(
		"CL_RED",
		"Argument 1 of Equipment= names CL_RED, which is a TRNG constant, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them"
	)]
	[DataRow(
		"STATIC_TEST",
		"Argument 1 of Equipment= names STATIC_TEST, which is a static, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them"
	)]
	[DataRow("#2", "Argument 1 of Equipment= writes #2, a hexadecimal form a slot does not take; write $2")]
	[DataRow("3", "Argument 1 of Equipment= names slot 3, but Objects.h defines slots 0 to 2")]
	[DataRow("IGNORE", "Argument 1 of Equipment= names slot -1, but Objects.h defines slots 0 to 2")]
	[DataRow("_", "Argument 1 of Equipment= names slot -1, but Objects.h defines slots 0 to 2")]
	[DataRow("NO_SLOT", "Argument 1 of Equipment= is invalid: NO_SLOT is neither a number nor a known name")]
	public void Slot_OfAnotherFormOrOutsideTheSlots_Fails(string argument, string expectedError)
	{
		// VB6 looks a name up among the slots alone (Module1.bas:7491-7516) and leaves a number unchecked (:7432-7436);
		// the port fails another form (PD-60) and a slot Objects.h does not define (PD-56).
		AssertFails(Parse(UnitRun.Stock("Equipment="), argument + ", 1"), expectedError);
	}

	[TestMethod]
	[DataRow("Snow=", "IGNORE", 6, 6)]
	[DataRow("Snow=", "ignore", 6, 6)]
	[DataRow("AssignSlot=", "SLOT_TEST, IGNORE", 23, 6)]
	public void Ignore_InANumericArgument_HintsAtTheDiscard(string command, string arguments, int column, int length)
	{
		// The port reads IGNORE as VB6 does and reads _ as the same -1 (PD-97), so IGNORE earns a hint that marks the argument.
		Parsed parsed = Parse(UnitRun.Stock(command), arguments);

		AssertParses(parsed);

		CompilerDiagnostic hint = DiagnosticAssert.HasHint(
			diagnostics: parsed.Unit.Diagnostics,
			Line,
			message: "Replace IGNORE with the _ discard",
			file: parsed.Unit.ScriptFile
		);

		Assert.AreEqual((column, length), (hint.Column, hint.Length));
		Assert.HasCount(1, parsed.Unit.Diagnostics);
	}

	[TestMethod]
	public void Ignore_InAnArrayValue_HintsOnThatValue()
	{
		Parsed parsed = Parse(UnitRun.Stock("Enemy="), "SLOT_TEST, 1, 2, 3, 4, 5, IGNORE");

		List<NgArgumentValue> values = AssertParses(parsed);

		CompilerDiagnostic hint = DiagnosticAssert.HasHint(
			diagnostics: parsed.Unit.Diagnostics,
			Line,
			message: "Replace IGNORE with the _ discard",
			file: parsed.Unit.ScriptFile
		);

		Assert.AreSequenceEqual(new[] { 5, -1 }, Elements(values[5]));
		Assert.AreEqual((33, 6), (hint.Column, hint.Length));
		Assert.HasCount(1, parsed.Unit.Diagnostics);
	}

	[TestMethod]
	public void Underscore_InANumericArgument_HintsAtNothing()
	{
		Parsed parsed = Parse(UnitRun.Stock("Enemy="), "SLOT_TEST, _, 2, 3, 4, 5, _");

		List<NgArgumentValue> values = AssertParses(parsed);

		Assert.AreEqual(-1, values[1].Int32Value);
		Assert.AreSequenceEqual(new[] { 5, -1 }, Elements(values[5]));

		DiagnosticAssert.NoDiagnostics(parsed.Unit.Diagnostics);
	}

	[TestMethod]
	public void Ignore_InAStringArgument_HintsAtNothing()
	{
		// A STRING argument reads IGNORE as *, and _ is text there, so the hint has no better spelling to give.
		Parsed parsed = Parse(UnitRun.Stock("WindowTitle="), "IGNORE");

		Assert.AreEqual("*", AssertParses(parsed).Single().StringValue);
		DiagnosticAssert.NoDiagnostics(parsed.Unit.Diagnostics);
	}

	[TestMethod]
	public void Ignore_InAnArgumentThatFailsItsRange_HintsAtNothing()
	{
		// The slot -1 fails the Objects.h range, and _ would fail it too, so the error stands alone.
		Parsed parsed = Parse(UnitRun.Stock("Equipment="), "IGNORE, 1");

		AssertFails(parsed, "Argument 1 of Equipment= names slot -1, but Objects.h defines slots 0 to 2");
		Assert.IsFalse(parsed.Unit.Diagnostics.Any(diagnostic => diagnostic.IsHint));
	}

	[TestMethod]
	public void Slot_WithoutObjectsH_TakesTheRangeOfAWord()
	{
		// Without Objects.h the slot count is unknown, so a slot is checked against the word that holds it (PD-56).
		AssertParses(Parse(UnitRun.Stock("Equipment="), "65535, 1", new UnitRun(objectsH: null)));

		AssertFails(
			Parse(UnitRun.Stock("Equipment="), "65536, 1", new UnitRun(objectsH: null)),
			"Argument 1 of Equipment= names slot 65536, but a slot lies between 0 and 65535"
		);
	}

	[TestMethod]
	public void TestPositionSlotField_TakesTheRangeOfAWord()
	{
		// With TPOS_TEST_ITEM_INDEX the slot field of TestPosition= holds an item index, which VB6 reads as a number (Module1.bas:7432-7436).
		List<NgArgumentValue> values = AssertParses(Parse(UnitRun.Stock("TestPosition="), "1, 0, 400, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0"));
		Assert.AreEqual(400, values[2].Int32Value);
	}

	[TestMethod]
	public void Array_TakesTheValuesAfterTheFixedArgumentsAsWords()
	{
		// VB6 reads each value from the array's position on as a word (Module1.bas:7388-7401), and none is required (:7097).
		List<NgArgumentValue> values = AssertParses(Parse(UnitRun.Stock("Enemy="), "SLOT_TEST, 1, 2, 3, 4, 5, IGNORE, $FFFF"));
		List<NgArgumentValue> empty = AssertParses(Parse(UnitRun.Stock("Enemy="), "SLOT_TEST, 1, 2, 3, 4"));

		Assert.HasCount(6, values);
		Assert.AreEqual(NgArgumentType.Array, values[5].Type);

		Assert.AreSequenceEqual(new[] { 5, -1, 65535 }, Elements(values[5]));
		Assert.AreEqual(0, empty[5].ArrayValue?.Count);
	}

	[TestMethod]
	[DataRow("SLOT_TEST, 1, 2", "Wrong number of arguments for 'Enemy=': expected at least 5, got 3")]
	[DataRow("SLOT_TEST, 1, 2, 3, 4, 70000", "Argument 6 of Enemy= is out of range: 70000 lies outside -32768 to 65535, the values its word holds")]
	public void Array_ThatDoesNotRead_Fails(string arguments, string expectedError)
	{
		// VB6 fails a line that stops before the array (Module1.bas:7095-7098) and width-checks each array word (:7442) (PD-56).
		AssertFails(Parse(UnitRun.Stock("Enemy="), arguments), expectedError);
	}

	[TestMethod]
	public void ByteAndNybbleArrays_TakeTheValuesTheirFieldHolds()
	{
		// VB6 takes 0 to 255 for an array byte and 0 to 15 for an array nybble (Module1.bas:7230-7246).
		List<NgArgumentValue> bytes = AssertParses(Parse(Custom(NgArgumentType.Word, NgArgumentType.ArrayByte), "1, 0, 255"));
		List<NgArgumentValue> nybbles = AssertParses(Parse(UnitRun.Stock("TextureSequence="), "1, 2, 3, 15, 0, 7"));

		Assert.AreEqual(NgArgumentType.ArrayByte, bytes[1].Type);
		Assert.AreSequenceEqual(new[] { 0, 255 }, Elements(bytes[1]));
		Assert.AreEqual(NgArgumentType.ArrayNybble, nybbles[3].Type);
		Assert.AreSequenceEqual(new[] { 15, 0, 7 }, Elements(nybbles[3]));
	}

	[TestMethod]
	[DataRow("1, 2, 3, 16", "Argument 4 of TextureSequence= is out of range: 16 lies outside 0 to 15, the values an array nybble holds")]
	[DataRow("1, 2, 3, -1", "Argument 4 of TextureSequence= is out of range: -1 lies outside 0 to 15, the values an array nybble holds")]
	public void NybbleOutsideItsFourBits_Fails(string arguments, string expectedError)
	{
		// VB6 fails a nybble outside 0 to 15 (Module1.bas:7241-7246).
		AssertFails(Parse(UnitRun.Stock("TextureSequence="), arguments), expectedError);
	}

	[TestMethod]
	public void ByteArrayOutsideItsByte_Fails()
	{
		// VB6 fails an array byte outside 0 to 255 (Module1.bas:7230-7235).
		AssertFails(
			Parse(Custom(NgArgumentType.Word, NgArgumentType.ArrayByte), "1, 256"),
			"Argument 2 of PluginCommand= is out of range: 256 lies outside 0 to 255, the values an array byte holds"
		);
	}

	[TestMethod]
	public void NybbleArrayOf256Values_Fails()
	{
		// The count of a byte or nybble array is one byte, which overflows at 256 values (Module1.bas:7195, 7257).
		AssertFails(
			Parse(UnitRun.Stock("TextureSequence="), "1, 2, 3, " + string.Join(", ", Enumerable.Repeat("1", 256))),
			"TextureSequence= gives 256 array values, but the array holds at most 255, because its count is one byte"
		);
	}

	[TestMethod]
	public void LongArray_TakesItsValuesAsLongs()
	{
		// VB6 reads each value of an ARRAYLONG as a dword (Module1.bas:7393-7396).
		List<NgArgumentValue> values = AssertParses(Parse(UnitRun.Stock("TriggerGroup="), "1, $2000, 406, 1, $72000, 407, 2"));

		Assert.AreSequenceEqual(new[] { 1, 0x2000, 406, 1 }, Values(values.Take(4)));
		Assert.AreEqual(NgArgumentType.ArrayLong, values[4].Type);
		Assert.AreSequenceEqual(new[] { 0x72000, 407, 2 }, Elements(values[4]));
	}

	[TestMethod]
	public void GroupedArrayEndingInAPartialGroup_Warns()
	{
		// Neither compiler checks the length of a grouped array, so a partial group reaches the engine (Module1.bas:7154-7172) (PD-57).
		Parsed parsed = Parse(UnitRun.Stock("TriggerGroup="), "1, $2000, 406, 1, $2000");

		Assert.IsNotNull(parsed.Arguments);

		DiagnosticAssert.HasWarning(
			parsed.Unit.Diagnostics,
			Line,
			"TriggerGroup= ends in a partial group of 1 value; each exported trigger takes 3 values"
		);
	}

	[TestMethod]
	public void GlobalTriggerOfSixArguments_GainsMinusOneAsItsSeventh()
	{
		// VB6 adds -1 for the missing PerformFalse argument of a GlobalTrigger= that gives six (Module1.bas:7082-7086).
		List<NgArgumentValue> values = AssertParses(Parse(UnitRun.Stock("GlobalTrigger="), "1, 0, 2, 3, 4, 5"));
		Assert.AreSequenceEqual(new[] { 1, 0, 2, 3, 4, 5, -1 }, Values(values));
	}

	[TestMethod]
	public void GlobalTriggerOfFiveArguments_Fails()
	{
		AssertFails(
			Parse(UnitRun.Stock("GlobalTrigger="), "1, 0, 2, 3, 4"),
			"Wrong number of arguments for 'GlobalTrigger=': expected 6 or 7, got 5"
		);
	}

	[TestMethod]
	public void PluginConstant_PutsItsPluginIdInTheHighWord_OfTheArgumentsVb6Marks()
	{
		// VB6 ORs the plugin ID into the high word of the first argument of Customize= and Parameters= and the second of AssignSlot=
		// (Module1.bas:7440-7456), and of no other argument.
		var unit = new UnitRun(
			pluginScripts: [new PluginScript("Plugin_X", "Plugin_X.script", ["<START_CONSTANTS>", "PLUGIN_X_CONST:$40", "<END>"])]
		);

		unit.Plugins.Declare(UnitRun.PluginLine(id: "2", name: "Plugin_X", location: unit.At(2)));

		Assert.AreEqual(0x0002_0040, AssertParses(Parse(UnitRun.Stock("Customize="), "PLUGIN_X_CONST, 1", unit))[0].Int32Value);
		Assert.AreEqual(0x0002_0040, AssertParses(Parse(UnitRun.Stock("Parameters="), "PLUGIN_X_CONST, 1", unit))[0].Int32Value);
		Assert.AreEqual(0x0002_0040, AssertParses(Parse(UnitRun.Stock("AssignSlot="), "LARA, PLUGIN_X_CONST", unit))[1].Int32Value);

		Assert.AreSequenceEqual(
			new[] { 0x40, 0x40 },
			Elements(AssertParses(Parse(UnitRun.Stock("Customize="), "1, PLUGIN_X_CONST, PLUGIN_X_CONST", unit))[1])
		);
	}
}
