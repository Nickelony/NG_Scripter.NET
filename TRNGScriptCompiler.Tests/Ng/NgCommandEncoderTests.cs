using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
[TestCategory("Fast")]
public sealed class NgCommandEncoderTests
{
	private const int Line = 7;

	private static readonly LanguageData s_language = UnitRun.Language(
		["Test Level", "Coastal Ruins"],
		[new ExtraNgString { Index = 31, Text = "Plugin_X" }]
	);

	private static NgArgumentValue Word(int value) => NgArgumentValue.ForInt32(NgArgumentType.Word, value);

	private static NgArgumentValue ArrayOf(NgArgumentType type, params int[] values)
	{
		return NgArgumentValue.ForArray(
			type,
			values.Select(value => NgArgumentValue.ForInt32(type == NgArgumentType.ArrayLong ? NgArgumentType.Long : NgArgumentType.Word, value))
		);
	}

	private static (List<short>? Words, UnitRun Unit) Compile(NgCommandDefinition definition, params NgArgumentValue[] arguments)
	{
		var unit = new UnitRun();
		return (new NgCommandEncoder(unit.Run.Diagnostics, s_language).Encode(definition, [.. arguments], unit.At(Line)), unit);
	}

	private static void AssertWords(short[] expected, NgCommandDefinition definition, params NgArgumentValue[] arguments)
	{
		(List<short>? words, UnitRun unit) = Compile(definition, arguments);

		Assert.IsNotNull(words);
		Assert.AreSequenceEqual(expected, words);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	private static void AssertFails(string expectedError, NgCommandDefinition definition, params NgArgumentValue[] arguments)
	{
		(List<short>? words, UnitRun unit) = Compile(definition, arguments);

		Assert.IsNull(words);
		DiagnosticAssert.SingleError(unit.Diagnostics, Line, expectedError);
	}

	[TestMethod]
	public void FirstWord_HoldsTheCodeAndTheWordCount()
	{
		// VB6 writes the code times 256 plus the count of the words that follow (Module1.bas:7369-7373), then each word value (:7578-7580).
		AssertWords([(2 * 256) + 1, 0x0102], UnitRun.Stock("Snow="), Word(0x0102));
		AssertWords([(26 * 256) + 4, 1, -1, 255, 0], UnitRun.Stock("ColorRGB="), Word(1), Word(-1), Word(255), Word(0));
	}

	[TestMethod]
	public void Long_WritesItsLowWordThenItsHighWord()
	{
		// VB6 splits a LONG into two words, the low one first (Module1.bas:7549-7576).
		AssertWords(
			[(1 * 256) + 3, 2, 0x5678, 0x1234],
			UnitRun.Stock("AssignSlot="),
			NgArgumentValue.ForInt32(NgArgumentType.ItemSlot, 2),
			NgArgumentValue.ForInt32(NgArgumentType.Long, 0x12345678)
		);
	}

	[TestMethod]
	public void String_WritesItsLanguageIndex()
	{
		// VB6 writes a STRING as the index GetIndiceStringa finds, the ExtraNG ones with bit 15 set, and * and IGNORE as -1 (Module1.bas:7518-7533,
		// 6555-6606).
		NgCommandDefinition windowTitle = UnitRun.Stock("WindowTitle=");

		AssertWords([(29 * 256) + 1, 1], windowTitle, NgArgumentValue.ForString("coastal ruins"));
		AssertWords([(29 * 256) + 1, -1], windowTitle, NgArgumentValue.ForString("*"));
		AssertWords([(29 * 256) + 1, unchecked((short)(0x8000 | 31))], windowTitle, NgArgumentValue.ForString("!31"));
		AssertWords([(29 * 256) + 1, unchecked((short)(0x8000 | 31))], windowTitle, NgArgumentValue.ForString("Plugin_X"));
	}

	[TestMethod]
	public void StringTheLanguageLacks_Fails()
	{
		// VB6 fails the command when its string does not resolve (Module1.bas:7529-7533).
		AssertFails(
			"Cannot find the string 'Lost Valley' in the first language file",
			UnitRun.Stock("WindowTitle="),
			NgArgumentValue.ForString("Lost Valley")
		);

		AssertFails("Cannot find the extra NG string with index: !32", UnitRun.Stock("WindowTitle="), NgArgumentValue.ForString("!32"));
	}

	[TestMethod]
	public void PluginName_MustBeAnExtraNgString()
	{
		// VB6 requires the name of Plugin= to resolve to an ExtraNG string (Module1.bas:7535-7540).
		NgArgumentValue[] declaration =
		[
			Word(1),
			NgArgumentValue.ForString("Plugin_X"),
			NgArgumentValue.ForInt32(NgArgumentType.Long, -1),
			ArrayOf(NgArgumentType.Array),
		];

		AssertWords([(43 * 256) + 4, 1, unchecked((short)(0x8000 | 31)), -1, -1], UnitRun.Stock("Plugin="), declaration);

		AssertFails(
			"The plugin name 'Test Level' must be an ExtraNG string",
			UnitRun.Stock("Plugin="),
			Word(1),
			NgArgumentValue.ForString("Test Level"),
			NgArgumentValue.ForInt32(NgArgumentType.Long, 0),
			ArrayOf(NgArgumentType.Array)
		);
	}

	[TestMethod]
	public void BoolCommand_WritesNoWord()
	{
		// VB6 turns a Bool command into a flag of its section and writes no command (Module1.bas:7112-7132, 8372-8383).
		AssertWords([], UnitRun.Stock("Diagnostic="), NgArgumentValue.ForBool(true));
	}

	[TestMethod]
	public void WordArray_WritesOneWordPerValue()
	{
		// VB6 writes each array value as a word (Module1.bas:7388-7401).
		AssertWords([(11 * 256) + 4, 1, 2, 3, -1], UnitRun.Stock("MirrorEffect="), Word(1), Word(2), ArrayOf(NgArgumentType.Array, 3, -1));
		AssertWords([(11 * 256) + 2, 1, 2], UnitRun.Stock("MirrorEffect="), Word(1), Word(2), ArrayOf(NgArgumentType.Array));
	}

	[TestMethod]
	public void ByteArray_PacksItsCountAndBytesTwoToAWord()
	{
		// VB6 writes the count byte, then the bytes, padded to an even count and packed low byte first (Module1.bas:7195-7196, 7237-7238,
		// 7277-7281, 7610-7615), and writes nothing for an empty array (:7182).
		var definition = new NgCommandDefinition
		{
			Name = "PluginBytes=",
			Code = (NgCommandCode)120,
			ArgumentTypes = [NgArgumentType.Word, NgArgumentType.ArrayByte],
		};

		AssertWords([(120 * 256) + 3, 1, 0x0103, 0x0302], definition, Word(1), ArrayOf(NgArgumentType.ArrayByte, 1, 2, 3));
		AssertWords([(120 * 256) + 3, 1, 0x0202, 0x0003], definition, Word(1), ArrayOf(NgArgumentType.ArrayByte, 2, 3));
		AssertWords([(120 * 256) + 1, 1], definition, Word(1), ArrayOf(NgArgumentType.ArrayByte));
	}

	[TestMethod]
	public void NybbleArray_PacksTwoNybblesToAByte_TheFirstLow()
	{
		// VB6 writes the nybble count, then each pair of nybbles as first + second * 16, padding an odd count with 0 (Module1.bas:7255-7281).
		AssertWords(
			[(17 * 256) + 5, 1, 2, 3, 0x0F03, 0x0007],
			UnitRun.Stock("TextureSequence="),
			Word(1),
			Word(2),
			Word(3),
			ArrayOf(NgArgumentType.ArrayNybble, 15, 0, 7)
		);

		AssertWords(
			[(17 * 256) + 4, 1, 2, 3, 0x2102],
			UnitRun.Stock("TextureSequence="),
			Word(1),
			Word(2),
			Word(3),
			ArrayOf(NgArgumentType.ArrayNybble, 1, 2)
		);
	}

	[TestMethod]
	public void LongArray_WritesTwoWordsPerValue()
	{
		// VB6 writes each ARRAYLONG value as a dword, low word first (Module1.bas:7393-7396, 7549-7576).
		AssertWords(
			[(21 * 256) + 9, 1, 0x2000, 0, 406, 0, 1, 0, 0x5678, 0x1234],
			UnitRun.Stock("TriggerGroup="),
			Word(1),
			NgArgumentValue.ForInt32(NgArgumentType.Long, 0x2000),
			NgArgumentValue.ForInt32(NgArgumentType.Long, 406),
			NgArgumentValue.ForInt32(NgArgumentType.Long, 1),
			ArrayOf(NgArgumentType.ArrayLong, 0x12345678)
		);
	}

	[TestMethod]
	public void FindStringIndex_ReportsNothing()
	{
		// The duplicate check keys a STRING argument on the index it resolves to (Module1.bas:7590-7604), so the lookup must not report.
		var unit = new UnitRun();
		var encoder = new NgCommandEncoder(unit.Run.Diagnostics, s_language);

		Assert.AreEqual(
			(1, 0x8000 | 31, -1),
			(encoder.FindStringIndex("Coastal Ruins"), encoder.FindStringIndex("!31"), encoder.FindStringIndex("Lost Valley"))
		);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}
}
