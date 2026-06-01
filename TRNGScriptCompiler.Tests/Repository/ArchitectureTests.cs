using System.Text.RegularExpressions;
using TRNGScriptCompiler.Cli;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>
/// Holds the library to the layer rules and the public surface that <c>docs/ARCHITECTURE.md</c> owns, so a breach fails
/// the suite.
/// </summary>
/// <remarks>
/// The scan reads text, so a <c>TRNGScriptCompiler.*</c> namespace in a <c>using</c>, a qualified name,
/// a <c>cref</c> or a comment is a reference, and so is the name of a type of the root namespace.
/// </remarks>
[TestClass]
[TestCategory("Fast")]
public sealed partial class ArchitectureTests
{
	/// <summary>Layer name of a source folder (first path segment under the library root).</summary>
	internal static readonly Dictionary<string, string> s_folderLayers = new(StringComparer.Ordinal)
	{
		["Kernel"] = "Kernel",
		["Model"] = "Model",
		["Plugins"] = "Plugins",
		["Language"] = "Language",
		["Constants"] = "Constants",
		["Ng"] = "NG",
		["Script"] = "Script",
		["Pipeline"] = "Pipeline",
		["Hosting"] = "Hosting",
	};

	/// <summary>Allowed namespace-reference edges: layer -> layers it may reference.</summary>
	internal static readonly Dictionary<string, string[]> s_allowedEdges = new(StringComparer.Ordinal)
	{
		["Kernel"] = [],
		["Model"] = ["Kernel"],
		["Plugins"] = ["Kernel", "Model"],
		["Language"] = ["Kernel", "Model"],
		["Constants"] = ["Kernel", "Model", "Plugins"],
		["NG"] = ["Kernel", "Model", "Constants", "Plugins"],
		["Script"] = ["Kernel", "Model", "Constants"],
		["Pipeline"] = ["Kernel", "Model", "Plugins", "Language", "Constants", "NG", "Script"],
		["Hosting"] = ["Kernel", "Pipeline"],
	};

	/// <summary>
	/// Allowed namespace-reference edges between the Kernel's folders: folder -> folders it may reference.
	/// </summary>
	internal static readonly Dictionary<string, string[]> s_kernelEdges = new(StringComparer.Ordinal)
	{
		["Text"] = [],
		["Numerics"] = [],
		["Diagnostics"] = [],
		["Infrastructure"] = ["Text"],
		["Runtime"] = ["Diagnostics", "Infrastructure", "Numerics", "Text"],
	};

	/// <summary>
	/// The namespaces of the public surface: a public type declares its folder's namespace
	/// when it is one of these, and the root namespace otherwise.
	/// </summary>
	internal static readonly string[] s_publicNamespaces = ["TRNGScriptCompiler", "TRNGScriptCompiler.Hosting"];

	/// <summary>The public types of the embedding surface, by full name.</summary>
	internal static readonly string[] s_publicTypes =
	[
		"TRNGScriptCompiler.AnalysisResult",
		"TRNGScriptCompiler.CompileResult",
		"TRNGScriptCompiler.CompilerDiagnostic",
		"TRNGScriptCompiler.DiagnosticSeverity",
		"TRNGScriptCompiler.Hosting.CompilationHost",
		"TRNGScriptCompiler.Hosting.DiagnosticsResult",
		"TRNGScriptCompiler.Hosting.IDiagnosticsProvider",
		"TRNGScriptCompiler.LogEntry",
		"TRNGScriptCompiler.LogEntryProducedEventArgs",
		"TRNGScriptCompiler.LogSeverity",
		"TRNGScriptCompiler.MainLanguageSelector",
		"TRNGScriptCompiler.TRNGCompiler",
		"TRNGScriptCompiler.TRNGCompilerOptions",
		"TRNGScriptCompiler.VirtualFileSystem",
	];

	/// <summary>
	/// The files of the publication stage, which write files and so list their folders afresh.
	/// <c>TRNGCompiler.cs</c> holds the publication path, and it names no resolver but the uncached one.
	/// </summary>
	private static readonly string[] s_publicationFiles =
	[
		Path.Combine("Pipeline", "OutputPublisher.cs"),
		Path.Combine("Pipeline", "OutputStaging.cs"),
		Path.Combine("Pipeline", "TRNGCompiler.cs"),
	];

