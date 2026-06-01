using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Infrastructure;

[TestClass]
public sealed class PathUtilitiesTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void EnumerateFilesInsensitive_MatchesPatternCaseInsensitively()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		Directory.CreateDirectory(workspace.PathOf("plugins"));

		File.WriteAllText(workspace.PathOf(Path.Combine("plugins", "PluginMatrix.SCRIPT")), string.Empty);
		File.WriteAllText(workspace.PathOf(Path.Combine("plugins", "readme.txt")), string.Empty);

		string[] matches = [.. PathUtilities.EnumerateFilesInsensitive(workspace.PathOf("plugins"), "plugin*.script")];

		Assert.HasCount(1, matches, "The pattern must match case-insensitively ('PLUGIN*.SCRIPT' vs 'PluginMatrix.SCRIPT').");
		Assert.AreEqual("PluginMatrix.SCRIPT", Path.GetFileName(matches[0]));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void EnumerateFilesInsensitive_ListsTheFilesOfOneFolderOnly()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		string plugins = workspace.PathOf("plugins");
		string nested = Path.Combine(plugins, "nested");

		Directory.CreateDirectory(nested);

		File.WriteAllText(Path.Combine(plugins, "PluginTop.script"), string.Empty);
		File.WriteAllText(Path.Combine(nested, "PluginNested.script"), string.Empty);

		string[] matches = [.. PathUtilities.EnumerateFilesInsensitive(plugins, "Plugin*.script")];

		Assert.HasCount(1, matches, "The listing must not enter the subfolder.");
		Assert.AreEqual("PluginTop.script", Path.GetFileName(matches[0]));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void EnumerateFilesInsensitive_MissingDirectory_ReturnsEmpty()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		Assert.IsEmpty(
			PathUtilities.EnumerateFilesInsensitive(workspace.PathOf("missing"), "*.dat"),
			"A missing directory must yield no matches instead of throwing."
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ChooseEntry_PrefersTheSpellingNamed()
	{
		string[] names = ["SCRIPT.TXT", "script.txt", "Script.txt"];
		Assert.AreEqual("script.txt", PathUtilities.ChooseEntry(names, "script.txt"));
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ChooseEntry_WithoutTheSpellingNamed_TakesTheFirstVariantInOrdinalOrder()
	{
		string[] names = ["script.TXT", "Other.txt", "SCRIPT.txt"];

		Assert.AreEqual("SCRIPT.txt", PathUtilities.ChooseEntry(names, "Script.txt"));

		Assert.AreEqual(
			"SCRIPT.txt",
			PathUtilities.ChooseEntry(names.Reverse(), "Script.txt"),
			"The order of the folder listing does not change the choice."
		);

		Assert.IsNull(PathUtilities.ChooseEntry(names, "Missing.txt"));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void EnumerateFilesInsensitive_ListsASymbolicLinkToAFile()
	{
		using var workspace = TestWorkspace.CreateEmpty();

		string plugins = workspace.PathOf("plugins");
		string target = Path.Combine(workspace.RootPath, "PluginTarget.script");
		string link = Path.Combine(plugins, "PluginLinked.script");

		Directory.CreateDirectory(plugins);
		File.WriteAllText(target, string.Empty);

		try
		{
			File.CreateSymbolicLink(link, target);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
		{
			Assert.Inconclusive($"Could not create a symbolic link in this environment: {ex.Message}");
		}

		string[] matches = [.. PathUtilities.EnumerateFilesInsensitive(plugins, "Plugin*.script")];

		Assert.HasCount(1, matches, "A symbolic link to a file is a file of the folder.");
		Assert.AreEqual("PluginLinked.script", Path.GetFileName(matches[0]));
	}
}
