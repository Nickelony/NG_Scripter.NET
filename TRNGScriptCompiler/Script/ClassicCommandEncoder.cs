using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Text;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Emits a classic command as the handler of its catalog entry says: from the entry's tag and field layout,
/// or through a method of its own.
/// </summary>
/// <remarks>
/// Each method sees only the <see cref="ClassicCompileContext"/> and the shared <see cref="ScriptArgumentResolver"/>,
/// so codegen has no ambient state.
/// </remarks>
internal sealed class ClassicCommandEncoder
{
	private readonly DiagnosticSink _diagnostics;
	private readonly ScriptArgumentResolver _resolver;

	internal ClassicCommandEncoder(DiagnosticSink diagnostics, ScriptArgumentResolver resolver)
	{
		_diagnostics = diagnostics;
		_resolver = resolver;
	}

	/// <summary>
	/// Dispatches one classic command through the catalog and encodes it, after it checks the command's argument count.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the command encoded; otherwise, <see langword="false"/>, after an error,
	/// as each <c>Encode</c> method of this type returns.
	/// </returns>
	internal bool TryEncode(ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		ClassicCommandInfo command = ClassicCommandCatalog.Get(context.Command);

		// Messages and comparisons use the canonical spelling, because VB6 matches a command without regard to case.
		context.Command = command.Name;

		if (
			GetArgumentCount(command) is { } expected
			&& !ScriptArgumentResolver.CheckArgumentCount(
				diagnostics: _diagnostics,
				args: arguments,
				expected,
				command: command.Name,
				location: context.Location
			)
		)
		{
			return false;
		}

		return command.Handler switch
		{
			ClassicHandler.Layout => EncodeLayout(command, context, arguments),
			ClassicHandler.SectionHeader => true,
			ClassicHandler.Level => EncodeLevel(context, arguments),
			ClassicHandler.Fmv => EncodeFmv(command, context, arguments),
			ClassicHandler.Legend => EncodeLegend(command, context),
			ClassicHandler.LensFlare => EncodeLensFlare(command, context, arguments),
			ClassicHandler.AnimatingMip => EncodeAnimatingMip(command, context, arguments),
			ClassicHandler.InventoryItem => EncodeInventoryItem(command, context, arguments),
			ClassicHandler.InventoryCombo => EncodeInventoryCombo(command, context, arguments),
			_ => throw new InvalidOperationException($"The catalog entry of {command.Name} names no handler {command.Handler}."),
		};
	}

	/// <summary>Gets the number of arguments a command takes, or <see langword="null"/> when it checks none.</summary>
	/// <remarks>
	/// <c>Legend=</c> takes the raw remainder of its line, and the section header writes <c>Name=</c>.
	/// </remarks>
	private static int? GetArgumentCount(ClassicCommandInfo command)
	{
		return command.Handler switch
		{
			ClassicHandler.Layout => command.Layout.Count,
			ClassicHandler.Level or ClassicHandler.Fmv or ClassicHandler.AnimatingMip => 2,
			ClassicHandler.LensFlare => 6,
			ClassicHandler.InventoryItem => 8,
			ClassicHandler.InventoryCombo => 9,
			_ => null,
		};
	}

