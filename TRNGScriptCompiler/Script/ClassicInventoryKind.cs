namespace TRNGScriptCompiler.Script;

/// <summary>Whether a classic command names an inventory item, an item combo, or neither.</summary>
internal enum ClassicInventoryKind
{
	/// <summary>Not an inventory command.</summary>
	None,

	/// <summary>An item command such as <c>Key=</c>, whose first argument names the item.</summary>
	Item,

	/// <summary>
	/// A combo command such as <c>KeyCombo=</c>, whose first two arguments name the combo and its piece.
	/// </summary>
	Combo,
}
