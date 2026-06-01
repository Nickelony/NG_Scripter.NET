using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>Guards the line ceilings Vale cannot count, and that each Vale length rule keeps its max.</summary>
[TestClass]
[TestCategory("Repository")]
public sealed partial class DocumentationGuardTests
{
	/// <summary>
	/// The lines of text an XML doc element holds at most, such as a <c>summary</c>,
	/// a <c>param</c> or a <c>returns</c>; a line holding only tags does not count.
	/// </summary>
	private const int XmlDocElementLines = 2;

	/// <summary>The lines of text a <c>remarks</c> holds at most, about one paragraph at 120 columns.</summary>
	private const int RemarksLines = 5;

	/// <summary>The <c>//</c> lines an inline comment run holds at most.</summary>
	private const int InlineCommentLines = 4;

	// The Vale rules that hold a word limit of STYLEGUIDE.md as their max, which Vale itself enforces.
	private static readonly string[] s_valeCeilingRules = ["ParagraphLength", "ListItemLength", "SentenceLength"];

	[GeneratedRegex(@"^max:\s*(?<limit>\d+)\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex ValeMaximum();

	[TestMethod]
	public void TheValeRules_EachHoldTheirCeilingAsAMax()
	{
		Dictionary<string, string> rules = s_valeCeilingRules.ToDictionary(
			rule => rule,
			rule => File.ReadAllText(RepositoryPaths.Below(".vale", "styles", "TRNG", rule + ".yml"))
		);

		string[] missing = [.. FindRulesWithoutMaximum(rules)];

		Assert.IsEmpty(
			missing,

			"Vale enforces a ceiling of STYLEGUIDE.md only through the max of its rule:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, missing)
		);
	}

	private static IEnumerable<string> FindRulesWithoutMaximum(IReadOnlyDictionary<string, string> rules)
	{
		return from rule in rules where ValeLimit(rule.Value) == 0 select $"{rule.Key}.yml states no max.";
	}

	// The max of a rule's text, or 0 when the rule states none.
	private static int ValeLimit(string rule)
	{
		Match maximum = ValeMaximum().Match(rule);
		return maximum.Success ? Limit(maximum) : 0;
	}

	// The sample holds a rule with a max, a rule with none, and a rule whose max is zero.
	[TestMethod]
	public void FindRulesWithoutMaximum_ReportsEachRuleThatStatesNoPositiveMax()
	{
		var rules = new Dictionary<string, string>
		{
			["ParagraphLength"] = "extends: occurrence\nmax: 80\n",
			["ListItemLength"] = "extends: occurrence\nmessage: x\n",
			["SentenceLength"] = "extends: occurrence\nmax: 0\n",
		};

		Assert.AreSequenceEqual(["ListItemLength.yml states no max.", "SentenceLength.yml states no max."], FindRulesWithoutMaximum(rules));
	}

	[TestMethod]
	public void SourceDocElements_StayWithinTheCeiling()
	{
		string[] violations =
		[
			.. SourceFiles(includeTests: true)
				.SelectMany(file =>
					FindLongDocElements(
						source: File.ReadAllText(file),
						path: RepoPath(file),
						ceiling: XmlDocElementLines,
						remarksCeiling: RemarksLines
					)
				),
		];

		Assert.IsEmpty(violations, FailMessage("Source documentation elements", violations));
	}

	private static IEnumerable<string> FindLongDocElements(string source, string path, int ceiling, int remarksCeiling)
	{
		return from element in DocElementTextLines(source, path)
			let limit = element.Name == "remarks" ? remarksCeiling : ceiling
			where element.Lines > limit
			select $"{path}: <{element.Name}> of {element.Lines} text lines at line {element.Line}, limit {limit}";
	}

	// The sample holds a summary and a remarks that pass their ceilings by one line, and a summary within its ceiling.
	[TestMethod]
	public void FindLongDocElements_ReportsEachElementPastItsCeilingAndNothingElse()
	{
		const string sample = """
			class Sample
			{
				/// <summary>
				/// one
				/// two
				/// three
				/// </summary>
				/// <remarks>
				/// a
				/// b
				/// c
				/// d
				/// e
				/// f
				/// </remarks>
				void Long() { }

				/// <summary>
				/// one
				/// two
				/// </summary>
				void Short() { }
			}
			""";

		Assert.AreSequenceEqual(
			["Sample.cs: <summary> of 3 text lines at line 3, limit 2", "Sample.cs: <remarks> of 6 text lines at line 8, limit 5"],
			FindLongDocElements(source: sample, path: "Sample.cs", ceiling: 2, remarksCeiling: 5)
		);
	}

	[TestMethod]
	public void InlineCommentRuns_StayWithinTheCeiling()
	{
		string[] violations =
		[
			.. SourceFiles(includeTests: true)
				.SelectMany(file =>
					FindLongRuns(
						source: File.ReadAllText(file),
						path: RepoPath(file),
						kind: SyntaxKind.SingleLineCommentTrivia,
						marker: "//",
						ceiling: InlineCommentLines
					)
				),
		];

		Assert.IsEmpty(violations, FailMessage("Inline comment runs", violations));
	}

	private static IEnumerable<string> FindLongRuns(string source, string path, SyntaxKind kind, string marker, int ceiling)
	{
		return from run in LineRuns(CommentLines(source, path, kind))
			where run.Length > ceiling
			select $"{path}: {run.Length}-line {marker} run at line {run.First}, limit {ceiling}";
	}

	// The sample holds a run of three comment lines, a comment that ends a code line and a string that holds the marker.
	[TestMethod]
	public void FindLongRuns_ReportsEachRunPastTheCeilingAndNothingElse()
	{
		const string sample = """
			class Sample
			{
				// one
				// two
				// three
				int a;

				int b; // trailing
				// lone
				string s = "// not a comment";
			}
			""";

		Assert.AreSequenceEqual(
			["Sample.cs: 3-line // run at line 3, limit 2"],
			FindLongRuns(source: sample, path: "Sample.cs", kind: SyntaxKind.SingleLineCommentTrivia, marker: "//", ceiling: 2)
		);
	}

	[TestMethod]
	public void AMemberComment_IsADocOnTheMember()
	{
		string[] offenders = [.. SourceFiles(includeTests: false).SelectMany(file => FindMemberComments(File.ReadAllText(file), RepoPath(file)))];

		Assert.IsEmpty(
			offenders,

			"A // comment stands directly above a member, where STYLEGUIDE.md asks for a /// doc:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, offenders)
		);
	}

	private static IEnumerable<string> FindMemberComments(string source, string path)
	{
		SyntaxNode root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();

		return from member in root.DescendantNodes()
			where IsMember(member) && HasCommentDirectlyAbove(member)
			select $"{path}: line {member.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
	}

	// The sample holds a comment directly above a member, one set off by a blank line, one inside a body, and a doc.
	[TestMethod]
	public void FindMemberComments_ReportsACommentDirectlyAboveAMemberAndNothingElse()
	{
		const string sample = """
			class Sample
			{
				// names the field
				int _field;

				// set off by a blank line

				int _other;

				void Method()
				{
					// inside a body
					int local = 0;
				}

				/// <summary>Documents the field.</summary>
				int _documented;
			}
			""";

		Assert.AreSequenceEqual(["Sample.cs: line 4"], FindMemberComments(sample, "Sample.cs"));
	}

	/// <summary>A member, or a positional parameter, which is a property of its record.</summary>
	private static bool IsMember(SyntaxNode node)
	{
		return node
			is ParameterSyntax { Parent.Parent: RecordDeclarationSyntax }
				or (MemberDeclarationSyntax and not (BaseNamespaceDeclarationSyntax or GlobalStatementSyntax));
	}

	// The run ends on the line above the member, with no blank line or directive between them.
	private static bool HasCommentDirectlyAbove(SyntaxNode member)
	{
		SyntaxTriviaList trivia = member.GetLeadingTrivia();
		int lineBreaks = 0;

		for (int index = trivia.Count - 1; index >= 0; index--)
		{
			if (trivia[index].IsKind(SyntaxKind.SingleLineCommentTrivia))
				return lineBreaks == 1;

			if (trivia[index].IsKind(SyntaxKind.EndOfLineTrivia))
				lineBreaks++;
			else if (!trivia[index].IsKind(SyntaxKind.WhitespaceTrivia))
				return false;
		}

		return false;
	}

	private static int Limit(Match match) => int.Parse(match.Groups["limit"].Value, CultureInfo.InvariantCulture);

	private static string[] SourceFiles(bool includeTests)
	{
		string[] folders = includeTests
			? ["TRNGScriptCompiler", "TRNGScriptCompiler.Cli", "TRNGScriptCompiler.Tests"]
			: ["TRNGScriptCompiler", "TRNGScriptCompiler.Cli"];

		return
		[
			.. folders
				.SelectMany(folder => RepositoryPaths.SourceFilesBelow(RepositoryPaths.Below(folder)))
				.OrderBy(file => file, StringComparer.Ordinal),
		];
	}

	/// <summary>
	/// The lines of the comments of a trivia kind that start their line; strings and disabled text never count.
	/// </summary>
	private static HashSet<int> CommentLines(string code, string path, SyntaxKind kind)
	{
		var source = SourceText.From(code);
		SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path);

		HashSet<int> lines = [];

		foreach (SyntaxTrivia trivia in tree.GetRoot().DescendantTrivia())
		{
			if (!trivia.IsKind(kind) || !StartsLine(source, trivia))
				continue;

			int start = source.Lines.IndexOf(trivia.FullSpan.Start) + 1;
			int last = trivia.FullSpan.End;

			// A doc comment's span reaches past its last line;
			// the walk takes the ending back to the last character the comment itself holds.
			while (last > trivia.FullSpan.Start && char.IsWhiteSpace(source[last - 1]))
				last--;

			int end = source.Lines.IndexOf(last - 1) + 1;

			for (int line = start; line <= end; line++)
				lines.Add(line);
		}

		return lines;
	}

	/// <summary>
	/// The top-level elements of each doc comment, such as a <c>summary</c> or a <c>param</c>, with the lines of text
	/// between their tags; a line holding only the <c>///</c> marker or the element's own tag is no text line.
	/// </summary>
	private static IEnumerable<(string Name, int Line, int Lines)> DocElementTextLines(string code, string path)
	{
		var source = SourceText.From(code);
		SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path);

		foreach (SyntaxTrivia trivia in tree.GetRoot().DescendantTrivia())
		{
			if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax comment)
				continue;

			foreach (XmlElementSyntax element in comment.Content.OfType<XmlElementSyntax>())
			{
				var inner = TextSpan.FromBounds(element.StartTag.Span.End, element.EndTag.SpanStart);

				LinePositionSpan lines = source.Lines.GetLinePositionSpan(inner);
				int textLines = 0;

				for (int index = lines.Start.Line; index <= lines.End.Line; index++)
				{
					TextSpan? part = source.Lines[index].Span.Intersection(inner);
					string text = part is null ? string.Empty : source.ToString(part.Value).Trim();

					if (text.StartsWith("///", StringComparison.Ordinal))
						text = text[3..].Trim();

					if (text.Length > 0)
						textLines++;
				}

				yield return (element.StartTag.Name.LocalName.ValueText, lines.Start.Line + 1, textLines);
			}
		}
	}

	// The `///` markers sit in a doc comment's FullSpan but outside its own span.
	private static bool StartsLine(SourceText source, SyntaxTrivia trivia)
	{
		TextLine line = source.Lines.GetLineFromPosition(trivia.SpanStart);
		int pointer = line.Start;

		while (pointer < line.End && char.IsWhiteSpace(source[pointer]))
			pointer++;

		return pointer >= trivia.FullSpan.Start && pointer < trivia.FullSpan.End;
	}

	private static IEnumerable<(int First, int Length)> LineRuns(IEnumerable<int> lines)
	{
		int[] sorted = [.. lines.OrderBy(line => line)];
		int index = 0;

		while (index < sorted.Length)
		{
			int first = sorted[index];
			int last = first;

			index++;

			while (index < sorted.Length && sorted[index] == last + 1)
			{
				last = sorted[index];
				index++;
			}

			yield return (first, last - first + 1);
		}
	}

	private static string RepoPath(string path) => Path.GetRelativePath(RepositoryPaths.Root(), path).Replace('\\', '/');

	private static string FailMessage(string subject, IEnumerable<string> violations)
	{
		return $"{subject} break the ceilings of STYLEGUIDE.md:"
			+ Environment.NewLine
			+ string.Join(Environment.NewLine, violations)
			+ Environment.NewLine
			+ "Split the run, or move its fact to the page that owns it.";
	}
}
