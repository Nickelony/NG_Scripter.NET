namespace TRNGScriptCompiler.Cli;

/// <summary>
/// Parses the console command line, NG Center's switches included, into a <see cref="CommandLineParseResult"/>.
/// </summary>
/// <remarks>
/// The one path, quoted or not, before or after the switches, is the engine folder or NG Center's <c>script.txt</c>;
/// PD-91 lists forms it rejects.
/// </remarks>
internal static class CommandLineParser
{
	private const string ScriptFileName = "script.txt";

	/// <summary>
	/// Builds compile options, taking each value left
	/// out from <see cref="TRNGCompilerOptions.CreateDefaults(string, MainLanguageSelector?)"/>.
	/// </summary>
	internal static TRNGCompilerOptions CreateOptions(
		string engineDirectoryPath,
		string? scriptDirectory = null,
		string? pluginDirectory = null,
		string? constantsPath = null,
		string? mainLanguage = null
	)
	{
		var defaults = TRNGCompilerOptions.CreateDefaults(engineDirectoryPath, mainLanguage);

		return defaults with
		{
			ConstantsFilePath = constantsPath ?? defaults.ConstantsFilePath,
			ScriptDirectory = scriptDirectory ?? defaults.ScriptDirectory,
			PluginDirectory = pluginDirectory ?? defaults.PluginDirectory,
		};
	}

	/// <summary>
	/// Parses console arguments:
	/// <c>&lt;engine-folder&gt; [options]</c> or <c>&lt;path&gt;\script\script.txt [options]</c>.
	/// </summary>
	/// <remarks>
	/// Unknown options, missing option values, contradicting options and a second path are errors rather than ignored.
	/// </remarks>
	internal static CommandLineParseResult Parse(string[] args)
	{
		if (args.Length == 0)
			return new CommandLineParseResult.Error("No engine folder specified");

		foreach (string argument in args)
		{
			if (IsHelpFlag(argument))
				return new CommandLineParseResult.Help();

			if (IsVersionFlag(argument))
				return new CommandLineParseResult.Version();
		}

		var switches = new Switches();
		var cursor = new ArgumentCursor(args);

		// The C runtime splits an unquoted path at its spaces, so the path is the run of arguments that are no switch, joined by one space. NG
		// Center takes the first quoted string of its command line, or the whole line when nothing is quoted (Module1.bas:3258-3285), and
		// matches its switches as whole words anywhere (:3740-3747), so a word of the path such as "-" or "-old\script" is no switch.
		var pathPieces = new List<string>();

		int lastPieceIndex = -1;
		string? firstUnknownWordInPath = null;

		while (cursor.MoveNext())
		{
			string argument = cursor.Current;

			if (!argument.StartsWith('-'))
			{
				if (pathPieces.Count > 0 && lastPieceIndex != cursor.Position - 1)
				{
					return new CommandLineParseResult.Error(
						$"'{argument}' follows an option, so it is a second path after '{string.Join(' ', pathPieces)}'; pass one path, and quote it"
					);
				}

				pathPieces.Add(argument);
				lastPieceIndex = cursor.Position;
				continue;
			}

			switch (argument.ToLowerInvariant())
			{
				case "--wait" or "-w":
					switches = switches with { WaitRequested = true };
					break;

				// NG Center matches its switches without regard to case (Option Compare Text, Module1.bas:4, 3740-3747).
				case "-nowait":
					switches = switches with { NoWait = true };
					break;

				case "-log":
					switches = switches with { WriteLogFile = true };
					break;

				// The console host shows no message box, so the switch has nothing to suppress.
				case "-nomsgbox":
					break;

				case "--quiet"
				or "-q"
				or "-concise":
					switches = switches with { ConciseLogging = true };
					break;

				case "--verbose"
				or "-v":
					switches = switches with { ConciseLogging = false };
					break;

				case "--main-language"
				or "-ml":
					if (cursor.TakeValue() is not { } mainLanguage)
						return new CommandLineParseResult.Error("Missing language value after main language option");

					switches = switches with { MainLanguage = mainLanguage };
					break;

				case "--script-folder"
				or "-sf":
					if (cursor.TakeValue() is not { } scriptDirectory)
						return new CommandLineParseResult.Error("Missing path after script folder option");

					switches = switches with { ScriptDirectory = scriptDirectory };
					break;

				case "--plugin-folder"
				or "-pf":
					if (cursor.TakeValue() is not { } pluginDirectory)
						return new CommandLineParseResult.Error("Missing path after plugin folder option");

					switches = switches with { PluginDirectory = pluginDirectory };
					break;

				case "--constants"
				or "-c":
					if (cursor.TakeValue() is not { } constantsPath)
						return new CommandLineParseResult.Error("Missing path after constants option");

					switches = switches with { ConstantsPath = constantsPath };
					break;

				// A word that is no switch continues the path it follows, which then names script.txt; otherwise, the word is an unknown option.
				default:
					if (pathPieces.Count == 0 || lastPieceIndex != cursor.Position - 1)
						return new CommandLineParseResult.Error($"Unknown option: {argument}");

					pathPieces.Add(argument);
					lastPieceIndex = cursor.Position;
					firstUnknownWordInPath ??= argument;
					break;
			}
		}

		return ToCompileResult(target: string.Join(' ', pathPieces), firstUnknownWordInPath, switches);
	}

