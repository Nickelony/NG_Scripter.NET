using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Plugins;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Resolves the form of a <c>TriggerGroup=</c> line, long or word, and translates the plugin IDs
/// of its exported triggers.
/// </summary>
internal sealed class NgTriggerGroupResolver
{
	/// <summary>Masks the plugin ID byte in bits 16-23 of a DWORD argument.</summary>
	private const int PluginIdHighByteMask = 0x00FF0000;

	private readonly DiagnosticSink _diagnostics;
	private readonly PluginRegistry _plugins;
	private readonly IReadOnlyDictionary<string, NgCommandDefinition> _commandDefinitions;

	/// <param name="diagnostics">The per-run sink that receives the errors for a plugin ID or a word value.</param>
	/// <param name="plugins">The registry that translates the define ID of a plugin into its <c>Plugin=</c> ID.</param>
	/// <param name="commandDefinitions">The command definitions, among which the word form is found.</param>
	internal NgTriggerGroupResolver(
		DiagnosticSink diagnostics,
		PluginRegistry plugins,
		IReadOnlyDictionary<string, NgCommandDefinition> commandDefinitions
	)
	{
		_diagnostics = diagnostics;
		_plugins = plugins;
		_commandDefinitions = commandDefinitions;
	}

	/// <summary>
	/// Reports whether any argument's first <c>$</c> starts a hex literal with bits 16-23 set,
	/// which carries a plugin ID.
	/// </summary>
	/// <remarks>
	/// Such a literal keeps the long form; a constant, a define or a decimal never does (Module1.bas:7302-7327).
	/// </remarks>
	internal static bool HasPluginIdLiteral(IReadOnlyList<string> arguments)
	{
		foreach (string argument in arguments)
		{
			int start = argument.IndexOf('$', StringComparison.Ordinal) + 1;

			if (start == 0)
				continue;

			int end = start;

			while (end < argument.Length && char.IsAsciiHexDigit(argument[end]))
				end++;

			if (
				ulong.TryParse(
					s: argument.AsSpan(start, end - start),
					style: NumberStyles.AllowHexSpecifier,
					provider: CultureInfo.InvariantCulture,
					result: out ulong value
				)
				&& (value & PluginIdHighByteMask) != 0
			)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Converts the <c>TriggerGroup=</c> plugin IDs from define IDs to script-assigned IDs,
	/// as VB6 ConvertiIdPluginTrigger does.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> after an error for a plugin that no <c>Plugin=</c> line declares, or whose define ID
	/// the high word cannot hold, reported once for each such plugin on the line.
	/// </returns>
	/// <remarks>
	/// The first DWORD of each triplet (positions 1, 4, 7, 10, 13...) may carry a plugin
	/// ID in the high word, bits 16-31. VB6 reads it as a signed word and skips the conversion for an ID of 32768
	/// or more (Module1.bas:7044, 7551, 7563-7565), where the port fails (PD-96).
	/// </remarks>
	internal bool TryConvertPluginIds(List<NgArgumentValue> commandArgs, SourceLocation location)
	{
		// Args layout: [0]=ID(Word), [1]=Long, [2]=Long, [3]=Long, [4]=ArrayLong
		// Triplet positions in data values (after ID): 0, 3, 6, 9...
		// → arg indices 1, 4(first array), 4(fourth array)...
		List<string> rejectedPlugins = [];

		// Convert fixed Long args at positions 1 (first of first triplet)
		if (commandArgs.Count > 1)
			commandArgs[1] = ConvertPluginId(commandArgs[1], rejectedPlugins);

		// Convert array args: first of each triplet (indices 0, 3, 6, 9...)
		if (commandArgs.Count > 4 && commandArgs[4].ArrayValue is { } array)
		{
			NgArgumentValue[] converted = [.. array];

			for (int i = 0; i < converted.Length; i += 3)
				converted[i] = ConvertPluginId(converted[i], rejectedPlugins);

			commandArgs[4] = NgArgumentValue.ForArray(commandArgs[4].Type, converted);
		}

		foreach (string message in rejectedPlugins)
			_diagnostics.Error(message, location);

		return rejectedPlugins.Count == 0;
	}

	/// <summary>
	/// Finds the <c>TriggerGroup=</c> word form, failing on a value outside the signed and unsigned reading
	/// of a word (PD-56).
	/// </summary>
	/// <remarks>
	/// The word form writes each value in one word, which VB6 width-checks (Module1.bas:7442, 7458-7462).
	/// </remarks>
	internal bool TryFindWordForm(
		List<NgArgumentValue> commandArgs,
		NgCommandCode wordForm,
		ScriptCommandLine line,
		[NotNullWhen(true)] out NgCommandDefinition? wordDefinition
	)
	{
		wordDefinition = _commandDefinitions.Values.FirstOrDefault(candidate => candidate.Code == wordForm);

		if (wordDefinition is null)
		{
			_diagnostics.Error($"The constants file defines no word form of TriggerGroup= (code {(int)wordForm})", line.Location);
			return false;
		}

		IEnumerable<NgArgumentValue> values = commandArgs.Skip(1).SelectMany(argument => argument.ArrayValue ?? [argument]);
		int argumentNumber = 1;

		foreach (NgArgumentValue value in values)
		{
			argumentNumber++;

			if (value.Int32Value is < short.MinValue or > ushort.MaxValue)
			{
				string range = FieldRange.DescribeArgument(
					command: "TriggerGroup=",
					argumentNumber,
					value: value.Int32Value,
					minimum: short.MinValue,
					maximum: ushort.MaxValue,
					holder: "its word"
				);

				_diagnostics.Error(
					$"{range}; TriggerGroup= keeps its long form only for a $ literal with a plugin ID, so write the value as one",
					line.ArgumentLocation(argumentNumber - 1)
				);

				wordDefinition = null;
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Translates the plugin ID in the high word of a value, or adds the error message that rejects it.
	/// </summary>
	private NgArgumentValue ConvertPluginId(NgArgumentValue arg, List<string> rejectedPlugins)
	{
		int val = arg.Int32Value;
		int highWord = (val >> 16) & 0xFFFF;

		if (highWord == 0)
			return arg;

		// VB6 holds the high word in an Integer, so a word of 0x8000 or more is negative and its conversion is skipped silently.
		if (highWord > short.MaxValue)
		{
			if (_plugins.Defines.TryGetName(highWord, out string? definedPlugin))
			{
				AddOnce(
					rejectedPlugins,
					$"TriggerGroup= names plugin '{definedPlugin}' by its ID {highWord}, which passes {short.MaxValue}, where VB6 skips the conversion and keeps the ID as written; give #DEFINE @{definedPlugin} an ID below {short.MaxValue + 1}"
				);
			}

			return arg;
		}

		if (!_plugins.TryTranslateTriggerPluginId(highWord, out int newId, out string? undeclaredPlugin))
		{
			AddOnce(
				rejectedPlugins,
				$"Exported trigger references plugin '{undeclaredPlugin}' which is not declared in any Plugin= command; declare it with a Plugin= line in [Options]"
			);

			return arg;
		}

		if (newId == highWord)
			return arg;

		return NgArgumentValue.ForInt32(arg.Type, (val & 0xFFFF) | (newId << 16));
	}

	private static void AddOnce(List<string> messages, string message)
	{
		if (!messages.Contains(message, StringComparer.OrdinalIgnoreCase))
			messages.Add(message);
	}
}
