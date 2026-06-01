using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Pipeline;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class OutputStagingTests
{
	// The faults are the seams of OutputStaging: a move or a delete that throws, where a real file system would fail only on a lock.
	private static readonly Action<string, string, bool> s_realMove = File.Move;

	private static void MoveFaultingOn(Func<string, string, bool> faults, string source, string destination, bool overwrite)
	{
		if (faults(source, destination))
			throw new IOException("the disk refused the move");

		s_realMove(source, destination, overwrite);
	}

	private static string StagedTemp(TestWorkspace workspace, string outputName, string tempName)
	{
		string tempPath = workspace.PathOf(tempName);

		workspace.WithTextFile(outputName, "old");
		workspace.WithTextFile(tempName, "new");

		return tempPath;
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CommitWhoseTempFileCannotMove_RestoresTheOutputFromItsBackup()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string outputPath = workspace.PathOf("Out.dat");
		string tempPath = StagedTemp(workspace, "Out.dat", "Out.dat.new.tmp");

		var staging = new OutputStaging(
			run.Logger,
			run.Diagnostics,
			moveFile: (source, destination, overwrite) => MoveFaultingOn(faults: (moved, _) => moved == tempPath, source, destination, overwrite)
		);

		bool committed = staging.TryCommitOutput(outputPath, tempPath, out string? backupPath);

		Assert.IsFalse(committed);
		Assert.IsNull(backupPath);

		DiagnosticAssert.SingleError(run.Diagnostics.Recorded, line: null, "Failed writing Out.dat: the disk refused the move");
		Assert.AreEqual("old", File.ReadAllText(outputPath), "The previous output is back in place.");

		string[] remaining = Directory.GetFiles(workspace.RootPath);

		Assert.AreSequenceEqual(new[] { outputPath }, remaining, "The staged file and the backup are gone.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void CommitWhoseRestoreFailsToo_NamesTheBackupToRename()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string outputPath = workspace.PathOf("Out.dat");
		string tempPath = StagedTemp(workspace, "Out.dat", "Out.dat.new.tmp");

		var staging = new OutputStaging(
			run.Logger,
			run.Diagnostics,

			moveFile: (source, destination, overwrite) =>
				MoveFaultingOn(
					faults: (origin, to) => origin == tempPath || (to == outputPath && origin != outputPath),
					source,
					destination,
					overwrite
				)
		);

		Assert.IsFalse(staging.TryCommitOutput(outputPath, tempPath, out _));

		string backupPath = Directory.GetFiles(workspace.RootPath, "Out.dat.*.bak").Single();

		DiagnosticAssert.Errors(
			run.Diagnostics.Recorded,
			(null, "Failed writing Out.dat: the disk refused the move"),

			(
				null,
				$"Could not restore Out.dat after the failed publication: the disk refused the move; rename {Path.GetFileName(backupPath)} to Out.dat"
			)
		);

		Assert.AreEqual("old", File.ReadAllText(backupPath), "The backup keeps the previous output for the user to rename.");
		Assert.IsFalse(File.Exists(outputPath));
		Assert.IsFalse(File.Exists(tempPath), "The staged file is deleted.");
	}

	[TestMethod]
	[TestCategory("IO")]
	public void RestoreOfANewOutputThatCannotBeDeleted_TellsTheUserToDeleteIt()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string outputPath = workspace.PathOf("Out.dat");
		workspace.WithTextFile("Out.dat", "new");

		var staging = new OutputStaging(run.Logger, run.Diagnostics, deleteFile: _ => throw new IOException("the file is in use"));

		Assert.IsFalse(staging.TryRestoreOutput(outputPath, backupPath: null));

		DiagnosticAssert.SingleError(
			run.Diagnostics.Recorded,
			line: null,
			"Could not restore Out.dat after the failed publication: the file is in use; delete Out.dat"
		);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void BackupThatCannotBeDeletedAfterPublishing_WarnsToDeleteIt()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string backupPath = workspace.PathOf("Out.dat.1234abcd.bak");
		workspace.WithTextFile("Out.dat.1234abcd.bak", "old");

		var staging = new OutputStaging(run.Logger, run.Diagnostics, deleteFile: _ => throw new IOException("the file is in use"));
		staging.DeleteAfterPublication(backupPath);

		DiagnosticAssert.HasWarning(
			run.Diagnostics.Recorded,
			line: null,
			"Could not delete Out.dat.1234abcd.bak after publishing the outputs: the file is in use; delete it"
		);

		Assert.IsEmpty(run.Diagnostics.Recorded.Where(diagnostic => diagnostic.IsError));
	}

	[TestMethod]
	[TestCategory("IO")]
	public void FailureWhoseMessageEndsInAPeriod_ReportsItsReasonWithoutThePeriod()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string backupPath = workspace.PathOf("Out.dat.1234abcd.bak");
		workspace.WithTextFile("Out.dat.1234abcd.bak", "old");

		string expected = "Could not delete Out.dat.1234abcd.bak after publishing the outputs: Access to the path is denied; delete it";
		var denied = new UnauthorizedAccessException("Access to the path is denied.");

		var staging = new OutputStaging(run.Logger, run.Diagnostics, deleteFile: _ => throw denied);
		staging.DeleteAfterPublication(backupPath);

		DiagnosticAssert.HasWarning(run.Diagnostics.Recorded, line: null, expected);
	}

	[TestMethod]
	[TestCategory("IO")]
	public void StagedFileThatCannotBeDeleted_WarnsAndKeepsGoing()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string tempPath = workspace.PathOf("Out.dat.1234abcd.tmp");
		workspace.WithTextFile("Out.dat.1234abcd.tmp", "new");

		var staging = new OutputStaging(run.Logger, run.Diagnostics, deleteFile: _ => throw new IOException("the file is in use"));
		staging.DeleteStagedOutput(tempPath);

		DiagnosticAssert.HasWarning(run.Diagnostics.Recorded, line: null, "Could not delete temporary file Out.dat.1234abcd.tmp: the file is in use");
		Assert.IsEmpty(run.Diagnostics.Recorded.Where(diagnostic => diagnostic.IsError));
	}
}
