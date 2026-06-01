using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Parity;

/// <summary>
/// Hand-derived byte expectations for the classic <c>Script.dat</c> and language <c>.dat</c> formats.
/// </summary>
/// <remarks>
/// Each test derives from the VB6 source and format, because the goldens are never regenerated, and a test of a parity
/// decision cites its row.
/// </remarks>
[TestClass]
[TestCategory("Parity")]
public sealed class HandDerivedParityTests
{
	// The classic header, as ScriptDatEncoder writes it, opens with Options.Flags int32, Options.InputTimeOut int32, Options.Security byte,
	// sectionCount byte, levelCount int16, levelNamesSize int16 and sectionsSize int16. The PSX and PC extension tables follow, 20 bytes each,
	// then the level path offsets (int16 each) and strings, the section offsets (int16 each) and bodies, and the language .DAT file names.
	private const string MinimalLevelScript =
		"[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n";

	// LoadCamera= 0,0,0,0,0,0,0 -> tag 0x91, six int32 zeros, then the final byte 0x00.
	private static readonly byte[] s_loadCameraBytes = [0x91, .. new byte[25]];

	// FMV= 1,0 -> tag 0x80 and index byte 0x01 (trigger 0 does not set bit 7).
	private static readonly byte[] s_fmvBytes = [0x80, 0x01];

	// A stored [Level] body holds its command bytes, sorted by the classic command order, then 0x81, the nameIndex byte, the flags word, the
	// level index byte and the CD byte, then 0x83.
	[TestMethod]
	public void MinimalLevel_ClassicPayload_MatchesHandDerivedBytes_AndNgHeaderFollows()
	{
		using var workspace = TestWorkspace.Create(MinimalLevelScript);
		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		byte[] scriptDat = workspace.ReadScriptDat();

		byte[] expectedClassic =
		[
			// Options: Flags = 0, InputTimeOut = 0 (no [Options] section), Security = 0
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			// One stored section (the [Level]) and one level path
			0x01,
			0x01,
			0x00,
			// levelNamesSize = "data\test" (9 bytes) + NUL = 10
			0x0A,
			0x00,
			// sectionsSize = 33 bytes (LoadCamera 26 + header 6 + end marker 1)
			0x21,
			0x00,
			// PSX and PC extension tables (both empty -> 20 NUL bytes each)
			.. new byte[20],
			.. new byte[20],
			// level path offset table: one entry at offset 0
			0x00,
			0x00,
			// level path: "data\test" + NUL
			0x64,
			0x61,
			0x74,
			0x61,
			0x5C,
			0x74,
			0x65,
			0x73,
			0x74,
			0x00,
			// section offset table: one entry at offset 0
			0x00,
			0x00,
			// section body: LoadCamera= 0,0,0,0,0,0,0
			0x91,
			.. new byte[25],
			// [Level] header: 0x81, name index "Test Level" = 0, flags 0, level index 0, CD 0
			0x81,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			// section end marker
			0x83,
			// language file name table: "English.DAT\0" (the extension is uppercased by VB6)
			0x45,
			0x6E,
			0x67,
			0x6C,
			0x69,
			0x73,
			0x68,
			0x2E,
			0x44,
			0x41,
			0x54,
			0x00,
		];

		Assert.AreSequenceEqual(
			expectedClassic,
			scriptDat[..expectedClassic.Length],
			"The classic Script.dat payload must match the hand-derived layout exactly."
		);

		// The NG header is appended directly after the classic payload and starts with the NG marker.
		Assert.AreEqual(0x4E, scriptDat[expectedClassic.Length], "The NG marker 'N' must follow the classic payload.");
		Assert.AreEqual(0x47, scriptDat[expectedClassic.Length + 1], "The NG marker 'G' must follow the classic payload.");
	}