	/// <summary>
	/// Maps a Kernel namespace to its folder, the third namespace segment; any other namespace maps
	/// to <see langword="null"/>.
	/// </summary>
	private static string? ResolveKernelFolder(string namespaceName)
	{
		string[] segments = namespaceName.Split('.');
		return segments.Length >= 3 && segments[0] == "TRNGScriptCompiler" && segments[1] == "Kernel" ? segments[2] : null;
	}

	/// <summary>Maps a namespace to its layer using segment-wise prefix matching.</summary>
	private static string? ResolveLayer(string namespaceName)
	{
		string[] segments = namespaceName.Split('.');

		if (segments.Length < 2 || segments[0] != "TRNGScriptCompiler")
			return null;

		return s_folderLayers.TryGetValue(segments[1], out string? layer) ? layer : null;
	}

	/// <summary>
	/// A library source, with the Kernel folder its path names, if any, and the public types it declares.
	/// </summary>
	private sealed record SourceFile(
		string Path,
		string RelativePath,
		string Layer,
		string? KernelFolder,
		string Namespace,
		string[] PublicTypes,
		string Text
	);

	private static List<SourceFile> LoadLibrarySources()
	{
		string libraryRoot = RepositoryPaths.Below("TRNGScriptCompiler");

		return
		[
			.. RepositoryPaths
				.SourceFilesBelow(libraryRoot)
				.Select(path => ParseSource(path, Path.GetRelativePath(libraryRoot, path), File.ReadAllText(path))),
		];
	}

	private static SourceFile ParseSource(string path, string relativePath, string text)
	{
		Match namespaceMatch = NamespaceDeclarationRegex().Match(text);
		string declaredNamespace = namespaceMatch.Success ? namespaceMatch.Groups[1].Value : "?";

		string[] segments = relativePath.Split(Path.DirectorySeparatorChar);
		string layer = s_folderLayers.TryGetValue(segments[0], out string? known) ? known : "?";
		string? kernelFolder = layer == "Kernel" && segments.Length > 2 ? segments[1] : null;
		string[] publicTypes = [.. PublicTypeRegex().Matches(text).Select(match => match.Groups[1].Value)];

		return new SourceFile(path, relativePath, layer, kernelFolder, Namespace: declaredNamespace, publicTypes, text);
	}

	private static List<string> ExtractNamespaceReferences(string text)
	{
		var references = new List<string>();

		foreach (Match match in NamespaceReferenceRegex().Matches(text))
			references.Add(match.Value);

		return references;
	}

	/// <summary>
	/// Finds the root-namespace types another file of the library names, which every file sees without a <c>using</c>,
	/// so the namespace scan cannot catch them.
	/// </summary>
	private static IEnumerable<SourceFile> RootTypeReferences(SourceFile source, List<SourceFile> sources)
	{
		foreach (SourceFile declaring in sources.Where(other => other.Namespace == "TRNGScriptCompiler" && other.Path != source.Path))
		{
			if (declaring.PublicTypes.Any(type => Regex.IsMatch(source.Text, $@"\b{type}\b")))
				yield return declaring;
		}
	}

	[TestMethod]
	public void LibrarySources_LiveInKnownLayers_WithMatchingNamespaces()
	{
		List<string> findings = FindNamespaceFindings(LoadLibrarySources());

		Assert.IsEmpty(
			findings,

			"Every source file must live in a known layer folder and declare its folder's namespace, or the root one for a public type:\n"
				+ string.Join("\n", findings)
		);
	}

	private static List<string> FindNamespaceFindings(List<SourceFile> sources)
	{
		var findings = new List<string>();

		foreach (SourceFile source in sources)
		{
			if (source.Layer == "?")
			{
				findings.Add($"{source.RelativePath}: file is outside every known layer folder ({string.Join(", ", s_folderLayers.Keys)}).");
				continue;
			}

			string directory = Path.GetDirectoryName(source.RelativePath) ?? string.Empty;

			string folderNamespace =
				directory.Length == 0 ? "TRNGScriptCompiler" : "TRNGScriptCompiler." + directory.Replace(Path.DirectorySeparatorChar, '.');

			// A public type lives in a public namespace, so an embedder imports the surface and never a layer.
			string expectedNamespace =
				source.PublicTypes.Length > 0 && !s_publicNamespaces.Contains(folderNamespace, StringComparer.Ordinal)
					? "TRNGScriptCompiler"
					: folderNamespace;

			if (source.Namespace != expectedNamespace)
				findings.Add($"{source.RelativePath}: declared namespace '{source.Namespace}' must be '{expectedNamespace}'.");
		}

		return findings;
	}

