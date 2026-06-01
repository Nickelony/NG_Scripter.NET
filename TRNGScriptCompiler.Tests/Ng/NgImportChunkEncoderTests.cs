using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Ng;

[TestClass]
public sealed class NgImportChunkEncoderTests
{
	/// <summary>The place of the TrovaNumeroFile number among the data words of an import-file chunk.</summary>
	private const int ImportFileNumberWord = 3; // ID, mode and type come first

	private const string ImportFileScriptTemplate = """
		[Language]
		File= 0,English.txt

		[Options]
		Title= DISABLED
		Security= 0
		ImportFile= 9,{PATH},FTYPE_USERFILE,IMPORT_TEMPORARY

		[Level]
		Name= Import Level
		Level= data\import,0
		LoadCamera= 0,0,0,0,0,0,0
		""";

	private static NgImportFile Import(string fileName, byte[] data, int line = 3)
	{
		return new()
		{
			ImportId = 1,
			FileName = fileName,
			ImportMode = NgImportMode.Memory,
			FileType = 3,
			Data = data,
			Location = new(line, File: "Script.txt"),
		};
	}

	// The 80-byte name field: the name, its terminator and spaces (ConvertString2Words, Module1.bas:8625-8627).
	private static short[] NameField(string fileName)
	{
		byte[] field = [.. System.Text.Encoding.ASCII.GetBytes(fileName + "\0"), .. Enumerable.Repeat((byte)' ', 80 - fileName.Length - 1)];
		return [.. Enumerable.Range(0, 40).Select(index => (short)(field[2 * index] | (field[(2 * index) + 1] << 8)))];
	}