	/// <summary>
	/// Encodes a command from its catalog entry's tag and fields, failing at the first bad argument so the error
	/// names it.
	/// </summary>
	/// <remarks>
	/// The command sets its level flag unless a <see cref="ClassicFieldKind.Switch"/> field reads <c>DISABLED</c>.
	/// </remarks>
	private bool EncodeLayout(ClassicCommandInfo command, ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		IReadOnlyList<ClassicField> layout = command.Layout;
		int tag = command.Tag;
		bool setsFlag = true;
		int[] values = new int[layout.Count];

		for (int i = 0; i < layout.Count; i++)
		{
			ClassicField field = layout[i];

			if (field.Kind == ClassicFieldKind.Switch)
			{
				if (!EnabledState.TryParse(arguments[i], out bool enabled))
				{
					_diagnostics.Error(EnabledState.InvalidValueError(i + 1, command.Name, arguments[i]), context.ArgumentLocation(i));
					return false;
				}

				setsFlag &= enabled;
				continue;
			}

			if (
				!_resolver.TryEvaluateArgument(args: arguments, index: i, command: command.Name, location: context.ArgumentLocation(i), out int value)
			)
			{
				return false;
			}

			if (field.Holder is not null && (value < field.Minimum || value > field.Maximum))
			{
				long maximum = field is { Kind: ClassicFieldKind.Byte, Maximum: int.MaxValue } ? byte.MaxValue : field.Maximum;

				_diagnostics.Error(
					FieldRange.DescribeArgument(
						command: command.Name,
						argumentNumber: i + 1,
						value,
						minimum: field.Minimum,
						maximum,
						holder: field.Holder
					),

					context.ArgumentLocation(i)
				);

				return false;
			}

			if (
				field.Kind == ClassicFieldKind.Byte
				&& !_resolver.ValidateWidth(value, widthInBytes: 1, command: command.Name, argumentIndex: i, location: context.ArgumentLocation(i))
			)
			{
				return false;
			}

			if (field.Kind == ClassicFieldKind.TagOffset)
				tag += value - field.Minimum;

			values[i] = value;
		}

		if (command.Tag != 0)
			context.Bytes.Add((byte)tag);

		for (int i = 0; i < layout.Count; i++)
		{
			if (layout[i].Kind == ClassicFieldKind.Byte)
				context.Bytes.Add((byte)values[i]);
			else if (layout[i].Kind == ClassicFieldKind.DWord)
				BinaryUtilities.WriteDWord(context.Bytes, values[i]);
		}

		if (setsFlag)
			context.Flags |= command.LevelFlag;

		return true;
	}

