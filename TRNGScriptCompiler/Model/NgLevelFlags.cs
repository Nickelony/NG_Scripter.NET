namespace TRNGScriptCompiler.Model;

/// <summary>Bit flags accumulated from NG level commands.</summary>
/// <remarks>
/// The values mirror the bit masks declared in <c>scripter_constants.txt</c>.
/// </remarks>
[Flags]
internal enum NgLevelFlags
{
	None = 0,

	/// <summary>Set by <c>ForceVolumetricFX= ENABLED</c> (<c>$0004</c>).</summary>
	ForceVolumetricFxEnabled = 1 << 2,

	/// <summary>Set by <c>ForceVolumetricFX= DISABLED</c> (<c>$0008</c>).</summary>
	ForceVolumetricFxDisabled = 1 << 3,

	/// <summary>Set by <c>CutScene= ENABLED</c> (<c>$0040</c>).</summary>
	CutScene = 1 << 6,

	/// <summary>Set by <c>PreserveInventory= ENABLED</c> (<c>$0080</c>).</summary>
	PreserveInventory = 1 << 7,
}
