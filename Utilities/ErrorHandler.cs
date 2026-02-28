using TRNGScriptCompiler.Models;

namespace TRNGScriptCompiler.Utilities;

public static class ErrorHandler
{
    public static void AddError(List<CompilerError> errors, int lineNumber, bool isFatal,
        string message, string sourceLine = "", string sourceFile = "")
    {
        errors.Add(new CompilerError
        {
            LineNumber = lineNumber,
            IsFatal = isFatal,
            Message = message,
            SourceLine = sourceLine,
            SourceFile = sourceFile
        });

        if (isFatal)
            Logger.LogError($"Line {lineNumber}: {message}");
        else
            Logger.LogWarning($"Line {lineNumber}: {message}");
    }

    public static bool HasFatalErrors(List<CompilerError> errors)
        => errors.Any(e => e.IsFatal);

    public static void DisplayErrors(List<CompilerError> errors)
    {
        if (errors.Count == 0)
        {
            Logger.Log("No compilation errors.");
            return;
        }

        Logger.Log($"\n{errors.Count} error(s) found:");

        foreach (var error in errors)
        {
            string errorType = error.IsFatal ? "ERROR" : "WARNING";
            string location = error.LineNumber >= 0 ? $"Line {error.LineNumber}" : "General";

            if (!string.IsNullOrEmpty(error.SourceFile))
                location += $" in {error.SourceFile}";

            Logger.Log($"  [{errorType}] {location}: {error.Message}");

            if (!string.IsNullOrEmpty(error.SourceLine))
                Logger.Log($"    >>> {error.SourceLine}");
        }
    }
}
