using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Ng;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Publishes the outputs a successful analysis composed as one unit, then removes the stale language <c>.dat</c> files
/// that <see cref="StaleLanguageFiles"/> names.
/// </summary>
/// <remarks>
/// A failed move, or an exception of any type during the moves, restores every output already moved, so the engine
/// folder holds either every new output or the previous ones (PD-23).
/// </remarks>
internal sealed class OutputPublisher
{
	private readonly CompilationRun _run;
	private readonly OutputStaging _staging;
	private readonly ImageEncryptor _images;

	/// <summary>Initializes the publisher of one run.</summary>
	/// <param name="run">The run whose outputs the publisher moves into place.</param>
	/// <param name="staging">
	/// Test seam: the staging that faults a move; a staging of the run when <see langword="null"/>.
	/// </param>
	internal OutputPublisher(CompilationRun run, OutputStaging? staging = null)
	{
		_run = run;
		_staging = staging ?? new OutputStaging(run.Logger, run.Diagnostics);
		_images = new ImageEncryptor(run.Logger, run.Diagnostics, PathResolver.Uncached);
	}

	/// <summary>Writes all generated output files for a successful analysis.</summary>
	/// <remarks>
	/// <paramref name="publishedFiles"/> holds the full paths in the order they moved into place, and is empty
	/// on failure.
	/// </remarks>
	internal bool TryPublish(AnalysisResult analysis, string engineDirectoryPath, out IReadOnlyList<string> publishedFiles)
	{
		// The host's log handler runs only after the engine folder is consistent again, so a handler that throws cannot stop a move, a
		// rollback or the cleanup halfway.
		_run.Logger.HoldEntries();

		try
		{
			return TryPublishOutputs(analysis, engineDirectoryPath, out publishedFiles);
		}
		finally
		{
			_run.Logger.ReleaseHeldEntries();
		}
	}

	private bool TryPublishOutputs(AnalysisResult analysis, string engineDirectoryPath, out IReadOnlyList<string> publishedFiles)
	{
		publishedFiles = [];

		if (analysis.ScriptModel is not { } scriptModel || analysis.Outputs is not { } outputs)
		{
			_run.Diagnostics.Error("Compile analysis did not produce the data required to write output files");
			return false;
		}

		// The images encrypt only now, after every output composed without an error. VB6 deletes imageN.bmp before it writes @imageN.bmp, while
		// it writes script.dat (Module1.bas:8161, 8183, 8505-8508), so a later error leaves the image encrypted and the outputs unwritten.
		var files = new List<(string Path, byte[] Bytes)>();
		var encryptedSources = new List<string>();
		var imageIds = new HashSet<int>();

		foreach (NgImageReference request in outputs.ImagesToEncrypt)
		{
			// Two levels can name one image; VB6 finds it encrypted the second time (Module1.bas:8135-8143).
			if (!imageIds.Add(request.ImageId))
				continue;

			ImagePaths imagePaths = _images.GetImagePaths(imageId: request.ImageId, engineDirectoryPath);

			if (ImageEncryptor.OnlyTheEncryptedImageExists(imagePaths))
			{
				_run.Logger.LogVerbose($"image{request.ImageId}.bmp has been already encrypted", indent: 2);
				continue;
			}

			EncryptedImage? image = _images.EncryptImage(imageId: request.ImageId, paths: imagePaths, location: request.Location);

			if (image is null)
				return false;

			files.Add((image.EncryptedPath, image.Bytes));
			encryptedSources.Add(image.SourcePath);
		}

		// Script.dat moves last, so until every other output is in place the engine folder keeps the Script.dat that names the previous ones.
		// Each output replaces a file whose name differs only in case, such as the script.dat VB6 writes, and keeps that file's name, so a
		// case-sensitive file system keeps no stale copy beside it (PD-29).
		files.AddRange(
			outputs.Files.OrderBy(file => IsScriptDat(file.Path)).Select(file => (PathResolver.Uncached.ResolveFullPath(file.Path), file.Bytes))
		);

		string scriptDatPath = PathResolver.Uncached.ResolveFullPath(Path.Combine(engineDirectoryPath, CompilerFileNames.ScriptDat));
		var publishedFileNames = new HashSet<string>(outputs.Files.Select(file => Path.GetFileName(file.Path)), StringComparer.OrdinalIgnoreCase);

		// The old Script.dat is read before the new one replaces it.
		HashSet<string> staleFileNames = new StaleLanguageFiles(_run.Diagnostics).Collect(
			scriptDatPath,
			scriptModel.Parsed.LanguageFiles,
			publishedFileNames
		);

		if (!TryPublishStaged(files))
			return false;

		foreach (string sourcePath in encryptedSources)
			_staging.DeleteAfterPublication(sourcePath);

		// Remove stale language files only after every new output is in place. VB6 deletes every .dat but script.dat before it reads
		// anything (Module1.bas:8829-8836); the port deletes only the language files the old or the new Script.dat names, and never a file the
		// run read, such as an ImportFile= named custom7.dat.
		_run.Logger.Log($"Removing stale language .dat files from the engine folder: {engineDirectoryPath}");
		_staging.DeleteDatFiles(engineDirectoryPath, staleFileNames, _run.WasFileRead);

		_run.Logger.Log("******** COMPLETED ********");
		publishedFiles = [.. files.Select(file => file.Path)];
		return true;
	}

