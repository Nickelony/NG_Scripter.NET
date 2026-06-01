using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// Collects the three file extensions of the <c>[PCExtensions]</c> or the <c>[PSXExtensions]</c> sections of a script.
/// </summary>
/// <remarks>
/// The slots are <c>Level=</c>, <c>FMV=</c> and <c>Cut=</c>,
/// in VB6's order (<c>Module1.bas:4525</c>, <c>5522-5586</c>).
/// </remarks>
internal sealed class ExtensionsSectionBuilder
{
	private static readonly string[] s_slotCommands =
	[
		ClassicCommandCatalog.LevelCommand,
		ClassicCommandCatalog.FmvCommand,
		ClassicCommandCatalog.CutCommand,
	];

	private readonly DiagnosticSink _diagnostics;
	private readonly string _sectionName;
	private readonly SettingRecorder _settings;
	private readonly string?[] _extensions = new string?[ParsedScript.ExtensionSlotCount];
	private readonly SourceLocation[] _locations = new SourceLocation[ParsedScript.ExtensionSlotCount];

	/// <remarks>
	/// <paramref name="sectionName"/> is the header of the sections the builder collects,
	/// such as <c>[PCExtensions]</c>.
	/// </remarks>
	internal ExtensionsSectionBuilder(DiagnosticSink diagnostics, string sectionName)
	{
		_diagnostics = diagnostics;
		_sectionName = sectionName;
		_settings = new SettingRecorder(diagnostics);
	}

	/// <summary>Gets the command of each slot, in slot order (<c>Module1.bas:4525</c>).</summary>
	internal static IReadOnlyList<string> SlotCommands => s_slotCommands;

	/// <summary>Gets the three extension slots; a slot no line fills is <see langword="null"/>.</summary>
	internal IReadOnlyList<string?> Extensions => _extensions;

	/// <summary>
	/// Gets the location of the command of the line that fills each slot; a slot no line fills points at no line.
	/// </summary>
	internal IReadOnlyList<SourceLocation> Locations => _locations;

	/// <summary>Gets the number of slots the lines fill, each counted once, as VB6 counts them.</summary>
	internal int FilledCount { get; private set; }

	/// <summary>
	/// Gets the location of the first header of the sections the builder collects; none when the script
	/// has no such section.
	/// </summary>
	internal SourceLocation HeaderLocation { get; private set; }

	/// <summary>Records the header line of a section the builder collects; the first header is kept.</summary>
	internal void AddHeader(SourceLocation location)
	{
		if (HeaderLocation == default)
			HeaderLocation = location;
	}

	/// <summary>Adds a command line of an extensions section.</summary>
	/// <returns>
	/// <see langword="true"/> when the line is valid; otherwise, <see langword="false"/>, after an error.
	/// </returns>
	internal bool TryAdd(ScriptCommandLine line)
	{
		int index = Array.FindIndex(s_slotCommands, slotCommand => slotCommand.IgnoreCaseEquals(line.Command));

		if (index < 0)
		{
			_diagnostics.Error(ScriptCommandMessages.UnknownCommand(_sectionName, line.Command), line.CommandLocation);
			return false;
		}

		if (
			!ScriptArgumentResolver.CheckArgumentCount(
				diagnostics: _diagnostics,
				args: line.Arguments,
				expected: 1,
				command: line.Command,
				location: line.CommandLocation
			)
		)
		{
			return false;
		}

		if (!_settings.TryRecord(key: s_slotCommands[index], value: line.Arguments[0], line))
			return false;

		if (string.IsNullOrEmpty(_extensions[index]))
			FilledCount++;

		_extensions[index] = line.Arguments[0];
		_locations[index] = line.CommandLocation;
		return true;
	}
}
