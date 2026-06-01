using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Ng;

/// <summary>An <c>Image=</c> command: the number of its bitmap, <c>pix\imageN.bmp</c>, and its location.</summary>
/// <param name="ImageId">The number in the bitmap's name, the <c>N</c> of <c>pix\imageN.bmp</c>.</param>
/// <param name="Location">The location of the <c>Image=</c> command.</param>
/// <remarks>
/// The publication encrypts an image with <c>IF_CRYPTIC</c> once every output is composed, where VB6 encrypts
/// it as it writes <c>script.dat</c> (Module1.bas:8505-8508).
/// </remarks>
internal sealed record NgImageReference(int ImageId, SourceLocation Location);
