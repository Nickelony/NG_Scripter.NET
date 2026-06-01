namespace TRNGScriptCompiler.Script;

/// <summary>
/// Bit flags written for an individual classic level or title section. The values match the VB6 <c>slf_</c>
/// constants (Mod_ScriptDat.bas:11-27).
/// </summary>
[Flags]
internal enum ScriptLevelFlags
{
	None = 0,

	/// <summary>Set by <c>YoungLara= ENABLED</c>.</summary>
	YoungLara = 1 << 0,

	/// <summary>
	/// Reserved: the VB6 source marks this bit "non verra' mai usato" (never used) and the port never sets it.
	/// </summary>
	Weather = 1 << 1,

	/// <summary>Set by <c>Horizon= ENABLED</c>.</summary>
	Horizon = 1 << 2,

	/// <summary>Set when a <c>Layer1=</c> command is present.</summary>
	Layer1 = 1 << 3,

	/// <summary>Set when a <c>Layer2=</c> command is present.</summary>
	Layer2 = 1 << 4,

	/// <summary>Set by <c>StarField= ENABLED</c>.</summary>
	StarField = 1 << 5,

	/// <summary>Set by <c>Lightning= ENABLED</c>.</summary>
	Lightning = 1 << 6,

	/// <summary>Set by <c>Train= ENABLED</c>.</summary>
	Train = 1 << 7,

	/// <summary>Set by <c>Pulse= ENABLED</c>.</summary>
	Pulse = 1 << 8,

	/// <summary>Set by <c>ColAddHorizon= ENABLED</c>.</summary>
	ColAddHorizon = 1 << 9,

	/// <summary>Set when a <c>ResetHUB=</c> command is present.</summary>
	ResetHub = 1 << 10,

	/// <summary>Set when a <c>LensFlare=</c> command is present.</summary>
	LensFlare = 1 << 11,

	/// <summary>Set by <c>Timer= ENABLED</c>.</summary>
	Timer = 1 << 12,

	/// <summary>Set when a <c>Mirror=</c> command is present.</summary>
	Mirror = 1 << 13,

	/// <summary>Set by <c>RemoveAmulet= ENABLED</c>.</summary>
	RemoveAmulet = 1 << 14,

	/// <summary>Set by <c>NoLevel= ENABLED</c>.</summary>
	NoLevel = 1 << 15,
}
