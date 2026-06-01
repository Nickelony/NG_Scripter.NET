namespace TRNGScriptCompiler.Constants;

/// <summary>Identifies the table a resolved name comes from.</summary>
internal enum ConstantKind
{
	/// <summary>A <c>#DEFINE</c> of the current file.</summary>
	Define,

	/// <summary>A constant of <c>scripter_constants.txt</c>.</summary>
	Constant,

	/// <summary>A constant of a plugin script.</summary>
	PluginConstant,

	/// <summary>A name of the <c>object_types</c> enum in <c>Objects.h</c>.</summary>
	Slot,

	/// <summary>A name of the <c>static_types</c> enum in <c>Objects.h</c>.</summary>
	Static,
}
