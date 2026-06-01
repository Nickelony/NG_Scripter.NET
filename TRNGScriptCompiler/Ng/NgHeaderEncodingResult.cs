namespace TRNGScriptCompiler.Ng;

/// <summary>
/// Holds the encoded NG header bytes and the images that level sections name, to encrypt or to leave plain.
/// </summary>
internal sealed class NgHeaderEncodingResult
{
	/// <summary>Gets the encoded NG header bytes that should be appended to <c>Script.dat</c>.</summary>
	internal byte[] HeaderBytes { get; init; } = [];

	/// <summary>Gets the deferred image encrypt operations discovered while encoding the NG header.</summary>
	internal IReadOnlyList<NgImageReference> ImageEncryptRequests { get; init; } = [];

	/// <summary>Gets the images that level sections name without encrypting them.</summary>
	internal IReadOnlyList<NgImageReference> PlainImages { get; init; } = [];
}
