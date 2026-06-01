using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>
/// The contents of <c>Script.txt</c> and its includes as the script parser collected them, before any command compiles.
/// </summary>
/// <remarks>
/// The parser has already rejected every unknown command, so each classic line names a classic command and each NG line
/// an NG one.
/// </remarks>
internal sealed record ParsedScript
{
	/// <summary>The number of extension slots of a <c>[PCExtensions]</c> or <c>[PSXExtensions]</c> section.</summary>
	internal const int ExtensionSlotCount = 3;

	/// <summary>Gets the classic options of every <c>[Options]</c> section.</summary>
	internal ScriptOptions Options { get; init; } = new();

	/// <summary>
	/// Gets the location of the first <c>[Options]</c> line, at which an error about the NG options section points;
	/// none when the script has no such section.
	/// </summary>
	internal SourceLocation OptionsHeaderLocation { get; init; }

	/// <summary>Gets the <c>[Title]</c> and <c>[Level]</c> sections in source order.</summary>
	internal IReadOnlyList<ParsedSection> Sections { get; init; } = [];

	/// <summary>Gets the language files of the <c>[Language]</c> section in index order.</summary>
	internal IReadOnlyList<string> LanguageFiles { get; init; } = [];

	/// <summary>
	/// Gets the location of the file name on each language file's <c>File=</c> line, in index order, at which an error
	/// about the name points.
	/// </summary>
	internal IReadOnlyList<SourceLocation> LanguageFileLocations { get; init; } = [];

	/// <summary>Gets the three PSX extension slots: <c>Level=</c>, <c>FMV=</c> and <c>Cut=</c>.</summary>
	internal IReadOnlyList<string?> PsxExtensions { get; init; } = new string?[ExtensionSlotCount];

	/// <summary>Gets the number of PSX extension slots to emit.</summary>
	/// <remarks>
	/// VB6 counts slot fills (each slot only once, the first time it becomes non-empty) and emits slots
	/// <c>0..count-1</c> in index order, including empty holes.
	/// </remarks>
	internal int PsxExtensionCount { get; init; }

	/// <summary>
	/// Gets the location of the line that fills each PSX extension slot, in slot order, at which an error
	/// about the slot points.
	/// </summary>
	/// <remarks>
	/// A slot that no line fills, or that lies past the end of the list, points at no line.
	/// </remarks>
	internal IReadOnlyList<SourceLocation> PsxExtensionLocations { get; init; } = [];

	/// <summary>Gets the three PC extension slots: <c>Level=</c>, <c>FMV=</c> and <c>Cut=</c>.</summary>
	internal IReadOnlyList<string?> PcExtensions { get; init; } = new string?[ExtensionSlotCount];

	/// <summary>Gets the number of PC extension slots to emit (see <see cref="PsxExtensionCount"/>).</summary>
	internal int PcExtensionCount { get; init; }

	/// <summary>
	/// Gets the location of the line that fills each PC extension slot (see <see cref="PsxExtensionLocations"/>).
	/// </summary>
	internal IReadOnlyList<SourceLocation> PcExtensionLocations { get; init; } = [];

	/// <summary>Gets the files the <c>ImportFile=</c> lines of <c>[Options]</c> read.</summary>
	internal IReadOnlyList<NgImportFile> ImportFiles { get; init; } = [];

	/// <summary>
	/// Gets the NG lines of all <c>[Options]</c> sections in source order, which VB6 appends
	/// to one list (Module1.bas:5448-5456).
	/// </summary>
	internal IReadOnlyList<NgLine> OptionsNgLines { get; init; } = [];

	/// <summary>Gets the level path of each section that gives one, in section order, one per <c>Level=</c>.</summary>
	/// <remarks>
	/// Two sections that share a file contribute two entries, as VB6's <c>TotLivelli</c> counts
	/// them (Module1.bas:5811-5814).
	/// </remarks>
	internal IReadOnlyList<string> LevelPaths =>
		[.. Sections.Where(section => !string.IsNullOrEmpty(section.LevelPath)).Select(section => section.LevelPath)];
}
