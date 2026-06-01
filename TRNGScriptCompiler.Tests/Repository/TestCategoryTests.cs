using System.Reflection;
using System.Text.RegularExpressions;

namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>Holds every test of the suite to the test layers table of <c>docs/TESTING.md</c>.</summary>
[TestClass]
[TestCategory("Repository")]
public sealed partial class TestCategoryTests
{
	/// <summary>Reads the category of one row of the test layers table, such as <c>| Unit | `Fast` |</c>.</summary>
	[GeneratedRegex(@"^\| [^|`]+ \| `(?<category>[A-Za-z]+)` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex LayerRow();

	[TestMethod]
	public void EveryTest_CarriesExactlyOneCategoryOfTheTestLayersTable()
	{
		string guide = File.ReadAllText(RepositoryPaths.Below("docs", "TESTING.md"));
		HashSet<string> categories = [.. LayerRow().Matches(guide).Select(match => match.Groups["category"].Value)];

		Assert.IsNotEmpty(categories, "docs/TESTING.md has no test layers table to read the categories from.");

		(string Name, string[] Categories)[] tests = [.. TestMethods()];

		string[] offenders = [.. FindMiscategorized(tests, categories)];

		Assert.IsEmpty(offenders, "Each test needs exactly one category of docs/TESTING.md:\n" + string.Join("\n", offenders));

		string[] unused = [.. categories.Where(category => !tests.Any(test => test.Categories.Contains(category)))];

		Assert.IsEmpty(unused, "docs/TESTING.md lists a category no test carries: " + string.Join(", ", unused));
	}

	[TestMethod]
	public void FindMiscategorized_ReportsATestWithNoCategoryTwoOrOneTheTableLacks()
	{
		HashSet<string> categories = ["Fast", "IO"];

		(string Name, string[] Categories)[] sample =
		[
			("Sample.One", ["Fast"]),
			("Sample.None", []),
			("Sample.Two", ["Fast", "IO"]),
			("Sample.Unlisted", ["Slow"]),
		];

		Assert.AreSequenceEqual(["Sample.None []", "Sample.Two [Fast, IO]", "Sample.Unlisted [Slow]"], FindMiscategorized(sample, categories));
	}

	private static IEnumerable<string> FindMiscategorized(IEnumerable<(string Name, string[] Categories)> tests, HashSet<string> categories)
	{
		return tests
			.Where(test => test.Categories.Length != 1 || !categories.Contains(test.Categories[0]))
			.Select(test => $"{test.Name} [{string.Join(", ", test.Categories)}]");
	}

	// A category on the assembly or the class applies to each of its tests, so the method's categories join them.
	private static IEnumerable<(string Name, string[] Categories)> TestMethods()
	{
		Assembly assembly = typeof(TestCategoryTests).Assembly;
		string[] assemblyCategories = CategoriesOf(assembly);

		foreach (Type type in assembly.GetTypes().Where(type => type.IsDefined(typeof(TestClassAttribute), inherit: false)))
		{
			string[] classCategories = CategoriesOf(type);

			foreach (MethodInfo method in type.GetMethods().Where(method => method.IsDefined(typeof(TestMethodAttribute), inherit: true)))
			{
				yield return (
					$"{type.Name}.{method.Name}",
					[.. assemblyCategories.Concat(classCategories).Concat(CategoriesOf(method)).Distinct(StringComparer.Ordinal)]
				);
			}
		}
	}

	private static string[] CategoriesOf(ICustomAttributeProvider target)
	{
		return
		[
			.. target
				.GetCustomAttributes(typeof(TestCategoryAttribute), inherit: true)
				.Cast<TestCategoryAttribute>()
				.SelectMany(attribute => attribute.TestCategories),
		];
	}
}
