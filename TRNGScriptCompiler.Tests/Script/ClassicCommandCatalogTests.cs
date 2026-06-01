using TRNGScriptCompiler.Script;

namespace TRNGScriptCompiler.Tests.Script;

[TestClass]
[TestCategory("Fast")]
public sealed class ClassicCommandCatalogTests
{
	// VB6's emission order table, VetOrdineTag (Module1.bas:5068-5074), as VB6 spells it.
	private static readonly string[] s_vb6OrderTable =
	[
		"Name=",
		"Key=",
		"KeyCombo=",
		"Puzzle=",
		"PuzzleCombo=",
		"Pickup=",
		"PickupCombo=",
		"Examine=",
		"Fmv=",
		"UVRotate=",
		"Layer1=",
		"Layer2=",
		"LensFlare=",
		"Fog=",
		"Mirror=",
		"AnimatingMIP=",
		"Legend=",
		"YoungLara=",
		"ResidentCut=",
		"Cut=",
		"Horizon=",
		"StarField=",
		"Lightning=",
		"Train=",
		"Pulse=",
		"ColAddHorizon=",
		"Timer=",
		"RemoveAmulet=",
		"LoadCamera=",
		"ResetHub=",
		"NoLevel=",
		"Level=",
	];

	// The commands VB6 allows in a [Level] or [Title] section, VetTagLevels (Module1.bas:3934-3940).
	private static readonly string[] s_vb6SectionCommands =
	[
		"Name=",
		"Level=",
		"Fmv=",
		"Cut=",
		"ResidentCut=",
		"Layer1=",
		"Layer2=",
		"UVRotate=",
		"Legend=",
		"LensFlare=",
		"Mirror=",
		"Fog=",
		"AnimatingMIP=",
		"LoadCamera=",
		"ResetHUB=",
		"Key=",
		"Puzzle=",
		"Pickup=",
		"Examine=",
		"KeyCombo=",
		"PuzzleCombo=",
		"PickupCombo=",
		"YoungLara=",
		"Horizon=",
		"StarField=",
		"Lightning=",
		"Train=",
		"Pulse=",
		"ColAddHorizon=",
		"Timer=",
		"RemoveAmulet=",
		"NoLevel=",
	];

	[TestMethod]
	public void Catalog_HoldsExactlyTheCommandsVb6AllowsInALevelSection()
	{
		// VB6 matches the commands without regard to case (Option Compare Text), so a spelling differs only in its case.
		string[] missing = [.. s_vb6SectionCommands.Where(name => !ClassicCommandCatalog.Contains(name))];
		string[] extra = [.. ClassicCommandCatalog.Names.Where(name => !s_vb6SectionCommands.Contains(name, StringComparer.OrdinalIgnoreCase))];

		Assert.IsEmpty(missing, "VB6 allows: " + string.Join(", ", missing));
		Assert.IsEmpty(extra, "VB6 rejects: " + string.Join(", ", extra));
		Assert.HasCount(s_vb6SectionCommands.Length, ClassicCommandCatalog.Names);
	}

	[TestMethod]
	public void InventoryKind_MatchesTheSortKeyVb6RefinesByItemAndPiece()
	{
		// VB6 adds the item number for the entries 1 to 7 of its order table (Module1.bas:5695), and the piece number for a command whose
		// name contains "combo" (Module1.bas:5701).
		for (int z = 0; z < s_vb6OrderTable.Length; z++)
		{
			string name = s_vb6OrderTable[z];
			ClassicCommandCatalog.TryGet(name, out ClassicCommandInfo? command);

			Assert.AreEqual(z is > 0 and < 8, command?.Inventory is not null and not ClassicInventoryKind.None, name);
			Assert.AreEqual(name.Contains("combo", StringComparison.OrdinalIgnoreCase), command?.Inventory == ClassicInventoryKind.Combo, name);
		}
	}
}
