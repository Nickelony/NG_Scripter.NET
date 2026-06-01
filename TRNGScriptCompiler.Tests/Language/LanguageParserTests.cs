using System.Globalization;
using System.Text;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Language;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Language;

[TestClass]
public sealed class LanguageParserTests
{
	private static (LanguageParser Parser, CompilerLog Logger) CreateParser() => CreateParser(out _);

	private static (LanguageParser Parser, CompilerLog Logger) CreateParser(out DiagnosticSink diagnostics)
	{
		var logger = new CompilerLog(conciseLogging: false);
		diagnostics = new DiagnosticSink(logger);
		return (new LanguageParser(logger, diagnostics), logger);
	}

	private static string HeaderWithCommentError(string header) =>
		$"The section header '{header}' is followed by a comment, so the section would not start; move the comment to a line of its own";

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_EmptyContent_SucceedsWithNoStrings()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("Empty.txt", string.Empty);

		Assert.IsNotNull(data);
		Assert.IsEmpty(data.Strings);
		Assert.IsEmpty(data.ExtraStrings);
		Assert.AreEqual(0, data.TotalSectionStrings);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_TracksSeparateSectionCounts()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		const string content = "[Strings]\nClassic\n[PSXStrings]\nPsxOne\n[PCStrings]\nPcOne\n";

		LanguageData? data = parser.Parse("Multi.txt", content);

		Assert.IsNotNull(data);
		Assert.AreEqual(1, data.TotalSectionStrings);
		Assert.AreEqual(1, data.TotalPsxStrings);
		Assert.AreEqual(1, data.TotalPcStrings);
		Assert.HasCount(3, data.Strings);

		string[] expected = ["Classic", "PsxOne", "PcOne"];
		Assert.AreSequenceEqual(expected, data.Strings);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_EmptySection_KeepsZeroSize()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		// [Strings] is empty; the strings after it belong to [PSXStrings].
		LanguageData? data = parser.Parse("Empty1.txt", "[Strings]\n[PSXStrings]\nPsxOnly\n");

		Assert.IsNotNull(data);
		Assert.AreEqual(0, data.TotalSectionStrings);
		Assert.AreEqual(1, data.TotalPsxStrings);

