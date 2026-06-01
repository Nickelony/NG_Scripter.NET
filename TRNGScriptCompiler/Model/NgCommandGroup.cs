namespace TRNGScriptCompiler.Model;

/// <summary>Group of NG commands for a section (Options or Level).</summary>
internal sealed record NgCommandGroup
{
	internal IReadOnlyList<NgCommand> Commands { get; init; } = [];

	/// <summary>Gets the NG option flags accumulated for an options section.</summary>
	internal NgMainFlags OptionFlags { get; init; }

	/// <summary>Gets the NG level flags accumulated for a level or title section.</summary>
	internal NgLevelFlags LevelFlags { get; init; }
}