	// The sample holds a file outside every layer, a namespace that is not its folder's, a public type in a layer namespace,
	// and the three forms that are right: an internal type in its folder, a public type in the root and one in Hosting.
	[TestMethod]
	public void FindNamespaceFindings_ReportsEachFileOutsideItsLayerOrNamespaceAndNothingElse()
	{
		List<SourceFile> sources =
		[
			Source(Path.Combine("Orphan", "Stray.cs"), "namespace TRNGScriptCompiler.Orphan;\n\ninternal sealed class Stray;"),
			Source(Path.Combine("Model", "Wrong.cs"), "namespace TRNGScriptCompiler.Ng;\n\ninternal sealed class Wrong;"),
			Source(Path.Combine("Pipeline", "Leaky.cs"), "namespace TRNGScriptCompiler.Pipeline;\n\npublic sealed class Leaky;"),
			Source(Path.Combine("Model", "Right.cs"), "namespace TRNGScriptCompiler.Model;\n\ninternal sealed class Right;"),
			Source(Path.Combine("Pipeline", "Facade.cs"), "namespace TRNGScriptCompiler;\n\npublic sealed class Facade;"),
			Source(Path.Combine("Hosting", "Host.cs"), "namespace TRNGScriptCompiler.Hosting;\n\npublic sealed class Host;"),
		];

		Assert.AreSequenceEqual(
			[
				$"{Path.Combine("Orphan", "Stray.cs")}: file is outside every known layer folder ({string.Join(", ", s_folderLayers.Keys)}).",
				$"{Path.Combine("Model", "Wrong.cs")}: declared namespace 'TRNGScriptCompiler.Ng' must be 'TRNGScriptCompiler.Model'.",
				$"{Path.Combine("Pipeline", "Leaky.cs")}: declared namespace 'TRNGScriptCompiler.Pipeline' must be 'TRNGScriptCompiler'.",
			],

			FindNamespaceFindings(sources)
		);
	}

	[TestMethod]
	public void LibraryReferences_RespectTheLayerEdgeTable()
	{
		List<string> findings = FindLayerBreaches(LoadLibrarySources());
		Assert.IsEmpty(findings, "Layer references must follow the allowed-edge table in docs/ARCHITECTURE.md:\n" + string.Join("\n", findings));
	}

	private static List<string> FindLayerBreaches(List<SourceFile> sources)
	{
		var findings = new List<string>();

		foreach (SourceFile source in sources)
		{
			if (source.Layer == "?")
				continue;

			foreach (SourceFile declaring in RootTypeReferences(source, sources))
			{
				if (declaring.Layer != source.Layer && !s_allowedEdges[source.Layer].Contains(declaring.Layer, StringComparer.Ordinal))
				{
					findings.Add(
						$"{source.RelativePath}: {source.Layer} must not reference {declaring.Layer} ('{string.Join(", ", declaring.PublicTypes)}')."
					);
				}
			}

			foreach (string reference in ExtractNamespaceReferences(source.Text))
			{
				string? targetLayer = ResolveLayer(reference);

				if (targetLayer is null)
				{
					findings.Add(
						$"{source.RelativePath}: reference to unknown namespace '{reference}' (add the folder to the layer map or move the type)."
					);

					continue;
				}

				if (targetLayer == source.Layer)
					continue;

				if (!s_allowedEdges[source.Layer].Contains(targetLayer, StringComparer.Ordinal))
					findings.Add($"{source.RelativePath}: {source.Layer} must not reference {targetLayer} ('{reference}').");
			}
		}

		return [.. findings.Distinct()];
	}

