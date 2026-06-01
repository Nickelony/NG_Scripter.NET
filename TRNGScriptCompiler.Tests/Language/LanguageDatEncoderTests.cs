using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Language;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Language;

[TestClass]
public sealed class LanguageDatEncoderTests
{
	private const string NotesAtFile = "[Strings]\nLevel\n[ExtraNG]\n5: @notes.txt\nTHE LAST STRING ENTRY\n";

	private static LanguageData Parse(string content, string file = "English.txt")
	{
		var logger = new CompilerLog(conciseLogging: false);
		var parser = new LanguageParser(logger, new DiagnosticSink(logger));

		LanguageData? data = parser.Parse(file, content);

		Assert.IsNotNull(data, logger.GetText());
		return data;
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void EncryptString_XorsEachWindows1252ByteWithA5()
	{
		// VB6 XORs each byte of a string with 0xA5 (Mod_ScriptDat.bas:246-288), and '\u00E9' is byte 0xE9 in Windows-1252.
		Assert.AreSequenceEqual(new byte[] { 0xE6, 0xC4, 0xC3, 0x4C }, LanguageDatEncoder.EncryptString("Caf\u00E9"));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_EmptyFile_WritesThreeCountsAndThreeSectionSizes()
	{
		// The game reads three counts and three section sizes (Mod_ScriptDat.bas:178-190), whatever sections the file holds.
		byte[] bytes = UnitRun.EncodeLanguage(Parse(string.Empty));
		Assert.AreSequenceEqual(new byte[12], bytes);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_SizesAndOffsets_CountTheDecodedBytesOfEachTable()
	{
		// VB6 counts each string's decoded bytes plus its zero into the offset and its table's size (Module1.bas:2059-2063), and writes the
		// counts, the three sizes and the offsets as words before the strings (:1784-1798). 'A\x42' decodes to 'AB'.
		byte[] bytes = UnitRun.EncodeLanguage(Parse("[PSXStrings]\nD\n[Strings]\nA\\x42\nC\n"));

		byte[] expected =
		[
			0x02,
			0x00,
			0x01,
			0x00,
			0x00,
			0x00, // counts: [Strings] 2, [PSXStrings] 1, [PCStrings] 0
			0x05,
			0x00,
			0x02,
			0x00,
			0x00,
			0x00, // sizes: 'AB' and 'C' take 5 bytes, 'D' 2, no PC string 0
			0x00,
			0x00,
			0x03,
			0x00,
			0x05,
			0x00, // offsets of 'AB', 'C' and 'D'
			0x41 ^ 0xA5,
			0x42 ^ 0xA5,
			0x00,
			0x43 ^ 0xA5,
			0x00,
			0x44 ^ 0xA5,
			0x00,
		];

		Assert.AreSequenceEqual(expected, bytes);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LineOfSpacesAndTagOnlyLine_AreEmptyStrings()
	{
		// A line of spaces and "NOTE:" pass VB6's gate (Module1.bas:2012); the trim (:2182) and the tag removal (:2244-2263) leave
		// nothing, so each is sized as its terminating zero alone (:2060) and written as that zero.
		byte[] bytes = UnitRun.EncodeLanguage(Parse("[Strings]\nA\n   \nNOTE:\nB\n"));

		// Counts (4,0,0), sizes (6,0,0), offsets 0, 2, 3 and 4; then 'A' XOR 0xA5, two empty strings and 'B' XOR 0xA5.
		byte[] expected =
		[
			0x04,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x06,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x02,
			0x00,
			0x03,
			0x00,
			0x04,
			0x00,
			0xE4,
			0x00,
			0x00,
			0x00,
			0xE7,
			0x00,
		];

		Assert.AreSequenceEqual(expected, bytes);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ClassicStringWithACommentAndItsSpace_IsWrittenAsItIsSized()
	{
		// PD-48: VB6 sizes a string from its trimmed text (Module1.bas:2002, 2060) but trims the text it writes only when the line
		// holds a colon (:2246-2266), so it writes "Pistols " into 8 bytes and the offset of Shotgun points one byte early.
		byte[] bytes = UnitRun.EncodeLanguage(Parse("[Strings]\nPistols ; note\nShotgun\n"));

		// Counts (2,0,0), sizes (16,0,0), offsets 0 and 8; then "Pistols" and "Shotgun", each XOR 0xA5 with a NUL terminator.
		byte[] header = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00];
		byte[] pistols = [.. "Pistols"u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00];
		byte[] shotgun = [.. "Shotgun"u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00];
		byte[] expected = [.. header, .. pistols, .. shotgun];

		Assert.AreSequenceEqual(expected, bytes);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Parse_NonAnsiCharacters_RoundTripThroughTheEncoder()
	{
		byte[] bytes = UnitRun.EncodeLanguage(Parse("[Strings]\nCaf\u00E9\n"));

		// Header: counts (1,0,0), section sizes (5,0,0) with 5 for "Café" = 4 bytes + NUL, offset 0.
		// Payload: each Windows-1252 byte XOR 0xA5, then a NUL terminator.
		byte[] expected = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE6, 0xC4, 0xC3, 0x4C, 0x00];

		Assert.AreSequenceEqual(expected, bytes, "'Café' must encode as 1252 bytes (0x43,0x61,0x66,0xE9) XOR 0xA5 with a NUL terminator.");
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void HighEscapesAndUndefinedBytes_RoundTripAsTheirWindows1252Bytes()
	{
		// VB6 decodes \xNN with Chr() through the ANSI code page (Module1.bas:2222), so \x93 and \x94 are the typographic
		// quotes that write back as 0x93 and 0x94. Raw bytes without a Windows-1252 character read and write back unchanged.
		byte[] undefinedBytes = [0x81, 0x8D, 0x8F, 0x90, 0x9D];
		string content = "[Strings]\n\\x93Hi\\x94\n" + Vb6Ansi.GetString(undefinedBytes) + "\n";

		byte[] bytes = UnitRun.EncodeLanguage(Parse(content));

		// Header: counts (2,0,0), section sizes (11,0,0), offsets 0 and 5; then each string XOR 0xA5 with a NUL terminator.
		byte[] header = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00];
		byte[] strings = [0x93, (byte)'H', (byte)'i', 0x94, 0x00, .. undefinedBytes, 0x00];
		byte[] expected = [.. header, .. strings.Select(value => value == 0 ? value : (byte)(value ^ 0xA5))];

		Assert.AreSequenceEqual(expected, bytes);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ClassicStringsPastTheSixteenBitOffsets_Fail()
	{
		// PD-58: the section sizes and string offsets are 16-bit words (Module1.bas:1784-1798), which VB6 cuts without a word. Two
		// strings of 40,000 bytes put the second offset at 40,001 and the [Strings] size at 80,002.
		string text = new('A', 40000);

		UnitRun.EncodeLanguage(Parse($"[Strings]\n{text}\n"));

		UnitRun.LanguageEncoding twice = UnitRun.RunLanguageEncoder(Parse($"[Strings]\n{text}\n{text}\n"));

		Assert.IsFalse(twice.Success, twice.Log);

		DiagnosticAssert.SingleError(
			twice.Diagnostics,
			line: null,
			"The classic strings take 80002 bytes, so a section size or a string offset passes the 65,535 a language .dat can hold; shorten or remove strings"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgTextOfSixtyFiveThousandFiveHundredThirtySixWords_Fails()
	{
		// PD-58: the word count of a string is a 16-bit value (Module1.bas:1879), which VB6 cuts without a word. Each text byte pair is a
		// word and a closing zero byte takes one more, so 131,068 bytes take 65,535 words and 131,070 take 65,536.
		const int LongestTextBytes = 131068;
		LanguageData data = Parse(NotesAtFile, "Ng.txt");

		Assert.IsTrue(UnitRun.RunLanguageEncoder(data, (_, _) => new string('A', LongestTextBytes)).Success);

		UnitRun.LanguageEncoding encoding = UnitRun.RunLanguageEncoder(data, (_, _) => new string('A', LongestTextBytes + 2));

		Assert.IsFalse(encoding.Success, encoding.Log);

		DiagnosticAssert.SingleError(
			diagnostics: encoding.Diagnostics,
			line: 4,
			message: "ExtraNG string 5 takes 65536 words, so its length passes the 65,535 a string can hold; shorten it or split it into two strings",
			file: "Ng.txt"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void SemicolonInAnAtFileText_Fails()
	{
		// PD-58: the @file text runs through the comment strip (Module1.bas:1874), which drops everything from its first ';'.
		UnitRun.LanguageEncoding encoding = UnitRun.RunLanguageEncoder(Parse(NotesAtFile, "Ng.txt"), (_, _) => "One; two");

		Assert.IsFalse(encoding.Success, encoding.Log);

		DiagnosticAssert.SingleError(
			diagnostics: encoding.Diagnostics,
			line: 4,
			message: "The file 'notes.txt' of ExtraNG string 5 holds a ';', where its text would end; write \\x3B in its place",
			file: "Ng.txt"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void BadEscapeInAnExtraNgText_Fails()
	{
		// PD-50: VB6 reports the escape and writes the text undecoded (Module1.bas:1874).
		LanguageData data = Parse("[Strings]\nLevel\n[ExtraNG]\n5: Bad \\xZZ\nTHE LAST STRING ENTRY\n", "Ng.txt");

		UnitRun.LanguageEncoding encoding = UnitRun.RunLanguageEncoder(data);

		Assert.IsFalse(encoding.Success, encoding.Log);

		DiagnosticAssert.SingleError(
			diagnostics: encoding.Diagnostics,
			line: 4,
			message: "ExtraNG string 5 holds an invalid hexadecimal escape; follow each \\x with two hexadecimal digits, such as \\x41",
			file: "Ng.txt"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ByteA5InAnAtFileText_Fails()
	{
		UnitRun.LanguageEncoding encoding = UnitRun.RunLanguageEncoder(Parse(NotesAtFile, "Ng.txt"), (_, _) => "Yen ¥");

		Assert.IsFalse(encoding.Success, encoding.Log);

		DiagnosticAssert.SingleError(
			diagnostics: encoding.Diagnostics,
			line: 4,
			message: "'@notes.txt' holds byte 0xA5 ('¥' in Windows-1252), where the game would end the string; remove the character",
			file: "Ng.txt"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_ExtraNgFileReference_LoadsAndObfuscatesContent()
	{
		byte[] bytes = UnitRun.EncodeLanguage(Parse(NotesAtFile, "Ng.txt"), (_, _) => "line1\r\nline2");

		// Header words: count 1, index 5, word count 6 (the 11-byte text is NUL-padded to 12 bytes).
		byte[] headerWords = [0x0A, 0x80, 0x01, 0x00, 0x05, 0x00, 0x06, 0x00];

		Assert.IsTrue(
			ByteSearch.Contains(bytes, headerWords),
			"The ExtraNG table must start with the 0x800A tag and the count/index/wordcount words."
		);

		// "line1\nline2" (CRLF normalized) XOR 0xA5 per byte, packed little-endian into words.
		byte[] payload = [0xC9, 0xCC, 0xCB, 0xC0, 0x94, 0xAF, 0xC9, 0xCC, 0xCB, 0xC0, 0x97, 0x00];

		Assert.IsTrue(
			ByteSearch.Contains(bytes, payload),
			"The loaded file content must be obfuscated with the ExtraNG XOR and NUL-padded to whole words."
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_AtFileText_TrimsOnlyTheSpacesAtTheEndsOfTheWholeText()
	{
		// VB6 turns CRLF into LF (Module1.bas:1865-1867) and trims the spaces at the two ends of the whole text (:2182), so
		// "  a  \r\nb  \r\n" is "a  \nb  \n": 8 bytes, then two NULs, 5 words.
		byte[] bytes = UnitRun.EncodeLanguage(Parse(NotesAtFile, "Ng.txt"), (_, _) => "  a  \r\nb  \r\n");

		// The tag, count 1, index 5 and 5 words; then 'a', two spaces, LF, 'b', two spaces, LF, each XOR 0xA5, and two NULs.
		byte[] chunk = [0x0A, 0x80, 0x01, 0x00, 0x05, 0x00, 0x05, 0x00, 0xC4, 0x85, 0x85, 0xAF, 0xC7, 0x85, 0x85, 0xAF, 0x00, 0x00];
		Assert.IsTrue(ByteSearch.Contains(bytes, chunk));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_AtFileText_KeepsTheTabThatEndsALine()
	{
		// VB6's Trim removes spaces alone (Module1.bas:2182), so a tab at the end of an @file line stays, as the corpus golden
		// LanguageAtFileTrailingTab holds: 37 bytes and a NUL, 19 words.
		LanguageData data = Parse("[Strings]\nLevel\n[ExtraNG]\n100: @trailing_tab.txt\nTHE LAST STRING ENTRY\n");

		byte[] bytes = UnitRun.EncodeLanguage(data, (_, _) => "First line before a tab\t\r\nSecond line\r\n");

		// The tag, count 1, index 100 and 19 words; then the text with LF line ends, each byte XOR 0xA5, and a NUL.
		byte[] text = [.. "First line before a tab\t\nSecond line\n"u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00];
		byte[] chunk = [0x0A, 0x80, 0x01, 0x00, 0x64, 0x00, 0x13, 0x00, .. text];

		Assert.IsTrue(ByteSearch.Contains(bytes, chunk));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_ExtraNgTextWithAComment_KeepsTheSpaceBeforeIt()
	{
		// VB6 trims the text (Module1.bas:2182) and then cuts it at its first ';' with Left (:2188, 2338-2341), so the space before
		// the comment stays, as the corpus golden LanguageExtraNGComment holds: 22 bytes and two NULs, 12 words.
		LanguageData data = Parse("[Strings]\nLevel\n[ExtraNG]\n100: Text before a comment ; the comment\nTHE LAST STRING ENTRY\n");

		byte[] bytes = UnitRun.EncodeLanguage(data);

		// The tag, count 1, index 100 and 12 words; then "Text before a comment " XOR 0xA5, and two NULs.
		byte[] text = [.. "Text before a comment "u8.ToArray().Select(value => (byte)(value ^ 0xA5)), 0x00, 0x00];
		byte[] chunk = [0x0A, 0x80, 0x01, 0x00, 0x64, 0x00, 0x0C, 0x00, .. text];

		Assert.IsTrue(ByteSearch.Contains(bytes, chunk));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_MissingExtraNgFile_Fails()
	{
		LanguageData data = Parse("[Strings]\nLevel\n[ExtraNG]\n5: @missing.txt\nTHE LAST STRING ENTRY\n", "Ng.txt");

		UnitRun.LanguageEncoding encoding = UnitRun.RunLanguageEncoder(data);

		Assert.IsFalse(encoding.Success, encoding.Log);

		DiagnosticAssert.SingleError(
			diagnostics: encoding.Diagnostics,
			line: 4,
			message: "Could not find referenced file: missing.txt",
			file: "Ng.txt"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ExtraNgStringReadFromNoLine_FailsWithoutALine()
	{
		LanguageData data = UnitRun.Language(["Level"], [new ExtraNgString { Index = 5, Text = "Bad \\xZZ" }]);

		UnitRun.LanguageEncoding encoding = UnitRun.RunLanguageEncoder(data);

		Assert.IsFalse(encoding.Success, encoding.Log);

		DiagnosticAssert.SingleError(
			diagnostics: encoding.Diagnostics,
			line: null,
			message: "ExtraNG string 5 holds an invalid hexadecimal escape; follow each \\x with two hexadecimal digits, such as \\x41"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageDatEncoder_LongExtraNgTable_CountsTheSecondSizeWord()
	{
		// The count, the index and the word count are 3 words, and the 65,530-byte text plus two NULs is 32,766 words.
		// VB6 adds the size and the tag (0x8003), and one more for the second size word once that passes 0x7FFF
		// (Module1.bas:1895-1898), so the chunk opens with 0x8000 | high word, then the low word 0x8004 and the tag 0x800A.
		string content = "[Strings]\nLevel\n[ExtraNG]\n7: " + new string('A', 65530) + "\nTHE LAST STRING ENTRY\n";

		byte[] bytes = UnitRun.EncodeLanguage(Parse(content));

		byte[] chunkStart = [0x4E, 0x47, 0x00, 0x80, 0x04, 0x80, 0x0A, 0x80, 0x01, 0x00, 0x07, 0x00, 0xFE, 0x7F];

		Assert.IsTrue(
			ByteSearch.Contains(bytes, chunkStart),
			"The NG marker, both size words, the tag, the count, the index and the word count 0x7FFE."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void AtFileText_IsReadFromTheScriptFolder()
	{
		// PD-47: VB6 reads an @file text from <engine>\script\ (Module1.bas:1857), the folder every language file sits in.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + TestScripts.Level,
			"[Strings]\nTest Level\n[ExtraNG]\n100: @diary.txt\nTHE LAST STRING ENTRY\n"
		);

		workspace.WithTextFile(Path.Combine("script", "diary.txt"), "Ab");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		// The tag, count 1, index 100 and 2 words; then 'A' and 'b' XOR 0xA5, and two NULs.
		byte[] chunk = [0x0A, 0x80, 0x01, 0x00, 0x64, 0x00, 0x02, 0x00, 0xE4, 0xC7, 0x00, 0x00];
		Assert.IsTrue(ByteSearch.Contains(workspace.ReadOutput("English.dat"), chunk));
	}
}
