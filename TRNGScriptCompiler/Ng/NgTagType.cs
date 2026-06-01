namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Identifies chunk tags written into the NG header payload. Each tag is the first word of the chunk it introduces.
/// </summary>
internal enum NgTagType
{
	/// <summary>Tag beginning the <c>[Options]</c> chunk of the NG payload.</summary>
	ScriptOptions = 0x800B,

	/// <summary>Tag beginning each level chunk of the NG payload.</summary>
	ScriptLevel = 0x800C,

	/// <summary>
	/// Tag beginning the security block, the randomized preamble that carries the level count and the option flags.
	/// </summary>
	/// <remarks>
	/// VB6 names it <c>NGTAG_CONTROLLO_OPTIONS</c> (<c>Module1.bas:8034</c>).
	/// </remarks>
	Security = 0x8016,

	/// <summary>Tag beginning an import-file entry chunk.</summary>
	ImportFile = 0x801F,

	/// <summary>Tag beginning the level-names chunk that an encrypted script carries.</summary>
	/// <remarks>
	/// VB6 adds the chunk only when the script is encrypted (<c>Module1.bas:8654-8655</c>).
	/// </remarks>
	LevelNames = 0x8033,
}
