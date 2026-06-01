using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace TRNGScriptCompiler.Script;

/// <summary>
/// The single table of the <c>[Level]</c> and <c>[Title]</c> classic commands; <c>[Options]</c> classics belong
/// to their builder.
/// </summary>
/// <remarks>
/// Parser classification, encoder dispatch, canonical spelling, the emission sort key and the repeat check
/// read this table, so they cannot disagree.
/// </remarks>
internal static class ClassicCommandCatalog
{
	/// <summary>The canonical name of <c>Name=</c>.</summary>
	internal const string NameCommand = "Name=";

	/// <summary>The canonical name of <c>Level=</c>.</summary>
	internal const string LevelCommand = "Level=";

	/// <summary>The canonical name of <c>FMV=</c>.</summary>
	internal const string FmvCommand = "FMV=";

	/// <summary>The canonical name of <c>Cut=</c>.</summary>
	internal const string CutCommand = "Cut=";

	/// <summary>The canonical name of <c>ResidentCut=</c>.</summary>
	internal const string ResidentCutCommand = "ResidentCut=";

	/// <summary>The canonical name of <c>Legend=</c>.</summary>
	internal const string LegendCommand = "Legend=";

	/// <summary>The canonical name of <c>LensFlare=</c>.</summary>
	internal const string LensFlareCommand = "LensFlare=";

	/// <summary>The canonical name of <c>AnimatingMIP=</c>.</summary>
	internal const string AnimatingMipCommand = "AnimatingMIP=";

	/// <summary>The canonical name of <c>LoadCamera=</c>.</summary>
	internal const string LoadCameraCommand = "LoadCamera=";

	/// <summary>All classic command entries keyed by canonical name (case-insensitive).</summary>
	private static readonly FrozenDictionary<string, ClassicCommandInfo> s_commands = BuildCommands()
		.ToFrozenDictionary(info => info.Name, StringComparer.OrdinalIgnoreCase);

	/// <summary>Gets the canonical name of every command, which the coverage test compares with its rows.</summary>
	internal static IReadOnlyCollection<string> Names => s_commands.Keys;

	internal static bool Contains(string command) => s_commands.ContainsKey(command);

	/// <summary>Gets the entry of a command the section builder admitted, without regard to case.</summary>
	/// <exception cref="KeyNotFoundException"><paramref name="command"/> is not in the catalog.</exception>
	internal static ClassicCommandInfo Get(string command) => s_commands[command];

	/// <summary>
	/// Looks up a classic command's catalog entry without regard to case, as VB6 matches commands
	/// (<c>Option Compare Text</c>).
	/// </summary>
	internal static bool TryGet(string command, [NotNullWhen(true)] out ClassicCommandInfo? info) => s_commands.TryGetValue(command, out info);

