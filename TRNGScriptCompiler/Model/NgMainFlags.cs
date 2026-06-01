namespace TRNGScriptCompiler.Model;

/// <summary>Bit flags accumulated from NG option commands.</summary>
/// <remarks>
/// The values mirror the bit masks declared in <c>scripter_constants.txt</c>.
/// </remarks>
[Flags]
internal enum NgMainFlags
{
	None = 0,

	/// <summary>Set by <c>Diagnostic= ENABLED</c> (<c>$0001</c>).</summary>
	Diagnostic = 1 << 0,

	/// <summary>Set by <c>CRS= ENABLED</c> (<c>$0008</c>).</summary>
	Crs = 1 << 3,

	/// <summary>Set by <c>ForceBumpMapping= ENABLED</c> (<c>$0010</c>).</summary>
	ForceBumpMappingEnabled = 1 << 4,

	/// <summary>Set by <c>ForceBumpMapping= DISABLED</c> (<c>$0020</c>).</summary>
	ForceBumpMappingDisabled = 1 << 5,

	/// <summary>Set by <c>ShowLaraInTitle= ENABLED</c> (<c>$0040</c>).</summary>
	ShowLaraInTitle = 1 << 6,

	/// <summary>Set by <c>NewSoundEngine= DISABLED</c> (<c>$0080</c>).</summary>
	NewSoundEngineDisabled = 1 << 7,
}
