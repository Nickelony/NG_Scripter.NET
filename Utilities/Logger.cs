namespace TRNGScriptCompiler.Utilities;

public static class Logger
{
    private static readonly List<string> _logMessages = [];
    private static bool _enableConsoleOutput = true;
    private static bool _conciseMode = false;

    public static void SetConciseMode(bool concise)
        => _conciseMode = concise;

    public static void SetConsoleOutput(bool enable)
        => _enableConsoleOutput = enable;

    public static void Log(string message)
    {
        _logMessages.Add(message);

        if (_enableConsoleOutput)
            Console.WriteLine(message);
    }

    public static void LogVerbose(string message)
    {
        if (!_conciseMode)
            Log(message);
    }

    public static void LogVerboseIf(bool condition, string message)
    {
        if (condition && !_conciseMode)
            Log(message);
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
        => File.WriteAllLines(filePath, _logMessages);

    public static string GetLog()
        => string.Join(Environment.NewLine, _logMessages);

    public static void Clear()
        => _logMessages.Clear();
}
