using System.Text;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class TextEncodingTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void ScriptFile_WithUtf8Bom_CompilesAsWithoutTheMark()
	{
		// PD-27: VB6 reads the mark's bytes EF BB BF as text in front of [Language] (Module1.bas:5150), so the compile fails.
		using var markedWorkspace = TestWorkspace.CreateCompilable();

		markedWorkspace.WithTextFile(
			Path.Combine("script", "Script.txt"),
			TestScripts.LanguageAndLevel,
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
		);

		using var plainWorkspace = TestWorkspace.CreateCompilable();

		TestCompileResult markedResult = markedWorkspace.Compile();
		TestCompileResult plainResult = plainWorkspace.Compile();

		Assert.IsTrue(markedResult.Success, markedResult.Log);
		Assert.IsTrue(plainResult.Success, plainResult.Log);

		DiagnosticAssert.HasWarning(
			diagnostics: markedResult.Diagnostics,
			line: null,
			message: "'Script.txt' has a UTF-8 byte-order mark; the file will be decoded as UTF-8",
			file: markedWorkspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.AreSequenceEqual(plainWorkspace.ReadScriptDat(), markedWorkspace.ReadScriptDat());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ExtraNgFileText_WithUtf8Bom_IsDecodedAndWarns()
	{
		using var workspace = TestWorkspace.CreateCompilable(languageContent: "[Strings]\nTest Level\n[ExtraNG]\n100: @diary.txt\n");
		workspace.WithTextFile(Path.Combine("script", "diary.txt"), "Café", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			diagnostics: result.Diagnostics,
			line: null,
			message: "'diary.txt' has a UTF-8 byte-order mark; the file will be decoded as UTF-8",
			file: workspace.PathOf(Path.Combine("script", "diary.txt"))
		);

		// "Café" in Windows-1252, XOR 0xA5 as the language encoder stores it.
		Assert.IsTrue(
			ByteSearch.Contains(workspace.ReadOutput("English.dat"), [0xE6, 0xC4, 0xC3, 0x4C]),
			"The text must be decoded, not copied with its mark."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow(
		"[Strings]\nTest Level\nCaf\u00E9 \u2192\n",
		3,
		"'Caf\u00E9 \u2192' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character"
	)]
	[DataRow(
		"[Strings]\nTest Level\n[ExtraNG]\n7: Caf\u00E9 \u2192\nTHE LAST STRING ENTRY\n",
		4,
		"'7: Caf\u00E9 \u2192' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character"
	)]
	public void LanguageFile_WithACharacterWindows1252CannotHold_Fails(string languageContent, int line, string expectedError)
	{
		// PD-30: only a file with a byte-order mark can carry such a character, because VB6 and the port read every other byte through
		// Windows-1252 (Module1.bas:1967, 1992); writing '?' would put text in the game that the author never wrote.
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithTextFile(Path.Combine("script", "English.txt"), languageContent, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line,
			message: expectedError,
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LevelPath_WithACharacterWindows1252CannotHold_Fails()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithTextFile(
			Path.Combine("script", "Script.txt"),
			"[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLevel= data\\caf\u2192,0\nLoadCamera= 0,0,0,0,0,0,0\n",
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
		);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 5,
			message: "'data\\caf\u2192' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.AreEqual(("Level= ".Length, "data\\caf\u2192".Length), (error.Column, error.Length), "The error marks the level path.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageFileName_WithACharacterWindows1252CannotHold_FailsAtItsFileLine()
	{
		// Script.dat names each language with the extension .DAT (Module1.bas:6768); a name Windows-1252 cannot hold would name a file
		// the engine cannot find.
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithTextFile(
			Path.Combine("script", "Script.txt"),
			"[Language]\nFile= 0, English.txt\nFile= 1, Caf\u2192.txt\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n",
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
		);

		workspace.WithLanguageFile("Caf\u2192.txt", TestWorkspace.CompilableLanguageContent);

		CompileResult result = TestWorkspace.CreateCompiler().Compile(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 3,
			message: "'Caf\u2192.txt' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.AreEqual(("File= 1, ".Length, "Caf\u2192.txt".Length), (error.Column, error.Length), "The error marks the file name.");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("PCExtensions")]
	[DataRow("PSXExtensions")]
	public void Extension_WithACharacterWindows1252CannotHold_FailsAtItsLine(string section)
	{
		// PD-30: the extension would be written as '.T' followed by the look-alike 'A', which names another file type.
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithTextFile(
			Path.Combine("script", "Script.txt"),
			$"[Language]\nFile= 0, English.txt\n[{section}]\nLevel= .TĀ\n[Level]\nName= Test Level\nLevel= data\\test,0\nLoadCamera= 0,0,0,0,0,0,0\n",
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
		);

		CompileResult result = TestWorkspace.CreateCompiler().Compile(workspace.CreateOptions());

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic error = DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,
			message: "'.TĀ' holds a character that Windows-1252 cannot represent, so the game would show another character in its place; replace the character",
			file: workspace.PathOf(Path.Combine("script", "Script.txt"))
		);

		Assert.AreEqual((0, "Level=".Length), (error.Column, error.Length), "The error marks the command of the extension.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void LevelPath_NonAscii_IsEncodedAsWindows1252()
	{
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithTextFile(
			Path.Combine("script", "Script.txt"),
			"[Language]\nFile= 0, English.txt\n[Level]\nName= Test Level\nLevel= data\\caf\u00E9,0\nLoadCamera= 0,0,0,0,0,0,0\n"
		);

		TRNGCompiler compiler = TestWorkspace.CreateCompiler();
		CompileResult result = compiler.Compile(workspace.CreateOptions());

		Assert.IsTrue(result.Success, $"A Windows-1252 level path must compile: {result.Log}");

		Assert.AreSequenceEqual(
			["data\\café"],
			ScriptDatReader.ReadFile(workspace.PathOf("Script.dat")).LevelPaths,
			"The level path must be written as Windows-1252 bytes ('caf' followed by 0xE9)."
		);
	}
}
