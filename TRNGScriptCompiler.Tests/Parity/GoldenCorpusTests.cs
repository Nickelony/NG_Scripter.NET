using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Parity;

/// <summary>
/// Compares each corpus workspace with the outputs the instrumented VB6 build wrote
/// for it (see <c>Fixtures/Corpus/README.md</c>).
/// </summary>
[TestClass]
public sealed class GoldenCorpusTests(TestContext testContext)
{
	private const double Seed = TestWorkspace.DefaultDeterministicSeed;

	public static IEnumerable<object[]> CorpusWorkspaces()
	{
		return Directory
			.EnumerateDirectories(TestFixtures.CorpusDirectory)
			.Select(Path.GetFileName)
			.OfType<string>()
			.Order(StringComparer.Ordinal)
			.Select(name => new object[] { name });
	}

	[TestMethod]
	[TestCategory("Parity")]
	[DynamicData(nameof(CorpusWorkspaces))]
	public void CorpusWorkspace_MatchesTheVb6Outputs(string workspaceName)
	{
		// The port publishes only the main language's .dat, so the workspace compiles once per language golden, with that
		// language as the main one. Script.dat does not depend on the main language, so every compile compares it with the
		// same golden.
		Assert.IsTrue(
			Directory.Exists(TestFixtures.CorpusWorkspaceDirectory(workspaceName)),
			$"The corpus folder {workspaceName} has no Workspace folder; add the workspace the VB6 outputs came from, or remove the folder."
		);

		string outputDirectory = TestFixtures.CorpusOutputDirectory(workspaceName);

		// VB6 writes SCRIPT.DAT, and on Linux a search pattern matches with regard to case unless told otherwise.
		var anyCase = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };

		string[] goldens = Directory.Exists(outputDirectory) ? Directory.GetFiles(outputDirectory, "*.dat", anyCase) : [];
		string? scriptGolden = goldens.SingleOrDefault(IsScriptDat);

		// A line VB6 rejects costs its workspace every output, so it belongs in a hand-derived test instead.
		Assert.IsNotNull(
			scriptGolden,
			$"{workspaceName} has no VB6 script.dat; move a line VB6 rejected to a hand-derived test, or commit the outputs."
		);

		string[] languageGoldens = [.. goldens.Where(path => !IsScriptDat(path)).Order(StringComparer.OrdinalIgnoreCase)];
		Assert.IsNotEmpty(languageGoldens, $"{workspaceName} has no language golden.");

		foreach (string golden in languageGoldens)
		{
			// VB6 names a language .dat with the casing of its File= line, such as JAPAN.DAT, so names compare without regard to case.
			string fileName = Path.GetFileName(golden);
			string mainLanguage = Path.GetFileNameWithoutExtension(fileName);

			using TestWorkspace workspace = TestWorkspace.CreateEmpty().CopyTreeFrom(TestFixtures.CorpusWorkspaceDirectory(workspaceName));

			TestCompileResult result = workspace.Compile(workspace.CreateOptions(mainLanguage, deterministicSeed: Seed));

			Assert.IsTrue(result.Success, $"Main language {mainLanguage}: {result.Log}");

			GoldenAssert.FileMatches(File.ReadAllBytes(scriptGolden), workspace.PathOf("Script.dat"), testContext);

			string? output = Directory
				.GetFiles(workspace.RootPath, "*.dat")
				.SingleOrDefault(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));

			Assert.IsNotNull(output, $"The port wrote no {fileName}.");
			GoldenAssert.FileMatches(File.ReadAllBytes(golden), output, testContext);
		}
	}

	private static bool IsScriptDat(string path) => Path.GetFileName(path).Equals("script.dat", StringComparison.OrdinalIgnoreCase);
}
