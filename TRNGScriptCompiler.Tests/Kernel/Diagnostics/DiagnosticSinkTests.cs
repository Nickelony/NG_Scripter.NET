using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Tests.Kernel.Diagnostics;

[TestClass]
public sealed class DiagnosticSinkTests
{
	[TestMethod]
	[TestCategory("Fast")]
	public void Warning_ForwardsTheLocation()
	{
		var log = new CompilerLog(conciseLogging: true);
		var sink = new DiagnosticSink(log);

		sink.Warning("span test", new SourceLocation(Line: 10, Column: 4, Length: 6, File: "test.txt"));

		Assert.HasCount(1, sink.Recorded);

		CompilerDiagnostic error = sink.Recorded[0];

		Assert.AreEqual(10, error.LineNumber);
		Assert.AreEqual(4, error.Column);
		Assert.AreEqual(6, error.Length);
		Assert.AreEqual("test.txt", error.SourceFile);
		Assert.IsFalse(error.IsError);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Error_DefaultsColumnAndLength()
	{
		var log = new CompilerLog(conciseLogging: true);
		var sink = new DiagnosticSink(log);

		sink.Error("no span", new SourceLocation(3));

		Assert.HasCount(1, sink.Recorded);
		Assert.AreEqual(0, sink.Recorded[0].Column);
		Assert.AreEqual(0, sink.Recorded[0].Length);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LocationWithoutALine_IsGeneral()
	{
		var log = new CompilerLog(conciseLogging: true);
		var sink = new DiagnosticSink(log);

		sink.Error("no line", new SourceLocation(Line: null, Column: 4, Length: 2, File: "English.txt"));

		CompilerDiagnostic diagnostic = sink.Recorded.Single();

		Assert.AreEqual(((int?)null, 0, 0, "English.txt"), (diagnostic.LineNumber, diagnostic.Column, diagnostic.Length, diagnostic.SourceFile));
		Assert.AreEqual("ERROR: General: no line", log.GetText());
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LocationOnLineZero_IsGeneral()
	{
		var log = new CompilerLog(conciseLogging: true);
		var sink = new DiagnosticSink(log);

		sink.Error("line zero", new SourceLocation(Line: 0, Column: 3, Length: 2, File: "x.txt"));

		CompilerDiagnostic diagnostic = sink.Recorded.Single();

		Assert.AreEqual(((int?)null, 0, 0, "x.txt"), (diagnostic.LineNumber, diagnostic.Column, diagnostic.Length, diagnostic.SourceFile));
		Assert.AreEqual("ERROR: General: line zero", log.GetText());
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void ErrorWithoutALocation_HasNoLineAndNoFile()
	{
		var entries = new List<LogEntry>();
		var sink = new DiagnosticSink(new CompilerLog(conciseLogging: true, entries.Add));

		sink.Error("general failure");

		CompilerDiagnostic diagnostic = sink.Recorded.Single();
		LogEntry entry = entries.Single();

		Assert.AreEqual<(int?, string?)>((null, null), (diagnostic.LineNumber, diagnostic.SourceFile));

		Assert.AreEqual<(int?, string?, string)>(
			(null, null, "ERROR: General: general failure"),
			(entry.LineNumber, entry.SourceFile, entry.FormattedMessage)
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Error_KeepsTheExceptionAndWritesTheLogLine()
	{
		var log = new CompilerLog(conciseLogging: true);
		var sink = new DiagnosticSink(log);
		var exception = new IOException("disk full");

		sink.Error("Failed writing Script.dat", new SourceLocation(7), exception);

		CompilerDiagnostic diagnostic = sink.Recorded.Single();

		Assert.AreSame(exception, diagnostic.Exception);
		Assert.IsTrue(sink.HasErrors());
		Assert.AreEqual("ERROR: Line 7: Failed writing Script.dat", log.GetText());
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LogFailureSummary_CountsErrorsAndWarningsApart()
	{
		var entries = new List<LogEntry>();
		var sink = new DiagnosticSink(new CompilerLog(conciseLogging: false, entries.Add));

		sink.Error("The first error");

		sink.Warning("The first warning");
		sink.Warning("The second warning");

		sink.LogFailureSummary();

		// The three diagnostic lines come first, then the separator and the count line alone.
		Assert.HasCount(5, entries);
		Assert.AreEqual("1 error(s) and 2 warning(s) found", entries[4].Message);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Hint_IsRecordedWithItsLocation_AndWritesNoLogLine()
	{
		var entries = new List<LogEntry>();
		var log = new CompilerLog(conciseLogging: false, entries.Add);
		var sink = new DiagnosticSink(log);

		sink.Hint("a better spelling", new SourceLocation(Line: 10, Column: 4, Length: 6, File: "test.txt"));

		CompilerDiagnostic hint = sink.Recorded.Single();

		Assert.AreEqual(
			(DiagnosticSeverity.Hint, "a better spelling", 10, 4, 6, "test.txt"),
			(hint.Severity, hint.Message, hint.LineNumber, hint.Column, hint.Length, hint.SourceFile)
		);

		Assert.IsFalse(sink.HasErrors());
		Assert.IsEmpty(entries);
		Assert.AreEqual(string.Empty, log.GetText());
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void Severity_ReadsAsExactlyOneOfTheThreeFlags()
	{
		var sink = new DiagnosticSink(new CompilerLog(conciseLogging: true));

		sink.Hint("hint");
		sink.Warning("warning");
		sink.Error("error");

		Assert.AreSequenceEqual(
			[
				(DiagnosticSeverity.Hint, true, false, false),
				(DiagnosticSeverity.Warning, false, true, false),
				(DiagnosticSeverity.Error, false, false, true),
			],

			sink.Recorded.Select(diagnostic => (diagnostic.Severity, diagnostic.IsHint, diagnostic.IsWarning, diagnostic.IsError))
		);
	}

	[TestMethod]
	[TestCategory("Fast")]
	public void LogFailureSummary_CountsNoHint()
	{
		var entries = new List<LogEntry>();
		var sink = new DiagnosticSink(new CompilerLog(conciseLogging: false, entries.Add));

		sink.Error("The first error");

		sink.Hint("The first hint");
		sink.Warning("The first warning");
		sink.Hint("The second hint");

		sink.LogFailureSummary();

		// The error and the warning come first, then the separator and the count line alone.
		Assert.HasCount(4, entries);
		Assert.AreEqual("1 error(s) and 1 warning(s) found", entries[3].Message);
	}
}