	// The sample holds a namespace of a layer the file may not reference, a root type of such a layer, a namespace of no layer,
	// the Kernel's reach for a layer above it, and an edge the table allows.
	[TestMethod]
	public void FindLayerBreaches_ReportsEachReferenceTheEdgeTableForbidsAndNothingElse()
	{
		List<SourceFile> sources =
		[
			Source(Path.Combine("Ng", "NgParser.cs"), "namespace TRNGScriptCompiler.Ng;\n\nusing TRNGScriptCompiler.Script;\n"),
			Source(Path.Combine("Ng", "Allowed.cs"), "namespace TRNGScriptCompiler.Ng;\n\nusing TRNGScriptCompiler.Constants;\n"),
			Source(Path.Combine("Model", "Uses.cs"), "namespace TRNGScriptCompiler.Model;\n\ninternal sealed class Uses { Facade _facade; }\n"),
			Source(Path.Combine("Model", "Odd.cs"), "namespace TRNGScriptCompiler.Model;\n\n// See TRNGScriptCompiler.Elsewhere.Thing.\n"),
			Source(Path.Combine("Kernel", "Text", "Reach.cs"), "namespace TRNGScriptCompiler.Kernel.Text;\n\nusing TRNGScriptCompiler.Model;\n"),
			Source(Path.Combine("Pipeline", "Facade.cs"), "namespace TRNGScriptCompiler;\n\npublic sealed class Facade;\n"),
		];

		Assert.AreSequenceEqual(
			[
				$"{Path.Combine("Ng", "NgParser.cs")}: NG must not reference Script ('TRNGScriptCompiler.Script').",
				$"{Path.Combine("Model", "Uses.cs")}: Model must not reference Pipeline ('Facade').",
				$"{Path.Combine("Model", "Odd.cs")}: reference to unknown namespace 'TRNGScriptCompiler.Elsewhere.Thing' (add the folder to the layer map or move the type).",
				$"{Path.Combine("Kernel", "Text", "Reach.cs")}: Kernel must not reference Model ('TRNGScriptCompiler.Model').",
			],

			FindLayerBreaches(sources)
		);
	}

	[TestMethod]
	public void KernelReferences_RespectTheKernelEdgeTable()
	{
		List<string> findings = FindKernelBreaches(LoadLibrarySources());
		Assert.IsEmpty(findings, "Kernel references must follow the Kernel edge table in docs/ARCHITECTURE.md:\n" + string.Join("\n", findings));
	}

	private static List<string> FindKernelBreaches(List<SourceFile> sources)
	{
		var findings = new List<string>();

		foreach (SourceFile source in sources.Where(source => source.Layer == "Kernel"))
		{
			string? folder = source.KernelFolder;

			if (folder is null || !s_kernelEdges.TryGetValue(folder, out string[]? allowed))
			{
				findings.Add($"{source.RelativePath}: file is outside every known Kernel folder ({string.Join(", ", s_kernelEdges.Keys)}).");
				continue;
			}

			foreach (SourceFile declaring in RootTypeReferences(source, sources).Where(declaring => declaring.KernelFolder is not null))
			{
				if (declaring.KernelFolder != folder && !allowed.Contains(declaring.KernelFolder, StringComparer.Ordinal))
				{
					findings.Add(
						$"{source.RelativePath}: Kernel/{folder} must not reference Kernel/{declaring.KernelFolder} ('{string.Join(", ", declaring.PublicTypes)}')."
					);
				}
			}

			foreach (string reference in ExtractNamespaceReferences(source.Text))
			{
				string? target = ResolveKernelFolder(reference);

				if (target is not null && target != folder && !allowed.Contains(target, StringComparer.Ordinal))
					findings.Add($"{source.RelativePath}: Kernel/{folder} must not reference Kernel/{target} ('{reference}').");
			}
		}

		return [.. findings.Distinct()];
	}

