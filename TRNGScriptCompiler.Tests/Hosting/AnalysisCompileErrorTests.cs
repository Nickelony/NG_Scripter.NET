using System.Text;
using TRNGScriptCompiler.Hosting;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Hosting;

/// <summary>
/// An analysis through <see cref="CompilationHost"/> reports each error a compile reports, and writes no file.
/// </summary>
/// <remarks>
/// The analysis compiles every command and composes every output, so a script VB6 rejects as it compiles
/// is one an analysis rejects.
/// </remarks>
[TestClass]
public sealed class AnalysisCompileErrorTests
{
	private const string CrypticImageScript =
		TestScripts.Language + TestScripts.Level + "Image= 1,4,IF_CRYPTIC,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n";

	[TestMethod]
	[TestCategory("IO")]
	public async Task LevelNameMissingFromTheLanguage_IsReported()
	{
		// VB6 looks the name up while it compiles the section and fails on a miss (Module1.bas:5995-6009).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Level]\nName= Unknown Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n"
		);

		// The error points at the Name= line.
		await AssertReportedAsync(workspace, 4, "Cannot find level name in language file: Unknown Level");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task MissingLevelCommand_IsReportedAtTheSectionHeader()
	{
		// VB6 fails a [Level] without Level= (Module1.bas:6430-6434).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "[Level]\nName= Test Level\nLoadCamera= 0,0,0,0,0,0,0\n");

		// The error points at the [Level] line.
		await AssertReportedAsync(workspace, 3, "Missing 'Level=' command in [Level] section");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task ClassicCommandError_IsReported()
	{
		// VB6 checks the cut number while it compiles the section (Module1.bas:5881-5885).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + TestScripts.Level + "Cut= 40\n");
		await AssertReportedAsync(workspace, 7, "Argument 1 of Cut= is out of range: 40 lies outside 0 to 31, the values a cutscene number holds");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task ErrorsInSeveralSections_AreAllReported()
	{
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language
				+ "[Level]\nName= First\nLevel= data\\a,0\nLoadCamera= 0,0,0,0,0,0,0\n[Level]\nName= Second\nLevel= data\\b,0\nLoadCamera= 0,0,0,0,0,0,0\n"
		);

		using var host = new CompilationHost();
		DiagnosticsResult result = await host.GetDiagnosticsAsync(workspace.CreateOptions(), dirtyFiles: null, CancellationToken.None);

		Assert.AreSequenceEqual(
			["Cannot find level name in language file: First", "Cannot find level name in language file: Second"],
			result.Diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Message),
			result.Log
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task InvalidEscapeInAnExtraNgString_IsReported()
	{
		// VB6 reports the escape while it writes the language .dat (Module1.bas:1874).
		using var workspace = TestWorkspace.CreateCompilable(languageContent: "[Strings]\nTest Level\n[ExtraNG]\n5: Bad \\xZZ\n");

		await AssertReportedAsync(
			workspace,
			line: 4,
			message: "ExtraNG string 5 holds an invalid hexadecimal escape; follow each \\x with two hexadecimal digits, such as \\x41",
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task MissingExtraNgFile_IsReported()
	{
		// VB6 reads an @file text while it writes the language .dat (Module1.bas:1854).
		using var workspace = TestWorkspace.CreateCompilable(languageContent: "[Strings]\nTest Level\n[ExtraNG]\n5: @missing.txt\n");

		await AssertReportedAsync(
			workspace,
			line: 4,
			message: "Could not find referenced file: missing.txt",
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task DuplicateOutputs_AreReported()
	{
		// ENGLISH2.TXT takes the casing of English2.txt on disk, so the two entries name one file on every file system (PD-29).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "File= 1, English2.txt\nFile= 2, ENGLISH2.TXT\n" + TestScripts.Level
		);

		workspace.WithLanguageFile("English2.txt", TestWorkspace.CompilableLanguageContent);

		await AssertReportedAsync(
			workspace,
			line: 4,
			message: "Multiple outputs map to the same file name 'ENGLISH2.dat'; give each [Language] File= entry a name of its own",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task MissingCrypticImage_IsReported()
	{
		// VB6 encrypts the image while it writes Script.dat, and fails when it is missing (Module1.bas:8144-8148).
		using var workspace = TestWorkspace.CreateCompilable(CrypticImageScript);
		await AssertReportedAsync(workspace, 7, $"Cannot find image4.bmp to encrypt in {workspace.PathOf("pix")} directory");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task ShortCrypticImage_IsReportedAndKept()
	{
		// VB6 deletes the image, then fails on a file shorter than 256 bytes (Module1.bas:8161-8170).
		using var workspace = TestWorkspace.CreateCompilable(CrypticImageScript);
		byte[] image = [.. Enumerable.Range(0, 58).Select(index => (byte)index)];

		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), image);

		await AssertReportedAsync(workspace, 7, "image4.bmp is too small to encrypt: expected at least 256 bytes, found 58 bytes");

		Assert.AreSequenceEqual(image, workspace.ReadOutput(Path.Combine("pix", "image4.bmp")), "An analysis leaves the image alone.");
		Assert.IsFalse(File.Exists(workspace.PathOf(Path.Combine("pix", "@image4.bmp"))), "An analysis encrypts no image.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task CrypticImage_IsNotEncryptedByAnAnalysis()
	{
		using var workspace = TestWorkspace.CreateCompilable(CrypticImageScript);
		byte[] image = [.. Enumerable.Range(0, 300).Select(index => (byte)index)];

		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), image);

		using var host = new CompilationHost();

		DiagnosticsResult result = await host.GetDiagnosticsAsync(workspace.CreateOptions(), dirtyFiles: null, CancellationToken.None);

		Assert.IsTrue(result.Success, result.Log);
		Assert.AreSequenceEqual(image, workspace.ReadOutput(Path.Combine("pix", "image4.bmp")), "An analysis leaves the image alone.");

		Assert.IsFalse(File.Exists(workspace.PathOf(Path.Combine("pix", "@image4.bmp"))), "An analysis encrypts no image.");
		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "An analysis writes no output.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task OversizedNgLevelSection_IsReported()
	{
		// VB6 checks the word count of each NG level section (Module1.bas:8559-8565); 128 Animation= commands make 32768 words.
		var script = new StringBuilder(TestScripts.LanguageAndLevel);

		for (int id = 1; id <= 127; id++)
			script.Append(TestWorkspace.AnimationCommand(id, 248));

		script.Append(TestWorkspace.AnimationCommand(128, 243));

		using var workspace = TestWorkspace.CreateCompilable(script.ToString());

		// The error points at the [Level] line.
		await AssertReportedAsync(workspace, 3, "NG level section 0 holds 32768 words; the format holds at most 32767");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task ImportFileNumberPastTheWord_IsReported()
	{
		// VB6 reads the number before the dot into a word, which overflows past 32767 (TrovaNumeroFile).
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nImportFile= 9,extras\\demo99999.bin,FTYPE_USERFILE,IMPORT_TEMPORARY\n" + TestScripts.Level
		);

		workspace.WithBinaryFile(Path.Combine("extras", "demo99999.bin"), [1, 2, 3, 4]);

		// The error points at the ImportFile= line.
		await AssertReportedAsync(
			workspace,
			4,
			"The file number 99999 in 'extras\\demo99999.bin' exceeds the 16-bit range (VB6 raised an overflow error here)"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task TooManySections_IsReported()
	{
		// VB6 writes the section count in a byte, which overflows past 255 (Module1.bas:6674-6675).
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + string.Concat(Enumerable.Repeat(TestScripts.Level, 256)));

		// The error points at the header of the 256th section, after the two [Language] lines and 255 sections of four lines.
		await AssertReportedAsync(workspace, 2 + (255 * 4) + 1, "Too many script sections (256); the format supports at most 255");
	}

	[TestMethod]
	[TestCategory("IO")]
	public async Task LevelNamesTablePastTheWord_IsReported()
	{
		// VB6 writes the size of the level names table in an Integer, which overflows past 32767 (Module1.bas:6680-6686).
		string level = $"[Level]\nName= Test Level\nLevel= data\\{new string('a', 130)},0\nLoadCamera= 0,0,0,0,0,0,0\n";

		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + string.Concat(Enumerable.Repeat(level, 255)));

		// Each level path takes 136 bytes with its terminator, so the 241st carries the table past 32767 bytes; the error points at its
		// section's header.
		await AssertReportedAsync(
			workspace,
			2 + (240 * 4) + 1,
			"Level names table is too large (34680 bytes); the format supports at most 32767 (VB6 errored on overflow)"
		);
	}

	private static async Task AssertReportedAsync(TestWorkspace workspace, int line, string message, string? file = null)
	{
		using var host = new CompilationHost();

		DiagnosticsResult result = await host.GetDiagnosticsAsync(workspace.CreateOptions(), dirtyFiles: null, CancellationToken.None);

		Assert.IsFalse(result.Success, result.Log);
		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "An analysis writes no output.");

		DiagnosticAssert.SingleError(diagnostics: result.Diagnostics, line, message, file);
	}
}
