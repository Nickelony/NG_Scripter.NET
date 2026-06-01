using TRNGScriptCompiler.Cli;

void WriteError(string text, ConsoleColor? color) => Write(Console.Error, text, color);

using var process = new CliProcess(WriteError, WaitForExit);

// A Ctrl+C before a compile starts, such as at a prompt, and the second one leave e.Cancel false,
// so the process ends where it stands.
void OnInterrupt(object? _, ConsoleCancelEventArgs e) => e.Cancel = process.HandleInterrupt();
Console.CancelKeyPress += OnInterrupt;

var application = new CliApplication(
	input: Console.In,
	write: (text, color) => Write(Console.Out, text, color),
	WriteError,
	compileStarted: process.MarkCompileStarted,
	cancellationToken: process.CancellationToken
);

try
{
	return process.Run(application.Run, args);
}
finally
{
	// The handler goes before the process disposes its token, so a late Ctrl+C prints no notice and ends the process.
	Console.CancelKeyPress -= OnInterrupt;
}

static void Write(TextWriter writer, string text, ConsoleColor? color)
{
	if (color is null)
	{
		writer.Write(text);
		return;
	}

	Console.ForegroundColor = color.Value;
	writer.Write(text);
	Console.ResetColor();
}

static void WaitForExit()
{
	// A batch run with redirected input has no key to read.
	if (Console.IsInputRedirected)
		return;

	Console.WriteLine("\nPress any key to exit...");
	Console.ReadKey();
}
