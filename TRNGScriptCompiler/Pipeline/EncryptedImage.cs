namespace TRNGScriptCompiler.Pipeline;

/// <summary>An image <c>IF_CRYPTIC</c> names, encrypted in memory and waiting to be published.</summary>
/// <param name="SourcePath">
/// The path of <c>imageN.bmp</c>, which the publication deletes once every output is in place.
/// </param>
/// <param name="EncryptedPath">The path of <c>@imageN.bmp</c>, which receives <paramref name="Bytes"/>.</param>
/// <param name="Bytes">The encrypted bitmap, as many bytes as the source.</param>
internal sealed record EncryptedImage(string SourcePath, string EncryptedPath, byte[] Bytes);
