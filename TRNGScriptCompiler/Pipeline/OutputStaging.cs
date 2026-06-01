using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Performs the file moves of one publication, from staging each output beside its final path to deleting the files
/// it replaces.
/// </summary>
/// <remarks>
/// Each failure is reported to the run's diagnostics; <see cref="OutputPublisher"/> decides the order and the rollback.
/// A test hands the constructor its own move and delete to fault one; the publisher passes none.
/// </remarks>
internal sealed class OutputStaging
{
	private readonly CompilerLog _logger;
	private readonly DiagnosticSink _diagnostics;
	private readonly Action<string, string, bool> _moveFile;
	private readonly Action<string> _deleteFile;

	/// <summary>
	/// Tags the staged and backup files of this instance's one publication with a name no other run and no user
	/// file holds.
	/// </summary>
	private readonly string _stagingId = Guid.NewGuid().ToString("N")[..8];

	/// <summary>Initializes the staging of one publication.</summary>
	/// <param name="logger">The log of the run.</param>
	/// <param name="diagnostics">The sink that takes each failure.</param>
	/// <param name="moveFile">
	/// Test seam: moves a file to a path, overwriting what is there when the flag is set;
	/// <c>File.Move</c> when <see langword="null"/>.
	/// </param>
	/// <param name="deleteFile">Test seam: deletes a file; <c>File.Delete</c> when <see langword="null"/>.</param>
	internal OutputStaging(
		CompilerLog logger,
		DiagnosticSink diagnostics,
		Action<string, string, bool>? moveFile = null,
		Action<string>? deleteFile = null
	)
	{
		_logger = logger;
		_diagnostics = diagnostics;
		_moveFile = moveFile ?? File.Move;
		_deleteFile = deleteFile ?? File.Delete;
	}

	/// <summary>
	/// Deletes the named <c>.dat</c> files <paramref name="isProtected"/> does not keep, matching names without regard
	/// to case.
	/// </summary>
	/// <remarks>
	/// A name with a folder never matches, and it runs only after every new output is published, so a failed compile
	/// keeps the previous outputs.
	/// </remarks>
	internal void DeleteDatFiles(string directory, IReadOnlyCollection<string> fileNames, Func<string, bool> isProtected)
	{
		if (!Directory.Exists(directory) || fileNames.Count == 0)
			return;

		var names = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
		string[] files;

		try
		{
			files = [.. PathUtilities.EnumerateFilesInsensitive(directory, "*.dat")];
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Warning($"Could not list {directory}, so its stale .dat files stay: {ex.Reason()}", exception: ex);
			return;
		}

		foreach (string file in files)
		{
			if (!names.Contains(Path.GetFileName(file)) || isProtected(file))
				continue;

			try
			{
				_deleteFile(file);
				_logger.LogVerbose($"Removed: {Path.GetFileName(file)}", indent: 1);
			}
			catch (Exception ex) when (ex.IsFileAccessFault())
			{
				_diagnostics.Warning($"Could not delete {Path.GetFileName(file)}: {ex.Reason()}", exception: ex);
			}
		}
	}

	/// <summary>
	/// Writes an output payload to a staged temp file beside it, flushed to disk, without touching the final
	/// output file.
	/// </summary>
	/// <remarks>
	/// The staged path, returned in <paramref name="tempPath"/>, is <c>&lt;output&gt;.&lt;id&gt;.tmp</c>.
	/// </remarks>
	internal bool TryStageOutput(string outputPath, byte[] data, out string tempPath)
	{
		tempPath = $"{outputPath}.{_stagingId}.tmp";

		try
		{
			using var stream = new FileStream(path: tempPath, mode: FileMode.CreateNew, access: FileAccess.Write, share: FileShare.None);
			stream.Write(data);
			stream.Flush(flushToDisk: true);

			return true;
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Error($"Failed staging {Path.GetFileName(outputPath)}: {ex.Reason()}", exception: ex);
			DeleteStagedOutput(tempPath);
			return false;
		}
	}