	[TestMethod]
	public void ExtensionSlots_AreWrittenInSlotOrderUpToTheCommandCount()
	{
		// PD-25: a command's slot is its place in ("Level=", "Fmv=", "Cut=") (Module1.bas:4525, 5526-5536), each new command adds one to
		// the count (:5552, 5575), and VB6 writes slots 0 to count-1 with a NUL each, then pads the table to 20 bytes (:6699-6722).
		using var workspace = TestWorkspace.Create(
			"[Language]\nFile= 0, English.txt\n[PSXExtensions]\nCut= .CUT\nFMV= .FMV\nLevel= .PSX\n[PCExtensions]\nFMV= .BIK\nLevel= .TR4\n"
				+ "[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n"
		);

		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		byte[] scriptDat = workspace.ReadScriptDat();
		byte[] psxTable = [.. ".PSX\0.FMV\0.CUT\0"u8, .. new byte[5]];
		byte[] pcTable = [.. ".TR4\0.BIK\0"u8, .. new byte[10]];

		Assert.AreSequenceEqual(psxTable, scriptDat[16..36], "The PSX table lists the slots in slot order, not in line order.");
		Assert.AreSequenceEqual(pcTable, scriptDat[36..56], "The PC table holds the two slots its section declares.");
	}

	[TestMethod]
	public void MinimalLevel_EnglishDat_MatchesHandDerivedBytes()
	{
		// Language table: counts (1, 0, 0), section sizes (11, 0, 0) with 11 for "Test Level" + NUL, offset 0,
		// then the string bytes XOR 0xA5 and a NUL terminator.
		using var workspace = TestWorkspace.Create(MinimalLevelScript);
		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\n[PSXStrings]\n[PCStrings]\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		byte[] expected =
		[
			0x01,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x0B,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00, // [PSXStrings] and [PCStrings] section sizes
			0x00,
			0x00,
			0xF1,
			0xC0,
			0xD6,
			0xD1,
			0x85,
			0xE9,
			0xC0,
			0xD3,
			0xC0,
			0xC9,
			0x00,
		];

		Assert.AreSequenceEqual(
			expected,
			workspace.ReadOutput("English.dat"),
			"English.dat must match the hand-derived string table (XOR 0xA5, NUL-terminated)."
		);
	}

	[TestMethod]
	public void EscapedLanguageString_IsDecodedOnce_BeforeEncoding()
	{
		// PD-08: "A\x41B" is stored raw but decoded exactly once at write time -> "AAB" -> 3 bytes.
		// The level name is resolved through the same table, so "Test Level" stays the first string.
		using var workspace = TestWorkspace.Create(MinimalLevelScript);
		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\nA\\x41B\n[PSXStrings]\n[PCStrings]\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		byte[] expected =
		[
			0x02,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x0F,
			0x00, // [Strings] section size: "Test Level" (11) + "AAB" (4)
			0x00,
			0x00,
			0x00,
			0x00, // [PSXStrings] and [PCStrings] section sizes
			0x00,
			0x00, // offset of "Test Level"
			0x0B,
			0x00, // offset of "AAB"
			0xF1,
			0xC0,
			0xD6,
			0xD1,
			0x85,
			0xE9,
			0xC0,
			0xD3,
			0xC0,
			0xC9,
			0x00,
			0xE4,
			0xE4,
			0xE7, // 'A', 'A', 'B' XOR 0xA5
			0x00,
		];

		Assert.AreSequenceEqual(expected, workspace.ReadOutput("English.dat"), "The \\x41 escape must decode before the string is written (PD-08).");
	}

	[TestMethod]
	public void DuplicateClassicCommand_EmitsBothOccurrences_BetweenCanonicalBodies()
	{
		// PD-02: each FMV= line is a movie of its own, so a repeat is written again, as VB6 writes it.
		const string header = "[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var single = TestWorkspace.Create(header + "Level= data\\test,0\nFMV= 1,0\n");
		single.WithLanguageFile("English.txt", "[Strings]\nTest Level\n");

		using var duplicate = TestWorkspace.Create(header + "Level= data\\test,0\nFMV= 1,0\nFMV= 1,0\n");
		duplicate.WithLanguageFile("English.txt", "[Strings]\nTest Level\n");

		TestCompileResult singleResult = single.Compile();
		TestCompileResult duplicateResult = duplicate.Compile();

		Assert.IsTrue(singleResult.Success, singleResult.Log);
		Assert.IsTrue(duplicateResult.Success, duplicateResult.Log);

		byte[] singleExpected = ExpectedClassicPrefix([.. s_fmvBytes, .. s_loadCameraBytes]);
		byte[] duplicateExpected = ExpectedClassicPrefix([.. s_fmvBytes, .. s_fmvBytes, .. s_loadCameraBytes]);

		Assert.AreSequenceEqual(
			singleExpected,
			single.ReadScriptDat()[..singleExpected.Length],
			"A single FMV= 1,0 must emit exactly one 0x80 0x01 pair."
		);

		Assert.AreSequenceEqual(
			duplicateExpected,
			duplicate.ReadScriptDat()[..duplicateExpected.Length],
			"The duplicate FMV= command must emit its pair again (PD-02)."
		);
	}