	// The sample holds a file outside every Kernel folder, a folder that reaches for one above it, a root type of such a folder,
	// and an edge the table allows.
	[TestMethod]
	public void FindKernelBreaches_ReportsEachKernelReferenceTheEdgeTableForbidsAndNothingElse()
	{
		const string text = "namespace TRNGScriptCompiler.Kernel.Text;\n\n";
		const string runtime = "namespace TRNGScriptCompiler.Kernel.Runtime;\n\n";

		List<SourceFile> sources =
		[
			Source(Path.Combine("Kernel", "Loose.cs"), "namespace TRNGScriptCompiler.Kernel;\n"),
			Source(Path.Combine("Kernel", "Text", "Reach.cs"), text + "using TRNGScriptCompiler.Kernel.Runtime;\n"),
			Source(Path.Combine("Kernel", "Text", "Holds.cs"), text + "internal sealed class Holds { Gauge _gauge; }\n"),
			Source(Path.Combine("Kernel", "Runtime", "Gauge.cs"), "namespace TRNGScriptCompiler;\n\npublic sealed class Gauge;\n"),
			Source(Path.Combine("Kernel", "Runtime", "Allowed.cs"), runtime + "using TRNGScriptCompiler.Kernel.Text;\n"),
		];

		Assert.AreSequenceEqual(
			[
				$"{Path.Combine("Kernel", "Loose.cs")}: file is outside every known Kernel folder ({string.Join(", ", s_kernelEdges.Keys)}).",
				$"{Path.Combine("Kernel", "Text", "Reach.cs")}: Kernel/Text must not reference Kernel/Runtime ('TRNGScriptCompiler.Kernel.Runtime').",
				$"{Path.Combine("Kernel", "Text", "Holds.cs")}: Kernel/Text must not reference Kernel/Runtime ('Gauge').",
			],

			FindKernelBreaches(sources)
		);
	}

	private static SourceFile Source(string relativePath, string text) => ParseSource(relativePath, relativePath, text);

	[TestMethod]
	public void AllowedEdges_FormAnAcyclicGraph()
	{
		// A cycle in a rule table itself would make the architecture unsatisfiable.
		List<string> cyclic = FindCycles(s_allowedEdges);
		Assert.IsEmpty(cyclic, "The allowed-edge table must stay acyclic. Layers in a cycle: " + string.Join(", ", cyclic));

		cyclic = FindCycles(s_kernelEdges);
		Assert.IsEmpty(cyclic, "The Kernel edge table must stay acyclic. Folders in a cycle: " + string.Join(", ", cyclic));
	}

	private static List<string> FindCycles(Dictionary<string, string[]> edges)
	{
		var visiting = new HashSet<string>(StringComparer.Ordinal);
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var cyclic = new List<string>();

		void Visit(string node)
		{
			if (visited.Contains(node))
				return;

			if (!visiting.Add(node))
			{
				cyclic.Add(node);
				return;
			}

			foreach (string next in edges[node])
			{
				if (edges.ContainsKey(next))
					Visit(next);
			}

			visiting.Remove(node);
			visited.Add(node);
		}

		foreach (string node in edges.Keys)
			Visit(node);

		return cyclic;
	}

	// The sample holds a cycle of two layers and a layer that only reaches into it.
	[TestMethod]
	public void FindCycles_NamesTheLayerThatClosesACycle()
	{
		var edges = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["Entry"] = ["Left"],
			["Left"] = ["Right"],
			["Right"] = ["Left"],
		};