	private static (bool Written, List<short> Words, UnitRun Unit) Encode(NgImportFile import)
	{
		var unit = new UnitRun();
		var words = new List<short>();

		bool written = new NgImportChunkEncoder(unit.Run.Logger, unit.Run.Diagnostics).TryEncode(words, import);
		return (written, words, unit);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Chunk_HoldsItsDwordSize_ItsFields_AndThePayload()
	{
		// VB6 writes the size as a dword with bit 15 of its high word set, the tag, the ID, the mode, the type, the number before the
		// extension, the name, the file size as a dword and the bytes (Module1.bas:8605-8633, 6797-6810, 6943-6967).
		(bool written, List<short> words, UnitRun unit) = Encode(Import(@"import\data7.bin", [1, 2, 3, 4]));

		short[] expected =
		[
			unchecked((short)0x8000),
			49 + 2,
			unchecked((short)NgTagType.ImportFile),
			1,
			1,
			3,
			7,
			.. NameField(@"import\data7.bin"),
			4,
			0,
			0x0201,
			0x0403,
		];

		Assert.IsTrue(written);
		Assert.AreSequenceEqual(expected, words);

		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow(1, 1, false)]
	[DataRow(2, 1, false)]
	[DataRow(3, 2, true)]
	[DataRow(5, 3, false)]
	[DataRow(7, 4, true)]
	public void PayloadOfASizeThatIs3Mod4_GainsAZeroWordPastTheChunk(int size, int declaredWords, bool extraWord)
	{
		// VB6 copies TotBytes / 2 words, which a Long rounds half to even, plus one for an odd size (AddBytesInVet, Module1.bas:6837-6838),
		// while the chunk declares the size rounded up to words (:8607-8610).
		(bool written, List<short> words, _) = Encode(Import("data.bin", [.. Enumerable.Repeat((byte)0xAB, size)]));

		Assert.IsTrue(written);
		Assert.AreEqual(49 + declaredWords, words[1]);
		Assert.HasCount(49 + declaredWords + (extraWord ? 1 : 0), words);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("readme.txt", 0)]
	[DataRow("readme", -1)]
	[DataRow(@"audio\012.wav", 12)]
	public void FileNumber_IsTheNumberBeforeTheExtension(string fileName, int expectedNumber)
	{
		// VB6 reads the digits before the last '.', 0 when there are none, and -1 for a name without a '.' (TrovaNumeroFile,
		// Module1.bas:6950-6965).
		(_, List<short> words, _) = Encode(Import(fileName, [0]));
		Assert.AreEqual(expectedNumber, words[6]);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void FileNumberPastAWord_Fails()
	{
		// TrovaNumeroFile returns an Integer, which overflows past 32767 (Module1.bas:6943, 6963-6965).
		(bool written, _, UnitRun unit) = Encode(Import("data40000.bin", [0], line: 5));

		Assert.IsFalse(written);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			5,
			"The file number 40000 in 'data40000.bin' exceeds the 16-bit range (VB6 raised an overflow error here)"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void FileNumberPastAnInt_FailsWithTheSixteenBitMessage()
	{
		(bool written, _, UnitRun unit) = Encode(Import("data99999999999.bin", [0], line: 5));

		Assert.IsFalse(written);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			5,
			"The file number 99999999999 in 'data99999999999.bin' exceeds the 16-bit range (VB6 raised an overflow error here)"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void FileNameWithACharacterWindows1252CannotHold_Fails()
	{
		// PD-30: the name would be written as 'caf' followed by the look-alike 'A', which names another file.
		(bool written, List<short> words, UnitRun unit) = Encode(Import("cafĀ.bin", [0], line: 6));

		Assert.IsFalse(written);
		Assert.IsEmpty(words);

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			6,
			"'cafĀ.bin' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NoImport_WritesNothing()
	{
		// VB6 writes the import chunks only when the script imports a file (Module1.bas:8585).
		var unit = new UnitRun();
		var words = new List<short>();

		Assert.IsTrue(new NgImportChunkEncoder(unit.Run.Logger, unit.Run.Diagnostics).TryEncodeAll(words, []));
		Assert.IsEmpty(words);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFile_TrailingNumberMatchesTrovaNumeroFileSemantics()
	{
		// PD-20: VB6 collects digits right-to-left before the last dot, returns 0 when the
		// dot exists but has no digits, and -1 when the name has no dot at all.
		Assert.AreEqual(7, CompileAndReadImportFileNumber("extras\\demo0007.bin"));
		Assert.AreEqual(0, CompileAndReadImportFileNumber("extras\\matrix.bin"));
		Assert.AreEqual(-1, CompileAndReadImportFileNumber("extras\\noextension"));

		// PD-20: values that do not fit the 16-bit field were VB6 overflow errors and are
		// reported as fatal diagnostics instead of silently wrapping.
		const string overflowName = "extras\\demo99999.bin";
		string script = ImportFileScriptTemplate.Replace("{PATH}", overflowName, StringComparison.Ordinal);

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Import Level"));
		workspace.WithBinaryFile(overflowName, [1, 2, 3, 4]);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A file number beyond the 16-bit range should be fatal.");

		DiagnosticAssert.SingleError(
			result.Diagnostics,
			7,
			"The file number 99999 in 'extras\\demo99999.bin' exceeds the 16-bit range (VB6 raised an overflow error here)"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(46, false)]
	[DataRow(47, false)]
	[DataRow(48, false)]
	[DataRow(49, false)]
	[DataRow(46, true)]
	[DataRow(47, true)]
	[DataRow(48, true)]
	[DataRow(49, true)]
	public void ImportOfASizeThatIsThreeMod4_IsFollowedByOneZeroWordOutsideItsChunk(int size, bool encrypted)
	{
		// VB6 copies the payload as TotBytes / 2, rounded half to even, plus one word for an odd size (Module1.bas:6837-6838),
		// while the chunk declares 49 words plus the size rounded up to whole words (Module1.bas:8607-8610). So a size that is
		// 3 mod 4 leaves one zero word after the chunk, whether or not Settings= encrypts the script.
		const string importPath = "extras\\data0001.bin";
		string script = ImportFileScriptTemplate.Replace("{PATH}", importPath, StringComparison.Ordinal);

		if (encrypted)
			script = script.Replace("Security= 0", "Security= 0\nSettings= 8", StringComparison.Ordinal);

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Import Level"));
		workspace.WithBinaryFile(importPath, [.. Enumerable.Range(1, size).Select(index => (byte)index)]);

		TestCompileResult result = workspace.Compile();
		Assert.IsTrue(result.Success, result.Log);

		var dat = ScriptDatReader.Read(workspace.ReadScriptDat());

		int importIndex = dat.NgFields.ToList().FindIndex(field => field.Tag == 0x801F);
		ScriptDatReader.NgField import = dat.NgFields[importIndex];

		int chunkEnd = import.DataOffset + (2 * import.DataWordCount);
		int next = importIndex + 1 < dat.NgFields.Count ? dat.NgFields[importIndex + 1].Offset : dat.Bytes.Length - 12;

		Assert.AreEqual(46 + ((size + 1) / 2), import.DataWordCount, "The declared size leaves the pad word out.");
		Assert.AreEqual(size % 4 == 3 ? 2 : 0, next - chunkEnd, "Only a size that is 3 mod 4 is followed by a zero word.");
		Assert.AreEqual(encrypted, dat.HeaderScrambled);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileNumber_IgnoresADotInTheTrleFolderName()
	{
		// PD-20: VB6 searches the full path, engine folder included, for its last dot (Module1.bas:6943-6967, 7648), so in an engine
		// folder named TRLE1.5 the name extras\data reads the digit 1; the port reads the name as written.
		string script = ImportFileScriptTemplate.Replace("{PATH}", "extras\\data", StringComparison.Ordinal);

		using var workspace = TestWorkspace.CreateEmpty();

		workspace.WithTextFile(Path.Combine("TRLE1.5", "script", "Script.txt"), script);
		workspace.WithTextFile(Path.Combine("TRLE1.5", "script", "English.txt"), TestWorkspace.LanguageFile("Import Level"));

		workspace.WithBinaryFile(Path.Combine("TRLE1.5", "extras", "data"), [.. Enumerable.Range(0, 12).Select(index => (byte)index)]);

		TRNGCompilerOptions options = TRNGCompilerOptions.CreateDefaults(workspace.PathOf("TRLE1.5"), "English") with
		{
			DeterministicSeed = TestWorkspace.DefaultDeterministicSeed,
		};

		TestCompileResult result = workspace.Compile(options);

		Assert.IsTrue(result.Success, result.Log);
		Assert.AreEqual(-1, ReadImportFileNumber(File.ReadAllBytes(workspace.PathOf(Path.Combine("TRLE1.5", "Script.dat")))));
	}

	private static short CompileAndReadImportFileNumber(string importPath)
	{
		string script = ImportFileScriptTemplate.Replace("{PATH}", importPath, StringComparison.Ordinal);

		using var workspace = TestWorkspace.Create(script);
		workspace.WithLanguageFile("English.txt", TestWorkspace.LanguageFile("Import Level"));
		workspace.WithBinaryFile(importPath, [.. Enumerable.Range(0, 12).Select(index => (byte)index)]);

		TestCompileResult result = workspace.Compile();
		Assert.IsTrue(result.Success, result.Log);

		return ReadImportFileNumber(workspace.ReadScriptDat());
	}

	private static short ReadImportFileNumber(byte[] datBytes)
	{
		var dat = ScriptDatReader.Read(datBytes);
		return dat.GetDataWords(dat.GetNgField((ushort)NgTagType.ImportFile))[ImportFileNumberWord];
	}
}
