using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Language;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Composes in memory the outputs a compile publishes: <c>Script.dat</c>, the main language <c>.dat</c> and the images
/// to encrypt.
/// </summary>
/// <remarks>
/// It reads files but writes none, and a failing part does not stop the others, so an analysis reports every error
/// a compile finds.
/// </remarks>
internal sealed class OutputComposer
{
	private readonly CompilationRun _run;
	private readonly ScriptDatComposer _scriptDatComposer;
	private readonly ImageEncryptor _images;
	private readonly string _engineDirectory;
	private readonly VirtualFileSystem? _overlay;

	/// <remarks>
	/// The seed is <see cref="TRNGCompilerOptions.DeterministicSeed"/>, and the overlay's <c>@file</c> texts replace
	/// those on disk.
	/// </remarks>
	internal OutputComposer(
		CompilationRun run,
		ExpressionEvaluator evaluator,
		double? deterministicSeed,
		string engineDirectory,
		VirtualFileSystem? overlay
	)
	{
		_run = run;
		_scriptDatComposer = new ScriptDatComposer(run, evaluator, deterministicSeed);
		_images = new ImageEncryptor(run.Logger, run.Diagnostics, run.PathResolver);
		_engineDirectory = engineDirectory;
		_overlay = overlay;
	}

	/// <summary>Composes every output of the script, or returns <see langword="null"/> after any error.</summary>
	/// <remarks>
	/// The script's NG commands are compiled, the languages follow its <c>File=</c> lines, and only the main language's
	/// <c>.dat</c> ships.
	/// </remarks>
	internal CompiledOutputs? Compose(ScriptModel scriptModel, IReadOnlyList<LanguageData> languages, string mainLanguageFile)
	{
		ScriptDatComposition? scriptDat = _scriptDatComposer.Compose(scriptModel, languages[0]);
		bool success = scriptDat is not null && CheckImages(scriptDat);

		var files = new List<(string Path, byte[] Bytes)>();

		if (scriptDat is not null)
			files.Add((Path.Combine(_engineDirectory, CompilerFileNames.ScriptDat), scriptDat.Bytes));

		success &= TryComposeLanguages(languages, scriptModel, mainLanguageFile, files);

		return success && scriptDat is not null ? new CompiledOutputs(files, scriptDat.ImagesToEncrypt) : null;
	}

	/// <summary>Checks the images the script names, without changing them.</summary>
	private bool CheckImages(ScriptDatComposition scriptDat)
	{
		bool success = true;

		foreach (NgImageReference request in scriptDat.ImagesToEncrypt)
			success &= _images.CheckImageToEncrypt(request.ImageId, _engineDirectory, request.Location);

		foreach (NgImageReference image in scriptDat.PlainImages)
			_images.WarnIfOnlyTheEncryptedImageExists(image.ImageId, _engineDirectory, image.Location);

		return success;
	}

	/// <summary>Encodes every language and adds the main language's <c>.dat</c> to the files to publish.</summary>
	/// <remarks>
	/// An error in any language fails the run, but only the main <c>.dat</c> ships, as VB6 copies
	/// no other (<c>Module1.bas:8961-8973</c>).
	/// </remarks>
	private bool TryComposeLanguages(
		IReadOnlyList<LanguageData> languages,
		ScriptModel scriptModel,
		string mainLanguageFile,
		List<(string Path, byte[] Bytes)> files
	)
	{
		_run.Logger.Log("SAVE LANGUAGES.DAT ...");

		var outputFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CompilerFileNames.ScriptDat };
		var encoder = new LanguageDatEncoder(_run.Logger, _run.Diagnostics);

		bool success = true;
		bool mainLanguageComposed = false;

		for (int index = 0; index < languages.Count; index++)
		{
			LanguageData languageData = languages[index];

			// VB6 names the .dat after the File= line (Module1.bas:8887-8888), so the name keeps the script's casing, whatever casing the
			// language file has on disk; an old .dat of another casing keeps its own name when the output replaces it (PD-29).
			string languageFile = Path.GetFileName(scriptModel.Parsed.LanguageFiles[index]);
			string languageDatPath = Path.Combine(_engineDirectory, Path.ChangeExtension(languageFile, ".dat"));
			string languageDatFileName = Path.GetFileName(languageDatPath);

			// The error points at the later of the two File= lines, whose output would replace the earlier one's.
			if (!outputFileNames.Add(languageDatFileName))
			{
				_run.Diagnostics.Error(
					$"Multiple outputs map to the same file name '{languageDatFileName}'; give each [Language] File= entry a name of its own",
					scriptModel.Parsed.LanguageFileLocations[index]
				);

				success = false;
				continue;
			}

			_run.Logger.Log($"Saving: {languageDatFileName}", indent: 1);

			if (!encoder.TryEncode(languageData, extraStringFileLoader: ReadExtraStringFile, bytes: out byte[] languageBytes))
			{
				success = false;
				continue;
			}

			if (PathUtilities.HaveSameFileName(languageFile, mainLanguageFile))
			{
				files.Add((languageDatPath, languageBytes));
				mainLanguageComposed = true;
			}
		}

		if (success && !mainLanguageComposed)
		{
			_run.Diagnostics.Error($"Selected main language was not compiled: {mainLanguageFile}");
			return false;
		}

		return success;
	}

	private string? ReadExtraStringFile(string folder, string fileName)
	{
		// An @file text names a file the way the author typed it, so the name takes the casing of the file on disk (PD-29).
		string path = _run.PathResolver.ResolveScriptPath(folder, fileName);

		if (!VirtualFileSystem.FileExists(path, _overlay))
			return null;

		_run.RecordFileRead(path);

		try
		{
			return TextInputReader.ReadAllText(path, _overlay, _run.Diagnostics);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_run.Diagnostics.Error($"Cannot read referenced file {Path.GetFileName(path)}: {ex.Reason()}", SourceLocation.InFile(path), ex);
			return null;
		}
	}
}