		Assert.AreSequenceEqual(["Left"], FindCycles(edges));
	}

	[TestMethod]
	public void PublicTypes_AreTheDocumentedEmbeddingSurface()
	{
		string[] actual =
		[
			.. typeof(TRNGCompiler).Assembly.GetExportedTypes().Select(type => type.FullName ?? type.Name).Order(StringComparer.Ordinal),
		];

		string[] differences =
		[
			.. Differences(subject: "Public types", left: s_publicTypes, leftName: "the test", right: actual, rightName: "the assembly"),
		];

		Assert.IsEmpty(differences, "The public types must match the embedding surface in docs/ARCHITECTURE.md:\n" + string.Join("\n", differences));
	}

	/// <summary>
	/// Names each entry that one list holds and the other lacks, so a comparison reports every difference at once.
	/// </summary>
	internal static IEnumerable<string> Differences(
		string subject,
		IEnumerable<string> left,
		string leftName,
		IEnumerable<string> right,
		string rightName
	)
	{
		string[] leftOnly = [.. left.Except(right, StringComparer.Ordinal)];
		string[] rightOnly = [.. right.Except(left, StringComparer.Ordinal)];

		return
		[
			.. leftOnly.Select(entry => $"{subject}: '{entry}' is in {leftName} and not in {rightName}."),
			.. rightOnly.Select(entry => $"{subject}: '{entry}' is in {rightName} and not in {leftName}."),
		];
	}

	[TestMethod]
	public void Differences_NamesEachEntryOfOneListThatTheOtherLacks()
	{
		Assert.AreSequenceEqual(
			["Edges: 'Plugins' is in the guide and not in the test.", "Edges: 'Hosting' is in the test and not in the guide."],
			Differences(subject: "Edges", left: ["Model", "Plugins"], leftName: "the guide", right: ["Hosting", "Model"], rightName: "the test")
		);

		Assert.IsEmpty(
			Differences(subject: "Edges", left: ["Model", "Plugins"], leftName: "the guide", right: ["Plugins", "Model"], rightName: "the test")
		);
	}

	[TestMethod]
	[DataRow(typeof(TRNGCompiler))]
	[DataRow(typeof(CliApplication))]
	public void InternalsVisibleTo_GrantsOnlyTheTestAssembly(Type typeInAssembly)
	{
		string[] grants =
		[
			.. typeInAssembly
				.Assembly.GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), inherit: false)
				.Cast<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
				.Select(attribute => attribute.AssemblyName),
		];

		// The console host builds against the library's public surface alone, so a grant to it would let it reach the machinery.
		Assert.AreSequenceEqual(new[] { "TRNGScriptCompiler.Tests" }, grants);
	}

	[TestMethod]
	public void ConsoleHost_DeclaresNoPublicType()
	{
		Type[] publicTypes = typeof(CliApplication).Assembly.GetExportedTypes();
		Assert.IsEmpty(publicTypes, string.Join(", ", publicTypes.Select(type => type.FullName)));
	}

	[TestMethod]
	public void PublicationStage_NamesPathResolverOnlyAsUncached()
	{
		string libraryRoot = RepositoryPaths.Below("TRNGScriptCompiler");

		string[] findings =
		[
			.. s_publicationFiles.SelectMany(file => FindCachedResolverUses(File.ReadAllText(Path.Combine(libraryRoot, file)), file)),
		];

		Assert.IsEmpty(
			findings,

			"The run's PathResolver keeps the folder listings of the analysis, which a publication's own writes make stale, so the publication stage resolves "
				+ "through PathResolver.Uncached (docs/ARCHITECTURE.md, Run state):\n"
				+ string.Join("\n", findings)
		);
	}

	// The sample holds the three ways a file reaches the cached resolver, and the one way it may not.
	[TestMethod]
	public void FindCachedResolverUses_ReportsEachNameOfTheResolverButUncached()
	{
		const string sample = """
			class Sample
			{
				readonly PathResolver _paths;

				void Publish(CompilationRun run)
				{
					string uncached = PathResolver.Uncached.ResolveFullPath("a");
					string cached = run.PathResolver.ResolveFullPath("b");
					var resolver = new PathResolver();
				}
			}
			""";

		Assert.AreSequenceEqual(
			[
				"Sample.cs:3: readonly PathResolver _paths;",
				"Sample.cs:8: string cached = run.PathResolver.ResolveFullPath(\"b\");",
				"Sample.cs:9: var resolver = new PathResolver();",
			],

			FindCachedResolverUses(sample, "Sample.cs")
		);
	}

	private static IEnumerable<string> FindCachedResolverUses(string text, string file)
	{
		string[] lines = text.Split('\n');

		for (int index = 0; index < lines.Length; index++)
		{
			if (CachedResolverRegex().IsMatch(lines[index]))
				yield return $"{file}:{index + 1}: {lines[index].Trim()}";
		}
	}

	[GeneratedRegex(@"\bPathResolver\b(?!\.Uncached\b)")]
	private static partial Regex CachedResolverRegex();

	[GeneratedRegex(@"(?m)^namespace\s+([\w\.]+);")]
	private static partial Regex NamespaceDeclarationRegex();

	[GeneratedRegex(@"TRNGScriptCompiler(?:\.[A-Za-z_]\w*)+")]
	private static partial Regex NamespaceReferenceRegex();

	[GeneratedRegex(
		@"(?m)^public\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:class|record|struct|interface|enum)\s+(?:class\s+|struct\s+)?(\w+)"
	)]
	private static partial Regex PublicTypeRegex();
}
