using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Plugins;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Compiles the NG lines of a parsed script after the whole script and the first language,
/// as VB6 does (Module1.bas:5655, 8283).
/// </summary>
/// <remarks>
/// <c>[Options]</c> comes first, then each section in order, with each <c>#DEFINE @plugin</c> line applied
/// where it stands (:8326, 8457).
/// </remarks>
internal sealed class NgScriptCompiler
{
	private readonly CompilationRun _run;
	private readonly NgCommandCatalog _commandCatalog;
	private readonly PluginRegistry _plugins;
	private readonly NgParser _parser;

	/// <remarks>
	/// The plugin registry holds the <c>@plugin</c> defines, and the string arguments resolve to indexes
	/// of the first language.
	/// </remarks>
	internal NgScriptCompiler(
		CompilationRun run,
		NgCommandCatalog commandCatalog,
		ExpressionEvaluator evaluator,
		ObjectsHCatalog objectsH,
		PluginRegistry plugins,
		LanguageData firstLanguage
	)
	{
		_run = run;
		_commandCatalog = commandCatalog;
		_plugins = plugins;

		_parser = new NgParser(run, commandCatalog, evaluator, objectsH, plugins, languageData: firstLanguage);
	}

	/// <summary>Compiles the NG lines of a parsed script into one command group per section.</summary>
	/// <remarks>
	/// A line that fails leaves an error in the run's diagnostics and no command.
	/// </remarks>
	internal NgScriptModel Compile(ParsedScript parsed, CancellationToken token)
	{
		NgCommandGroup options = CompileOptions(lines: parsed.OptionsNgLines, out bool encryptScript, out int settings, token);

		// VB6 empties the define table before the first level section and never between two (Module1.bas:8420), so a block in one level
		// holds in the levels after it.
		_plugins.Defines.Clear();

		var sections = new List<NgCommandGroup>(parsed.Sections.Count);

		foreach (ParsedSection section in parsed.Sections)
		{
			token.ThrowIfCancellationRequested();
			sections.Add(CompileSection(section));
		}

		var compiled = new NgScriptModel
		{
			OptionsCommands = options,
			LevelCommands = [.. sections],
			ImportFiles = [.. parsed.ImportFiles],
			EnableScriptEncryption = encryptScript,
			NgSettings = settings,
		};

		CheckImportArguments(compiled);
		return compiled;
	}

	private NgCommandGroup CompileOptions(IReadOnlyList<NgLine> lines, out bool encryptScript, out int settings, CancellationToken token)
	{
		var commands = new List<NgCommand>();
		NgMainFlags flags = 0;
		encryptScript = false;
		settings = 0;

		foreach (NgLine line in lines)
		{
			token.ThrowIfCancellationRequested();

			ScriptCommandLine source = line.CommandLine;

			NgCommand? command = _parser.Parse(source, isOptionsCommand: true);

			if (command is null)
				continue;

			if (TryGetFlagValue(command, out int flagValue))
			{
				flags |= (NgMainFlags)flagValue;
				continue;
			}

			if (command.CommandCode == NgCommandCode.Settings && command.Arguments.Count > 0)
			{
				int value = command.Arguments[0].Int32Value;

				// The NG security block stores Settings= in a VB6 Byte, which overflows outside 0-255 (Module1.bas:8008).
				if (value is < 0 or > 255)
				{
					_run.Diagnostics.Error(
						FormattableString.Invariant($"The value of '{source.Command}' must be between 0 and 255: {value}"),
						source.CommandLocation
					);

					continue;
				}

				encryptScript |= (value & 8) != 0;
				settings = value;
			}

			commands.Add(command);
		}

		return new NgCommandGroup { Commands = [.. commands], OptionFlags = flags };
	}

	private NgCommandGroup CompileSection(ParsedSection section)
	{
		_parser.ResetOccurrenceCounts();

		var commands = new List<NgCommand>();
		NgLevelFlags flags = 0;

		foreach (NgLine line in section.NgLines)
		{
			if (line.IsPluginDefine)
			{
				_plugins.Defines.Apply(line);
				continue;
			}

			ScriptCommandLine source = line.CommandLine;

			NgCommand? command = _parser.Parse(source, isOptionsCommand: false, isTitleCommand: section.SectionType == ScriptSectionType.Title);

			if (command is null)
				continue;

			if (TryGetFlagValue(command, out int flagValue))
				flags |= (NgLevelFlags)flagValue;
			else
				commands.Add(command);
		}

		return new NgCommandGroup { Commands = [.. commands], LevelFlags = flags };
	}

	/// <summary>
	/// Reads the flag bits of a command whose definition is a flag, which VB6 tells by its one <c>Bool</c> argument
	/// whatever its code (Module1.bas:7112).
	/// </summary>
	private bool TryGetFlagValue(NgCommand command, out int value)
	{
		value = 0;

		if (!_commandCatalog.Definitions.TryGetValue(command.CommandName, out NgCommandDefinition? definition) || !definition.IsFlag)
			return false;

		value = command.Arguments[0].BoolValue ? definition.BoolEnabledValue : definition.BoolDisabledValue;
		return true;
	}

	/// <summary>
	/// Fails, as VB6 does, an <c>IMPORT</c> ID that no <c>ImportFile=</c> declares,
	/// unless it is -1 (Module1.bas:7464-7484).
	/// </summary>
	/// <remarks>
	/// The IDs compare by the word the import chunk writes, where VB6 compares the argument as written (PD-95).
	/// </remarks>
	private void CheckImportArguments(NgScriptModel ngModel)
	{
		HashSet<short> declaredIds = [.. ngModel.ImportFiles.Select(importFile => (short)importFile.ImportId)];

		foreach (NgCommand command in ngModel.OptionsCommands.Commands.Concat(ngModel.LevelCommands.SelectMany(group => group.Commands)))
		{
			for (int i = 0; i < command.Arguments.Count; i++)
			{
				NgArgumentValue argument = command.Arguments[i];

				if (argument.Type != NgArgumentType.Import || argument.Int32Value == -1 || declaredIds.Contains((short)argument.Int32Value))
					continue;

				_run.Diagnostics.Error(
					FormattableString.Invariant(
						$"Argument {i + 1} of {command.CommandName} names import ID {argument.Int32Value}, which no ImportFile= declares; declare it in [Options] or write a declared ID"
					),

					command.Location
				);
			}
		}
	}
}
