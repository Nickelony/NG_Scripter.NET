namespace TRNGScriptCompiler.Pipeline;

/// <summary>The paths of an image <c>IF_CRYPTIC</c> names, resolved once to their on-disk casing.</summary>
/// <param name="PixDirectory">The <c>pix</c> folder of the engine folder.</param>
/// <param name="SourcePath">The path of <c>imageN.bmp</c>.</param>
/// <param name="EncryptedPath">The path of <c>@imageN.bmp</c>.</param>
internal readonly record struct ImagePaths(string PixDirectory, string SourcePath, string EncryptedPath);
