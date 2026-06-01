using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Kernel.Infrastructure;

[TestClass]
public sealed class PathResolverTests
{
	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveFullPath_RestoresOnDiskCasing) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveFullPath_RestoresOnDiskCasing) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveFullPath_RestoresOnDiskCasing(bool cached)
	{
		using var workspace = TestWorkspace.CreateEmpty();

		string baseDirectory = workspace.RootPath;
		string actualDirectory = Path.Combine(baseDirectory, "EngineX", "SubDir");

		Directory.CreateDirectory(actualDirectory);
		File.WriteAllText(Path.Combine(actualDirectory, "Script.txt"), string.Empty);

		PathResolver resolver = CreateResolver(cached);

		string resolvedDirectory = resolver.ResolveFullPath(Path.Combine(baseDirectory, "enginex", "SUBDIR"));
		Assert.EndsWith(Path.Combine("EngineX", "SubDir"), resolvedDirectory, "Directory casing must be restored from disk.");

		string resolvedFile = resolver.ResolveFullPath(Path.Combine(path1: baseDirectory, path2: "ENGINEX", path3: "subdir", path4: "script.TXT"));

		Assert.EndsWith(Path.Combine("SubDir", "Script.txt"), resolvedFile, "File casing must be restored from disk.");
		Assert.IsTrue(File.Exists(resolvedFile), "The resolved file path must exist.");

		string resolvedMissing = resolver.ResolveFullPath(
			Path.Combine(path1: baseDirectory, path2: "enginex", path3: "subdir", path4: "MISSING.DAT")
		);

		Assert.EndsWith(
			Path.Combine("SubDir", "MISSING.DAT"),
			resolvedMissing,
			"Non-existent segments keep their requested casing while resolved parents are used."
		);
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveFullPath_RestoresCasingOfHiddenEntry) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveFullPath_RestoresCasingOfHiddenEntry) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveFullPath_RestoresCasingOfHiddenEntry(bool cached)
	{
		using var workspace = TestWorkspace.CreateEmpty();
		DirectoryInfo directory = Directory.CreateDirectory(workspace.PathOf("HiddenDir"));

		directory.Attributes |= FileAttributes.Hidden;

		Assert.EndsWith(Path.DirectorySeparatorChar + "HiddenDir", CreateResolver(cached).ResolveFullPath(workspace.PathOf("hiddendir")));
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveFullPath_WildcardSegment_MatchesOnlyItself) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveFullPath_WildcardSegment_MatchesOnlyItself) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveFullPath_WildcardSegment_MatchesOnlyItself(bool cached)
	{
		using var workspace = TestWorkspace.CreateEmpty();
		File.WriteAllText(workspace.PathOf("Script.txt"), string.Empty);

		Assert.EndsWith(
			Path.DirectorySeparatorChar + "*",
			CreateResolver(cached).ResolveFullPath(workspace.PathOf("*")),
			"A wildcard segment must not match an existing entry."
		);
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveFullPath_FolderThatHoldsTwoSpellings_TakesTheOneNamed) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveFullPath_FolderThatHoldsTwoSpellings_TakesTheOneNamed) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveFullPath_FolderThatHoldsTwoSpellings_TakesTheOneNamed(bool cached)
	{
		using var workspace = TestWorkspace.CreateCaseSensitive();

		File.WriteAllText(workspace.PathOf("Script.txt"), "upper");
		File.WriteAllText(workspace.PathOf("script.txt"), "lower");

		PathResolver resolver = CreateResolver(cached);

		Assert.AreEqual("script.txt", Path.GetFileName(resolver.ResolveFullPath(workspace.PathOf("script.txt"))));
		Assert.AreEqual("Script.txt", Path.GetFileName(resolver.ResolveFullPath(workspace.PathOf("SCRIPT.TXT"))));
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveFullPath_EmptyOrWhitespace_IsReturnedUnchanged) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveFullPath_EmptyOrWhitespace_IsReturnedUnchanged) + " (Uncached)")]
	[TestCategory("Fast")]
	public void ResolveFullPath_EmptyOrWhitespace_IsReturnedUnchanged(bool cached)
	{
		PathResolver resolver = CreateResolver(cached);

		Assert.AreEqual(string.Empty, resolver.ResolveFullPath(string.Empty));
		Assert.AreEqual("   ", resolver.ResolveFullPath("   "));
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveScriptPath_ReadsABackslashAsAFolderSeparator) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveScriptPath_ReadsABackslashAsAFolderSeparator) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveScriptPath_ReadsABackslashAsAFolderSeparator(bool cached)
	{
		// A script is written for Windows, so "sub\Inc.txt" names a file in a folder on every file system.
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithTextFile(Path.Combine("Sub", "Inc.txt"), string.Empty);

		string resolved = CreateResolver(cached).ResolveScriptPath(workspace.RootPath, @"sub\INC.TXT");

		Assert.EndsWith(Path.DirectorySeparatorChar + Path.Combine("Sub", "Inc.txt"), resolved);
		Assert.IsTrue(File.Exists(resolved), resolved);
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveScriptPath_KeepsTheBackslashesOfTheFolder) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveScriptPath_KeepsTheBackslashesOfTheFolder) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveScriptPath_KeepsTheBackslashesOfTheFolder(bool cached)
	{
		// Only the script's part is written for Windows, so on Unix a folder whose name holds '\' keeps it; the folder does not exist, so
		// the path keeps every segment as given.
		using var workspace = TestWorkspace.CreateEmpty();
		PathResolver resolver = CreateResolver(cached);

		string folder = Path.Join(resolver.ResolveFullPath(workspace.RootPath), @"trle\one");

		string resolved = resolver.ResolveScriptPath(folder, @"sub\a.txt");

		Assert.AreEqual(Path.Join(folder, "sub", "a.txt"), resolved);
	}

	[TestMethod]
	[DataRow(true, DisplayName = nameof(ResolveScriptPath_RootedScriptPath_NamesItself) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveScriptPath_RootedScriptPath_NamesItself) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveScriptPath_RootedScriptPath_NamesItself(bool cached)
	{
		using TestWorkspace workspace = TestWorkspace.CreateEmpty().WithTextFile("Inc.txt", string.Empty);
		PathResolver resolver = CreateResolver(cached);

		string resolved = resolver.ResolveScriptPath(Path.Join(workspace.RootPath, "other"), workspace.PathOf("INC.TXT"));

		Assert.AreEqual(resolver.ResolveFullPath(workspace.PathOf("Inc.txt")), resolved);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux)]
	[DataRow(true, DisplayName = nameof(ResolveScriptPath_FolderNameThatHoldsABackslash_FindsTheFile) + " (Cached)")]
	[DataRow(false, DisplayName = nameof(ResolveScriptPath_FolderNameThatHoldsABackslash_FindsTheFile) + " (Uncached)")]
	[TestCategory("IO")]
	public void ResolveScriptPath_FolderNameThatHoldsABackslash_FindsTheFile(bool cached)
	{
		// Linux allows '\' in a folder name, which Windows reads as a separator, so the test runs on Linux alone.
		using var workspace = TestWorkspace.CreateEmpty();
		PathResolver resolver = CreateResolver(cached);

		string folder = Path.Join(resolver.ResolveFullPath(workspace.RootPath), @"trle\one");

		Directory.CreateDirectory(Path.Join(folder, "Sub"));
		File.WriteAllText(Path.Join(folder, "Sub", "Inc.txt"), string.Empty);

		string resolved = resolver.ResolveScriptPath(folder, @"sub\INC.TXT");

		Assert.AreEqual(Path.Join(folder, "Sub", "Inc.txt"), resolved);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ResolveFullPath_SeveralPathsBelowOneFolder_ListsEachFolderOnce()
	{
		using TestWorkspace workspace = TestWorkspace
			.CreateEmpty()
			.WithTextFile(Path.Combine("sub", "A.txt"), string.Empty)
			.WithTextFile(Path.Combine("sub", "B.txt"), string.Empty)
			.WithTextFile("Top.txt", string.Empty)
			.WithTextFile(Path.Combine("other", "C.txt"), string.Empty);

		var resolver = new PathResolver();

		string first = resolver.ResolveFullPath(workspace.PathOf(@"SUB\a.txt"));
		int listedForFirst = resolver.ListingCount;

		string second = resolver.ResolveFullPath(workspace.PathOf(@"sub\B.TXT"));
		string third = resolver.ResolveFullPath(workspace.PathOf("top.txt"));

		Assert.AreEqual(listedForFirst, resolver.ListingCount, "Paths below folders already listed read no listing.");

		string fourth = resolver.ResolveFullPath(workspace.PathOf(@"OTHER\c.txt"));

		Assert.AreEqual(listedForFirst + 1, resolver.ListingCount, "A path below a new folder lists that folder alone.");

		Assert.EndsWith(Path.Combine("sub", "A.txt"), first);
		Assert.EndsWith(Path.Combine("sub", "B.txt"), second);
		Assert.EndsWith(Path.DirectorySeparatorChar + "Top.txt", third);
		Assert.EndsWith(Path.Combine("other", "C.txt"), fourth);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ResolveFullPath_FolderThatCannotBeListed_IsTriedOnce()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var resolver = new PathResolver();

		string first = resolver.ResolveFullPath(workspace.PathOf(@"Missing\A.txt"));
		int listedForFirst = resolver.ListingCount;

		string second = resolver.ResolveFullPath(workspace.PathOf(@"Missing\B.txt"));

		Assert.AreEqual(listedForFirst, resolver.ListingCount, "A folder that cannot be listed is remembered as such.");
		Assert.EndsWith(Path.Combine("Missing", "A.txt"), first);
		Assert.EndsWith(Path.Combine("Missing", "B.txt"), second);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void ResolveFullPath_FileCreatedAfterTheListing_IsSeenOnlyByANewResolverAndByTheUncachedPath()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		string requested = workspace.PathOf("data.txt");

		var resolver = new PathResolver();

		Assert.EndsWith(Path.DirectorySeparatorChar + "data.txt", resolver.ResolveFullPath(requested));

		File.WriteAllText(workspace.PathOf("Data.txt"), string.Empty);

		Assert.EndsWith(
			Path.DirectorySeparatorChar + "data.txt",
			resolver.ResolveFullPath(requested),
			"The resolver keeps the listing it read before the file existed."
		);

		Assert.EndsWith(Path.DirectorySeparatorChar + "Data.txt", new PathResolver().ResolveFullPath(requested), "Resolvers share no listing.");

		Assert.EndsWith(
			Path.DirectorySeparatorChar + "Data.txt",
			PathResolver.Uncached.ResolveFullPath(requested),
			"The uncached resolver lists on every call."
		);
	}

	private static PathResolver CreateResolver(bool cached) => cached ? new PathResolver() : PathResolver.Uncached;
}
