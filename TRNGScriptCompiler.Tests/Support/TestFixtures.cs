using System.Globalization;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>Resolves the checked-in fixture locations used by the parity tests.</summary>
public static class TestFixtures
{
	/// <summary>Gets the fixture workspace directory (source project, copied verbatim for compilation).</summary>
	public static string WorkspaceDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Workspace");

	/// <summary>Gets the VB6 golden-output directory for a deterministic seed.</summary>
	/// <param name="seed">The deterministic seed whose goldens are requested.</param>
	public static string Vb6OutputDirectory(int seed) =>
		Path.Combine(path1: AppContext.BaseDirectory, path2: "Fixtures", path3: "VB6 Outputs", path4: seed.ToString(CultureInfo.InvariantCulture));

	/// <summary>
	/// Gets the quirks fixture workspace, which covers the VB6 behaviors the main workspace does not reach.
	/// </summary>
	public static string QuirksWorkspaceDirectory =>
		Path.Combine(path1: AppContext.BaseDirectory, path2: "Fixtures", path3: "Quirks", path4: "Workspace");

	/// <summary>Gets the VB6 golden-output directory of the quirks fixture, produced with seed 99999.</summary>
	public static string QuirksOutputDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Quirks", "VB6 Outputs", "99999");

	/// <summary>Gets the golden corpus directory, which holds one folder per corpus workspace.</summary>
	public static string CorpusDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Corpus");

	/// <summary>Gets the input workspace of one corpus workspace.</summary>
	/// <param name="name">The name of the corpus workspace folder.</param>
	public static string CorpusWorkspaceDirectory(string name) => Path.Combine(CorpusDirectory, name, "Workspace");

	/// <summary>
	/// Gets the stock TRLE <c>Objects.h</c>, a copy of the one the corpus workspaces hold, in <c>Fixtures/Stock/</c>.
	/// </summary>
	public static string StockObjectsHPath => Path.Combine(path1: AppContext.BaseDirectory, path2: "Fixtures", path3: "Stock", path4: "Objects.h");

	/// <summary>Gets the VB6 golden-output directory of one corpus workspace, produced with seed 99999.</summary>
	/// <param name="name">The name of the corpus workspace folder.</param>
	public static string CorpusOutputDirectory(string name) =>
		Path.Combine(path1: CorpusDirectory, path2: name, path3: "VB6 Outputs", path4: "99999");
}
