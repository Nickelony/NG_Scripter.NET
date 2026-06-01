using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class LeftoverFileWarningTests
{
	private const string ImageScriptFormat = TestScripts.ImageLevel + "Image= 1,4,{0},IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n";

	[TestMethod]
	[TestCategory("IO")]
	public void LanguageDatFilesInTheScriptFolder_Warn()
	{
		// VB6 leaves a .dat for every language in script\, and NG Center's language switch copies the selected one into the engine folder
		// when that folder lacks it (Principale.frm:2654-2690).
		using var workspace = TestWorkspace.CreateCompilable();

		workspace.WithScriptBinaryFile("French.dat", [0x00]);
		workspace.WithScriptBinaryFile("English.DAT", [0x00]);
		workspace.WithScriptBinaryFile("Script.dat", [0x00]);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			result.Diagnostics,
			line: null,
			"The script folder holds English.DAT, French.dat, which NG Center's language switch copies into the engine folder, so a stale language can reach the game; delete them"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImageWithoutIfCrypticWhoseOnlyCopyIsEncrypted_Warns()
	{
		// IF_CRYPTIC encryption deletes imageN.bmp (Module1.bas:8161), so a script that later drops the flag names a bitmap that is gone.
		using var workspace = TestWorkspace.Create(string.Format(System.Globalization.CultureInfo.InvariantCulture, ImageScriptFormat, "IGNORE"));
		workspace.WithLanguageFile("English.txt", "[Strings]\nImage Level\n");
		workspace.WithBinaryFile(Path.Combine("pix", "@image4.bmp"), new byte[300]);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);

		DiagnosticAssert.HasWarning(
			result.Diagnostics,
			10,
			"The pix folder holds only @image4.bmp, the encrypted copy of image4.bmp, and without IF_CRYPTIC the engine looks for image4.bmp; add IF_CRYPTIC, or restore image4.bmp"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ImageWithoutIfCrypticThatExists_DoesNotWarn()
	{
		using var workspace = TestWorkspace.Create(
			TestWorkspace.ExtensionSectionsContent + string.Format(System.Globalization.CultureInfo.InvariantCulture, ImageScriptFormat, "IGNORE")
		);

		workspace.WithLanguageFile("English.txt", "[Strings]\nImage Level\n").WithObjectsH(TestWorkspace.CleanObjectsHContent);

		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), new byte[300]);
		workspace.WithBinaryFile(Path.Combine("pix", "@image4.bmp"), new byte[300]);

		TestCompileResult result = workspace.Compile();

		Assert.IsTrue(result.Success, result.Log);
		DiagnosticAssert.NoErrorsOrWarnings(result.Diagnostics);
	}
}