	/// <summary>
	/// Stages every output, flushed to disk, then moves the staged files over the final paths in order.
	/// </summary>
	/// <remarks>
	/// A failure deletes the staged files and restores each moved output from its backup; the backups are deleted
	/// only after the last move. An exception of any type during the moves does the same, then reaches the caller.
	/// </remarks>
	internal bool TryPublishStaged(IReadOnlyList<(string Path, byte[] Bytes)> outputs)
	{
		var staged = new List<(string OutputPath, string TempPath)>();

		foreach ((string Path, byte[] Bytes) output in outputs)
		{
			if (!_staging.TryStageOutput(output.Path, output.Bytes, out string tempPath))
			{
				foreach ((string OutputPath, string TempPath) pending in staged)
					_staging.DeleteStagedOutput(pending.TempPath);

				return false;
			}

			staged.Add((output.Path, tempPath));
		}

		var committed = new List<(string OutputPath, string? BackupPath)>();
		bool moveFailed = false;

		try
		{
			for (int index = 0; index < staged.Count && !moveFailed; index++)
			{
				moveFailed = !_staging.TryCommitOutput(staged[index].OutputPath, staged[index].TempPath, out string? backupPath);

				if (!moveFailed)
					committed.Add((staged[index].OutputPath, backupPath));
			}
		}
		catch (Exception)
		{
			// An exception of any type leaves the engine folder as the failed move does, and then reaches the caller: the rollback hides
			// nothing.
			RollBack(staged, committed);
			throw;
		}

		if (moveFailed)
		{
			RollBack(staged, committed);
			return false;
		}

		foreach ((string OutputPath, string? BackupPath) output in committed)
		{
			if (output.BackupPath is not null)
				_staging.DeleteAfterPublication(output.BackupPath);

			_run.Logger.Log($"Created: {Path.GetFileName(output.OutputPath)}");
		}

		return true;
	}

	/// <summary>
	/// Deletes the staged files not yet moved and moves each committed output back from its backup, last first.
	/// </summary>
	private void RollBack(List<(string OutputPath, string TempPath)> staged, List<(string OutputPath, string? BackupPath)> committed)
	{
		for (int remaining = committed.Count; remaining < staged.Count; remaining++)
			_staging.DeleteStagedOutput(staged[remaining].TempPath);

		for (int moved = committed.Count - 1; moved >= 0; moved--)
			_staging.TryRestoreOutput(committed[moved].OutputPath, committed[moved].BackupPath);
	}

	private static bool IsScriptDat(string path) => Path.GetFileName(path).IgnoreCaseEquals(CompilerFileNames.ScriptDat);
}