	/// <summary>
	/// Checks the path and the switches together, and builds the compile they ask for, the engine folder or NG Center's
	/// <c>script.txt</c> form.
	/// </summary>
	private static CommandLineParseResult ToCompileResult(string target, string? firstUnknownWordInPath, Switches switches)
	{
		if (switches.WaitRequested && switches.NoWait)
			return new CommandLineParseResult.Error("--wait and -NoWait contradict each other; keep one of them");

		if (string.IsNullOrWhiteSpace(target))
			return new CommandLineParseResult.Error("No engine folder specified");

		bool scriptFileForm = Path.GetExtension(target).Equals(".txt", StringComparison.OrdinalIgnoreCase);

		if (!scriptFileForm && firstUnknownWordInPath is not null)
			return new CommandLineParseResult.Error($"Unknown option: {firstUnknownWordInPath}");

		if (scriptFileForm && !Path.GetFileName(target).Equals(ScriptFileName, StringComparison.OrdinalIgnoreCase))
		{
			return new CommandLineParseResult.Error(
				$"NG Center's command line compiles script.txt in the script folder, so '{target}' names another file; pass the path of script.txt"
			);
		}

		string engineDirectoryPath = scriptFileForm ? EngineDirectoryOf(target) : target;

		// NG Center compiles <TRLE>\script\SCRIPT.TXT whatever folder the path names (Module1.bas:8815).
		string? scriptDirectory = scriptFileForm ? switches.ScriptDirectory ?? Path.Combine(engineDirectoryPath, "script") : switches.ScriptDirectory;

		TRNGCompilerOptions options = CreateOptions(
			engineDirectoryPath,
			scriptDirectory,
			pluginDirectory: switches.PluginDirectory,
			constantsPath: switches.ConstantsPath,
			mainLanguage: switches.MainLanguage
		);

		// NG Center's command-line compile waits for a key at the end unless -NoWait is given (Principale.frm:6790, Module1.bas:8978).
		bool waitForKey = switches.WaitRequested || (scriptFileForm && !switches.NoWait);

		return new CommandLineParseResult.Compile(options, ConciseLogging: switches.ConciseLogging, waitForKey, WriteLogFile: switches.WriteLogFile);
	}

	/// <summary>
	/// Finds the engine folder of a <c>script.txt</c> path: its folder, less a last <c>script</c> folder.
	/// </summary>
	/// <remarks>
	/// NG Center drops a last <c>\script</c>, matched without regard to case, from the folder to find the engine
	/// folder (Module1.bas:3727-3737).
	/// </remarks>
	private static string EngineDirectoryOf(string scriptFilePath)
	{
		string folder = Path.GetDirectoryName(Path.GetFullPath(scriptFilePath)) ?? scriptFilePath;

		if (Path.GetFileName(folder).Equals("script", StringComparison.OrdinalIgnoreCase))
			return Path.GetDirectoryName(folder) ?? folder;

		return folder;
	}

	private static bool IsHelpFlag(string argument) => argument.ToLowerInvariant() is "--help" or "-h" or "-?" or "/?";

	private static bool IsVersionFlag(string argument) => argument.Equals("--version", StringComparison.OrdinalIgnoreCase);

	/// <summary>The values the switches of a command line set.</summary>
	private sealed record Switches
	{
		internal string? MainLanguage { get; init; }

		internal string? ScriptDirectory { get; init; }

		internal string? PluginDirectory { get; init; }

		internal string? ConstantsPath { get; init; }

		internal bool ConciseLogging { get; init; }

		internal bool WaitRequested { get; init; }

		internal bool NoWait { get; init; }

		internal bool WriteLogFile { get; init; }
	}
}