	[TestMethod]
	public void SharedSortKeyLines_FollowedByLowerKeyLine_ComeOutReversed()
	{
		// PD-32: VB6 keys each line by its place in the order table (Module1.bas:5680-5706), so both ResidentCut= lines share
		// key 18000, and sorts with an exchange sort that swaps only on a strictly greater key (Module1.bas:5711-5733).
		// Name= 0, LoadCamera= 28000, Level= 31000, ResidentCut= 18000, ResidentCut= 18000, Legend= 16000 sorts to
		// Name=, Legend=, ResidentCut= 2, ResidentCut= 1, LoadCamera=, Level=.
		const string header = "[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.Create(header + "Level= data\\test,0\nResidentCut= 1,5\nResidentCut= 2,6\nLegend= Test Legend\n");
		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\nTest Legend\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		// Legend= -> 0x8C and string index 1 (Module1.bas:5975-5994); ResidentCut= slot,cut -> tag 0x85 + slot - 1 and the cut byte
		// (Module1.bas:5892-5921).
		byte[] expected = ExpectedClassicPrefix([0x8C, 0x01, 0x86, 0x06, 0x85, 0x05, .. s_loadCameraBytes]);

		Assert.AreSequenceEqual(expected, workspace.ReadScriptDat()[..expected.Length]);
	}

	[TestMethod]
	public void ExtraNgReference_WritesBaseOffsetWord_AndExtraNgTableFrame()
	{
		// PD-10/PD-12: "!0" resolves to 32768 + 0 = 0x8000; ExtraNG strings are written into the
		// language .dat after the classic table as a 0x800A chunk inside a full NG header frame.
		// The first language file is parsed after the whole script is read, whatever section follows [Language],
		// and the NG lines compile after it, so NG string arguments resolve.
		const string script =
			"[Language]\nFile= 0, English.txt\n[Options]\nTitle= DISABLED\nSecurity= 0\n"
			+ "[Level]\nName= Test Level\nLevel= data\\test,0\n"
			+ "LoadCamera= 0,0,0,0,0,0,0\nWindowTitle= !0\n";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\n[PSXStrings]\n[PCStrings]\n[ExtraNG]\n0: hello\nTHE LAST STRING ENTRY\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		// WindowTitle= is command 29 (0x1D); its compiled words are one header word
		// (code * 256 + argument words = 0x1D01) followed by the single String argument word.
		Assert.AreSequenceEqual(
			new byte[] { 0x01, 0x1D, 0x00, 0x80 },
			ReadLevelMiniTag(workspace, code: 29).ToBytes(),
			"WindowTitle= !0 must write the ExtraNG base offset 0x8000 as the string index word."
		);

		// English.dat: the classic table, then the hand-derived ExtraNG frame:
		// NG marker, 0x800A chunk (count 1, index 0, 3 words), zero words, NGLE marker, frame size.
		byte[] expectedEnglishDat =
		[
			0x01,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x0B,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00, // [PSXStrings] and [PCStrings] section sizes
			0x00,
			0x00,
			0xF1,
			0xC0,
			0xD6,
			0xD1,
			0x85,
			0xE9,
			0xC0,
			0xD3,
			0xC0,
			0xC9,
			0x00,
			0x4E,
			0x47,
			0x08,
			0x00,
			0x0A,
			0x80,
			0x01,
			0x00,
			0x00,
			0x00,
			0x03,
			0x00,
			0xCD,
			0xC0,
			0xC9,
			0xC9,
			0xCA,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x4E,
			0x47,
			0x4C,
			0x45,
			0x1E,
			0x00,
			0x00,
			0x00,
		];

		Assert.AreSequenceEqual(
			expectedEnglishDat,
			workspace.ReadOutput("English.dat"),
			"The ExtraNG table frame must match the hand-derived bytes (chunk 0x800A, 'hello' XOR 0xA5)."
		);
	}

