namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>
/// Holds the literal tables of <see cref="ArchitectureTests"/> to the tables of <c>docs/ARCHITECTURE.md</c>,
/// so the guide and the test cannot differ.
/// </summary>
/// <remarks>
/// The guide's tables are read as pipe-separated rows under their headings: the layers and the Kernel
/// folders under <c>## Layers</c>, and the public types under <c>## Public surface</c>.
/// </remarks>
[TestClass]
[TestCategory("Repository")]
public sealed class ArchitectureGuideTests
{
	private const string GuideName = "the guide";
	private const string LiteralName = "ArchitectureTests";

	[TestMethod]
	public void TheTablesOfTheArchitectureGuide_EqualTheLiteralsOfArchitectureTests()
	{
		string guide = File.ReadAllText(RepositoryPaths.Below("docs", "ARCHITECTURE.md"));

		var literals = new Literals(
			FolderLayers: ArchitectureTests.s_folderLayers,
			AllowedEdges: ArchitectureTests.s_allowedEdges,
			KernelEdges: ArchitectureTests.s_kernelEdges,
			PublicTypes: ArchitectureTests.s_publicTypes,
			PublicNamespaces: ArchitectureTests.s_publicNamespaces
		);

		List<string> differences = FindDifferences(guide, literals);

		Assert.IsEmpty(
			differences,

			"docs/ARCHITECTURE.md and the tables of ArchitectureTests must state the same layers, edges and public types:\n"
				+ string.Join("\n", differences)
		);
	}

	// The sample guide differs from its literals by a folder, an edge of a layer, a Kernel folder and an edge of another, a public
	// type and a public namespace, and each difference is named.
	[TestMethod]
	public void FindDifferences_NamesEachEntryThatTheGuideAndTheLiteralsDoNotShare()
	{
		const string guide = """
			# Architecture

			## Layers

			| Layer | Folder | Holds | May reference |
			| --- | --- | --- | --- |
			| Kernel | `Kernel/` | The run state | Nothing outside the Kernel |
			| Model | `Model/` | The data | Kernel |
			| NG | `Ng/` | The commands | Kernel, Model |

			The Kernel's folders follow a table of their own:

			| Kernel folder | May reference |
			| --- | --- |
			| `Kernel/Text` | No other Kernel folder |
			| `Kernel/Runtime` | `Kernel/Text`, `Kernel/Numerics` |

			## Public surface

			| Area | Namespace | Public types |
			| --- | --- | --- |
			| Entry | `TRNGScriptCompiler` | `Compiler`, `Result` |
			| Hosting | `TRNGScriptCompiler.Hosting` | `Host` |
			""";

		var folderLayers = new Dictionary<string, string>
		{
			["Kernel"] = "Kernel",
			["Model"] = "Model",
			["Ng"] = "NG",
			["Script"] = "Script",
		};

		var allowedEdges = new Dictionary<string, string[]>
		{
			["Kernel"] = [],
			["Model"] = ["Kernel"],
			["NG"] = ["Kernel"],
			["Script"] = ["Kernel"],
		};

		var kernelEdges = new Dictionary<string, string[]>
		{
			["Text"] = [],
			["Numerics"] = [],
			["Runtime"] = ["Text"],
		};

		var literals = new Literals(
			folderLayers,
			allowedEdges,
			kernelEdges,
			PublicTypes: ["TRNGScriptCompiler.Compiler", "TRNGScriptCompiler.Hosting.Host"],
			PublicNamespaces: ["TRNGScriptCompiler"]
		);

		Assert.AreSequenceEqual(
			[
				"Folder layers: 'Script -> Script' is in ArchitectureTests and not in the guide.",
				"Layer edges: 'NG -> Model' is in the guide and not in ArchitectureTests.",
				"Layer edges: 'Script -> Kernel' is in ArchitectureTests and not in the guide.",
				"Kernel edges: 'Runtime -> Numerics' is in the guide and not in ArchitectureTests.",
				"Kernel edges: 'Numerics' is in ArchitectureTests and not in the guide.",
				"Public types: 'TRNGScriptCompiler.Result' is in the guide and not in ArchitectureTests.",
				"Public namespaces: 'TRNGScriptCompiler.Hosting' is in the guide and not in ArchitectureTests.",
			],

			FindDifferences(guide, literals)
		);
	}

	[TestMethod]
	public void FindDifferences_ReportsEachTableThatTheGuideLacks()
	{
		var literals = new Literals(
			FolderLayers: new Dictionary<string, string>(),
			AllowedEdges: new Dictionary<string, string[]>(),
			KernelEdges: new Dictionary<string, string[]>(),
			PublicTypes: [],
			PublicNamespaces: []
		);

		Assert.AreSequenceEqual(
			[
				"The guide holds no table 1 under '## Layers'.",
				"The guide holds no table 2 under '## Layers'.",
				"The guide holds no table 1 under '## Public surface'.",
			],

			FindDifferences("# Architecture\n\n## Layers\n\nNo table here.\n", literals)
		);
	}

