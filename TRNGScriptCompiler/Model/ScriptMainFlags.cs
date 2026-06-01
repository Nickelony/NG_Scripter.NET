namespace TRNGScriptCompiler.Model;

/// <summary>Bit flags written for classic script-wide options.</summary>
/// <remarks>
/// The values match the VB6 option-flag bits in Mod_ScriptDat.bas.
/// </remarks>
[Flags]
internal enum ScriptMainFlags
{
	None = 0,

	/// <summary>Set by <c>FlyCheat= ENABLED</c>.</summary>
	FlyCheat = 1 << 0,

	/// <summary>Set by <c>LoadSave= ENABLED</c>.</summary>
	LoadSave = 1 << 1,

	/// <summary>Set by <c>Title= ENABLED</c>; the <c>[Title]</c> section is stored either way.</summary>
	Title = 1 << 2,

	/// <summary>Set by <c>PlayAnyLevel= ENABLED</c>.</summary>
	PlayAnyLevel = 1 << 3,

	/// <summary>Set by <c>DemoDisc= ENABLED</c>.</summary>
	DemoDisc = 1 << 7,
}
