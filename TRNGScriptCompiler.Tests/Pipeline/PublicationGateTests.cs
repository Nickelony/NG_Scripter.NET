using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

/// <summary>
/// A fatal diagnostic recorded while composing the outputs stops the compile before any output is written.
/// </summary>
[TestClass]
public sealed class PublicationGateTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void ErrorDuringComposition_PublishesNothing()
	{
		// image4.bmp does not exist, so composing the Image= command records a fatal diagnostic.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel + "Image= 1,4,IF_CRYPTIC,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n"
		);

		workspace.WithBinaryFile("English.dat", [1, 2, 3]);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);
		DiagnosticAssert.SingleError(result.Diagnostics, 7, $"Cannot find image4.bmp to encrypt in {workspace.PathOf("pix")} directory");
		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "A failed compile must not publish Script.dat.");

		Assert.AreSequenceEqual(
			new byte[] { 1, 2, 3 },
			workspace.ReadOutput("English.dat"),
			"A failed compile must leave the previous outputs alone."
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ErrorAfterTheImageIsComposed_LeavesTheImageUnencrypted()
	{
		// VB6 encrypts the image and deletes imageN.bmp while it writes script.dat (Module1.bas:8161, 8505-8508), before it writes the
		// language files, so an error there leaves the image encrypted and the outputs unwritten. The port encrypts during publication.
		using var workspace = TestWorkspace.CreateCompilable(
			TestScripts.LanguageAndLevel + "Image= 1,4,IF_CRYPTIC,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n",
			languageContent: "[Strings]\nTest Level\n[ExtraNG]\n5: @missing.txt\n"
		);

		byte[] image = [.. Enumerable.Range(0, 300).Select(index => (byte)index)];
		workspace.WithBinaryFile(Path.Combine("pix", "image4.bmp"), image);

		TestCompileResult result = workspace.Compile();

		Assert.IsFalse(result.Success, result.Log);

		DiagnosticAssert.SingleError(
			diagnostics: result.Diagnostics,
			line: 4,
			message: "Could not find referenced file: missing.txt",
			file: workspace.PathOf(Path.Combine("script", "English.txt"))
		);

		Assert.AreSequenceEqual(image, workspace.ReadOutput(Path.Combine("pix", "image4.bmp")), "The image is left as it was.");

		Assert.IsFalse(File.Exists(workspace.PathOf(Path.Combine("pix", "@image4.bmp"))), "No encrypted copy is written.");
		Assert.IsFalse(File.Exists(workspace.PathOf("Script.dat")), "A failed compile publishes nothing.");
	}
}