		string[] expected = ["PsxOnly"];
		Assert.AreSequenceEqual(expected, data.Strings);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("   ", "The line '   ' counts as an empty string, which takes an index of its own")]
	[DataRow("NOTE:", "The line 'NOTE:' counts as an empty string, which takes an index of its own")]
	[DataRow("[Bracketed Note]", "The line '[Bracketed Note]' is not a section header, so it counts as a string")]
	[DataRow(
		"Note: text",
		"'Note:' at the start of 'Note: text' is removed from the text as a tag; if the text is meant to start with it, put a dummy tag in front, as in 'T:Note: text'"
	)]
	[DataRow(
		"12:30",
		"'12:' at the start of '12:30' is removed from the text as a tag; if the text is meant to start with it, put a dummy tag in front, as in 'T:12:30'"
	)]
	[DataRow(
		":Hello",
		"':' at the start of ':Hello' is removed from the text as a tag; if the text is meant to start with it, put a dummy tag in front, as in 'T::Hello'"
	)]
	public void LineThatReadsAsSomethingElse_Warns(string line, string expectedWarning)
	{
		// PD-59: VB6 counts a line of spaces and a tag alone as an empty string (Module1.bas:2012), a bracketed line other than a header as
		// a string (:2015-2018), and removes any leading word that ends in ':' as a tag (:2244-2263), all without a word.
		(LanguageParser? parser, CompilerLog? logger) = CreateParser(out DiagnosticSink diagnostics);

		LanguageData? data = parser.Parse("English.txt", $"[Strings]\nA\n{line}\nB\n");

		Assert.IsNotNull(data, logger.GetText());
		Assert.AreEqual(3, data.TotalSectionStrings);

		DiagnosticAssert.HasWarning(diagnostics.Recorded, 3, expectedWarning);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("NOTE: text")]
	[DataRow("T2_X: text")]
	[DataRow("Plain text")]
	public void UpperCaseTagOrPlainText_DoesNotWarn(string line)
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNotNull(parser.Parse("English.txt", $"[Strings]\n{line}\n"));
		DiagnosticAssert.NoDiagnostics(diagnostics.Recorded);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LookupKey_IsTheRawLineWithItsSpecialCharactersEscaped()
	{
		// VB6 keys a string by the raw line after StringaAbs_To_C (Module1.bas:1993, stored at :2055, compared at :6591-6596), so the
		// key keeps a comment and a trailing space, and a tab reads as \t.
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("English.txt", "[Strings]\nPistols ; note\nA\tB\nBig Room \n");

		Assert.IsNotNull(data);
		Assert.AreEqual(0, data.FindStringIndex("Pistols ; note"));
		Assert.AreEqual(-1, data.FindStringIndex("Pistols"));
		Assert.AreEqual(1, data.FindStringIndex("A\\tB"));
		Assert.AreEqual(2, data.FindStringIndex("Big Room "));
		Assert.AreEqual(-1, data.FindStringIndex("Big Room"));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ReorderedSections_FillTheTablesTheyNameAndWriteThreeSizes()
	{
		// VB6 numbers sections in the order they appear (Module1.bas:2015-2031) and writes one size per section seen
		// (:1792-1793), while its reader always takes three sizes (Mod_ScriptDat.bas:185-190). The port matches each section by
		// name and groups the strings by table, so "Pc" is a PC string at index 1.
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("English.txt", "[PCStrings]\nPc\n[Strings]\nClassic\n");

		Assert.IsNotNull(data);
		Assert.AreEqual(1, data.FindStringIndex("Pc"));
		Assert.AreEqual(0, data.FindStringIndex("Classic"));

		byte[] bytes = UnitRun.EncodeLanguage(data);

		// Counts (1,0,1), sizes (8,0,3), offsets 0 and 8; then "Classic" and "Pc", each XOR 0xA5 with a NUL terminator.
		byte[] header = [0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x08, 0x00];
		byte[] classic = [.. "Classic"u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00];
		byte[] pc = [.. "Pc"u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00];
		byte[] expected = [.. header, .. classic, .. pc];

		Assert.AreSequenceEqual(expected, bytes);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void RepeatedSectionHeader_Fails()
	{
		// VB6 numbers a second [Strings] as the next table (Module1.bas:2028-2031),
		// so after [Strings] and [PSXStrings] its strings land in the PC table.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", "[Strings]\nOne\n[PSXStrings]\n[Strings]\nTwo\n"));
		DiagnosticAssert.SingleError(diagnostics.Recorded, 4, "The section header [Strings] appears twice; merge the two sections into one");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_DuplicateStrings_AreBothStoredAndFirstWinsLookup()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("Dup.txt", "[Strings]\nSame\nOther\nSAME\n");

		Assert.IsNotNull(data);
		Assert.HasCount(3, data.Strings);
		Assert.AreEqual(0, data.FindStringIndex("same"), "VB6 returns the FIRST matching entry.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void Parse_Utf8BomFile_IsDecodedAndWarns()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		using TestWorkspace workspace = TestWorkspace
			.CreateEmpty()
			.WithTextFile("script/Levels.txt", "[Strings]\nLevel\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

		string path = workspace.PathOf("script/Levels.txt");

		LanguageData? data = parser.Parse(path, overlay: null);

		Assert.IsNotNull(data);

		string[] expected = ["Level"];
		Assert.AreSequenceEqual(expected, data.Strings, "The byte-order mark must be stripped before parsing.");

		DiagnosticAssert.HasWarning(
			diagnostics: diagnostics.Recorded,
			line: null,
			message: "'Levels.txt' has a UTF-8 byte-order mark; the file will be decoded as UTF-8",
			file: path
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ColonAtPositionZero_IsAnEmptyTagThatIsWrittenWithoutTheColon()
	{
		// VB6 removes a leading word that ends in ':' with no space before it, the empty one included (Module1.bas:2244-2263), and looks a
		// string up by its raw line (:2055, 6591-6596), so ":Hello" is written as "Hello" and found as ":Hello".
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("English.txt", "[Strings]\n:Hello\nHello\n");

		Assert.IsNotNull(data);
		Assert.AreEqual(0, data.FindStringIndex(":Hello"));
		Assert.AreEqual(1, data.FindStringIndex("Hello"));

		byte[] bytes = UnitRun.EncodeLanguage(data);

		// Header: counts (2,0,0), section sizes (12,0,0), offsets 0 and 6; then "Hello" twice, each XOR 0xA5 with a NUL terminator.
		byte[] header = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x00];
		byte[] hello = [.. "Hello"u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00];
		byte[] expected = [.. header, .. hello, .. hello];

		Assert.AreSequenceEqual(expected, bytes);
	}

	// ── ExtraNG index handling ──────────────────────────────────────────────

	private const string ExtraNgContent = "[Strings]\nLevel\n[ExtraNG]\n0: zero\n32767: max\nTHE LAST STRING ENTRY\n";

	[TestMethod]
	[TestCategory("Fast")]
	public void ResolveStringIndex_ExtraNgBounds_Accept0AndMaxRejectBeyond()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser();
		LanguageData? data = parser.Parse("Ng.txt", ExtraNgContent);

		Assert.IsNotNull(data);

		Assert.AreEqual(32768, data.ResolveStringIndex("!0"), "!0 maps to the ExtraNG base offset 32768.");
		Assert.AreEqual(65535, data.ResolveStringIndex("!32767"), "!32767 maps to the highest encodable index (32768 + 32767).");

		Assert.AreEqual(-1, data.ResolveStringIndex("!32768"), "32768 is beyond the ExtraNG index range.");
		Assert.AreEqual(-1, data.ResolveStringIndex("!65535"), "65535 is beyond the ExtraNG index range.");
		Assert.AreEqual(-1, data.ResolveStringIndex("!-1"));
		Assert.AreEqual(-1, data.ResolveStringIndex("!abc"));
		Assert.AreEqual(-1, data.ResolveStringIndex("!1"), "The index must actually exist in the table.");

		Assert.AreEqual(-1, data.ResolveStringIndex("!0", allowExtraNgText: false), "Classic lookups must reject ExtraNG references.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("40000")]
	[DataRow("-1")]
	[DataRow("99999999999")]
	public void ExtraNgIndexOutsideTheRange_Fails(string index)
	{
		// PD-58: VB6 reports the index and keeps the entry (Module1.bas:2119-2126), which it writes cut to 16 bits (:1849).
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("Ng.txt", $"[Strings]\nLevel\n[ExtraNG]\n{index}: text\nTHE LAST STRING ENTRY\n"));
		DiagnosticAssert.SingleError(diagnostics.Recorded, 4, $"The ExtraNG index {index} is outside 0-32767; use an index in that range");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TwoExtraNgTextsWithOneIndex_Fail()
	{
		// PD-58: VB6 writes both entries (Module1.bas:2133-2138), and every lookup finds the first.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("Ng.txt", "[Strings]\nLevel\n[ExtraNG]\n100: One\n100: Two\nTHE LAST STRING ENTRY\n"));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			5,
			"The ExtraNG index 100 holds two texts, 'One' and 'Two'; give each text an index of its own"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void IdenticalExtraNgRepeat_WarnsAndKeepsBothEntries()
	{
		// VB6 writes both entries of one index (Module1.bas:2133-2138), so the bytes keep the repeat.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		LanguageData? data = parser.Parse("Ng.txt", "[Strings]\nLevel\n[ExtraNG]\n100: One\n100: One\nTHE LAST STRING ENTRY\n");

		Assert.IsNotNull(data);

		Assert.HasCount(2, data.ExtraStrings);

		DiagnosticAssert.HasWarning(diagnostics.Recorded, 5, "The ExtraNG string 100: One is listed twice; remove the repeat");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("   ; note", "   ; note")]
	[DataRow("\t; note", "\\t; note")]
	[DataRow(" \t ; note", " \\t ; note")]
	public void IndentedCommentInAClassicSection_Fails(string line, string shown)
	{
		// PD-58: VB6's gate tests the first character of the line (Module1.bas:2012), after a tab became \t (:1993), so the line is a string
		// that moves every later index.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", $"[Strings]\nA\n{line}\nB\n"));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			3,
			$"The line '{shown}' is indented, so it counts as a string rather than a comment; remove the indent to make it a comment, or write a line of spaces for an empty string"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("[PCStrings] ", "[PCStrings] ")]
	[DataRow(" [PSXStrings]", " [PSXStrings]")]
	[DataRow("[ExtraNG]  ", "[ExtraNG]  ")]
	[DataRow("[PCStrings]\t", "[PCStrings]\\t")]
	[DataRow("\t[PSXStrings]", "\\t[PSXStrings]")]
	[DataRow(" \t[ExtraNG] \t", " \\t[ExtraNG] \\t")]
	public void SectionHeaderWithSpacesOrTabsAround_Fails(string header, string shown)
	{
		// PD-58: VB6 compares the line, with a tab as \t (Module1.bas:1993), with the header names (:2015-2018), so the line is a string
		// of the table above it.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", $"[Strings]\nA\n{header}\nB\n"));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			3,
			$"The section header '{shown}' has spaces or tabs around it, so it would count as a string of the section above it; remove them"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("[PSXStrings] ; psx")]
	[DataRow("[ExtraNG] ; ng")]
	[DataRow("[Strings]; note")]
	[DataRow("  [PCStrings]  ;")]
	public void SectionHeaderFollowedByAComment_Fails(string header)
	{
		// PD-58: VB6 compares the whole line with the header names (Module1.bas:2015-2018) and cuts the comment only to size the text
		// (:1997, 2188), so the line is a string and its section never starts.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", $"[Strings]\nA\n{header}\nB\n"));

		DiagnosticAssert.SingleError(diagnostics.Recorded, 3, HeaderWithCommentError(header));
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("[PSXStrings] ; psx")]
	[DataRow("[ExtraNG] ; ng")]
	public void SectionHeaderFollowedByACommentAboveTheFirstHeader_FailsWithTheMoveTheCommentError(string header)
	{
		// PD-58: VB6 drops the line without a word (Module1.bas:2049), and the section never starts.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", $"{header}\nA\n"));

		DiagnosticAssert.SingleError(diagnostics.Recorded, 1, HeaderWithCommentError(header));
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("[Strings]\nYen ¥ sign\n", 2, "'Yen ¥ sign' holds byte 0xA5 ('¥' in Windows-1252)")]
	[DataRow("[Strings]\nByte \\xA5 escaped\n", 2, "'Byte \\xA5 escaped' holds byte 0xA5 ('¥' in Windows-1252)")]
	[DataRow("[Strings]\nZero \\x00 escaped\n", 2, "'Zero \\x00 escaped' holds a zero byte")]
	[DataRow("[Strings]\nLevel\n[ExtraNG]\n7: Zero \\x00 escaped\nTHE LAST STRING ENTRY\n", 4, "'7: Zero \\x00 escaped' holds a zero byte")]
	public void CharacterThatEndsTheStringInTheGame_Fails(string content, int expectedLine, string expectedError)
	{
		// PD-58: the XOR with 0xA5 (Mod_ScriptDat.bas:246-288) turns byte 0xA5 into the zero that ends a string in the game, and an
		// escaped zero is that zero once the game decrypts it.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", content));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			expectedLine,
			expectedError + ", where the game would end the string; remove the character"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("boom", "boom is not a number; write decimal digits with an optional sign, or a $, &H or &O value")]
	[DataRow("12a", "12a is not a number; a decimal number holds digits only")]
	[DataRow("#82", "#82 is not a number here; write $82 for a hexadecimal value")]
	[DataRow("1+1", "1+1 is not a number; a decimal number holds digits only")]
	[DataRow("&HFFFF", "VB reads &HFFFF as -1; write $FFFF for 65535, or -1 for the value VB reads")]
	public void ExtraNgIndexThatIsNotANumber_Fails(string index, string reason)
	{
		// PD-58: VB6 fails an index IsNumeric rejects (Module1.bas:2109-2116), though it goes on to store Val's prefix of it (:2118).
		// PD-51: a language file has no #DEFINE scope, so a sum, a name and # fail, and so does an &H value VB reads as negative.
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("Ng.txt", $"[Strings]\nLevel\n[ExtraNG]\n12: Hello\n{index}: Bye\nTHE LAST STRING ENTRY\n"));
		DiagnosticAssert.SingleError(diagnostics.Recorded, 5, $"The ExtraNG index '{index}' is invalid: {reason}");
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("&H82", 130)]
	[DataRow("&h7FFE", 32766)]
	[DataRow("&O17", 15)]
	[DataRow("$82", 130)]
	[DataRow("0130", 130)]
	public void ExtraNgIndexInHexadecimalOrOctal_IsReadAsItsValue(string index, int expected)
	{
		// VB6 tests the index with IsNumeric and reads it with Val (Module1.bas:2109-2118), so &H82 is 130 and &O17 is 15. Val reads
		// $82 as 0, so the $ form is a port extension, as in a script.
		(LanguageParser? parser, CompilerLog? logger) = CreateParser();

		LanguageData? data = parser.Parse("Ng.txt", $"[Strings]\nLevel\n[ExtraNG]\n{index}: Bye\nTHE LAST STRING ENTRY\n");

		Assert.IsNotNull(data, logger.GetText());

		Assert.AreEqual(expected, data.ExtraStrings.Single().Index);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_ExtraNgFileReference_KeepsRawMarker()
	{
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("Ng.txt", "[Strings]\nLevel\n[ExtraNG]\n5: @notes.txt\nTHE LAST STRING ENTRY\n");

		Assert.IsNotNull(data);

		Assert.AreEqual("@notes.txt", data.ExtraStrings[0].Text, "The @ marker must be preserved for write-time file loading.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgSection_SkipsAnIndentedCommentAndKeepsTheTextRaw()
	{
		// VB6 reads [ExtraNG] lines raw and trims their spaces before the ';' test (Module1.bas:2083-2086), so an indented comment is
		// skipped, even one that holds a colon, and a tab stays a tab.
		(LanguageParser? parser, CompilerLog? logger) = CreateParser();

		LanguageData? data = parser.Parse("Ng.txt", "[Strings]\nLevel\n[ExtraNG]\n   ; 9: note\n0: a\tb\nTHE LAST STRING ENTRY\n");

		Assert.IsNotNull(data, logger.GetText());

		Assert.HasCount(1, data.ExtraStrings);
		Assert.AreEqual("a\tb", data.ExtraStrings[0].Text);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgSection_NothingAfterTheSentinelIsRead()
	{
		// VB6 leaves the ExtraNG loop at the sentinel and closes the file (Module1.bas:2090-2092), so a header after it starts nothing.
		(LanguageParser? parser, CompilerLog _) = CreateParser();

		LanguageData? data = parser.Parse("Ng.txt", "[Strings]\nLevel\n[ExtraNG]\n0: a\nTHE LAST STRING ENTRY\n[Strings]\nLate\n[PCStrings]\nPc\n");

		Assert.IsNotNull(data);

		string[] expected = ["Level"];
		Assert.AreSequenceEqual(expected, data.Strings);

		Assert.AreEqual(1, data.TotalSectionStrings, "[Strings] holds 'Level' alone.");
		Assert.AreEqual(0, data.TotalPcStrings);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgSection_HeaderBeforeTheSentinel_Fails()
	{
		// In VB6's ExtraNG loop a section header is a line without ':', which is fatal (Module1.bas:2095-2103).
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("Ng.txt", "[Strings]\nLevel\n[ExtraNG]\n0: a\n[PCStrings]\nPc\n"));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			5,
			"The section header [PCStrings] sits inside [ExtraNG], which must be the last section; move the section above [ExtraNG]"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void TextAboveTheFirstSectionHeader_Fails()
	{
		// VB6 drops a line above the first header without a word (Module1.bas:2049).
		(LanguageParser? parser, CompilerLog _) = CreateParser(out DiagnosticSink diagnostics);

		Assert.IsNull(parser.Parse("English.txt", "Hello\n[Strings]\nLevel\n"));

		DiagnosticAssert.SingleError(
			diagnostics.Recorded,
			1,
			"'Hello' sits above the first section header, so it belongs to no table; move it under a section header, or start it with ';' to make it a comment"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void BlankAndCommentLinesAboveTheFirstSectionHeader_AreLegal()
	{
		(LanguageParser? parser, CompilerLog? logger) = CreateParser();

		LanguageData? data = parser.Parse("English.txt", "; Strings\n   \n\t; indented\n[Strings]\nLevel\n");

		Assert.IsNotNull(data, logger.GetText());

		string[] expected = ["Level"];
		Assert.AreSequenceEqual(expected, data.Strings);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageLookup_UsesFirstMatchCaseInsensitively()
	{
		var run = new CompilationRun(conciseLogging: true);
		var parser = new LanguageParser(run.Logger, run.Diagnostics);

		LanguageData? data = parser.Parse("test.txt", "[Strings]\nDup\nOther\nDUP\n[ExtraNG]\n0: hello\nTHE LAST STRING ENTRY\n");

		Assert.IsNotNull(data, "Language file should parse successfully.");

		Assert.AreEqual(0, data.FindStringIndex("dup"), "VB6 returns the FIRST matching entry, case-insensitively.");
		Assert.AreEqual(32768, data.ResolveStringIndex("!0", allowExtraNgText: true));
		Assert.AreEqual(-1, data.ResolveStringIndex("!1", allowExtraNgText: true), "VB6 verifies the ExtraNG index exists.");
		Assert.AreEqual(-1, data.ResolveStringIndex("!0", allowExtraNgText: false), "Classic lookups may not use ExtraNG indexes.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageParser_UnknownBracketLine_IsOrdinaryStringContent()
	{
		var run = new CompilationRun(conciseLogging: true);
		var parser = new LanguageParser(run.Logger, run.Diagnostics);

		LanguageData? data = parser.Parse("test.txt", "[Strings]\nBefore\n[NotASection]\nAfter\n");

		Assert.IsNotNull(data, "Language file should parse successfully.");

		string[] expected = ["Before", "[NotASection]", "After"];

		Assert.AreSequenceEqual(
			expected,
			data.Strings,
			"VB6 recognizes only [Strings]/[PSXStrings]/[PCStrings]/[ExtraNG]; other bracket lines are string content."
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgSentinel_StopsSectionAndMalformedLineFails()
	{
		var run = new CompilationRun(conciseLogging: true);
		var parser = new LanguageParser(run.Logger, run.Diagnostics);

		LanguageData? data = parser.Parse("test.txt", "[Strings]\nLevel\n[ExtraNG]\n0: first\nTHE LAST STRING ENTRY\n1: ignored\n");

		Assert.IsNotNull(data, "Language file should parse successfully.");

		Assert.HasCount(1, data.ExtraStrings, "Everything after the sentinel is ignored.");
		Assert.AreEqual("first", data.ExtraStrings[0].Text);

		LanguageData? bad = parser.Parse("bad.txt", "[Strings]\nLevel\n[ExtraNG]\nmissing colon\n");

		Assert.IsNull(bad, "An ExtraNG line without ':' aborts the file, matching VB6.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void StringLookup_DoesNotDependOnTheWindowsLocale()
	{
		// PD-38: VB6 compares through the Windows locale (Option Compare Text, Module1.bas:4, 6592), where ß can equal ss; the
		// port compares character by character without regard to case, so under the German locale it finds Straße, not Strasse.
		CultureInfo previousCulture = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

			using var workspace = TestWorkspace.CreateCompilable(
				TestScripts.LanguageAndLevel + "Pickup= 1,Straße,13,14,15,16,17,18\n",
				"[Strings]\nTest Level\nStrasse\nStraße\n"
			);

			TestCompileResult result = workspace.Compile();

			Assert.IsTrue(result.Success, result.Log);
			byte[] pickupFields = [0xAB, 0x02, 0x00, 0x0D, 0x00, 0x0E, 0x00, 0x0F, 0x00, 0x10, 0x00, 0x11, 0x00, 0x12, 0x00];

			Assert.AreSequenceEqual(
				pickupFields,
				ScriptDatReader.Read(workspace.ReadScriptDat()).GetSectionBytes(0).Take(pickupFields.Length),
				"Pickup= names string 2, Straße."
			);
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
		}
	}
}
