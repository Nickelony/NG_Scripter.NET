namespace TRNGScriptCompiler.Script;

/// <summary>How the generic classic encoder reads an argument and writes its value.</summary>
internal enum ClassicFieldKind
{
	/// <summary>A number written as one byte after the tag.</summary>
	Byte,

	/// <summary>A number written as a little-endian dword after the tag.</summary>
	DWord,

	/// <summary>A number added to the tag, less the field's minimum, and not written on its own.</summary>
	TagOffset,

	/// <summary>
	/// An <c>ENABLED</c> or <c>DISABLED</c> argument that decides whether the command sets its level flag.
	/// </summary>
	Switch,
}
