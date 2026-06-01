using System.Globalization;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Parses NG command arguments according to the definitions loaded from <c>scripter_constants.txt</c>.
/// </summary>
/// <remarks>
/// Every argument error fails the run, because VB6 treats
/// each one as fatal (<c>ErroreSintassi</c>, <c>Module1.bas:7668-7670</c>). A numeric or <c>SLOT</c> argument takes
/// <c>_</c> as the discard <c>IGNORE</c> names, which VB6 rejects (PD-97), and an <c>IGNORE</c> it accepts earns a hint
/// to write <c>_</c>.
/// </remarks>
internal sealed class NgArgumentParser
{
	private readonly DiagnosticSink _diagnostics;
	private readonly ObjectsHCatalog _objectsH;
	private readonly ExpressionEvaluator _expressionEvaluator;

	/// <summary>
	/// Initializes a parser that reports an error for each argument that does not parse and bounds a <c>SLOT</c>
	/// argument by the <c>Objects.h</c> slot count.
	/// </summary>
	/// <remarks>
	/// The parser also reports a warning for a grouped array that ends in a partial group and a hint
	/// for an <c>IGNORE</c> argument.
	/// </remarks>
	internal NgArgumentParser(DiagnosticSink diagnostics, ObjectsHCatalog objectsH, ExpressionEvaluator expressionEvaluator)
	{
		_diagnostics = diagnostics;
		_objectsH = objectsH;
		_expressionEvaluator = expressionEvaluator;
	}

	/// <summary>
	/// Parses the raw argument strings of a command into a typed list, or returns <see langword="null"/>
	/// after a fatal diagnostic.
	/// </summary>
	/// <remarks>
	/// The line names the file and line of the command, which scope the <c>#DEFINE</c> values its arguments may use.
	/// An error about one argument marks it in the source line, and one about the command marks the whole line.
	/// </remarks>
	internal List<NgArgumentValue>? Parse(NgCommandDefinition definition, ScriptCommandLine line)
	{
		IReadOnlyList<string> arguments = line.Arguments;

		if (definition.IsFlag)
			return ParseSwitch(definition, line);

		// VB6 backward compatibility: GlobalTrigger with 6 args auto-adds -1 as 7th arg
		if (definition.Code == NgCommandCode.GlobalTrigger && arguments.Count == 6 && definition.ArgumentTypes.Count == 7)
			arguments = [.. arguments, "-1"];

		bool hasVariableArray = definition.ArgumentTypes.Any(type => type.IsArray());

		int fixedArgCount = hasVariableArray ? definition.ArgumentTypes.Count - 1 : definition.ArgumentTypes.Count;

		if (!hasVariableArray && arguments.Count != definition.ArgumentTypes.Count)
		{
			string expected = definition.Code == NgCommandCode.GlobalTrigger ? "6 or 7" : $"{definition.ArgumentTypes.Count}";
			_diagnostics.Error(ArgumentCountMessage.Wrong(definition.Name, expected, arguments.Count), line.Location);
			return null;
		}

		if (hasVariableArray && arguments.Count < fixedArgCount)
		{
			_diagnostics.Error(ArgumentCountMessage.Wrong(definition.Name, $"at least {fixedArgCount}", arguments.Count), line.Location);
			return null;
		}

		var parsed = new List<NgArgumentValue>();

		for (int i = 0; i < fixedArgCount; i++)
		{
			NgArgumentType argType = definition.ArgumentTypes[i];
			int pluginId = 0;

			NgArgumentValue? argValue =
				argType == NgArgumentType.ItemSlot
					? ParseSlot(definition, line, argumentIndex: i, argument: arguments[i])
					: ParseArgument(definition, line, argumentIndex: i, type: argType, argument: arguments[i], out pluginId);

			if (argValue is null || !ValidateRange(definition, line, argumentIndex: i, type: argType, argument: argValue.Value))
				return null;

			if (argType != NgArgumentType.String)
				HintDiscard(line, argumentIndex: i, argument: arguments[i]);

			if (i == 0 && definition.Code is NgCommandCode.Customize or NgCommandCode.Parameters && argType == NgArgumentType.Long && pluginId > 0)
			{
				int originalValue = argValue.Value.Int32Value;
				argValue = NgArgumentValue.ForInt32(argType, originalValue | (pluginId << 16));
			}

			if (i == 1 && definition.Code == NgCommandCode.AssignSlot && argType == NgArgumentType.Long && pluginId > 0)
			{
				int originalValue = argValue.Value.Int32Value;
				argValue = NgArgumentValue.ForInt32(argType, originalValue | (pluginId << 16));
			}

			parsed.Add(argValue.Value);
		}

		if (hasVariableArray)
		{
			if (ParseArray(definition, line, arguments, fixedArgCount) is not { } array)
				return null;

			parsed.Add(array);
			WarnOnPartialGroup(definition, line, arguments.Count);
		}

		return parsed;
	}

