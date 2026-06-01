using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Model;

/// <summary>
/// A <c>[Level]</c> or <c>[Title]</c> section as the script parser collected it, before its commands compile.
/// </summary>
internal sealed record ParsedSection
{
	/// <summary>Gets the section kind, <c>Level</c> or <c>Title</c>.</summary>
	internal ScriptSectionType SectionType { get; init; }

	/// <summary>Gets the rest of the line after the last <c>Name=</c>, commas included.</summary>
	internal string LevelName { get; init; } = string.Empty;

	/// <summary>Gets the first argument of the last <c>Level=</c>.</summary>
	internal string LevelPath { get; init; } = string.Empty;

	/// <summary>Gets the location of <see cref="LevelPath"/> in its <c>Level=</c> line.</summary>
	internal SourceLocation LevelPathLocation { get; init; }

	/// <summary>Gets the location of the <c>[Level]</c> or <c>[Title]</c> line.</summary>
	internal SourceLocation HeaderLocation { get; init; }

	/// <summary>Gets the classic command lines in source order.</summary>
	internal IReadOnlyList<ScriptCommandLine> ClassicLines { get; init; } = [];

	/// <summary>Gets the NG command lines and <c>#DEFINE @plugin</c> lines in source order.</summary>
	internal IReadOnlyList<NgLine> NgLines { get; init; } = [];
}
