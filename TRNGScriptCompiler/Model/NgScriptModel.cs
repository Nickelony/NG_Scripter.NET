namespace TRNGScriptCompiler.Model;

/// <summary>The NG commands of a script after the NG compile, grouped by section, with its import files.</summary>
/// <remarks>
/// Each command holds its encoded words, so the NG header encoder writes them as they stand.
/// </remarks>
internal sealed record NgScriptModel
{
	internal NgCommandGroup OptionsCommands { get; init; } = new();

	/// <summary>Gets the NG command groups emitted for each level or title section.</summary>
	internal IReadOnlyList<NgCommandGroup> LevelCommands { get; init; } = [];

	/// <summary>Gets the imported files that should be embedded in the NG header.</summary>
	internal IReadOnlyList<NgImportFile> ImportFiles { get; init; } = [];

	/// <summary>
	/// Gets a value indicating whether the VB6 <c>Script.dat</c> header encryption should be applied after NG output
	/// is written.
	/// </summary>
	internal bool EnableScriptEncryption { get; init; }

	/// <summary>
	/// Gets the value of the last <c>Settings=</c> line of the options, or 0 when the script has none.
	/// </summary>
	/// <remarks>
	/// The security block stores it in byte 19, and a value outside 0-255 is an error.
	/// </remarks>
	internal int NgSettings { get; init; }
}