	/// <summary>
	/// Builds VB6's <c>VetTagLevels</c> (<c>Module1.bas:3934-3940</c>) with the tags VB6 writes
	/// (<c>Module1.bas:5763-6297</c>).
	/// </summary>
	private static ClassicCommandInfo[] BuildCommands()
	{
		return
		[
			// The section header writes the name, and the CD slot that Level= records.
			new(NameCommand, SortOrder: 0) { Handler = ClassicHandler.SectionHeader },
			new(LevelCommand, SortOrder: 31) { Handler = ClassicHandler.Level },
			new(FmvCommand, SortOrder: 8) { Tag = 0x80, Handler = ClassicHandler.Fmv },

			new(CutCommand, SortOrder: 19)
			{
				Tag = 0x84,
				Layout = [new(Kind: ClassicFieldKind.Byte, Minimum: 0, Maximum: 31, Holder: "a cutscene number")],
			},

			new(ResidentCutCommand, SortOrder: 18)
			{
				Tag = 0x85,

				Layout =
				[
					new(Kind: ClassicFieldKind.TagOffset, Minimum: 1, Maximum: 4, Holder: "a cutscene number"),
					new(ClassicFieldKind.Byte, Minimum: 0, Holder: "a room number"),
				],
			},

			Layer(name: "Layer1=", sortOrder: 10, tag: 0x89, flag: ScriptLevelFlags.Layer1),
			Layer(name: "Layer2=", sortOrder: 11, tag: 0x8A, flag: ScriptLevelFlags.Layer2),
			new("UVRotate=", SortOrder: 9) { Tag = 0x8B, Layout = [new(ClassicFieldKind.Byte)] },
			new(LegendCommand, SortOrder: 16) { Tag = 0x8C, Handler = ClassicHandler.Legend },

			new(LensFlareCommand, SortOrder: 12)
			{
				Tag = 0x8D,
				LevelFlag = ScriptLevelFlags.LensFlare,
				Handler = ClassicHandler.LensFlare,
			},

			new("Mirror=", SortOrder: 14)
			{
				Tag = 0x8E,
				LevelFlag = ScriptLevelFlags.Mirror,

				Layout =
				[
					new(ClassicFieldKind.Byte, Minimum: 0, Holder: "a room number"),
					new(ClassicFieldKind.DWord, Minimum: 0, Holder: "a mirror origin"),
				],
			},

			new("Fog=", SortOrder: 13) { Tag = 0x8F, Layout = [Color(), Color(), Color()] },
			new(AnimatingMipCommand, SortOrder: 15) { Tag = 0x90, Handler = ClassicHandler.AnimatingMip },

			new(LoadCameraCommand, SortOrder: 28)
			{
				Tag = 0x91,

				Layout =
				[
					new(ClassicFieldKind.DWord),
					new(ClassicFieldKind.DWord),
					new(ClassicFieldKind.DWord),
					new(ClassicFieldKind.DWord),
					new(ClassicFieldKind.DWord),
					new(ClassicFieldKind.DWord),
					new(ClassicFieldKind.Byte, Minimum: 0, Holder: "a room number"),
				],
			},

			new("ResetHUB=", SortOrder: 29)
			{
				Tag = 0x92,
				LevelFlag = ScriptLevelFlags.ResetHub,
				Layout = [new(ClassicFieldKind.Byte, Minimum: 0, Holder: "a level number")],
			},

			Switch("YoungLara=", sortOrder: 17, ScriptLevelFlags.YoungLara),
			Switch("Horizon=", sortOrder: 20, ScriptLevelFlags.Horizon),
			Switch("StarField=", sortOrder: 21, ScriptLevelFlags.StarField),
			Switch("Lightning=", sortOrder: 22, ScriptLevelFlags.Lightning),
			Switch("Train=", sortOrder: 23, ScriptLevelFlags.Train),
			Switch("Pulse=", sortOrder: 24, ScriptLevelFlags.Pulse),
			Switch("ColAddHorizon=", sortOrder: 25, ScriptLevelFlags.ColAddHorizon),
			Switch("Timer=", sortOrder: 26, ScriptLevelFlags.Timer),
			Switch("RemoveAmulet=", sortOrder: 27, ScriptLevelFlags.RemoveAmulet),
			Switch("NoLevel=", sortOrder: 30, ScriptLevelFlags.NoLevel),
			Inventory(name: "Key=", sortOrder: 1, kind: ClassicInventoryKind.Item, tag: 0x93, count: 12),
			Inventory(name: "Puzzle=", sortOrder: 3, kind: ClassicInventoryKind.Item, tag: 0x9F, count: 12),
			Inventory(name: "Pickup=", sortOrder: 5, kind: ClassicInventoryKind.Item, tag: 0xAB, count: 4),
			Inventory(name: "Examine=", sortOrder: 7, kind: ClassicInventoryKind.Item, tag: 0xAF, count: 3),
			Inventory(name: "KeyCombo=", sortOrder: 2, kind: ClassicInventoryKind.Combo, tag: 0xB2, count: 8),
			Inventory(name: "PuzzleCombo=", sortOrder: 4, kind: ClassicInventoryKind.Combo, tag: 0xC2, count: 8),
			Inventory(name: "PickupCombo=", sortOrder: 6, kind: ClassicInventoryKind.Combo, tag: 0xD2, count: 4),
		];
	}

	/// <summary>
	/// Creates a layer whose three colors are unsigned and whose speed is signed, so a negative speed reverses
	/// the movement.
	/// </summary>
	private static ClassicCommandInfo Layer(string name, int sortOrder, byte tag, ScriptLevelFlags flag)
	{
		return new(name, sortOrder)
		{
			Tag = tag,
			LevelFlag = flag,
			Layout = [Color(), Color(), Color(), new(ClassicFieldKind.Byte)],
		};
	}

	private static ClassicField Color() => new(ClassicFieldKind.Byte, Minimum: 0, Holder: "a color");

	private static ClassicCommandInfo Switch(string name, int sortOrder, ScriptLevelFlags flag) =>
		new(name, sortOrder) { LevelFlag = flag, Layout = [new(ClassicFieldKind.Switch)] };

	private static ClassicCommandInfo Inventory(string name, int sortOrder, ClassicInventoryKind kind, byte tag, int count)
	{
		return new(name, sortOrder)
		{
			Tag = tag,
			Inventory = kind,
			InventoryCount = count,
			Handler = kind == ClassicInventoryKind.Combo ? ClassicHandler.InventoryCombo : ClassicHandler.InventoryItem,
		};
	}
}
