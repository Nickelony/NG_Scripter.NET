using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>
/// Holds every test of the suite to asserting on the diagnostics and the log entries of a run, not on a search
/// of its log text.
/// </summary>
/// <remarks>
/// The scan reads syntax alone, so it misses the text a method returns.
/// </remarks>
[TestClass]
[TestCategory("Repository")]
public sealed class LogAssertionGuardTests
{
	// The string, collection and regex methods that search the receiver or, called statically, their first argument.
	private static readonly HashSet<string> s_searchMethods = new(StringComparer.Ordinal)
	{
		"Contains",
		"DoesNotContain",
		"IndexOf",
		"IndexOfAny",
		"LastIndexOf",
		"LastIndexOfAny",
		"StartsWith",
		"EndsWith",
		"All",
		"Where",
		"Find",
		"FindAll",
		"FindIndex",
		"FindLast",
		"FindLastIndex",
		"Exists",
		"TrueForAll",
		"IsMatch",
		"Match",
		"Matches",
		"EnumerateMatches",
	};

	// The LINQ methods that search with a predicate, and without one pick an element or count, as an index does.
	private static readonly HashSet<string> s_predicateMethods = new(StringComparer.Ordinal)
	{
		"Any",
		"Count",
		"First",
		"FirstOrDefault",
		"Last",
		"LastOrDefault",
		"Single",
		"SingleOrDefault",
	};

	// The methods whose result still holds the log text, as its lines, a part of it or a copy of it.
	private static readonly HashSet<string> s_textTransforms = new(StringComparer.Ordinal)
	{
		"Split",
		"Trim",
		"TrimStart",
		"TrimEnd",
		"Replace",
		"ReplaceLineEndings",
		"Substring",
		"ToLower",
		"ToLowerInvariant",
		"ToUpper",
		"ToUpperInvariant",
		"Normalize",
		"AsSpan",
		"AsEnumerable",
		"ToArray",
		"ToList",
		"ToHashSet",
		"Select",
		"Skip",
		"Take",
		"Reverse",
		"Distinct",
		"ElementAt",
		"ToString",
	};

	// The static string methods whose result holds the text of each of their arguments.
	private static readonly HashSet<string> s_textBuilders = new(StringComparer.Ordinal) { "Concat", "Join", "Format" };

	// The console members that hold what a test's captured console received: the text of both streams, of each stream, and their lines.
	private static readonly HashSet<string> s_consoleTexts = new(StringComparer.Ordinal)
	{
		"AllText",
		"AllLines",
		"OutputText",
		"OutputLines",
		"ErrorText",
		"ErrorLines",
	};

	// The console methods that return the text, or the lines, written in one color.
	private static readonly HashSet<string> s_consoleColorTexts = new(StringComparer.Ordinal) { "TextIn", "LinesIn" };

	// The Assert methods whose first or second argument is the text or the collection they search.
	private static readonly HashSet<string> s_assertSearches = new(StringComparer.Ordinal)
	{
		"Contains",
		"DoesNotContain",
		"StartsWith",
		"DoesNotStartWith",
		"EndsWith",
		"DoesNotEndWith",
		"MatchesRegex",
		"DoesNotMatchRegex",
		"ContainsSingle",
	};

	// The CollectionAssert methods whose first or second argument is the collection they search.
	private static readonly HashSet<string> s_collectionAssertSearches = new(StringComparer.Ordinal)
	{
		"Contains",
		"DoesNotContain",
		"IsSubsetOf",
		"IsNotSubsetOf",
	};

	// The log text is a Log property, a GetText() call, the text or the lines of a captured console, a transform, concatenation or
	// interpolation of either, and a local, a field, a pattern or a deconstructed variable that holds one. A search is a string, LINQ or
	// regex search of it, a StringAssert on it, or an Assert or CollectionAssert containment check of it; an exact comparison and a
	// failure message are no search.
	[TestMethod]
	public void NoTest_SearchesTheLogText()
	{
		string testRoot = RepositoryPaths.Below("TRNGScriptCompiler.Tests");

		// The test classes live in the feature folders of the project, and the build output holds no source of the suite.
		string[] sources = [.. RepositoryPaths.SourceFilesBelow(testRoot).Select(path => Path.GetRelativePath(testRoot, path))];

		Assert.Contains(Path.Combine("Repository", nameof(LogAssertionGuardTests) + ".cs"), sources, "The scan must read the feature folders.");

		string[] offenders = [.. sources.SelectMany(path => FindLogSearches(File.ReadAllText(Path.Combine(testRoot, path)), path))];

		Assert.IsEmpty(
			offenders,

			"Assert a diagnostic with DiagnosticAssert, and a log line by its LogEntry, not by a search of the log text:\n"
				+ string.Join("\n", offenders)
		);
	}