	/// <summary>Reads the one <c>ENABLED</c> or <c>DISABLED</c> argument of a Bool command.</summary>
	private List<NgArgumentValue>? ParseSwitch(NgCommandDefinition definition, ScriptCommandLine line)
	{
		IReadOnlyList<string> arguments = line.Arguments;

		if (arguments.Count != 1)
		{
			_diagnostics.Error(ArgumentCountMessage.Wrong(definition.Name, expected: 1, actual: arguments.Count), line.Location);
			return null;
		}

		if (EnabledState.TryParse(arguments[0], out bool enabled))
			return [NgArgumentValue.ForBool(enabled)];

		_diagnostics.Error(EnabledState.InvalidValueError(1, definition.Name, arguments[0]), line.ArgumentLocation(0));
		return null;
	}

	/// <summary>Reads the arguments after the fixed ones as the command's trailing array.</summary>
	private NgArgumentValue? ParseArray(NgCommandDefinition definition, ScriptCommandLine line, IReadOnlyList<string> arguments, int fixedArgCount)
	{
		NgArgumentType arrayType = definition.ArgumentTypes[fixedArgCount];
		var arrayArgs = new List<NgArgumentValue>();
		int elementCount = arguments.Count - fixedArgCount;

		// The count of a byte or nybble array is one byte, which overflows VB6 at 256 elements (Module1.bas:7195, 7257).
		if (arrayType is NgArgumentType.ArrayByte or NgArgumentType.ArrayNybble && elementCount > byte.MaxValue)
		{
			_diagnostics.Error(
				$"{definition.Name} gives {elementCount} array values, but the array holds at most {byte.MaxValue}, because its count is one byte",
				line.Location
			);

			return null;
		}

		for (int i = fixedArgCount; i < arguments.Count; i++)
		{
			NgArgumentType elementType = arrayType == NgArgumentType.ArrayLong ? NgArgumentType.Long : NgArgumentType.Word;

			NgArgumentValue? elementValue = ParseArgument(
				definition,
				line,
				argumentIndex: i,
				type: elementType,
				argument: arguments[i],
				pluginId: out _
			);

			if (elementValue is null || !ValidateRange(definition, line, argumentIndex: i, type: arrayType, argument: elementValue.Value))
				return null;

			HintDiscard(line, argumentIndex: i, argument: arguments[i]);
			arrayArgs.Add(elementValue.Value);
		}

		return NgArgumentValue.ForArray(arrayType, arrayArgs);
	}

	/// <summary>
	/// Warns when the values of a command the help groups, as <see cref="NgCommandPolicies"/> records,
	/// end in a partial group.
	/// </summary>
	/// <remarks>
	/// Neither compiler checks the length, so a partial group reaches the engine; it is a warning because scripts VB6
	/// compiles hold partial groups.
	/// </remarks>
	private void WarnOnPartialGroup(NgCommandDefinition definition, ScriptCommandLine line, int argumentCount)
	{
		if (NgCommandPolicies.Get(definition.Code).Groups is not { } groups)
			return;

		int remainder = (argumentCount - groups.FirstValue) % groups.Size;

		if (remainder != 0)
		{
			_diagnostics.Warning(
				$"{definition.Name} ends in a partial group of {remainder} {(remainder == 1 ? "value" : "values")}; each {groups.Name} takes {groups.Size} values",
				line.Location
			);
		}
	}

