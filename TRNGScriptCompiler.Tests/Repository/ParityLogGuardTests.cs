using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>
/// Holds the parity decision log to the tests it cites and to the sections of the differences pages it links.
/// </summary>
/// <remarks>
/// The last column of a row lists its tests, apart from the notes in parentheses, and commas and semicolons
/// separate the citations. A link of the decision column names a topic page below <c>docs/differences</c> and a heading
/// of that page. A citation is a class, a method of the class cited before it, <c>Class.Method</c>, or a list of corpus
/// workspaces in backticks. A heading makes the anchor GitHub makes of it, and a heading that repeats gains <c>-1</c>,
/// <c>-2</c> and so on.
/// </remarks>
[TestClass]
[TestCategory("Repository")]
public sealed partial class ParityLogGuardTests
{
	private const int ColumnCount = 5;
	private const int DecisionColumn = 2;
	private const int TestsColumn = 4;

	[GeneratedRegex(@"^\|\s*(?<id>PD-\d+)\s*\|", RegexOptions.CultureInvariant)]
	private static partial Regex LogRowStart();

	[GeneratedRegex(@"\([^()]*\)", RegexOptions.CultureInvariant)]
	private static partial Regex Note();

	[GeneratedRegex(@"\]\((?<path>[^)#\s]+)#(?<anchor>[^)\s]+)\)", RegexOptions.CultureInvariant)]
	private static partial Regex DetailsLink();

	[GeneratedRegex(@"^(?<class>\w+)\.(?<method>\w+)$", RegexOptions.CultureInvariant)]
	private static partial Regex QualifiedTestName();

	[GeneratedRegex(@"^\w+$", RegexOptions.CultureInvariant)]
	private static partial Regex BareName();

	[GeneratedRegex(@"^(?<prefix>corpus )?`(?<name>\w+)`$", RegexOptions.CultureInvariant)]
	private static partial Regex CorpusWorkspace();

	[GeneratedRegex(@"^#{1,6} +(?<text>.+)$", RegexOptions.CultureInvariant)]
	private static partial Regex Heading();

