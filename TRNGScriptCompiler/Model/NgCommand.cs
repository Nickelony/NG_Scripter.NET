using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>Holds one compiled NG command: its name and code, its resolved arguments and its encoded words.</summary>
internal sealed record NgCommand
{
	/// <summary>Gets the normalized NG command name.</summary>
	internal required string CommandName { get; init; }

	internal required NgCommandCode CommandCode { get; init; }

	/// <summary>Gets the parsed command arguments in their resolved runtime form.</summary>
	internal required IReadOnlyList<NgArgumentValue> Arguments { get; init; }

	/// <summary>Gets the binary word payload compiled for the command.</summary>
	internal required IReadOnlyList<short> CompiledWords { get; init; }

	/// <summary>Gets the 1-based line that declared the command, with the full path of its file.</summary>
	internal required SourceLocation Location { get; init; }
}
