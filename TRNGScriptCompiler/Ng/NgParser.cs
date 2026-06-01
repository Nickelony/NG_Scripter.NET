using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Plugins;

namespace TRNGScriptCompiler.Ng;

/// <summary>Admits each NG command to its section, parses its arguments and encodes it into words.</summary>
internal sealed class NgParser
{
	private readonly CompilationRun _run;
	private readonly IReadOnlyDictionary<string, NgCommandDefinition> _commandDefinitions;

	/// <summary>
	/// Tracks the commands of every <c>[Options]</c> section as one list,
	/// as VB6 checks them (Module1.bas:5448-5456, 8306-8311).
	/// </summary>
	private readonly NgSectionState _optionsState = new();
	private readonly NgSectionState _levelState = new();

	private readonly NgArgumentParser _argumentParser;
	private readonly NgCommandEncoder _encoder;
	private readonly NgDuplicateChecker _duplicates;
	private readonly NgTriggerGroupResolver _triggerGroups;
	private readonly PluginRegistry _plugins;

	/// <summary>
	/// Initializes a parser that resolves string arguments to indexes of the first language, bounds a <c>SLOT</c>
	/// argument by <c>Objects.h</c> and translates the plugin IDs of exported triggers, logging each verbosely.
	/// </summary>
	internal NgParser(
		CompilationRun run,
		NgCommandCatalog commandCatalog,
		ExpressionEvaluator evaluator,
		ObjectsHCatalog objectsH,
		PluginRegistry plugins,
		LanguageData languageData
	)
	{
		_run = run;
		_argumentParser = new NgArgumentParser(run.Diagnostics, objectsH, evaluator);
		_encoder = new NgCommandEncoder(run.Diagnostics, languageData);
		_commandDefinitions = commandCatalog.Definitions;
		_duplicates = new NgDuplicateChecker(run.Diagnostics, _encoder);
		_triggerGroups = new NgTriggerGroupResolver(run.Diagnostics, plugins, _commandDefinitions);
		_plugins = plugins;
	}

	/// <summary>
	/// Resets the occurrence counts and duplicate keys of level commands (called when starting a new level).
	/// </summary>
	/// <remarks>
	/// Every <c>[Options]</c> section of a script shares one table, which this never resets.
	/// </remarks>
	internal void ResetOccurrenceCounts() => _levelState.Reset();

	/// <summary>
	/// Parses an NG command from its arguments, or returns <see langword="null"/> when it does not parse.
	/// </summary>
	/// <remarks>
	/// The name is normalized with its trailing <c>=</c>. The line's location serves the diagnostics of the whole line
	/// and the <c>#DEFINE</c> values, its argument locations mark an argument in a diagnostic about it, and its command
	/// location marks the name in a diagnostic about the command.
	/// </remarks>
	internal NgCommand? Parse(ScriptCommandLine line, bool isOptionsCommand, bool isTitleCommand = false)
	{
		string commandName = line.Command;
		IReadOnlyList<string> arguments = line.Arguments;

		SourceLocation location = line.Location;
		SourceLocation commandLocation = line.CommandLocation;

		// The script parse admits to the NG lines only a name of this table, so the lookup cannot fail.
		NgCommandDefinition definition = _commandDefinitions[commandName];
		NgSectionState sectionState = isOptionsCommand ? _optionsState : _levelState;

		if (!AdmitToSection(commandName, definition, sectionState, nameLocation: commandLocation, isOptionsCommand, isTitleCommand))
			return null;

		if (definition.Code == NgCommandCode.Plugin && _plugins.IsRejected(location))
			return null;

		// Parse arguments into local list
		List<NgArgumentValue>? parsedArgs = _argumentParser.Parse(definition, line);

		if (parsedArgs is null)
			return null;

		NgCommandCode commandCode = definition.Code;

		// VB6 chooses the word form from the argument text before it reads a value (Module1.bas:7302-7335), and converts plugin
		// IDs only in the long form (:7558-7574).
		NgCommandCode? wordForm = NgCommandPolicies.Get(commandCode).WordForm;
		NgCommandCode? appliedWordForm = wordForm is not null && !NgTriggerGroupResolver.HasPluginIdLiteral(arguments) ? wordForm : null;

		if (wordForm is not null && appliedWordForm is null)
		{
			if (!_triggerGroups.TryConvertPluginIds(parsedArgs, location))
				return null;
		}

		if (!_duplicates.TryRegister(sectionState, definition, commandArgs: parsedArgs, nameLocation: commandLocation))
			return null;

		if (appliedWordForm is { } form)
		{
			if (!_triggerGroups.TryFindWordForm(commandArgs: parsedArgs, wordForm: form, line, out NgCommandDefinition? wordDefinition))
				return null;

			commandCode = wordDefinition.Code;
			definition = wordDefinition;
		}

		// Compile to binary words
		List<short>? compiledWords = _encoder.Encode(definition, parsedArgs, location);

		if (compiledWords is null)
			return null;

		// The low byte of the header word holds the argument word count, and VB6 rejects 256 or more (Module1.bas:7358-7362).
		if (compiledWords.Count - 1 > byte.MaxValue)
		{
			_run.Diagnostics.Error(
				$"{commandName} writes {compiledWords.Count - 1} words of arguments, but a command holds at most {byte.MaxValue}; give fewer values",
				commandLocation
			);

			return null;
		}

		return new NgCommand
		{
			CommandName = definition.Name,
			CommandCode = commandCode,
			Arguments = [.. parsedArgs],
			CompiledWords = [.. compiledWords],
			Location = location,
		};
	}

	/// <summary>Admits a command to its section and counts it there, or reports why it may not stand there.</summary>
	private bool AdmitToSection(
		string commandName,
		NgCommandDefinition definition,
		NgSectionState sectionState,
		SourceLocation nameLocation,
		bool isOptionsCommand,
		bool isTitleCommand
	)
	{
		// The port names the word form of TriggerGroup= internally, and VB6 has no such command.
		if (definition.Code == NgCommandCode.TriggerGroupWord)
		{
			_run.Diagnostics.Error(
				$"{commandName} is not a script command; write TriggerGroup=, which takes the word form when no argument is a $ literal with a plugin ID",
				nameLocation
			);

			return false;
		}

		if (definition.IsOptionsOnly && !isOptionsCommand)
		{
			_run.Diagnostics.Error($"{commandName} can only be used in [Options] section", nameLocation);
			return false;
		}

		if (NgCommandPolicies.Get(definition.Code).IsTitleOnly && !isTitleCommand)
		{
			_run.Diagnostics.Error($"{commandName} can only be used in [Title] section", nameLocation);
			return false;
		}

		// VB6 error 284 (Module1.bas:5441-5445).
		if (isOptionsCommand && !definition.IsOptionsOnly)
		{
			_run.Diagnostics.Error($"{commandName} can only be used in a [Level] or [Title] section", nameLocation);
			return false;
		}

		if (!sectionState.TryIncrementOccurrence(definition.Name, definition.MaxOccurrences))
		{
			_run.Diagnostics.Error($"Too many {commandName} commands; a section holds at most {definition.MaxOccurrences}", nameLocation);
			return false;
		}

		return true;
	}
}