	[GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownLink();

	[TestMethod]
	public void EveryTestAndAnchorTheParityLogNames_Exists()
	{
		string testRoot = RepositoryPaths.Below("TRNGScriptCompiler.Tests");
		string log = File.ReadAllText(RepositoryPaths.Below("docs", "PARITY-DECISIONS.md"));

		Dictionary<string, string> pages = ReadDifferencesPages();

		HashSet<string> workspaces =
		[
			.. Directory.EnumerateDirectories(Path.Combine(testRoot, "Fixtures", "Corpus")).Select(path => Path.GetFileName(path)),
		];

		Dictionary<string, HashSet<string>> tests = IndexTypes(RepositoryPaths.SourceFilesBelow(testRoot).Select(File.ReadAllText));

		Assert.IsTrue(tests.ContainsKey(nameof(ParityLogGuardTests)), "The scan must read the feature folders.");
		Assert.Contains("PD-01", ReadRows(log).Select(row => row.Id), "The scan must read the rows of the log.");
		Assert.Contains("GoldenCorpusTests", tests.Keys, "The scan must read the parity tests.");
		Assert.Contains("differences/language-files.md", pages.Keys, "The scan must read the topic pages.");

		string[] offenders = [.. FindOffenders(log, pages, tests, workspaces)];

		Assert.IsEmpty(
			offenders,

			"A row of docs/PARITY-DECISIONS.md cites a test, a corpus workspace or a section of the differences pages that does not exist:\n"
				+ string.Join("\n", offenders)
		);
	}

	// The sample holds each form of citation the guard reads, a partial class, and a heading that repeats. Its offenders are
	// a class, a method, a bare name, a corpus workspace, a citation of no form and an empty cell, and a page and an anchor.
	[TestMethod]
	public void FindOffenders_ReportsEachMissingTestAndAnchorAndNothingElse()
	{
		const string sources = """
			public partial class Alpha
			{
				public void Found() { }
			}

			public partial class Alpha
			{
				public void FromPartial() { }
			}

			public class Beta
			{
				public void Present() { }
			}
			""";

		const string topic = """
			# Differences in spaces

			```text
			## Fenced
			```

			## PD-02: spaces before `=`

			## Again

			## Again

			## It's [linked](Elsewhere.md) text
			""";

		const string log = """
			| ID | Topic | Decision | VB6 citation | Enforcing test |
			|---|---|---|---|---|
			| PD-01 | a | Intentional ([details](differences/spaces.md#again-1)) | b | Alpha.Found, FromPartial (a note, with a comma); Beta |
			| PD-02 | a | Intentional ([details](differences/spaces.md#pd-02-spaces-before-)) | b | Beta.Present; corpus `Sample`, `Other` |
			| PD-03 | a | Match VB6 | b | Alpha.Found; Gamma.Lost; Beta.Missing; Present; Nothing |
			| PD-04 | a | Intentional ([details](differences/spaces.md#pd-09-nowhere)) | b | Beta.Present |
			| PD-05 | a | Match VB6 | b | corpus `Nowhere`; see the notes |
			| PD-06 | a | Match VB6 | b |  |
			| PD-07 | a | Intentional ([details](differences/spaces.md#fenced)) | b | Beta.Present |
			| PD-08 | a | Intentional ([details](differences/spaces.md#its-linked-text)) | b | Beta.Present |
			| PD-09 | a | Match VB6 | b | Beta.Present | one cell too many |
			| PD-10 | a | Intentional ([details](differences/elsewhere.md#pd-02-spaces-before-)) | b | Beta.Present |
			| PD-11 | a | Intentional ([details](DIFFERENCES-FROM-VB6.md#again)) | b | Beta.Present |
			""";

		Assert.AreSequenceEqual(
			[
				"PD-03: Gamma is no test class",
				"PD-03: Beta.Missing is no method of Beta",
				"PD-03: Nothing is no test class and no method of Beta",
				"PD-04: the link differences/spaces.md#pd-09-nowhere names no heading of that page",
				"PD-05: Nowhere is no workspace of the corpus",
				"PD-05: 'see the notes' is no form of citation the guard reads",
				"PD-06: the cell cites no test",
				"PD-07: the link differences/spaces.md#fenced names no heading of that page",
				"PD-09: the row has 6 cells, not 5",
				"PD-10: the link differences/elsewhere.md#pd-02-spaces-before- names no page of docs",
				"PD-11: the link DIFFERENCES-FROM-VB6.md#again names no page of docs",
			],

			FindOffenders(
				log,
				pages: new Dictionary<string, string> { ["differences/spaces.md"] = topic },
				tests: IndexTypes([sources]),
				workspaces: ["Sample", "Other"]
			)
		);
	}

	// Every topic page below docs/differences, by the path a link of the log writes. The index lists the topic pages and holds no
	// section, so a link of the log never points into it.
	private static Dictionary<string, string> ReadDifferencesPages()
	{
		var pages = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (string path in Directory.EnumerateFiles(RepositoryPaths.Below("docs", "differences"), "*.md"))
			pages[$"differences/{Path.GetFileName(path)}"] = File.ReadAllText(path);

		return pages;
	}

	private static IEnumerable<LogRow> ReadRows(string log)
	{
		return log.ReplaceLineEndings("\n")
			.Split('\n')
			.Select(line => line.Trim())
			.Where(line => LogRowStart().IsMatch(line))
			.Select(line => new LogRow(LogRowStart().Match(line).Groups["id"].Value, [.. line.Trim('|').Split('|').Select(cell => cell.Trim())]));
	}

	private static IEnumerable<string> FindOffenders(
		string log,
		Dictionary<string, string> pages,
		Dictionary<string, HashSet<string>> tests,
		HashSet<string> workspaces
	)
	{
		var anchorsByPage = pages.ToDictionary(page => page.Key, page => HeadingAnchors(page.Value), StringComparer.Ordinal);
		var offenders = new List<string>();

		foreach (LogRow row in ReadRows(log))
		{
			if (row.Cells.Length != ColumnCount)
			{
				offenders.Add($"{row.Id}: the row has {row.Cells.Length} cells, not {ColumnCount}");
				continue;
			}

			foreach (Match link in DetailsLink().Matches(row.Cells[DecisionColumn]))
			{
				string path = link.Groups["path"].Value;
				string anchor = link.Groups["anchor"].Value;

				if (!anchorsByPage.TryGetValue(path, out HashSet<string>? anchors))
					offenders.Add($"{row.Id}: the link {path}#{anchor} names no page of docs");
				else if (!anchors.Contains(anchor))
					offenders.Add($"{row.Id}: the link {path}#{anchor} names no heading of that page");
			}

			offenders.AddRange(CheckCitations(id: row.Id, cell: row.Cells[TestsColumn], tests, workspaces));
		}

		return offenders;
	}

	// The citations of one cell, each checked against the test sources or the corpus.
	private static List<string> CheckCitations(string id, string cell, Dictionary<string, HashSet<string>> tests, HashSet<string> workspaces)
	{
		var offenders = new List<string>();

		string? current = null;

		bool inCorpusList = false;
		bool cited = false;

		foreach (string piece in Note().Replace(cell, string.Empty).Split(',', ';'))
		{
			string token = piece.Trim().TrimEnd('.');

			if (token.Length == 0)
				continue;

			cited = true;

			Match corpus = CorpusWorkspace().Match(token);

			if (corpus.Success && (corpus.Groups["prefix"].Success || inCorpusList))
			{
				inCorpusList = true;

				if (!workspaces.Contains(corpus.Groups["name"].Value))
					offenders.Add($"{id}: {corpus.Groups["name"].Value} is no workspace of the corpus");

				continue;
			}

			inCorpusList = false;

			Match qualified = QualifiedTestName().Match(token);

			if (qualified.Success)
			{
				current = qualified.Groups["class"].Value;

				if (!tests.TryGetValue(current, out HashSet<string>? methods))
					offenders.Add($"{id}: {current} is no test class");
				else if (!methods.Contains(qualified.Groups["method"].Value))
					offenders.Add($"{id}: {token} is no method of {current}");
			}
			else if (BareName().IsMatch(token))
			{
				if (tests.ContainsKey(token))
					current = token;
				else if (current is null)
					offenders.Add($"{id}: {token} is no test class");
				else if (tests.TryGetValue(current, out HashSet<string>? methods) && !methods.Contains(token))
					offenders.Add($"{id}: {token} is no test class and no method of {current}");
			}
			else
			{
				offenders.Add($"{id}: '{token}' is no form of citation the guard reads");
			}
		}

		if (!cited)
			offenders.Add($"{id}: the cell cites no test");

		return offenders;
	}

	// Every type of the sources by name, with the methods its declarations hold, a partial class's included.
	private static Dictionary<string, HashSet<string>> IndexTypes(IEnumerable<string> sources)
	{
		var types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

		foreach (string source in sources)
		{
			SyntaxNode root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();

			foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
			{
				if (!types.TryGetValue(declaration.Identifier.Text, out HashSet<string>? methods))
					types[declaration.Identifier.Text] = methods = new HashSet<string>(StringComparer.Ordinal);

				methods.UnionWith(declaration.Members.OfType<MethodDeclarationSyntax>().Select(method => method.Identifier.Text));
			}
		}

		return types;
	}

	// The anchors GitHub makes of the headings: lowercase, spaces to hyphens, every other character but a letter, a digit, `-` and `_`
	// dropped, and a repeated heading numbered from -1. A line inside a code fence is no heading.
	private static HashSet<string> HeadingAnchors(string page)
	{
		var anchors = new HashSet<string>(StringComparer.Ordinal);
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);

		bool inFence = false;

		foreach (string line in page.ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
			{
				inFence = !inFence;
				continue;
			}

			Match heading = Heading().Match(line);

			if (inFence || !heading.Success)
				continue;

			string text = MarkdownLink().Replace(heading.Groups["text"].Value.Trim(), "${text}");

			string slug = string.Concat(
				text.ToLowerInvariant()
					.Select(letter =>
						letter == ' ' ? "-"
						: char.IsLetterOrDigit(letter) || letter is '-' or '_' ? letter.ToString()
						: string.Empty
					)
			);

			int repeats = counts.GetValueOrDefault(slug);
			counts[slug] = repeats + 1;
			anchors.Add(repeats == 0 ? slug : $"{slug}-{repeats}");
		}

		return anchors;
	}

	// A row of the log: its ID and the text of each cell.
	private sealed record LogRow(string Id, string[] Cells);
}
