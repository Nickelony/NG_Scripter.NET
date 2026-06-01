namespace TRNGScriptCompiler.Script;

/// <summary>One classic command of a <c>[Level]</c> or <c>[Title]</c> section, as the catalog declares it.</summary>
/// <param name="Name">The canonical spelling, with its <c>=</c>, that every message and comparison uses.</param>
/// <param name="SortOrder">
/// The command's index in VB6's emission order table, <c>VetOrdineTag</c> (<c>Module1.bas:5068-5074</c>).
/// </param>
internal sealed record ClassicCommandInfo(string Name, int SortOrder)
{
	/// <summary>Gets how the encoder writes the command; by default, from its tag and layout.</summary>
	internal ClassicHandler Handler { get; init; }

	/// <summary>Gets the first byte the command writes, or 0 for a command that writes no tag.</summary>
	/// <remarks>
	/// An inventory command adds its item and piece to this base, and a <see cref="ClassicFieldKind.TagOffset"/> field
	/// adds to it.
	/// </remarks>
	internal byte Tag { get; init; }

	/// <summary>Gets the arguments the generic encoder reads, one field per argument in order.</summary>
	internal IReadOnlyList<ClassicField> Layout { get; init; } = [];

	/// <summary>
	/// Gets the level flag the command sets, unless an <c>ENABLED</c>/<c>DISABLED</c> field turns it off.
	/// </summary>
	internal ScriptLevelFlags LevelFlag { get; init; }

	/// <summary>
	/// Gets the inventory kind, which also decides whether the item and piece numbers refine the sort key.
	/// </summary>
	internal ClassicInventoryKind Inventory { get; init; }

	/// <summary>
	/// Gets the number of inventory items or combos the command can name, or 0 for a command
	/// that is not an inventory command.
	/// </summary>
	internal int InventoryCount { get; init; }
}
