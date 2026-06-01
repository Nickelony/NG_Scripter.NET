namespace TRNGScriptCompiler.Tests.Repository;

/// <summary>Locates the repository root that the guards read, and the C# sources below it.</summary>
internal static class RepositoryPaths
{
	private const string SolutionFile = "TRNGScriptCompiler.slnx";

	/// <summary>Finds the root by walking up for the solution file, and fails when it is absent.</summary>
	internal static string Root()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
				return directory.FullName;

			directory = directory.Parent;
		}

		Assert.Fail(
			$"The repository root could not be found above '{AppContext.BaseDirectory}', so the repository "
				+ "guards cannot run and this build proves nothing about them. A full checkout is needed."
		);

		return string.Empty;
	}

	internal static string Below(params string[] segments) => Path.Combine([Root(), .. segments]);

	/// <summary>
	/// Lists the C# sources below a folder, never entering a <c>bin</c>, <c>obj</c> or <c>TestResults</c> folder.
	/// </summary>
	/// <remarks>
	/// The test workspaces of a running suite live in the build output, so a walk that entered it would race
	/// their creation and deletion.
	/// </remarks>
	internal static IEnumerable<string> SourceFilesBelow(string folder)
	{
		foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
			yield return file;

		foreach (string child in Directory.EnumerateDirectories(folder))
		{
			if (Path.GetFileName(child) is "bin" or "obj" or "TestResults")
				continue;

			foreach (string file in SourceFilesBelow(child))
				yield return file;
		}
	}
}