	// The sample holds each way the scan follows the log text, or the text of a captured console, to a search, and the calls it leaves alone.
	[TestMethod]
	public void FindLogSearches_ReportsEachSearchOfTheLogTextAndNothingElse()
	{
		const string sample = """
			class Sample
			{
				void Search(TestCompileResult result, string other)
				{
					bool direct = result.Log.Contains("direct");

					string text = result.Log;
					bool aliased = text.Contains("aliased");

					var (head, tail) = (result.Log, "tail");
					bool deconstructed = head.StartsWith("deconstructed");
					bool fromTail = tail.Contains("fromTail");

					bool unrelated = other.Contains("unrelated");
					Assert.AreEqual("last", result.Log.Split(',')[^1]);
				}

				void SearchConsole(CapturedConsole console, string[] expected)
				{
					bool direct = console.OutputText.Contains("direct");
					string[] lines = console.ErrorLines;
					int found = Array.FindIndex(lines, line => line.StartsWith("prompt"));
					Assert.StartsWith("banner", console.AllText);
					Assert.ContainsSingle(line => line == "once", console.AllLines);
					CollectionAssert.IsSubsetOf(expected, console.AllLines);
					bool colored = console.LinesIn(ConsoleColor.Red).Any(line => line == "red");
					Assert.AreEqual("last", console.AllLines[^1]);
					Assert.AreEqual(string.Empty, console.ErrorText);
				}
			}
			""";

		Assert.AreSequenceEqual(
			[
				"Sample.cs:5: result.Log.Contains(\"direct\")",
				"Sample.cs:8: text.Contains(\"aliased\")",
				"Sample.cs:11: head.StartsWith(\"deconstructed\")",
				"Sample.cs:20: console.OutputText.Contains(\"direct\")",
				"Sample.cs:22: Array.FindIndex(lines, line => line.StartsWith(\"prompt\"))",
				"Sample.cs:23: Assert.StartsWith(\"banner\", console.AllText)",
				"Sample.cs:24: Assert.ContainsSingle(line => line == \"once\", console.AllLines)",
				"Sample.cs:25: CollectionAssert.IsSubsetOf(expected, console.AllLines)",
				"Sample.cs:26: console.LinesIn(ConsoleColor.Red).Any(line => line == \"red\")",
			],

			FindLogSearches(sample, "Sample.cs")
		);
	}

	private static IEnumerable<string> FindLogSearches(string source, string path)
	{
		SyntaxNode root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path).GetRoot();

		HashSet<(SyntaxNode? Scope, string Name)> aliases = FindAliases(root);

