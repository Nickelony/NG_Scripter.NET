namespace TRNGScriptCompiler.Script;

/// <summary>Words the diagnostics for a command line that no section builder or encoder accepts.</summary>
internal static class ScriptCommandMessages
{
	/// <summary>Words the error for a command that its section does not know.</summary>
	/// <remarks>
	/// The section is its header, such as <c>[Level]</c>, and the command is as the author wrote it.
	/// </remarks>
	internal static string UnknownCommand(string sectionHeader, string command) =>
		$"Unknown command for {sectionHeader} section: {command}; correct its name or remove the line";
}
