using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>The outputs an analysis composed, which a compile publishes without composing them again.</summary>
/// <param name="Files">
/// The files to publish, with their full paths: <c>Script.dat</c> first, then the main language <c>.dat</c>.
/// </param>
/// <param name="ImagesToEncrypt">The images that <c>IF_CRYPTIC</c> asks publication to encrypt.</param>
internal sealed record CompiledOutputs(IReadOnlyList<(string Path, byte[] Bytes)> Files, IReadOnlyList<NgImageReference> ImagesToEncrypt);
