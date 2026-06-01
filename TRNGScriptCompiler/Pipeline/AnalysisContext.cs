using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>Per-run context shared by the analysis stage.</summary>
/// <remarks>
/// <c>Options</c> are fully resolved, and <c>Overlay</c> holds the optional in-memory text of dirty editor buffers.
/// </remarks>
internal sealed record AnalysisContext(
	CompilationRun Run,
	TRNGCompilerOptions Options,
	AnalysisPaths Paths,
	VirtualFileSystem? Overlay,
	CancellationToken Token
);
