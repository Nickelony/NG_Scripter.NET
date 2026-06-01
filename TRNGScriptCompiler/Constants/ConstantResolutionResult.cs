namespace TRNGScriptCompiler.Constants;

/// <summary>
/// Holds the outcome of resolving a named constant during expression evaluation: its value and kind, or the error.
/// </summary>
/// <remarks>
/// <see cref="PluginId"/> is the defining plugin's <c>Plugin=</c> ID, or 0; <see cref="Error"/> names the fix
/// when a known name does not resolve.
/// </remarks>
internal readonly record struct ConstantResolutionResult(bool Success, int PluginId, int Value, ConstantKind Kind, string? Error = null)
{
	/// <summary>Gets a value indicating whether one of the tables holds the name, whether or not it resolves.</summary>
	internal bool IsKnown => Success || Error is not null;
}
