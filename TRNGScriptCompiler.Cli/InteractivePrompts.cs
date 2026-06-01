namespace TRNGScriptCompiler.Cli;

/// <summary>Asks for the folders of a compile in interactive mode and builds the options from the answers.</summary>
/// <param name="input">The reader that the answers come from.</param>
/// <param name="write">The callback that writes text to standard output, without adding a line break.</param>
/// <remarks>
/// A prompt ends in no line break, so the answer follows it on the same line. Redirected input is not echoed,
/// so there the next prompt follows the previous one on the same line.
/// </remarks>
internal sealed class InteractivePrompts(TextReader input, Action<string, ConsoleColor?> write)
{
	/// <summary>Asks for the engine folder, the script folder and the plugin folder.</summary>
	/// <returns>The options of the compile, or <see langword="null"/> when the engine folder answer is empty.</returns>
	/// <remarks>
	/// An empty script folder answer autodetects the folder, and an empty plugin folder answer takes the default
	/// plugin folder.
	/// </remarks>
	internal TRNGCompilerOptions? Ask()
	{
		write("Enter engine folder path: ", null);

		string? engineDirectoryPath = ReadPath();

		if (string.IsNullOrEmpty(engineDirectoryPath))
			return null;

		write("Enter script folder path (press Enter to autodetect): ", null);
		string? scriptDirectory = ReadPath();

		if (string.IsNullOrEmpty(scriptDirectory))
		{
			scriptDirectory = TRNGCompilerOptions.ResolveScriptDirectory(engineDirectoryPath);
			write($"  Autodetect -> {scriptDirectory}{Environment.NewLine}", null);
		}

		write("Enter plugin folder path (press Enter for NG_Center, or the engine folder without it): ", null);
		string? pluginDirectory = ReadPath();

		return CommandLineParser.CreateOptions(engineDirectoryPath, scriptDirectory, string.IsNullOrEmpty(pluginDirectory) ? null : pluginDirectory);
	}

	/// <summary>Reads an answered path without the double quotes that enclose it.</summary>
	/// <remarks>
	/// A folder dragged onto the console window arrives in double quotes when its path holds a space, and a quote
	/// is never part of a Windows path.
	/// </remarks>
	private string? ReadPath()
	{
		string? path = input.ReadLine()?.Trim();
		return path is ['"', .., '"'] ? path[1..^1].Trim() : path;
	}
}
