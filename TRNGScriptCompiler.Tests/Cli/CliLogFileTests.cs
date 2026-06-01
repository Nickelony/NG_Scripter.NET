using TRNGScriptCompiler.Cli;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Cli;

[TestClass]
public sealed class CliLogFileTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void Lines_ReplaceTheOldFileAndAreEncodedAsWindows1252()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		string path = workspace.PathOf(CliLogFile.FileName);

		File.WriteAllText(path, "The log of the last compile");

		bool written = CliLogFile.TryWrite(workspace.RootPath, ["Café", "Done"], out string? error);

		Assert.IsTrue(written);
		Assert.IsNull(error);

		// Windows-1252 holds the 'é' in the one byte 0xE9, where UTF-8 takes two.
		byte[] lineBreak = [.. Environment.NewLine.Select(c => (byte)c)];
		byte[] expected = [.. "Caf"u8, 0xE9, .. lineBreak, .. "Done"u8, .. lineBreak];

		Assert.AreSequenceEqual(expected, File.ReadAllBytes(path));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FolderInPlaceOfTheFile_GivesTheErrorLine()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		string path = workspace.PathOf(CliLogFile.FileName);

		Directory.CreateDirectory(path);

		bool written = CliLogFile.TryWrite(workspace.RootPath, ["Done"], out string? error);

		Assert.IsFalse(written);

		// The line ends in the operating system's message, which the test cannot know.
		string prefix = $"Error: The log file {path} could not be written: ";

		Assert.IsNotNull(error);
		Assert.AreEqual(prefix, error[..prefix.Length]);
	}
}