		return root.DescendantNodes()
			.OfType<InvocationExpressionSyntax>()
			.Where(invocation => IsLogSearch(invocation, aliases))
			.Select(invocation => $"{path}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {Shown(invocation)}");
	}

	// A ?. call reads in full from the start of the conditional access that holds it.
	private static SyntaxNode Shown(SyntaxNode node) =>
		node.Parent is ConditionalAccessExpressionSyntax access && access.WhenNotNull == node ? Shown(access) : node;

	// A name that holds the log text, keyed by the member that declares it or, for a field or a property, by its type, and grown until an
	// alias of an alias is found.
	private static HashSet<(SyntaxNode? Scope, string Name)> FindAliases(SyntaxNode root)
	{
		var aliases = new HashSet<(SyntaxNode? Scope, string Name)>();
		bool grown;

		do
		{
			grown = false;

			foreach ((SyntaxNode at, string name, ExpressionSyntax? value, BindingScope scope) in root.DescendantNodes().SelectMany(Bindings))
			{
				if (value is not null && !IsLogText(value, aliases))
					continue;

				SyntaxNode? key = scope switch
				{
					BindingScope.Member => MemberOf(at),
					BindingScope.Type => TypeOf(at),
					_ => Declares(MemberOf(at), name) ? MemberOf(at) : TypeOf(at),
				};

				if (aliases.Add((key, name)))
					grown = true;
			}
		} while (grown);

		return aliases;
	}

	// Each name a node binds and the value it takes; a null value is the Log property a property pattern names.
	private static IEnumerable<(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope)> Bindings(SyntaxNode node)
	{
		switch (node)
		{
			case VariableDeclaratorSyntax { Initializer: { } initializer } declarator:
				yield return (
					node,
					declarator.Identifier.Text,
					initializer.Value,
					node.Parent?.Parent is BaseFieldDeclarationSyntax ? BindingScope.Type : BindingScope.Member
				);

				break;

			case ForEachStatementSyntax loop:
				yield return (node, loop.Identifier.Text, loop.Expression, BindingScope.Member);
				break;

			case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
				foreach (
					(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope) binding in Targets(assignment.Left, assignment.Right)
				)
				{
					yield return binding;
				}

				break;

			case IsPatternExpressionSyntax test when DesignatedName(test.Pattern) is { } name:
				yield return (node, name, test.Expression, BindingScope.Member);
				break;

			case SubpatternSyntax subpattern when NamesLog(subpattern.ExpressionColon?.Expression) && DesignatedName(subpattern.Pattern) is { } name:
				yield return (node, name, null, BindingScope.Member);
				break;
		}
	}

	// The names an assignment or a deconstruction binds, each with the value at its position of a tuple.
	private static IEnumerable<(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope)> Targets(
		ExpressionSyntax target,
		ExpressionSyntax value
	)
	{
		switch (target)
		{
			case IdentifierNameSyntax identifier:
				yield return (target, identifier.Identifier.Text, value, BindingScope.DeclaringMember);
				break;

			case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } field:
				yield return (target, field.Name.Identifier.Text, value, BindingScope.Type);
				break;

			case DeclarationExpressionSyntax declaration:
				foreach (
					(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope) binding in Designations(declaration.Designation, value)
				)
				{
					yield return binding;
				}

				break;

			case TupleExpressionSyntax targets when value is TupleExpressionSyntax values && targets.Arguments.Count == values.Arguments.Count:
				for (int index = 0; index < targets.Arguments.Count; index++)
				{
					foreach (
						(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope) binding in Targets(
							targets.Arguments[index].Expression,
							values.Arguments[index].Expression
						)
					)
					{
						yield return binding;
					}
				}

				break;
		}
	}

	private static IEnumerable<(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope)> Designations(
		VariableDesignationSyntax designation,
		ExpressionSyntax value
	)
	{
		if (designation is SingleVariableDesignationSyntax single)
			yield return (designation, single.Identifier.Text, value, BindingScope.Member);

		if (
			designation is ParenthesizedVariableDesignationSyntax group
			&& value is TupleExpressionSyntax tuple
			&& group.Variables.Count == tuple.Arguments.Count
		)
		{
			for (int index = 0; index < group.Variables.Count; index++)
			{
				foreach (
					(SyntaxNode At, string Name, ExpressionSyntax? Value, BindingScope Scope) binding in Designations(
						group.Variables[index],
						tuple.Arguments[index].Expression
					)
				)
				{
					yield return binding;
				}
			}
		}
	}

	// The name a pattern gives the whole value it tests, as `is string text`, `is { } text` or `is var text` do.
	private static string? DesignatedName(PatternSyntax pattern)
	{
		return pattern switch
		{
			DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax single } => single.Identifier.Text,
			RecursivePatternSyntax { Designation: SingleVariableDesignationSyntax single } => single.Identifier.Text,
			VarPatternSyntax { Designation: SingleVariableDesignationSyntax single } => single.Identifier.Text,
			_ => null,
		};
	}

	private static bool NamesLog(ExpressionSyntax? expression)
	{
		return expression switch
		{
			IdentifierNameSyntax identifier => NamesLogText(identifier.Identifier.Text),
			MemberAccessExpressionSyntax member => NamesLogText(member.Name.Identifier.Text),
			MemberBindingExpressionSyntax binding => NamesLogText(binding.Name.Identifier.Text),
			_ => false,
		};
	}

	private static bool NamesLogText(string name) => name == "Log" || s_consoleTexts.Contains(name);

	private static SyntaxNode? MemberOf(SyntaxNode node) => node.FirstAncestorOrSelf<MemberDeclarationSyntax>();

	private static SyntaxNode? TypeOf(SyntaxNode node) => node.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>();

	// Whether a member declares a local, a parameter or a pattern variable of the name, so that the name is no field of its type.
	private static bool Declares(SyntaxNode? member, string name)
	{
		return member is not null and not BaseFieldDeclarationSyntax
			&& member
				.DescendantNodes()
				.Any(node =>
					node switch
					{
						VariableDeclaratorSyntax declarator => declarator.Identifier.Text == name,
						ParameterSyntax parameter => parameter.Identifier.Text == name,
						SingleVariableDesignationSyntax single => single.Identifier.Text == name,
						ForEachStatementSyntax loop => loop.Identifier.Text == name,
						_ => false,
					}
				);
	}

	private static bool IsAlias(SyntaxNode node, string name, HashSet<(SyntaxNode? Scope, string Name)> aliases) =>
		aliases.Contains((MemberOf(node), name)) || (aliases.Contains((TypeOf(node), name)) && !Declares(MemberOf(node), name));

	// The name of a called method and its receiver; the receiver of a ?. call is the expression of the conditional access that holds it.
	private static (string Method, ExpressionSyntax? Receiver) CalledMember(InvocationExpressionSyntax invocation)
	{
		return invocation.Expression switch
		{
			MemberAccessExpressionSyntax member => (member.Name.Identifier.Text, member.Expression),
			MemberBindingExpressionSyntax binding => (binding.Name.Identifier.Text, BoundReceiver(binding)),
			_ => (string.Empty, null),
		};
	}

	private static ExpressionSyntax? BoundReceiver(MemberBindingExpressionSyntax binding)
	{
		return binding
			.Ancestors()
			.OfType<ConditionalAccessExpressionSyntax>()
			.FirstOrDefault(access => access.WhenNotNull.Span.Contains(binding.Span))
			?.Expression;
	}

	private static bool IsLogText(ExpressionSyntax expression, HashSet<(SyntaxNode? Scope, string Name)> aliases)
	{
		return expression switch
		{
			ParenthesizedExpressionSyntax parenthesized => IsLogText(parenthesized.Expression, aliases),

			PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression) => IsLogText(
				suppressed.Operand,
				aliases
			),

			CastExpressionSyntax cast => IsLogText(cast.Expression, aliases),
			ElementAccessExpressionSyntax element => IsLogText(element.Expression, aliases),
			ConditionalAccessExpressionSyntax access => IsLogText(access.WhenNotNull, aliases),

			BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) || binary.IsKind(SyntaxKind.AddExpression) => IsLogText(
				binary.Left,
				aliases
			) || IsLogText(binary.Right, aliases),

			ConditionalExpressionSyntax conditional => IsLogText(conditional.WhenTrue, aliases) || IsLogText(conditional.WhenFalse, aliases),

			InterpolatedStringExpressionSyntax interpolated => interpolated
				.Contents.OfType<InterpolationSyntax>()
				.Any(part => IsLogText(part.Expression, aliases)),

			MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } field => NamesLog(field)
				|| aliases.Contains((TypeOf(field), field.Name.Identifier.Text)),

			MemberAccessExpressionSyntax or MemberBindingExpressionSyntax => NamesLog(expression),
			IdentifierNameSyntax identifier => IsAlias(identifier, identifier.Identifier.Text, aliases),

			InvocationExpressionSyntax invocation => CalledMember(invocation) switch
			{
				("GetText", _) => invocation.ArgumentList.Arguments.Count == 0,
				(string method, _) when s_consoleColorTexts.Contains(method) => true,

				(string method, { } receiver)
					when s_textTransforms.Contains(method) || (s_predicateMethods.Contains(method) && invocation.ArgumentList.Arguments.Count == 0) =>
					IsLogText(receiver, aliases),

				(string method, PredefinedTypeSyntax or IdentifierNameSyntax { Identifier.Text: "String" }) when s_textBuilders.Contains(method) =>
					invocation.ArgumentList.Arguments.Any(argument => IsLogText(argument.Expression, aliases)),

				_ => false,
			},

			_ => false,
		};
	}

	private static bool IsLogSearch(InvocationExpressionSyntax invocation, HashSet<(SyntaxNode? Scope, string Name)> aliases)
	{
		(string method, ExpressionSyntax? target) = CalledMember(invocation);

		if (target is null)
			return false;

		SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
		bool IsLogArgument(int index) => arguments.Count > index && IsLogText(arguments[index].Expression, aliases);

		string receiver = target switch
		{
			IdentifierNameSyntax identifier => identifier.Identifier.Text,
			MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.Text,
			_ => string.Empty,
		};

		return receiver switch
		{
			"StringAssert" => IsLogArgument(0),
			"Assert" => s_assertSearches.Contains(method) && (IsLogArgument(0) || IsLogArgument(1)),
			"CollectionAssert" => s_collectionAssertSearches.Contains(method) && (IsLogArgument(0) || IsLogArgument(1)),

			_ => (s_searchMethods.Contains(method) || (s_predicateMethods.Contains(method) && arguments.Count > 0))
				&& (IsLogText(target, aliases) || IsLogArgument(0)),
		};
	}

	// Where a bound name is keyed: by its member, by its type, or by the member when the member declares it and by the type otherwise.
	private enum BindingScope
	{
		Member,
		Type,
		DeclaringMember,
	}
}
