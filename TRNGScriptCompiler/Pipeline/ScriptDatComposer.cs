using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Script;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Composes the complete <c>Script.dat</c> byte image: classic payload, NG header, and the optional VB6
/// header encryption.
/// </summary>
/// <remarks>
/// It lives in the pipeline so the Script and NG formats stay independent, and it touches no file:
/// publication encrypts the <c>IF_CRYPTIC</c> images.
/// </remarks>
internal sealed class ScriptDatComposer
{
	private readonly CompilationRun _run;
	private readonly ExpressionEvaluator _evaluator;
	private readonly NgHeaderEncoder _headerEncoder;

	/// <remarks>
	/// The seed is <see cref="TRNGCompilerOptions.DeterministicSeed"/>.
	/// </remarks>
	internal ScriptDatComposer(CompilationRun run, ExpressionEvaluator evaluator, double? deterministicSeed)
	{
		_run = run;
		_evaluator = evaluator;
		_headerEncoder = new NgHeaderEncoder(logger: run.Logger, diagnostics: run.Diagnostics, deterministicSeed, timerSeconds: run.TimerSeconds);
	}

	/// <summary>
	/// Composes the <c>Script.dat</c> image, resolving classic string references
	/// to <paramref name="firstLanguage"/> indexes, or returns <see langword="null"/> after an error.
	/// </summary>
	/// <remarks>
	/// The NG header encodes even when a classic section fails, so one composition reports the errors of both.
	/// </remarks>
	internal ScriptDatComposition? Compose(ScriptModel scriptModel, LanguageData firstLanguage)
	{
		IReadOnlyList<byte[]>? sectionBytes = new ClassicSectionCompiler(_run, _evaluator, firstLanguage).Compile(scriptModel.Parsed.Sections);

		NgHeaderEncodingResult? header = _headerEncoder.Encode(scriptModel);

		if (header is null || sectionBytes is null)
			return null;

		if (!new ScriptDatEncoder(_run.Logger, _run.Diagnostics).TryEncode(scriptModel, sectionBytes, out byte[] classicBytes))
			return null;

		byte[] headerBytes = header.HeaderBytes;
		byte[] complete = new byte[classicBytes.Length + headerBytes.Length];

		classicBytes.CopyTo(complete, 0);
		headerBytes.CopyTo(complete, classicBytes.Length);

		// VB6 encrypted the first 64 bytes of the finished file; apply the same transform to the
		// composed in-memory image instead of mutating the published file.
		if (scriptModel.NgModel.EnableScriptEncryption && !LegacyCipher.TryScrambleScriptDatHeader(complete))
		{
			_run.Diagnostics.Error("Script.dat is too small to encrypt");
			return null;
		}

		return new ScriptDatComposition(complete, header.ImageEncryptRequests, header.PlainImages);
	}
}
