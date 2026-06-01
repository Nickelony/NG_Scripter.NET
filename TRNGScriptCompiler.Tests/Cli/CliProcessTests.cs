using TRNGScriptCompiler.Cli;
using TRNGScriptCompiler.Tests.Support;

namespace TRNGScriptCompiler.Tests.Cli;

[TestClass]
[TestCategory("Fast")]
public sealed class CliProcessTests
{
	[TestMethod]
	public void InterruptBeforeACompileStarts_LetsTheProcessEndAndPrintsNothing()
	{
		var console = new CapturedConsole();
		using CliProcess process = console.CreateProcess(waitForExit: () => { });

		bool goesOn = process.HandleInterrupt();

		Assert.IsFalse(goesOn, "A Ctrl+C at a prompt has no compile to cancel, so the process ends.");
		Assert.IsFalse(process.CancellationToken.IsCancellationRequested);
		Assert.AreEqual(string.Empty, console.AllText);
	}

	[TestMethod]
	public void FirstInterrupt_CancelsTheTokenAndPrintsTheNotice()
	{
		var console = new CapturedConsole();
		using CliProcess process = console.CreateProcess(waitForExit: () => { });

		process.MarkCompileStarted();

		Assert.IsFalse(process.CancellationToken.IsCancellationRequested, "No Ctrl+C has arrived, so the token is still live.");

		bool goesOn = process.HandleInterrupt();

		Assert.IsTrue(goesOn, "The first Ctrl+C is handled, so the process goes on.");
		Assert.IsTrue(process.CancellationToken.IsCancellationRequested);
		Assert.AreSequenceEqual(new[] { "Canceling; press Ctrl+C again to end the process at once." }, console.ErrorLines);
		Assert.AreEqual(string.Empty, console.OutputText);
	}

	[TestMethod]
	public void SecondInterrupt_LetsTheProcessEndAndPrintsNothingMore()
	{
		var console = new CapturedConsole();
		using CliProcess process = console.CreateProcess(waitForExit: () => { });

		process.MarkCompileStarted();
		process.HandleInterrupt();

		string afterFirst = console.AllText;
		bool goesOn = process.HandleInterrupt();

		Assert.IsFalse(goesOn, "The second Ctrl+C is not handled, so the process ends.");
		Assert.AreEqual(afterFirst, console.AllText);
	}

	[TestMethod]
	public void InterruptAfterTheProcessDisposedIt_LetsTheProcessEndAndPrintsNothing()
	{
		var console = new CapturedConsole();
		CliProcess process = console.CreateProcess(waitForExit: () => { });

		process.MarkCompileStarted();
		process.Dispose();

		bool goesOn = process.HandleInterrupt();

		Assert.IsFalse(goesOn, "A Ctrl+C after the run ended has nothing to cancel, so the process ends.");
		Assert.AreEqual(string.Empty, console.AllText);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void InterruptAtTheWaitForAKey_LetsTheProcessEndAndPrintsNothing(bool faulted)
	{
		var console = new CapturedConsole();

		bool goesOn = true;
		bool printed = false;

		CliProcess? process = null;

		void InterruptAtTheKeyPrompt()
		{
			string before = console.AllText;
			goesOn = process?.HandleInterrupt() ?? true;
			printed = console.AllText != before;
		}

		process = console.CreateProcess(waitForExit: InterruptAtTheKeyPrompt);
		using CliProcess owner = process;

		process.MarkCompileStarted();

		if (faulted)
			process.Run(_ => throw new InvalidOperationException("the host broke"), []);
		else
			process.Run(_ => new CliRunResult(ExitCode: 0, WaitForKey: true), ["engine"]);

		Assert.IsFalse(goesOn, "The run ended, so a Ctrl+C at the key prompt ends the process.");
		Assert.IsFalse(printed, "No notice is printed, since nothing is left to cancel.");
		Assert.IsFalse(process.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Run_PassesTheArgumentsOnAndReturnsTheExitCode()
	{
		var console = new CapturedConsole();
		using CliProcess process = console.CreateProcess(waitForExit: () => { });

		string[] received = [];

		int exitCode = process.Run(
			args =>
			{
				received = args;
				return new CliRunResult(ExitCode: 130, WaitForKey: false);
			},

			["engine", "-q"]
		);

		Assert.AreEqual(130, exitCode);
		Assert.AreSequenceEqual(new[] { "engine", "-q" }, received);
		Assert.AreEqual(string.Empty, console.AllText);
	}

	[TestMethod]
	[DataRow(true, 1)]
	[DataRow(false, 0)]
	public void Run_WaitsForAKeyOnlyWhenTheResultAsksFor(bool waitForKey, int expectedWaits)
	{
		var console = new CapturedConsole();
		int waits = 0;

		using CliProcess process = console.CreateProcess(waitForExit: () => waits++);

		process.Run(_ => new CliRunResult(ExitCode: 0, waitForKey), ["engine"]);

		Assert.AreEqual(expectedWaits, waits);
	}

	[TestMethod]
	public void Run_UnexpectedFault_PrintsItsMessageAndStackTraceInRedAndReturnsOne()
	{
		var console = new CapturedConsole();
		using CliProcess process = console.CreateProcess(waitForExit: () => { });

		Exception? fault = null;

		int exitCode = process.Run(
			_ =>
			{
				fault = new InvalidOperationException("the host broke");
				throw fault;
			},

			["engine"]
		);

		Assert.AreEqual(1, exitCode);
		Assert.IsNotNull(fault);

		Assert.AreEqual($"\nFatal error: the host broke\nStack trace:\n{fault.StackTrace}\n", console.TextIn(ConsoleColor.Red));
		Assert.AreEqual(console.AllText, console.ErrorText, "The report goes to standard error alone.");
	}

	[TestMethod]
	public void Run_UnexpectedFaultWithoutAStackTrace_SaysSo()
	{
		var console = new CapturedConsole();
		using CliProcess process = console.CreateProcess(waitForExit: () => { });

		int exitCode = process.Run(_ => throw new StacklessException(), ["engine"]);

		Assert.AreEqual(1, exitCode);
		Assert.AreEqual("\nFatal error: no trace\nStack trace:\nNo stack trace available.\n", console.TextIn(ConsoleColor.Red));
	}

	[TestMethod]
	[DataRow(0, 1)]
	[DataRow(1, 0)]
	public void Run_UnexpectedFault_WaitsForAKeyOnlyInInteractiveMode(int argumentCount, int expectedWaits)
	{
		var console = new CapturedConsole();
		int waits = 0;

		using CliProcess process = console.CreateProcess(waitForExit: () => waits++);

		process.Run(_ => throw new InvalidOperationException("the host broke"), [.. Enumerable.Repeat("engine", argumentCount)]);

		Assert.AreEqual(expectedWaits, waits);
	}

	private sealed class StacklessException() : Exception("no trace")
	{
		public override string? StackTrace => null;
	}
}
