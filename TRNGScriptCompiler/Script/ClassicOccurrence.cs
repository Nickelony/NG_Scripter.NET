namespace TRNGScriptCompiler.Script;

/// <summary>One compiled line with the setting identity and the value its repeat check compares.</summary>
/// <remarks>
/// The command is canonical, the value is resolved, and a <see langword="null"/> <see cref="Key"/> marks a line
/// that may repeat freely.
/// </remarks>
internal readonly record struct ClassicOccurrence(ParsedClassicLine Line, string Command, string? Key, string Value);