	/// <summary>
	/// Hints at the <c>_</c> discard when a numeric or <c>SLOT</c> argument that parsed is written <c>IGNORE</c>.
	/// </summary>
	/// <remarks>
	/// The caller skips a <c>STRING</c> argument, where <c>_</c> is text. A hint follows the range check,
	/// so it never stands beside an error about the same argument.
	/// </remarks>
	private void HintDiscard(ScriptCommandLine line, int argumentIndex, string argument)
	{
		if (IsIgnoreValue(argument.Trim()))
			_diagnostics.Hint("Replace IGNORE with the _ discard", line.ArgumentLocation(argumentIndex));
	}

	/// <summary>Parses one argument of the given type and hands back the plugin ID of the constant it names.</summary>
	/// <remarks>
	/// The plugin ID is 0 when the argument names no plugin constant or does not parse.
	/// </remarks>
	private NgArgumentValue? ParseArgument(
		NgCommandDefinition definition,
		ScriptCommandLine line,
		int argumentIndex,
		NgArgumentType type,
		string argument,
		out int pluginId
	)
	{
		pluginId = 0;

		switch (type)
		{
			case NgArgumentType.Word:
			case NgArgumentType.Integer:
				if (!TryEvaluateArgument(definition, line, argumentIndex, argument, value: out int wordValue, out pluginId))
					return null;

				return NgArgumentValue.ForInt32(type, wordValue);

			case NgArgumentType.Long:
				if (!TryEvaluateArgument(definition, line, argumentIndex, argument, value: out int longValue, out pluginId))
					return null;

				return NgArgumentValue.ForInt32(type, longValue);

			case NgArgumentType.String:
				string str = argument.Trim();

				// Only * and IGNORE mean -1 here; VB6 looks any other text up, _ included (Module1.bas:7521-7529).
				if (str == "*" || IsIgnoreValue(str))
					return NgArgumentValue.ForString("*");

				// VB6 reads the index of a !NNN reference with GetArgNumerico (Module1.bas:6535-6540), so !$82 and !MY_ID name an
				// ExtraNG string too; the reference keeps the decimal index the language data looks up.
				if (str.StartsWith('!'))
				{
					if (!TryEvaluateArgument(definition, line, argumentIndex, argument: str[1..], value: out int extraNgIndex, out pluginId))
						return null;

					return NgArgumentValue.ForString(string.Create(CultureInfo.InvariantCulture, $"!{extraNgIndex}"));
				}

				return NgArgumentValue.ForString(str);

			case NgArgumentType.Import:
				if (!TryEvaluateArgument(definition, line, argumentIndex, argument, value: out int importId, out pluginId))
					return null;

				return NgArgumentValue.ForInt32(type, importId);

			default:
				_diagnostics.Error($"Unsupported argument type: {type}", line.ArgumentLocation(argumentIndex));
				return null;
		}
	}

	/// <summary>
	/// Parses a <c>SLOT</c> argument, which VB6 takes as a number, a $ literal
	/// or a slot name (Module1.bas:7432-7436, 7499-7506).
	/// </summary>
	/// <remarks>
	/// The port also takes a <c>#DEFINE</c> and sums of them, and fails on a TRNG constant, a static, a plugin constant
	/// and the # form (PD-60).
	/// </remarks>
	private NgArgumentValue? ParseSlot(NgCommandDefinition definition, ScriptCommandLine line, int argumentIndex, string argument)
	{
		string trimmed = argument.Trim();

		if (IsDiscardValue(trimmed))
			return NgArgumentValue.ForInt32(NgArgumentType.ItemSlot, -1);

		NumericEvaluation evaluation = _expressionEvaluator.Evaluate(trimmed, line.Location.File, IsSlotTerm);

		if (evaluation.Success)
			return NgArgumentValue.ForInt32(NgArgumentType.ItemSlot, evaluation.Value);

		string prefix = $"Argument {argumentIndex + 1} of {definition.Name}";

		_diagnostics.Error(
			evaluation.Rejection switch
			{
				{ Term: var term, Kind: { } kind } =>
					$"{prefix} names {term}, which is {kind.Describe()}, not a slot; write a slot name, a number, a $ literal, a #DEFINE or a sum of them",

				{ Term: var term } => $"{prefix} writes {term}, a hexadecimal form a slot does not take; write ${term[1..]}",
				null => $"{prefix} is invalid: {evaluation.Error}",
			},

			line.ArgumentLocation(argumentIndex)
		);

		return null;
	}

