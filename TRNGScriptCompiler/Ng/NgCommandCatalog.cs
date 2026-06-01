using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Numerics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Holds the NG command definitions that <see cref="Parse"/> reads once per run from the shared
/// <see cref="ConstantsFile"/> lines.
/// </summary>
/// <remarks>
/// A <c>scripter_constants.txt</c> definition
/// reads CommandCode#MaxOccurrences#CommandName=:ArgType1:ArgType2:...#BoolEnabled#BoolDisabled.
/// </remarks>
internal sealed class NgCommandCatalog
{
	private readonly FrozenDictionary<string, NgCommandDefinition> _definitions;

	private NgCommandCatalog(FrozenDictionary<string, NgCommandDefinition> definitions) => _definitions = definitions;

	/// <summary>Gets all known NG command definitions keyed by command name.</summary>
	internal IReadOnlyDictionary<string, NgCommandDefinition> Definitions => _definitions;

	/// <summary>
	/// Parses the <c>&lt;START_NEW_COMMANDS&gt;</c> definitions, failing on a malformed one, which would compile
	/// wrong (PD-62).
	/// </summary>
	/// <remarks>
	/// Names match without regard to case, and the code, limit and flags take <c>$</c>, as VB6 reads
	/// them (<c>Module1.bas:4557-4606</c>).
	/// </remarks>
	internal static NgCommandCatalog Parse(IReadOnlyList<string> lines, string filePath, DiagnosticSink diagnostics)
	{
		var definitions = new Dictionary<string, (NgCommandDefinition Definition, int LineNumber)>(StringComparer.OrdinalIgnoreCase);

		foreach ((int lineNumber, string text) in ConstantsFileSectionReader.Read(lines, "<START_NEW_COMMANDS>"))
		{
			var location = new SourceLocation(lineNumber, File: filePath);
			string line = text.StripInlineComment(";");

			if (line.Length == 0 || !TryParseDefinition(line, location, diagnostics, definition: out NgCommandDefinition? parsed))
				continue;

			// The constants file names the word form of TriggerGroup= (code 46) TriggerGroup= too; the port names it apart.
			if (parsed.Code == NgCommandCode.TriggerGroupWord && parsed.Name.IgnoreCaseEquals("TriggerGroup="))
				parsed = parsed with { Name = "TriggerGroupWord=" };

			if (definitions.TryGetValue(parsed.Name, out (NgCommandDefinition Definition, int LineNumber) first))
			{
				diagnostics.Error(
					$"The definition of {parsed.Name} repeats line {first.LineNumber}, and a command takes one definition; remove one of the two lines",
					location
				);

				continue;
			}

			definitions.Add(parsed.Name, (parsed, lineNumber));
		}

		return new NgCommandCatalog(
			definitions.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.Definition, StringComparer.OrdinalIgnoreCase)
		);
	}

	private static bool TryReadArgumentTypes(
		string name,
		string[] typeNames,
		SourceLocation location,
		DiagnosticSink diagnostics,
		[NotNullWhen(true)] out List<NgArgumentType>? argumentTypes
	)
	{
		argumentTypes = [];

		foreach (string piece in typeNames)
		{
			string typeName = piece.Trim();

			if (!TryParseArgumentType(typeName, out NgArgumentType type))
			{
				diagnostics.Error(
					typeName.Length == 0
						? $"The definition of {name} has an empty argument type; remove the extra ':'"
						: $"The definition of {name} has the unknown argument type '{typeName}'; write Slot, String, Word, Integer, Long, Bool, Import, Array, ArrayByte, ArrayNybble or ArrayLong",

					location
				);

				argumentTypes = null;
				return false;
			}

			argumentTypes.Add(type);
		}

		return true;
	}

	private static bool TryParseDefinition(
		string line,
		SourceLocation location,
		DiagnosticSink diagnostics,
		[NotNullWhen(true)] out NgCommandDefinition? definition
	)
	{
		// Format: Code#MaxOccurrences#[*]Name=:Type1:Type2:...#BoolEnabled#BoolDisabled
		definition = null;
		string[] parts = line.Split('#');

		if (parts.Length is < 3 or > 5)
		{
			diagnostics.Error(
				$"The definition '{line}' does not have the form CODE#LIMIT#Name=:Type:...#ENABLED#DISABLED, with the last two fields optional",
				location
			);

			return false;
		}

		string[] pieces = parts[2].Split(':');
		string nameWithEquals = pieces[0].Trim();

		if (
			!TryReadField(text: parts[0], field: "code", line, location, diagnostics, value: out int code)
			|| !TryReadField(text: parts[1], field: "limit", line, location, diagnostics, value: out int maxOccurrences)
		)
		{
			return false;
		}

		int boolEnabled = 0;
		int boolDisabled = 0;

		if (
			(parts.Length >= 4 && !TryReadField(text: parts[3], field: "enabled value", line, location, diagnostics, value: out boolEnabled))
			|| (parts.Length == 5 && !TryReadField(text: parts[4], field: "disabled value", line, location, diagnostics, value: out boolDisabled))
		)
		{
			return false;
		}

		// VB6 skips a definition named RESERVED (Module1.bas:4621).
		if (nameWithEquals.IgnoreCaseEquals("RESERVED"))
			return false;

		bool isOptionsOnly = nameWithEquals.StartsWith('*');
		string name = isOptionsOnly ? nameWithEquals[1..] : nameWithEquals;

		if (name.Length < 2 || !name.EndsWith('=') || name.AsSpan(0, name.Length - 1).ContainsAny(['=', ' ', '\t']))
		{
			diagnostics.Error($"The definition '{line}' names no command; write the name with its '=', such as Snow=", location);
			return false;
		}

		if (!TryReadArgumentTypes(name, typeNames: pieces[1..], location, diagnostics, out List<NgArgumentType>? argumentTypes))
			return false;

		if (!TryCheckEncodable(name, code, maxOccurrences, argumentTypes, location, diagnostics))
			return false;

		// VB6 makes a Bool command a flag only when one of its values is not 0 (Module1.bas:7121), and a flag reads one argument.
		if (argumentTypes.Contains(NgArgumentType.Bool))
		{
			if (argumentTypes.Count > 1)
			{
				diagnostics.Error($"The definition of {name} gives a Bool with other arguments, but a Bool command takes one argument", location);
				return false;
			}

			if (boolEnabled == 0 && boolDisabled == 0)
			{
				diagnostics.Error(
					$"The definition of {name} gives 0 for both values of its Bool, so the flag sets nothing; write the flag after the next '#'",
					location
				);

				return false;
			}
		}

		definition = new NgCommandDefinition
		{
			Name = name,
			Code = (NgCommandCode)code,
			ArgumentTypes = [.. argumentTypes],
			IsOptionsOnly = isOptionsOnly,
			MaxOccurrences = maxOccurrences,
			BoolEnabledValue = boolEnabled,
			BoolDisabledValue = boolDisabled,
		};

		return true;
	}

	private static bool TryCheckEncodable(
		string name,
		int code,
		int maxOccurrences,
		List<NgArgumentType> argumentTypes,
		SourceLocation location,
		DiagnosticSink diagnostics
	)
	{
		if (code is < byte.MinValue or > byte.MaxValue)
		{
			diagnostics.Error(
				$"The code {code} of the definition of {name} lies outside 0 to {byte.MaxValue}, the values the high byte of a command word holds; write a code in that range",
				location
			);

			return false;
		}

		if (maxOccurrences < -1)
		{
			diagnostics.Error(
				$"The limit {maxOccurrences} of the definition of {name} is below -1; write -1 for no limit, or the most times a section holds the command",
				location
			);

			return false;
		}

		int arrayCount = argumentTypes.Count(type => type.IsArray());

		if (arrayCount > 1)
		{
			diagnostics.Error($"The definition of {name} gives {arrayCount} array types, but a command takes one array; keep one", location);
			return false;
		}

		if (arrayCount == 1 && !argumentTypes[^1].IsArray())
		{
			diagnostics.Error(
				$"The definition of {name} gives an array type before its last argument type, but an array takes the remaining values; move the array to the end",
				location
			);

			return false;
		}

		return true;
	}

	private static bool TryReadField(string text, string field, string line, SourceLocation location, DiagnosticSink diagnostics, out int value)
	{
		if (NumericLiteral.TryReadStandalone(text, out value, out string? reason))
			return true;

		diagnostics.Error($"The {field} of the definition '{line}' is invalid: {reason}", location);
		return false;
	}

	private static bool TryParseArgumentType(string typeName, out NgArgumentType type)
	{
		type = typeName.ToUpperInvariant() switch
		{
			"SLOT" => NgArgumentType.ItemSlot,
			"STRING" => NgArgumentType.String,
			"WORD" => NgArgumentType.Word,
			"ARRAYBYTE" => NgArgumentType.ArrayByte,
			"INTEGER" => NgArgumentType.Integer,
			"LONG" => NgArgumentType.Long,
			"BOOL" => NgArgumentType.Bool,
			"ARRAY" => NgArgumentType.Array,
			"ARRAYNYBBLE" => NgArgumentType.ArrayNybble,
			"IMPORT" => NgArgumentType.Import,
			"ARRAYLONG" => NgArgumentType.ArrayLong,
			_ => NgArgumentType.None,
		};

		return type != NgArgumentType.None;
	}
}