	[TestMethod]
	public void ExtraNgString_OfEvenLength_TakesASecondZeroByte()
	{
		// VB6's ConvertString2Words adds a zero byte to an even-length text, then the terminating zero, and XORs every non-zero byte with
		// 0xA5 (TR_Explorer/Mod_NextGeneration.bas:2073-2101). "ab" is the words C4 C7, 00 00; "abc" is C4 C7, C6 00.
		using var workspace = TestWorkspace.CreateCompilable(
			languageContent: "[Strings]\nTest Level\n[PSXStrings]\n[PCStrings]\n[ExtraNG]\n0: ab\n1: abc\nTHE LAST STRING ENTRY\n"
		);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		// The string count, then per string its index, its word count and its words.
		byte[] expectedStrings = [0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0xC4, 0xC7, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0xC4, 0xC7, 0xC6, 0x00];

		Assert.IsTrue(
			ByteSearch.Contains(workspace.ReadOutput("English.dat"), expectedStrings),
			"The ExtraNG words must match the hand-derived bytes."
		);
	}

	[TestMethod]
	public void ClassicStringArgument_StillWritesTheClassicIndex()
	{
		// Contrast case for the !N test: a classic lookup resolves to the plain index 0.
		// The NG line compiles after the first language file is parsed, as in the !N case above.
		const string script =
			"[Language]\nFile= 0, English.txt\n[Options]\nTitle= DISABLED\nSecurity= 0\n"
			+ "[Level]\nName= Test Level\nLevel= data\\test,0\n"
			+ "LoadCamera= 0,0,0,0,0,0,0\nWindowTitle= Test Level\n";

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", "[Strings]\nTest Level\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		Assert.AreSequenceEqual(
			new byte[] { 0x01, 0x1D, 0x00, 0x00 },
			ReadLevelMiniTag(workspace, code: 29).ToBytes(),
			"A classic string reference must write the plain language index 0 (not the ExtraNG base)."
		);
	}

	private static ScriptDatReader.MiniTag ReadLevelMiniTag(TestWorkspace workspace, int code)
	{
		var reader = ScriptDatReader.Read(workspace.ReadScriptDat());
		return reader.GetMiniTags(reader.GetNgField((ushort)NgTagType.ScriptLevel)).Single(tag => tag.Code == code);
	}

	private static byte[] ExpectedClassicPrefix(byte[] sectionBody)
	{
		// The stored section size covers the command bytes plus the fixed 6-byte [Level] header
		// (0x81, name index, flags word, level index, CD) and the 0x83 end marker.
		const int levelHeaderAndEndMarkerSize = 7;
		int sectionSize = sectionBody.Length + levelHeaderAndEndMarkerSize;

		var expected = new List<byte>
		{
			// Options: Flags = 0, InputTimeOut = 0, Security = 0
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			0x00,
			// One section, one level
			0x01,
			0x01,
			0x00,
			// "data\test" + NUL = 10 bytes
			0x0A,
			0x00,
			// section data size
			(byte)(sectionSize & 0xFF),
			(byte)(sectionSize >> 8),
		};

		expected.AddRange(new byte[20]); // PSX extension table
		expected.AddRange(new byte[20]); // PC extension table
		expected.AddRange([0x00, 0x00]); // level path offset
		expected.AddRange("data\\test"u8.ToArray());

		expected.Add(0x00);
		expected.AddRange([0x00, 0x00]); // section offset

		expected.AddRange(sectionBody);

		expected.AddRange([0x81, 0x00, 0x00, 0x00, 0x00, 0x00, 0x83]); // [Level] header + end marker
		expected.AddRange("English.DAT"u8.ToArray());

		expected.Add(0x00);

		return [.. expected];
	}
}
