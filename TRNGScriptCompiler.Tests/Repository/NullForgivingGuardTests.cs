using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>
/// Holds every test of the suite to K7 of <c>CONVENTIONS.md</c> where an assertion already proves a name
/// is not <see langword="null"/>, so the <c>!</c> adds nothing.
/// </summary>
/// <remarks>
/// <c>Assert.IsNotNull</c> carries <c>[NotNull]</c>, so the code after it needs no <c>!</c> on the name it checked.
/// The scan reads syntax alone: a name is an identifier or a member access, and two names match when their text does.
/// </remarks>
[TestClass]
[TestCategory("Repository")]
public sealed class NullForgivingGuardTests
{
	[TestMethod]
	public void NoTest_ForgivesANameThatAnEarlierAssertionChecks()
	{
		string testRoot = RepositoryPaths.Below("TRNGScriptCompiler.Tests");

		// The test classes live in the feature folders of the project, and the build output holds no source of the suite.
		string[] sources = [.. RepositoryPaths.SourceFilesBelow(testRoot).Select(path => Path.GetRelativePath(testRoot, path))];

		Assert.Contains(Path.Combine("Repository", nameof(NullForgivingGuardTests) + ".cs"), sources, "The scan must read the feature folders.");

		string[] offenders = [.. sources.SelectMany(path => FindRedundantForgiving(File.ReadAllText(Path.Combine(testRoot, path)), path))];

		Assert.IsEmpty(
			offenders,

			"Assert.IsNotNull already tells the compiler the name is not null, so the ! after it hides nothing and K7 of CONVENTIONS.md asks it gone:\n"
				+ string.Join("\n", offenders)
		);
	}

	// The sample holds a name forgiven after its check, once spelled with spaces, and the cases that stay: a name forgiven before its
	// check, a name never checked, and a name checked in another method.
	[TestMethod]
	public void FindRedundantForgiving_ReportsEachForgivenNameAfterItsCheckAndNothingElse()
	{
		const string sample = """
			class Sample
			{
				void Redundant(Result result, Result other)
				{
					Assert.IsNotNull(result.Value);
					int length = result.Value!.Length;
					string kept = other.Value!;
					var again = result . Value !;
				}

				void Before(Result result)
				{
					string early = result.Value!;
					Assert.IsNotNull(result.Value, "message");
				}

				void Elsewhere(Result result)
				{
					string late = result.Value!;
				}
			}
			""";

		Assert.AreSequenceEqual(["Sample.cs:6: result.Value!", "Sample.cs:8: result.Value!"], FindRedundantForgiving(sample, "Sample.cs"));
	}

	private static IEnumerable<string> FindRedundantForgiving(string source, string path)
	{
		SyntaxNode root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path).GetRoot();

		foreach (InvocationExpressionSyntax check in root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsNotNullCheck))
		{
			ExpressionSyntax value = check.ArgumentList.Arguments[0].Expression;

			if (value is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
				continue;

			// The check holds for the code that follows it in the member or the lambda that holds it.
			SyntaxNode scope =
				check.FirstAncestorOrSelf<SyntaxNode>(node =>
					node
						is BaseMethodDeclarationSyntax
							or AccessorDeclarationSyntax
							or LocalFunctionStatementSyntax
							or AnonymousFunctionExpressionSyntax
				) ?? root;

			string name = Spelled(value);

			foreach (PostfixUnaryExpressionSyntax forgiving in scope.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
			{
				if (
					forgiving.IsKind(SyntaxKind.SuppressNullableWarningExpression)
					&& forgiving.SpanStart >= check.Span.End
					&& Spelled(forgiving.Operand) == name
				)
				{
					yield return $"{path}:{forgiving.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {name}!";
				}
			}
		}
	}

	private static bool IsNotNullCheck(InvocationExpressionSyntax invocation)
	{
		return invocation
			is {
				Expression: MemberAccessExpressionSyntax
				{
					Expression: IdentifierNameSyntax { Identifier.Text: "Assert" },
					Name.Identifier.Text: "IsNotNull",
				},
				ArgumentList.Arguments.Count: > 0,
			};
	}

	private static string Spelled(SyntaxNode node) =>
		string.Concat(node.WithoutTrivia().ToString().Where(character => !char.IsWhiteSpace(character)));
}
