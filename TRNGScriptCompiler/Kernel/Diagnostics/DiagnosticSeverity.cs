namespace TRNGScriptCompiler;

/// <summary>Ranks how much a diagnostic matters, from a suggestion to an error that fails the compilation.</summary>
public enum DiagnosticSeverity
{
	/// <summary>A suggestion about input the compiler accepts, which is no problem in itself.</summary>
	Hint,

	/// <summary>A non-fatal warning that does not prevent compilation.</summary>
	Warning,

	/// <summary>A fatal error that fails the compilation.</summary>
	Error,
}