	private static bool IsSlotTerm(string term, ConstantKind? kind)
	{
		return kind switch
		{
			null => !term.StartsWith('#'),
			ConstantKind.Define or ConstantKind.Slot => true,
			_ => false,
		};
	}

	/// <summary>
	/// Checks an argument's range, reading a word both signed and unsigned (PD-56) and keeping VB6's array byte
	/// and nybble ranges.
	/// </summary>
	/// <remarks>
	/// VB6 checks a word to -65536..65535 (Module1.bas:4915-4937; arrays, :7230-7246), so a value that fits
	/// only by wrapping passes.
	/// </remarks>
	private bool ValidateRange(
		NgCommandDefinition definition,
		ScriptCommandLine line,
		int argumentIndex,
		NgArgumentType type,
		NgArgumentValue argument
	)
	{
		if (argument.Type is NgArgumentType.String or NgArgumentType.Bool)
			return true;

		int value = argument.Int32Value;
		string prefix = $"Argument {argumentIndex + 1} of {definition.Name}";
		SourceLocation location = line.ArgumentLocation(argumentIndex);

		switch (type)
		{
			case NgArgumentType.Word or NgArgumentType.Integer or NgArgumentType.Import or NgArgumentType.Array:
			// With TPOS_TEST_ITEM_INDEX the slot field of TestPosition= holds an item index.
			case NgArgumentType.ItemSlot when definition.Code == NgCommandCode.TestPosition:
				if (FieldRange.Fits(value, widthInBytes: 2, out long minimum, out long maximum))
					return true;

				_diagnostics.Error(
					FieldRange.DescribeArgument(
						command: definition.Name,
						argumentNumber: argumentIndex + 1,
						value,
						minimum,
						maximum,
						holder: "its word"
					),

					location
				);

				return false;

			case NgArgumentType.ItemSlot:
				int slotCount = _objectsH.SlotCount ?? (ushort.MaxValue + 1);

				if (value >= 0 && value < slotCount)
					return true;

				_diagnostics.Error(
					_objectsH.SlotCount is null
						? FormattableString.Invariant($"{prefix} names slot {value}, but a slot lies between 0 and {ushort.MaxValue}")
						: FormattableString.Invariant($"{prefix} names slot {value}, but Objects.h defines slots 0 to {slotCount - 1}"),

					location
				);

				return false;

			case NgArgumentType.ArrayByte
			or NgArgumentType.ArrayNybble:
				int max = type == NgArgumentType.ArrayByte ? byte.MaxValue : 15;

				if (value >= 0 && value <= max)
					return true;

				string holder = max == 15 ? "an array nybble" : "an array byte";

				_diagnostics.Error(
					FieldRange.DescribeArgument(command: definition.Name, argumentNumber: argumentIndex + 1, value, minimum: 0, maximum: max, holder),
					location
				);

				return false;

			default:
				return true;
		}
	}

	private bool TryEvaluateArgument(
		NgCommandDefinition definition,
		ScriptCommandLine line,
		int argumentIndex,
		string argument,
		out int value,
		out int pluginId
	)
	{
		argument = argument.Trim();
		pluginId = 0;

		if (IsDiscardValue(argument))
		{
			value = -1;
			return true;
		}

		NumericEvaluation evaluation = _expressionEvaluator.Evaluate(argument, line.Location.File);
		value = evaluation.Value;

		if (evaluation.Success)
		{
			pluginId = evaluation.PluginId;
			return true;
		}

		_diagnostics.Error($"Argument {argumentIndex + 1} of {definition.Name} is invalid: {evaluation.Error}", line.ArgumentLocation(argumentIndex));

		return false;
	}

	private static bool IsIgnoreValue(string value) => value.IgnoreCaseEquals("IGNORE");

	private static bool IsDiscardValue(string value) => value == "_" || IsIgnoreValue(value);
}
