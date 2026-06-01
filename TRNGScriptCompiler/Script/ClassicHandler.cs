namespace TRNGScriptCompiler.Script;

/// <summary>How the encoder writes a classic command, which its catalog entry names.</summary>
internal enum ClassicHandler
{
	/// <summary>The generic encoder writes the command from the entry's tag and field layout.</summary>
	Layout,

	/// <summary>The section header writes the command, so its line writes no bytes.</summary>
	SectionHeader,

	/// <summary><c>Level=</c>: stores the CD slot, which the section header writes as its CD byte.</summary>
	Level,

	/// <summary><c>FMV=</c>: the slot index plus the trigger flag.</summary>
	Fmv,

	/// <summary><c>Legend=</c>: the string index of the raw remainder of the line.</summary>
	Legend,

	/// <summary><c>LensFlare=</c>: coordinates that the format stores divided by 256.</summary>
	LensFlare,

	/// <summary><c>AnimatingMIP=</c>: the slot and the distance, packed into one byte.</summary>
	AnimatingMip,

	/// <summary>An inventory item command, which adds its item and piece to the tag.</summary>
	InventoryItem,

	/// <summary>An inventory combo command, which adds its combo and piece to the tag.</summary>
	InventoryCombo,
}
