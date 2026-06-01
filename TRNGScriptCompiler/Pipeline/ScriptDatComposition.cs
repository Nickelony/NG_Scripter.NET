using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>A composed <c>Script.dat</c> with the images its <c>Image=</c> commands name.</summary>
/// <param name="Bytes">The whole file, its header scrambled when the script asks for it.</param>
/// <param name="ImagesToEncrypt">The images that <c>IF_CRYPTIC</c> asks publication to encrypt.</param>
/// <param name="PlainImages">The images the engine reads unencrypted.</param>
internal sealed record ScriptDatComposition(
	byte[] Bytes,
	IReadOnlyList<NgImageReference> ImagesToEncrypt,
	IReadOnlyList<NgImageReference> PlainImages
);
