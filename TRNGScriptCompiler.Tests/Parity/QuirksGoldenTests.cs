using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Parity;

/// <summary>
/// Compares the quirks fixture with the outputs the instrumented VB6 build wrote
/// for it (see <c>Fixtures/Quirks/README.md</c>).
/// </summary>
[TestClass]
public sealed class QuirksGoldenTests(TestContext testContext)
{
	private const double Seed = TestWorkspace.DefaultDeterministicSeed;

	[TestMethod]
	[TestCategory("Parity")]
	[DataRow("English")]
	[DataRow("French")]
	[DataRow("German")]
	public void QuirksWorkspace_MatchesTheVb6Outputs(string mainLanguage)
	{
		// The port publishes only the main language, so each row compiles with a different main language. Script.dat does not
		// depend on the main language, so every row compares it with the same golden.
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().CopyTreeFrom(TestFixtures.QuirksWorkspaceDirectory);

		TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage, deterministicSeed: Seed));

		Assert.IsTrue(result.Success, result.Log);

		GoldenAssert.FileMatches(ReadGolden("script.dat"), workspace.PathOf("Script.dat"), testContext);
		GoldenAssert.FileMatches(ReadGolden(mainLanguage + ".dat"), workspace.PathOf(mainLanguage + ".dat"), testContext);
		GoldenAssert.FileMatches(ReadGolden("@image4.bmp"), workspace.PathOf(Path.Combine("pix", "@image4.bmp")), testContext);
	}

	// VB6 writes its own casing, such as ENGLISH.DAT, so the golden is found without regard to case.
	private static byte[] ReadGolden(string fileName)
	{
		return File.ReadAllBytes(
			Directory
				.EnumerateFiles(TestFixtures.QuirksOutputDirectory)
				.Single(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
		);
	}
}
