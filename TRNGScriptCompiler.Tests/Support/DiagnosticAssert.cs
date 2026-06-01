using TRNGScriptCompiler.Kernel.Diagnostics;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Asserts on the fields of a run's diagnostics, so a test checks the error, the warning or the hint
/// itself and not the log text.
/// </summary>
/// <remarks>
/// A failed assertion lists every diagnostic of the run.
/// </remarks>
internal static class DiagnosticAssert
{
	/// <summary>
	/// Asserts that the run reported exactly one error, with the line and the full message given, and returns it.
	/// </summary>
	/// <remarks>
	/// The line is 1-based or <see langword="null"/> for no line, and the file is the full path the error names,
	/// or <see langword="null"/> to leave it unchecked.
	/// </remarks>
	public static CompilerDiagnostic SingleError(IReadOnlyList<CompilerDiagnostic> diagnostics, int? line, string message, string? file = null)
	{
		CompilerDiagnostic[] errors = [.. diagnostics.Where(diagnostic => diagnostic.IsError)];

		Assert.HasCount(1, errors, $"Expected one error. {Describe(diagnostics)}");
		AssertFields(diagnostic: errors[0], line, message, file, diagnostics);
		return errors[0];
	}

	/// <summary>
	/// Asserts that the run reported exactly one error, which names the unknown <c>[Options]</c> command given,
	/// on the line given.
	/// </summary>
	/// <remarks>
	/// <see cref="TestWorkspace.ScriptWithOptionsCommand"/> builds such a script, and its command stands
	/// on <see cref="TestWorkspace.OptionsCommandLine"/>.
	/// </remarks>
	public static CompilerDiagnostic UnknownOptionsCommand(
		IReadOnlyList<CompilerDiagnostic> diagnostics,
		int line,
		string command,
		string? file = null
	) => SingleError(diagnostics, line, message: $"Unknown command for [Options] section: {command}; correct its name or remove the line", file);

	/// <summary>Asserts that the run reported exactly the errors given, in the order given.</summary>
	/// <param name="diagnostics">The diagnostics of the run.</param>
	/// <param name="errors">The 1-based line or <see langword="null"/>, and the full message of each error.</param>
	public static void Errors(IReadOnlyList<CompilerDiagnostic> diagnostics, params (int? Line, string Message)[] errors) =>
		Errors(diagnostics, file: null, errors);

	/// <summary>Asserts that the run reported exactly the errors given, in the order given, all in one file.</summary>
	/// <param name="diagnostics">The diagnostics of the run.</param>
	/// <param name="file">The full path every error names, or <see langword="null"/> to leave it unchecked.</param>
	/// <param name="errors">The 1-based line or <see langword="null"/>, and the full message of each error.</param>
	public static void Errors(IReadOnlyList<CompilerDiagnostic> diagnostics, string? file, params (int? Line, string Message)[] errors)
	{
		CompilerDiagnostic[] actual = [.. diagnostics.Where(diagnostic => diagnostic.IsError)];

		Assert.HasCount(errors.Length, actual, $"Expected {errors.Length} errors. {Describe(diagnostics)}");

		for (int i = 0; i < errors.Length; i++)
			AssertFields(diagnostic: actual[i], line: errors[i].Line, message: errors[i].Message, file, diagnostics);
	}

	/// <summary>
	/// Asserts that exactly one warning of the run has the full message given and points at the line given,
	/// and returns it.
	/// </summary>
	/// <remarks>
	/// The arguments read as for <see cref="SingleError"/>, and other warnings
	/// may stand beside it, such as that of a missing <c>Objects.h</c>.
	/// </remarks>
	public static CompilerDiagnostic HasWarning(IReadOnlyList<CompilerDiagnostic> diagnostics, int? line, string message, string? file = null)
	{
		CompilerDiagnostic[] warnings = [.. diagnostics.Where(diagnostic => diagnostic.IsWarning && diagnostic.Message == message)];

		Assert.HasCount(1, warnings, $"Expected one warning '{message}'. {Describe(diagnostics)}");
		AssertFields(diagnostic: warnings[0], line, message, file, diagnostics);
		return warnings[0];
	}

	/// <summary>
	/// Asserts that exactly one hint of the run has the full message given and points at the line given,
	/// and returns it.
	/// </summary>
	/// <remarks>
	/// The arguments read as for <see cref="HasWarning"/>, and other hints may stand beside it.
	/// </remarks>
	public static CompilerDiagnostic HasHint(IReadOnlyList<CompilerDiagnostic> diagnostics, int? line, string message, string? file = null)
	{
		CompilerDiagnostic[] hints = [.. diagnostics.Where(diagnostic => diagnostic.IsHint && diagnostic.Message == message)];

		Assert.HasCount(1, hints, $"Expected one hint '{message}'. {Describe(diagnostics)}");
		Assert.IsFalse(hints[0].IsError, $"A hint is never an error. {Describe(diagnostics)}");

		AssertFields(diagnostic: hints[0], line, message, file, diagnostics);
		return hints[0];
	}

	/// <summary>Asserts that the run reported no error, no warning and no hint.</summary>
	/// <param name="diagnostics">The diagnostics of the run.</param>
	public static void NoDiagnostics(IReadOnlyList<CompilerDiagnostic> diagnostics) => Assert.IsEmpty(diagnostics, Describe(diagnostics));

	/// <summary>Asserts that the run reported no error and no warning, and lets a hint stand.</summary>
	/// <param name="diagnostics">The diagnostics of the run.</param>
	/// <remarks>
	/// Use it where the input is valid and a hint about its spelling is no concern of the test,
	/// such as an <c>IGNORE</c> argument.
	/// </remarks>
	public static void NoErrorsOrWarnings(IReadOnlyList<CompilerDiagnostic> diagnostics) =>
		Assert.IsEmpty([.. diagnostics.Where(diagnostic => !diagnostic.IsHint)], Describe(diagnostics));

	private static void AssertFields(
		CompilerDiagnostic diagnostic,
		int? line,
		string message,
		string? file,
		IReadOnlyList<CompilerDiagnostic> diagnostics
	)
	{
		Assert.AreEqual(message, diagnostic.Message, Describe(diagnostics));
		Assert.AreEqual(line, diagnostic.LineNumber, $"The diagnostic points at the wrong line. {Describe(diagnostics)}");

		if (file is not null)
			Assert.AreEqual(file, diagnostic.SourceFile, $"The diagnostic names the wrong file. {Describe(diagnostics)}");
	}

	private static string Describe(IReadOnlyList<CompilerDiagnostic> diagnostics)
	{
		if (diagnostics.Count == 0)
			return "The run reported no diagnostics.";

		IEnumerable<string> lines = diagnostics.Select(diagnostic =>
			$"{Environment.NewLine}  {diagnostic.Severity} at {DiagnosticSink.FormatLocation(new SourceLocation(diagnostic.LineNumber, File: diagnostic.SourceFile))}: {diagnostic.Message}"
		);

		return "The run reported:" + string.Concat(lines);
	}
}
