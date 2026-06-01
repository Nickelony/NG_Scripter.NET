using System.Globalization;
using System.Runtime.InteropServices;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Checks the settings a <c>[Level]</c> or <c>[Title]</c> section gives more than once, warning on an identical repeat
/// and failing a different value.
/// </summary>
/// <remarks>
/// It reads the compiled bytes of each line to find the setting that line gives (PD-02, PD-31).
/// </remarks>
internal sealed class ClassicRepeatChecker
{
	private readonly DiagnosticSink _diagnostics;

	/// <param name="diagnostics">The per-run sink that receives the error or the warning for a repeat.</param>
	internal ClassicRepeatChecker(DiagnosticSink diagnostics) => _diagnostics = diagnostics;

	/// <summary>Finds the setting a compiled line gives and the value it gives it.</summary>
	/// <remarks>
	/// VB6 counts a repeat by its tag (<c>Module1.bas:8781-8790</c>), so the key is the tag of resolved values:
	/// <c>Key= $1</c> is <c>Key= 1</c>.
	/// </remarks>
	internal static ClassicOccurrence GetOccurrence(ParsedClassicLine line, List<byte> compiledBytes, int start, ScriptLevelFlags lineFlags)
	{
		string written = line.CommandLine.Command;
		string command = ClassicCommandCatalog.TryGet(written, out ClassicCommandInfo? info) ? info.Name : written;

		int length = compiledBytes.Count - start;

		return command switch
		{
			// Each FMV= line is a movie of its own.
			ClassicCommandCatalog.FmvCommand => new ClassicOccurrence(line, command, Key: null, Value: string.Empty),

			// VB6 looks the name up without regard to case, so two spellings that differ only in case name one string.
			ClassicCommandCatalog.NameCommand => new ClassicOccurrence(
				line,
				command,
				Key: command,
				Value: ScriptLineLexer.GetRawRemainder(line.CommandLine.Text).ToUpperInvariant()
			),

			ClassicCommandCatalog.LevelCommand => new ClassicOccurrence(line, command, Key: command, Value: string.Empty),

			// AnimatingMIP= writes one tag for every slot, so its key adds the slot.
			ClassicCommandCatalog.AnimatingMipCommand => new ClassicOccurrence(
				line,
				command,
				Key: $"{command}{compiledBytes[start + 1] & 0x0F}",
				Value: (compiledBytes[start + 1] >> 4).ToString(CultureInfo.InvariantCulture)
			),

			// A level flag writes no tag, so it keys on its command.
			_ when length == 0 => new ClassicOccurrence(line, command, Key: command, Value: ((int)lineFlags).ToString(CultureInfo.InvariantCulture)),

			// The tag differs per ResidentCut= slot and per inventory item and piece, so each is a setting of its own.
			_ => new ClassicOccurrence(
				line,
				command,
				Key: compiledBytes[start].ToString("X2", CultureInfo.InvariantCulture),
				Value: Convert.ToHexString(CollectionsMarshal.AsSpan(compiledBytes).Slice(start + 1, length - 1))
			),
		};
	}

	/// <summary>
	/// Checks the settings a section gives more than once, warning on an identical repeat (PD-02, PD-31).
	/// </summary>
	/// <remarks>
	/// VB6 writes every repeat and warns (<c>Module1.bas:6409-6419</c>), but a different value fails,
	/// because the script does not say which applies.
	/// </remarks>
	internal bool TryCheck(ParsedSection section, List<ClassicOccurrence> occurrences)
	{
		IEnumerable<List<ClassicOccurrence>> repeatedSettings = occurrences
			.Where(occurrence => occurrence.Key is not null)
			.GroupBy(occurrence => occurrence.Key, StringComparer.Ordinal)
			.Select(group => group.OrderBy(occurrence => occurrence.Line.SourceIndex).ToList())
			.Where(group => group.Count > 1)
			.OrderBy(group => group[1].Line.SourceIndex);

		bool success = true;

		foreach (List<ClassicOccurrence> repeats in repeatedSettings)
		{
			ClassicOccurrence first = repeats[0];

			// LoadCamera= keeps VB6's warning, because the help's own [Level] example gives two and the engine uses the last one written.
			if (first.Command == ClassicCommandCatalog.LoadCameraCommand)
			{
				ScriptCommandLine last = repeats[^1].Line.CommandLine;

				_diagnostics.Warning(
					$"There are {repeats.Count} 'LoadCamera=' lines in level '{section.LevelName}'; the engine uses the last one written",
					last.Location
				);

				continue;
			}

			ScriptCommandLine firstLine = first.Line.CommandLine;

			foreach (ClassicOccurrence repeat in repeats.Skip(1))
			{
				ScriptCommandLine line = repeat.Line.CommandLine;
				SourceLocation location = line.Location;
				string subject = GetRepeatSubject(repeat);

				// A second Level= fails even when identical, because VB6 adds a path that points every later level at the wrong file (Module1.bas:5811-5814).
				if (repeat.Command == ClassicCommandCatalog.LevelCommand)
				{
					_diagnostics.Error(RepeatedSettingMessages.SecondLevelLine(firstLine.Location, line.Location.File), location);
					success = false;
				}
				else if (!string.Equals(repeat.Value, first.Value, StringComparison.Ordinal))
				{
					_diagnostics.Error(
						RepeatedSettingMessages.DifferentValue(subject, firstLine.Location, currentFile: line.Location.File),
						location
					);

					success = false;
				}
				else
				{
					_diagnostics.Warning(RepeatedSettingMessages.SameValue(subject, firstLine.Location, currentFile: line.Location.File), location);
				}
			}
		}

		return success;
	}

	/// <summary>
	/// Names the setting as the author wrote it: the command, with the slot or item a slot command names.
	/// </summary>
	private static string GetRepeatSubject(ClassicOccurrence occurrence)
	{
		IReadOnlyList<string> arguments = occurrence.Line.CommandLine.Arguments;

		ClassicInventoryKind inventory = ClassicCommandCatalog.TryGet(occurrence.Command, out ClassicCommandInfo? info)
			? info.Inventory
			: ClassicInventoryKind.None;

		if (inventory == ClassicInventoryKind.Combo && arguments.Count > 1)
			return $"{occurrence.Command} {arguments[0]},{arguments[1]}";

		bool namesASlot =
			occurrence.Command is ClassicCommandCatalog.ResidentCutCommand or ClassicCommandCatalog.AnimatingMipCommand
			|| inventory != ClassicInventoryKind.None;

		return namesASlot && arguments.Count > 0 ? $"{occurrence.Command} {arguments[0]}" : occurrence.Command;
	}
}
