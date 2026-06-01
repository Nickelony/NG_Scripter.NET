namespace TRNGScriptCompiler.Cli;

/// <summary>The outcome of a console run.</summary>
/// <param name="ExitCode">
/// 0 on success, including help; 130 when Ctrl+C canceled the compile before it published, the shell's code
/// for an interrupt; 1 on any other failure, such as a compile that succeeded but could not write its <c>-Log</c> file.
/// </param>
/// <param name="WaitForKey">
/// Whether the process waits for a key press before it exits, which help and a rejected command line never do.
/// </param>
internal readonly record struct CliRunResult(int ExitCode, bool WaitForKey);
