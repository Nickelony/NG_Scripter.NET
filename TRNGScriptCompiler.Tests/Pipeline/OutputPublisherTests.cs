using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Pipeline;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Pipeline;

[TestClass]
public sealed class OutputPublisherTests
{
	[TestMethod]
	[TestCategory("IO")]
	public void MoveThatThrowsANonIOException_RestoresTheMovedOutputs_AndRethrows()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string firstPath = workspace.PathOf("First.dat");
		string secondPath = workspace.PathOf("Second.dat");

		workspace.WithTextFile("First.dat", "old first");
		workspace.WithTextFile("Second.dat", "old second");

		// The second output's staged file fails to move, after the first output moved into place.
		void FaultingMove(string source, string destination, bool overwrite)
		{
			if (destination == secondPath && source.EndsWith(".tmp", StringComparison.Ordinal))
				throw new InvalidOperationException("the move broke");

			File.Move(source, destination, overwrite);
		}

		var staging = new OutputStaging(run.Logger, run.Diagnostics, moveFile: FaultingMove);
		var publisher = new OutputPublisher(run, staging);

		(string Path, byte[] Bytes)[] outputs = [(firstPath, [1]), (secondPath, [2])];

		Assert.ThrowsExactly<InvalidOperationException>(() => publisher.TryPublishStaged(outputs));

		Assert.AreEqual("old first", File.ReadAllText(firstPath), "The output that moved is restored.");
		Assert.AreEqual("old second", File.ReadAllText(secondPath), "The output whose move threw keeps its old bytes.");
		Assert.AreSequenceEqual(new[] { firstPath, secondPath }, Directory.GetFiles(workspace.RootPath).Order().ToArray());
	}

	[TestMethod]
	[TestCategory("IO")]
	public void RollbackThatThrowsANonIOException_AfterAFailedMove_RunsOnce()
	{
		using var workspace = TestWorkspace.CreateEmpty();
		var run = new CompilationRun(conciseLogging: true);

		string firstPath = workspace.PathOf("First.dat");
		string secondPath = workspace.PathOf("Second.dat");

		workspace.WithTextFile("First.dat", "old first");
		workspace.WithTextFile("Second.dat", "old second");

		int restoresOfFirst = 0;

		// The second output's staged file fails to move with an I/O fault, and then the restore of the first output breaks.
		void FaultingMove(string source, string destination, bool overwrite)
		{
			if (destination == secondPath && source.EndsWith(".tmp", StringComparison.Ordinal))
				throw new IOException("the move failed");

			if (destination == firstPath && source.EndsWith(".bak", StringComparison.Ordinal))
			{
				restoresOfFirst++;
				throw new InvalidOperationException("the restore broke");
			}

			File.Move(source, destination, overwrite);
		}

		var staging = new OutputStaging(run.Logger, run.Diagnostics, moveFile: FaultingMove);
		var publisher = new OutputPublisher(run, staging);

		(string Path, byte[] Bytes)[] outputs = [(firstPath, [1]), (secondPath, [2])];

		Assert.ThrowsExactly<InvalidOperationException>(() => publisher.TryPublishStaged(outputs));

		Assert.AreEqual(1, restoresOfFirst, "The rollback runs once, so it reports no second failure to restore.");
	}
}