	private static List<string> FindDifferences(string guide, Literals literals)
	{
		var differences = new List<string>();

		List<string[]> layerRows = Rows(guide, heading: "Layers", index: 0, columns: 4, differences);
		List<string[]> kernelRows = Rows(guide, heading: "Layers", index: 1, columns: 2, differences);
		List<string[]> surfaceRows = Rows(guide, heading: "Public surface", index: 0, columns: 3, differences);

		// A layer's folder is the name of its folder less the slash, and the cell lists the layers it may reference.
		string[] guideFolders = [.. layerRows.Select(row => $"{row[1].Trim('`').TrimEnd('/')} -> {row[0]}")];
		string[] guideEdges = [.. layerRows.SelectMany(row => References(row[3], string.Empty).Select(target => $"{row[0]} -> {target}"))];

		string[] guideKernel =
		[
			.. kernelRows.SelectMany(row =>
				new[] { FolderOf(row[0]) }.Concat(References(row[1], "Kernel/").Select(target => $"{FolderOf(row[0])} -> {target}"))
			),
		];

		string[] guideTypes = [.. surfaceRows.SelectMany(row => row[2].Split(',').Select(type => $"{row[1].Trim('`')}.{type.Trim().Trim('`')}"))];
		string[] guideNamespaces = [.. surfaceRows.Select(row => row[1].Trim('`')).Distinct(StringComparer.Ordinal)];

		string[] literalFolders = [.. literals.FolderLayers.Select(pair => $"{pair.Key} -> {pair.Value}")];
		string[] literalEdges = [.. literals.AllowedEdges.SelectMany(pair => pair.Value.Select(target => $"{pair.Key} -> {target}"))];

		string[] literalKernel =
		[
			.. literals.KernelEdges.SelectMany(pair => new[] { pair.Key }.Concat(pair.Value.Select(target => $"{pair.Key} -> {target}"))),
		];

		differences.AddRange(Compare(subject: "Folder layers", guideEntries: guideFolders, literalEntries: literalFolders));
		differences.AddRange(Compare(subject: "Layer edges", guideEntries: guideEdges, literalEntries: literalEdges));
		differences.AddRange(Compare(subject: "Kernel edges", guideEntries: guideKernel, literalEntries: literalKernel));
		differences.AddRange(Compare(subject: "Public types", guideEntries: guideTypes, literalEntries: literals.PublicTypes));
		differences.AddRange(Compare(subject: "Public namespaces", guideEntries: guideNamespaces, literalEntries: literals.PublicNamespaces));

		return differences;
	}

	private static IEnumerable<string> Compare(string subject, IEnumerable<string> guideEntries, IEnumerable<string> literalEntries) =>
		ArchitectureTests.Differences(subject, left: guideEntries, leftName: GuideName, right: literalEntries, rightName: LiteralName);

	private static string FolderOf(string cell) => cell.Trim('`').Replace("Kernel/", string.Empty, StringComparison.Ordinal);

	// A cell that names no layer or folder says so in words, and any other lists its names after commas.
	private static IEnumerable<string> References(string cell, string prefix)
	{
		if (cell.StartsWith("Nothing", StringComparison.Ordinal) || cell.StartsWith("No other", StringComparison.Ordinal))
			yield break;

		foreach (string part in cell.Split(','))
		{
			string name = part.Trim().Trim('`');
			yield return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
		}
	}

	/// <summary>
	/// The body rows of the table at an index under a heading, each split into its cells; a row of another width
	/// is a difference.
	/// </summary>
	private static List<string[]> Rows(string guide, string heading, int index, int columns, List<string> differences)
	{
		string[] lines = guide.ReplaceLineEndings("\n").Split('\n');
		int start = Array.FindIndex(lines, line => line.TrimEnd() == "## " + heading);

		var tables = new List<List<string>>();
		bool inTable = false;

		for (int line = start + 1; start >= 0 && line < lines.Length && !lines[line].StartsWith("## ", StringComparison.Ordinal); line++)
		{
			if (!lines[line].StartsWith('|'))
			{
				inTable = false;
				continue;
			}

			if (!inTable)
				tables.Add([]);

			inTable = true;
			tables[^1].Add(lines[line]);
		}

		if (index >= tables.Count)
		{
			differences.Add($"The guide holds no table {index + 1} under '## {heading}'.");
			return [];
		}

		var rows = new List<string[]>();

		// The first two lines of a table are its header and its separator.
		foreach (string line in tables[index].Skip(2))
		{
			string[] cells = [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];

			if (cells.Length == columns)
				rows.Add(cells);
			else
				differences.Add($"A row of table {index + 1} under '## {heading}' holds {cells.Length} cells, not {columns}: {line}");
		}

		return rows;
	}

	private sealed record Literals(
		IReadOnlyDictionary<string, string> FolderLayers,
		IReadOnlyDictionary<string, string[]> AllowedEdges,
		IReadOnlyDictionary<string, string[]> KernelEdges,
		IReadOnlyCollection<string> PublicTypes,
		IReadOnlyCollection<string> PublicNamespaces
	);
}
