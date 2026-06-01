using System.Text;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class ImportAndIncludePathTests
{
	private static readonly byte[] s_engineBytes = [0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8];
	private static readonly byte[] s_scriptBytes = [0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8];

	private static string ImportScript(string path) =>
		TestScripts.Language + $"[Options]\nImportFile= 1,{path},FTYPE_USERFILE,IMPORT_TEMPORARY\n" + TestScripts.Level;

	private static byte[] ReadImportedBytes(TestWorkspace workspace)
	{
		var reader = ScriptDatReader.Read(workspace.ReadScriptDat());
		return reader.GetImportedBytes(reader.GetNgField((ushort)NgTagType.ImportFile));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileInBothFolders_EmbedsTheEngineFolderFile()
	{
		// VB6 reads an import path relative to the engine folder only (Module1.bas:7635-7640).
		using var workspace = TestWorkspace.CreateCompilable(ImportScript("data.bin"));
		workspace.WithBinaryFile("data.bin", s_engineBytes);
		workspace.WithScriptBinaryFile("data.bin", s_scriptBytes);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		Assert.AreSequenceEqual(
			s_engineBytes,
			ReadImportedBytes(workspace),
			"The file in the engine folder is embedded, and the one in the script folder is not."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileOnlyInTheScriptFolder_IsTheFallback()
	{
		// PD-83: VB6 fails when the engine folder lacks the file (Module1.bas:7636-7640); the port falls back to the script file's folder.
		using var workspace = TestWorkspace.CreateCompilable(ImportScript("data.bin"));
		workspace.WithScriptBinaryFile("data.bin", s_scriptBytes);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.AreSequenceEqual(s_scriptBytes, ReadImportedBytes(workspace));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImportFileWithAnAbsolutePath_Fails()
	{
		// VB6 prefixes the engine folder to the path as written (Module1.bas:7635), so an absolute path never resolves.
		using var workspace = TestWorkspace.CreateCompilable();
		workspace.WithBinaryFile("data.bin", s_engineBytes);
		workspace.WithScript(ImportScript(workspace.PathOf("data.bin")));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,
			message: $"The ImportFile= path '{workspace.PathOf("data.bin")}' is absolute, but an import path is relative to the engine folder; write it relative to {workspace.RootPath}",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(79, true)]
	[DataRow(80, false)]
	public void ImportFileNameOf79Characters_Compiles_AndOf80Fails(int length, bool compiles)
	{
		// PD-56: the name field holds 80 bytes with the terminator, and VB6 cuts a longer name with no terminator (Module1.bas:7645).
		string name = new string('n', length - 4) + ".bin";

		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language + "[Options]\nImportFile= 1," + name + ",FTYPE_USERFILE,0\n" + TestScripts.Level
		);

		workspace.WithBinaryFile(name, s_engineBytes);

		TestCompileResult result = workspace.Compile();

		Assert.AreEqual(compiles, result.Success, result.Log);

		if (!compiles)
		{
			DiagnosticAssert.SingleError(
				diagnostics: result.Diagnostics,
				line: 4,
				message: $"The ImportFile= path '{name}' is 80 characters long, but its field holds 79 and a terminator; move the file to a shorter path",
				file: workspace.ScriptFile
			);
		}
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeWithTextAfterTheClosingQuote_Fails()
	{
		// PD-37: VB6 includes a.txt and ignores the rest of the line (Module1.bas:5016-5018); the port fails on it.
		using var workspace = TestWorkspace.CreateCompilable("#INCLUDE \"a.txt\" \"b.txt\"\n" + TestScripts.Language + TestScripts.Level);
		workspace.WithLanguageFile("a.txt", "; a\n").WithLanguageFile("b.txt", "; b\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 1,
			message: "The #INCLUDE line goes on after the quoted name \"a.txt\"; write one #INCLUDE line per file",
			file: workspace.ScriptFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeFoundInBothFolders_ThatDiffers_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "#INCLUDE \"x\\a.txt\"\n");

		workspace.WithTextFile(Path.Combine("script", "x", "a.txt"), "#INCLUDE \"b.txt\"\n");
		workspace.WithTextFile(Path.Combine("script", "x", "b.txt"), TestScripts.Level);
		workspace.WithTextFile(Path.Combine("script", "b.txt"), TestScripts.Level.Replace("data\\test", "data\\other", StringComparison.Ordinal));

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		string besideTheInclude = workspace.PathOf(Path.Combine("script", "x", "b.txt"));
		string inTheScriptFolder = workspace.PathOf(Path.Combine("script", "b.txt"));

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 1,
			message: $"The #INCLUDE file 'b.txt' exists as {besideTheInclude} and as {inTheScriptFolder}, and the two differ; delete one of them or make them the same",
			file: workspace.PathOf(Path.Combine("script", "x", "a.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeFoundInBothFolders_ThatCannotBeRead_FailsWithTheIOExceptionAttached()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "#INCLUDE \"x\\a.txt\"\n");

		workspace.WithTextFile(Path.Combine("script", "x", "a.txt"), "#INCLUDE \"b.txt\"\n");
		workspace.WithTextFile(Path.Combine("script", "x", "b.txt"), TestScripts.Level);
		workspace.WithTextFile(Path.Combine("script", "b.txt"), TestScripts.Level);

		string lockedFile = Path.Combine("script", "b.txt");
		string lockedPath = workspace.PathOf(lockedFile);

		TestCompileResult result = workspace.WithFileLocked(lockedFile, () => workspace.Compile());

		Assert.IsFalse(result.Success, result.Log);

		// The message ends in the operating system's message, which the error keeps in its exception.
		CompilerDiagnostic diagnostic = result.Diagnostics.Single(entry => entry.IsError);

		Assert.IsInstanceOfType<IOException>(diagnostic.Exception, "The diagnostic keeps the I/O exception as evidence.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 1,
			message: $"Cannot read file {lockedPath}: {diagnostic.Exception.Reason()}",
			file: workspace.PathOf(Path.Combine("script", "x", "a.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeFilesOfOneNameInTwoFolders_KeepTheirOwnDefines()
	{
		// VB6 keys a define on the name after the file's last '\' or ':' (Module1.bas:10085-10091), which names one file because VB6 opens
		// every include from the script folder; the port opens an include beside the file that names it, so it keys on the full path.
		using var workspace = TestWorkspace.CreateClean("#INCLUDE \"a\\defs.txt\"\n#INCLUDE \"b\\defs.txt\"\n");

		workspace.WithTextFile(Path.Combine("script", "a", "defs.txt"), "#DEFINE SPEED 1\n");
		workspace.WithTextFile(Path.Combine("script", "b", "defs.txt"), "#DEFINE SPEED 2\nUVRotate= SPEED\n");

		using var written = TestWorkspace.CreateClean("UVRotate= 2\n");

		TestCompileResult result = workspace.Compile();
		TestCompileResult writtenResult = written.Compile();

		Assert.IsTrue(result.Success, result.Log);
		Assert.IsTrue(writtenResult.Success, writtenResult.Log);

		DiagnosticAssert.NoDiagnostics(result.Diagnostics);

		Assert.AreSequenceEqual(written.ReadScriptDat(), workspace.ReadScriptDat(), "UVRotate= reads SPEED from the file that holds it.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FileIncludedFromTwoSections_ReportsEachDiagnosticOnce()
	{
		// The file warns about its mark when it is read and fails its define when its defines are scanned, and two #INCLUDE lines read and
		// list it twice.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.Language
				+ "[Level]\nName= First\nLevel= data\\first,0\n"
				+ TestScripts.LoadCamera
				+ "\n#INCLUDE \"common.inc\"\n"
				+ "[Level]\nName= Second\nLevel= data\\second,0\n"
				+ TestScripts.LoadCamera
				+ "\n#INCLUDE \"common.inc\"\n",

			languageContent: "[Strings]\nFirst\nSecond\n"
		);

		workspace.WithTextFile(Path.Combine("script", "common.inc"), "#DEFINE BAD\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

		AnalysisResult result = workspace.Analyze();

		Assert.IsFalse(result.Success, result.Log);

		string commonFile = workspace.PathOf(Path.Combine("script", "common.inc"));

		DiagnosticAssert.HasWarning(
			diagnostics: result.Diagnostics,
			line: null,
			message: "'common.inc' has a UTF-8 byte-order mark; the file will be decoded as UTF-8",
			file: commonFile
		);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 1,
			message: "Wrong number of arguments for '#DEFINE': expected 2, got 1",
			file: commonFile
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeCycle_Fails()
	{
		using var workspace = TestWorkspace.Create(TestScripts.Language + "#INCLUDE \"Self.txt\"\n");

		workspace.WithLanguageFile("English.txt", TestWorkspace.CompilableLanguageContent);
		workspace.WithLanguageFile("Self.txt", "#INCLUDE \"Self.txt\"\n");

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A self-including file must fail instead of recursing forever.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 1,
			message: "Too many nested #INCLUDE files, past the limit of 20; check for an include cycle",
			file: workspace.PathOf(Path.Combine("script", "Self.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void IncludeOfMissingFile_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable(TestScripts.Language + "#INCLUDE \"nope.txt\"\n" + TestScripts.Level);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, "A missing #INCLUDE target must fail the compile.");

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "The include file was not found: nope.txt",
			file: workspace.ScriptFile
		);
	}
}
