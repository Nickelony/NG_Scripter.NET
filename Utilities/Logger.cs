using TRNGScriptCompiler.Models;

namespace TRNGScriptCompiler.Utilities;

public static class Logger
{
    private static readonly List<string> _logMessages = new();
    private static bool _enableConsoleOutput = true;
    private static bool _conciseMode = false;
    
    public static void SetConciseMode(bool concise)
    {
        _conciseMode = concise;
    }
    
    public static void SetConsoleOutput(bool enable)
    {
        _enableConsoleOutput = enable;
    }
    
    public static void Log(string message)
    {
        _logMessages.Add(message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine(message);
        }
    }
    
    public static void LogVerbose(string message)
    {
        if (!_conciseMode)
        {
            Log(message);
        }
    }
    
    public static void LogVerboseIf(bool condition, string message)
    {
        if (condition && !_conciseMode)
        {
            Log(message);
        }
    }
    
    public static void LogError(string message)
    {
        string errorMsg = $"ERROR: {message}";
        _logMessages.Add(errorMsg);
        
        if (_enableConsoleOutput)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(errorMsg);
            Console.ResetColor();
        }
    }
    
    public static void LogWarning(string message)
    {
        string warningMsg = $"WARNING: {message}";
        _logMessages.Add(warningMsg);
        
        if (_enableConsoleOutput)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(warningMsg);
            Console.ResetColor();
        }
    }
    
    public static void SaveLog(string filePath)
    {
        File.WriteAllLines(filePath, _logMessages);
    }
    
    public static string GetLog()
    {
        return string.Join(Environment.NewLine, _logMessages);
    }
    
    public static void Clear()
    {
        _logMessages.Clear();
    }
}

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
        {
            Logger.LogError($"Line {lineNumber}: {message}");
        }
        else
        {
            Logger.LogWarning($"Line {lineNumber}: {message}");
        }
    }
    
    public static bool HasFatalErrors(List<CompilerError> errors)
    {
        return errors.Any(e => e.IsFatal);
    }
    
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
            {
                location += $" in {error.SourceFile}";
            }
            
            Logger.Log($"  [{errorType}] {location}: {error.Message}");
            
            if (!string.IsNullOrEmpty(error.SourceLine))
            {
                Logger.Log($"    >>> {error.SourceLine}");
            }
        }
    }
}
