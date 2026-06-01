using TRNGScriptCompiler.Script;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
public sealed class LanguageSectionBuilderTests
{
	private static (LanguageSectionBuilder Builder, UnitRun Unit) CreateBuilder()
	{
		var unit = new UnitRun();
		return (new LanguageSectionBuilder(unit.Run.Diagnostics, unit.Evaluator), unit);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void FileLines_ListTheLanguagesInIndexOrder()
	{
		// VB6 reads the index with GetArgNumerico, so $1 names index 1 (Module1.bas:5493), and takes the extension without regard to case
		// (:5501, Option Compare Text at :4).
		(LanguageSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsTrue(builder.TryAdd(unit.CommandLine(2, "File= 0, English.txt")));
		Assert.IsTrue(builder.TryAdd(unit.CommandLine(3, "file= $1, FRENCH.TXT")));

		Assert.AreSequenceEqual(["English.txt", "FRENCH.TXT"], builder.LanguageFiles);
		DiagnosticAssert.NoDiagnostics(unit.Diagnostics);
	}

	[TestMethod]
	[TestCategory("Fast")]
	[DataRow("Name= English.txt", "Unknown command for [Language] section: Name=; correct its name or remove the line")]
	[DataRow("File= 0", "Wrong number of arguments for 'File=': expected 2, got 1")]
	[DataRow("File= 1, English.txt", "Unexpected index 1 for language file; number the File= lines from 0 in order, so this one is 0")]
	[DataRow("File= FIRST, English.txt", "The index of the language file is invalid: FIRST is neither a number nor a known name")]
	[DataRow("File= 0, English.dat", "The language file 'English.dat' lacks the .txt extension; write the name of a .txt file")]
	public void InvalidFileLine_Fails(string line, string expectedError)
	{
		// VB6 takes File= alone, with two arguments, an index that counts the files read so far and a .txt name (Module1.bas:5481-5505).
		(LanguageSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsFalse(builder.TryAdd(unit.CommandLine(2, line)));
		DiagnosticAssert.SingleError(unit.Diagnostics, 2, expectedError);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LanguageInAFolder_Fails()
	{
		// Script.dat names the language with its folder (Module1.bas:6768), but VB6 fails to copy such a main language (:8968), so
		// the engine could not open it.
		(LanguageSectionBuilder builder, UnitRun unit) = CreateBuilder();

		Assert.IsFalse(builder.TryAdd(unit.CommandLine(2, @"File= 0, lang\English.txt")));

		DiagnosticAssert.SingleError(
			unit.Diagnostics,
			2,
			@"The language file 'lang\English.txt' names a folder, so Script.dat would name a language file the engine cannot find; move the file into the script folder and write its name alone"
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void NinthLanguage_Fails()
	{
		// VB6 fails a ninth File= line (Module1.bas:5516-5520).
		(LanguageSectionBuilder builder, UnitRun unit) = CreateBuilder();

		for (int index = 0; index < 8; index++)
			Assert.IsTrue(builder.TryAdd(unit.CommandLine(index + 2, $"File= {index}, Language{index}.txt")));

		Assert.IsFalse(builder.TryAdd(unit.CommandLine(10, "File= 8, Language8.txt")));
		DiagnosticAssert.SingleError(unit.Diagnostics, 10, "Too many language files; a script lists at most 8");
	}

	[TestMethod]
	[TestCategory("IO")]
	[DataRow("lang\\English.txt")]
	[DataRow("lang/English.txt")]
	[DataRow("C:English.txt")]
	public void LanguageFileNameWithAFolder_Fails(string fileName)
	{
		// PD-58: VB6 writes the name with its folder into Script.dat (Module1.bas:6768) and fails to copy such a main language
		// (:8968), while the port publishes the .dat under its bare name, so the engine could not open the language.
		using var workspace = TestWorkspace.CreateCompilable($"[Language]\nFile= 0, {fileName}\n" + TestScripts.Level);
		workspace.WithLanguageFile(Path.Combine("lang", "English.txt"), TestWorkspace.CompilableLanguageContent);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		CompilerDiagnostic diagnostic = result.Diagnostics.First(entry => entry.IsError);

		Assert.AreEqual(
			$"The language file '{fileName}' names a folder, so Script.dat would name a language file the engine cannot find; move the file into the script folder and write its name alone",
			diagnostic.Message
		);

		Assert.AreEqual(2, diagnostic.LineNumber);
	}
}
