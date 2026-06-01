namespace TRNGScriptCompiler.Model;

/// <summary>
/// Holds the classic script-wide options written to <c>Script.dat</c>: the main flags, the input timeout
/// and the security value.
/// </summary>
internal sealed class ScriptOptions
{
	internal ScriptMainFlags Flags { get; init; }

	/// <summary>Gets the value emitted for the classic <c>InputTimeOut=</c> option.</summary>
	internal int InputTimeOut { get; init; }

	/// <summary>Gets the value emitted for the classic <c>Security=</c> option.</summary>
	internal int Security { get; init; }
}
