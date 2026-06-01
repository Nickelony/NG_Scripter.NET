namespace TRNGScriptCompiler;

/// <summary>Ranks how much a log line matters, from a suppressible detail to a fatal error.</summary>
public enum LogSeverity
{
	/// <summary>A detail message, which a compiler with <c>ConciseLogging</c> set suppresses.</summary>
	Verbose,

	/// <summary>A progress message, which concise logging keeps.</summary>
	Info,

	/// <summary>A non-fatal warning that does not prevent compilation.</summary>
	Warning,

	/// <summary>A fatal error that prevents successful compilation.</summary>
	Error,
}