	/// <summary>Moves a staged temp file over its final output path, keeping the previous output as a backup.</summary>
	/// <remarks>
	/// A failed move deletes the staged file and keeps the old output, as does an exception from moving the new file,
	/// which then reaches the caller; one from setting the old output aside leaves the staged file to the caller.
	/// With no old output, <paramref name="backupPath"/> is <see langword="null"/>. PD-23 gives what a process killed
	/// between the moves leaves behind, and how to restore it.
	/// </remarks>
	internal bool TryCommitOutput(string outputPath, string tempPath, out string? backupPath)
	{
		backupPath = null;
		string? backup = File.Exists(outputPath) ? $"{outputPath}.{_stagingId}.bak" : null;

		if (backup is not null)
		{
			try
			{
				_moveFile(outputPath, backup, false);
			}
			catch (Exception ex) when (ex.IsFileAccessFault())
			{
				_diagnostics.Error(
					$"Failed setting the old {Path.GetFileName(outputPath)} aside, so it is not replaced: {ex.Reason()}",
					exception: ex
				);

				DeleteStagedOutput(tempPath);
				return false;
			}
		}

		try
		{
			_moveFile(tempPath, outputPath, false);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Error($"Failed writing {Path.GetFileName(outputPath)}: {ex.Reason()}", exception: ex);
			DeleteStagedOutput(tempPath);

			if (backup is not null)
				TryRestoreOutput(outputPath, backup);

			return false;
		}
		catch (Exception)
		{
			DeleteStagedOutput(tempPath);

			if (backup is not null)
				TryRestoreOutput(outputPath, backup);

			throw;
		}

		backupPath = backup;
		return true;
	}

	/// <summary>
	/// Restores the output a commit replaced: moves the backup
	/// <see cref="TryCommitOutput"/> returned back, or deletes the new file when that backup is <see langword="null"/>.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when the old state is back; otherwise, <see langword="false"/>, after an error that names
	/// the fix.
	/// </returns>
	internal bool TryRestoreOutput(string outputPath, string? backupPath)
	{
		try
		{
			if (backupPath is null)
				_deleteFile(outputPath);
			else
				_moveFile(backupPath, outputPath, true);

			return true;
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			string fix = backupPath is null
				? $"delete {Path.GetFileName(outputPath)}"
				: $"rename {Path.GetFileName(backupPath)} to {Path.GetFileName(outputPath)}";

			_diagnostics.Error($"Could not restore {Path.GetFileName(outputPath)} after the failed publication: {ex.Reason()}; {fix}", exception: ex);
			return false;
		}
	}

	/// <summary>
	/// Deletes a file a successful publication leaves behind, such as a backup or an encrypted image's source,
	/// warning on failure.
	/// </summary>
	/// <remarks>
	/// A read-only file loses that attribute first, because the new output is a new file that is not read-only.
	/// </remarks>
	internal void DeleteAfterPublication(string path)
	{
		try
		{
			ClearReadOnly(path);
			_deleteFile(path);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Warning($"Could not delete {Path.GetFileName(path)} after publishing the outputs: {ex.Reason()}; delete it", exception: ex);
		}
	}

	private static void ClearReadOnly(string path)
	{
		if (!File.Exists(path))
			return;

		FileAttributes attributes = File.GetAttributes(path);

		if ((attributes & FileAttributes.ReadOnly) != 0)
			File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
	}

	/// <summary>Deletes a staged temp file after a failed or abandoned publication, warning on failure.</summary>
	internal void DeleteStagedOutput(string tempPath)
	{
		try
		{
			if (File.Exists(tempPath))
				_deleteFile(tempPath);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			_diagnostics.Warning($"Could not delete temporary file {Path.GetFileName(tempPath)}: {ex.Reason()}", exception: ex);
		}
	}
}