	/// <summary>Encodes the <c>FMV=</c> command (slot index plus trigger flag).</summary>
	private bool EncodeFmv(ClassicCommandInfo command, ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 0,
				command: command.Name,
				location: context.ArgumentLocation(0),
				value: out int index
			)
			|| !_resolver.TryEvaluateArgument(
				args: arguments,
				index: 1,
				command: command.Name,
				location: context.ArgumentLocation(1),
				value: out int trigger
			)
		)
		{
			return false;
		}

		// Bit 7 of the written byte is the trigger flag (Module1.bas:5789-5790), so VB6 writes FMV= 200,0 as movie 72 with the trigger set.
		if (index is < 0 or > 127)
		{
			_diagnostics.Error(
				FormattableString.Invariant($"The FMV= index must be between 0 and 127, because bit 7 of its byte is the trigger flag: {index}"),
				context.ArgumentLocation(0)
			);

			return false;
		}

		if (trigger is < 0 or > 1)
		{
			_diagnostics.Error(FormattableString.Invariant($"The FMV= trigger must be 0 or 1: {trigger}"), context.ArgumentLocation(1));
			return false;
		}

		if (trigger == 1)
			index |= 128;

		context.Bytes.Add(command.Tag);
		context.Bytes.Add((byte)index);

		return true;
	}

	/// <summary>Encodes the <c>Level=</c> command (stores the CD slot).</summary>
	private bool EncodeLevel(ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 1,
				command: context.Command,
				location: context.ArgumentLocation(1),
				value: out int cdVal
			)
		)
		{
			return false;
		}

		if (cdVal is < 0 or > 255)
		{
			_diagnostics.Error(
				FieldRange.DescribeArgument(
					command: ClassicCommandCatalog.LevelCommand,
					argumentNumber: 2,
					value: cdVal,
					minimum: 0,
					maximum: 255,
					holder: "a CD track"
				),

				context.ArgumentLocation(1)
			);

			return false;
		}

		context.CdSlot = cdVal;
		return true;
	}

	/// <summary>Encodes the <c>Legend=</c> command (string index from the raw line remainder).</summary>
	private bool EncodeLegend(ClassicCommandInfo command, ClassicCompileContext context)
	{
		// VB6 takes the raw remainder of the line after "Legend=" (commas are NOT argument
		// separators here: "there could be a comma in the string").
		string text = ScriptLineLexer.GetRawRemainder(context.Text);
		int index = _resolver.ResolveStringIndex(text, notFoundMessage: $"Cannot find legend text in language file: {text}", context.Location);

		if (index < 0)
			return false;

		if (!_resolver.ValidateOneByteStringIndex(index, text, command: command.Name, location: context.Location))
			return false;

		context.Bytes.Add(command.Tag);
		context.Bytes.Add((byte)index);

		return true;
	}

	private bool EncodeLensFlare(ClassicCommandInfo command, ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		context.Bytes.Add(command.Tag);
		context.Flags |= command.LevelFlag;

		for (int i = 0; i < 3; i++)
		{
			if (
				!_resolver.TryEvaluateArgument(
					args: arguments,
					index: i,
					command: context.Command,
					location: context.ArgumentLocation(i),
					out int value
				)
			)
			{
				return false;
			}

			// VB6 reads the coordinate 4 bytes wide and writes it divided by 256 as a word (Module1.bas:6028-6034), so a quotient outside the
			// signed word wraps.
			int compressed = value / 256;

			if (compressed is < short.MinValue or > short.MaxValue)
			{
				_diagnostics.Error(
					FormattableString.Invariant(
						$"Coordinate {i + 1} of LensFlare= is out of range: {arguments[i].Trim()} divided by 256 must fit -32768 to 32767, so the coordinate lies between {(short.MinValue * 256) - 255} and {(short.MaxValue * 256) + 255}"
					),

					context.ArgumentLocation(i)
				);

				return false;
			}

			BinaryUtilities.WriteWord(context.Bytes, (short)compressed);
		}

		for (int i = 3; i < 6; i++)
		{
			if (
				!_resolver.TryEvaluateArgument(
					args: arguments,
					index: i,
					command: context.Command,
					location: context.ArgumentLocation(i),
					out int value
				)
			)
			{
				return false;
			}

			if (value < 0)
			{
				_diagnostics.Error(
					FieldRange.DescribeArgument(command: command.Name, argumentNumber: i + 1, value, minimum: 0, maximum: 255, holder: "a color"),
					context.ArgumentLocation(i)
				);

				return false;
			}

			if (!_resolver.ValidateWidth(value, widthInBytes: 1, command: command.Name, argumentIndex: i, location: context.ArgumentLocation(i)))
				return false;

			context.Bytes.Add((byte)value);
		}

		return true;
	}

	private bool EncodeAnimatingMip(ClassicCommandInfo command, ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 0,
				command: context.Command,
				location: context.ArgumentLocation(0),
				value: out int slot
			)
		)
		{
			return false;
		}

		if (slot is < 1 or > 16)
		{
			_diagnostics.Error(
				FieldRange.DescribeArgument(command: command.Name, argumentNumber: 1, value: slot, minimum: 1, maximum: 16, holder: "a MIP slot"),
				context.ArgumentLocation(0)
			);

			return false;
		}

		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 1,
				command: context.Command,
				location: context.ArgumentLocation(1),
				value: out int distance
			)
		)
		{
			return false;
		}

		if (distance is < 0 or > 15)
		{
			_diagnostics.Error(
				FieldRange.DescribeArgument(command: command.Name, argumentNumber: 2, value: distance, minimum: 0, maximum: 15, holder: "a distance"),
				context.ArgumentLocation(1)
			);

			return false;
		}

		context.Bytes.Add(command.Tag);
		context.Bytes.Add((byte)((distance * 16) + (slot - 1)));

		return true;
	}

	/// <summary>
	/// Encodes a <c>Key=</c>, <c>Puzzle=</c>, <c>Pickup=</c> or <c>Examine=</c> item from its entry's first item tag
	/// and count.
	/// </summary>
	private bool EncodeInventoryItem(ClassicCommandInfo command, ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		int maxCount = command.InventoryCount;

		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 0,
				command: context.Command,
				location: context.ArgumentLocation(0),
				value: out int itemNumber
			)
		)
		{
			return false;
		}

		if (itemNumber < 1 || itemNumber > maxCount)
		{
			_diagnostics.Error(
				FieldRange.DescribeArgument(
					command: context.Command,
					argumentNumber: 1,
					value: itemNumber,
					minimum: 1,
					maximum: maxCount,
					holder: "an item number"
				),

				context.ArgumentLocation(0)
			);

			return false;
		}

		context.Bytes.Add((byte)(command.Tag + itemNumber - 1));

		return TryEncodeInventoryWords(context, arguments, stringArgument: 1);
	}

	/// <summary>
	/// Encodes a <c>KeyCombo=</c>, <c>PuzzleCombo=</c> or <c>PickupCombo=</c> piece from its entry's first piece tag
	/// and count.
	/// </summary>
	private bool EncodeInventoryCombo(ClassicCommandInfo command, ClassicCompileContext context, IReadOnlyList<string> arguments)
	{
		int maxCount = command.InventoryCount;

		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 0,
				command: context.Command,
				location: context.ArgumentLocation(0),
				value: out int comboNumber
			)
		)
		{
			return false;
		}

		if (comboNumber < 1 || comboNumber > maxCount)
		{
			_diagnostics.Error(
				FieldRange.DescribeArgument(
					command: context.Command,
					argumentNumber: 1,
					value: comboNumber,
					minimum: 1,
					maximum: maxCount,
					holder: "a combo number"
				),

				context.ArgumentLocation(0)
			);

			return false;
		}

		if (
			!_resolver.TryEvaluateArgument(
				args: arguments,
				index: 1,
				command: context.Command,
				location: context.ArgumentLocation(1),
				value: out int piece
			)
		)
		{
			return false;
		}

		if (piece is < 1 or > 2)
		{
			_diagnostics.Error(
				FieldRange.DescribeArgument(
					command: context.Command,
					argumentNumber: 2,
					value: piece,
					minimum: 1,
					maximum: 2,
					holder: "a piece number"
				),

				context.ArgumentLocation(1)
			);

			return false;
		}

		context.Bytes.Add((byte)(command.Tag + ((comboNumber - 1) * 2) + (piece - 1)));

		return TryEncodeInventoryWords(context, arguments, stringArgument: 2);
	}

	/// <summary>
	/// Writes an inventory command's string index and the six word arguments after it, which every inventory command
	/// ends with.
	/// </summary>
	private bool TryEncodeInventoryWords(ClassicCompileContext context, IReadOnlyList<string> arguments, int stringArgument)
	{
		int stringIndex = _resolver.ResolveStringIndex(
			arguments[stringArgument],
			notFoundMessage: $"Cannot find string '{arguments[stringArgument]}' in language file",
			context.ArgumentLocation(stringArgument)
		);

		if (stringIndex < 0)
			return false;

		BinaryUtilities.WriteWord(context.Bytes, (short)stringIndex);

		for (int i = stringArgument + 1; i < arguments.Count; i++)
		{
			if (
				!_resolver.TryEvaluateArgument(
					args: arguments,
					index: i,
					command: context.Command,
					location: context.ArgumentLocation(i),
					out int value
				)
			)
			{
				return false;
			}

			if (!_resolver.ValidateWidth(value, widthInBytes: 2, command: context.Command, argumentIndex: i, location: context.ArgumentLocation(i)))
				return false;

			BinaryUtilities.WriteWord(context.Bytes, (short)value);
		}

		return true;
	}
}
