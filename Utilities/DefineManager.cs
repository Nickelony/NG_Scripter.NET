using TRNGScriptCompiler.Models;

namespace TRNGScriptCompiler.Utilities;

/// <summary>
/// Manages #define directives and plugin definitions.
/// </summary>
public sealed class DefineManager
{
    private readonly Dictionary<string, int> _userDefines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pluginDefines = new(StringComparer.OrdinalIgnoreCase);
    private readonly CompilerGlobals _globals;
    private readonly ExpressionEvaluator _expressionEvaluator;

    public DefineManager(CompilerGlobals globals, ExpressionEvaluator expressionEvaluator)
    {
        _globals = globals;
        _expressionEvaluator = expressionEvaluator;
    }

    /// <summary>
    /// Process a #define directive line.
    /// Returns true if line was processed (even if there was an error).
    /// </summary>
    public bool ProcessDefineLine(string line, int lineNumber, string originalLine)
    {
        line = line.Trim();

        if (!line.StartsWith("#DEFINE", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "ERROR: wrong number of arguments in #define directive");

            return true;
        }

        var name = parts[1];
        var valueOrCommand = parts[2];

        // Check if it's a plugin define: #define @PluginName ID
        if (name.StartsWith('@'))
            return ProcessPluginDefine(name, valueOrCommand, lineNumber, originalLine);

        // Regular #define: #define CONSTANT_NAME value
        if (!_expressionEvaluator.TryEvaluate(valueOrCommand, out int value))
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                $"ERROR: the define value [{valueOrCommand}] is not a valid decimal or hexadecimal value");

            return true;
        }

        // Check for duplicate
        if (_userDefines.ContainsKey(name))
        {
            Logger.LogWarning($"WARNING: found two #define directives with same name [{name}]. The second directive will be ignored");
        }
        else
        {
            _userDefines[name] = value;

            // Add to global defines list for ScriptWriter and other components
            _globals.Defines.Add(new DefineRecord
            {
                Name = name,
                Value = value,
                FileName = Path.GetFileName(_globals.CurrentSourceFile)
            });

            Logger.LogVerboseIf(_globals.Verbose, $"  #define {name} = {value}");
        }

        return true;
    }

    private bool ProcessPluginDefine(string pluginName, string valueOrCommand, int lineNumber, string originalLine)
    {
        // Remove @ prefix
        pluginName = pluginName[1..];

        // Check for clear command: #define @plugins clear
        if (pluginName.Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            valueOrCommand.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _pluginDefines.Clear();

            Logger.LogVerboseIf(_globals.Verbose, "  Cleared plugin definitions");
            return true;
        }

        // Parse plugin ID
        if (!_expressionEvaluator.TryEvaluate(valueOrCommand, out int pluginId))
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                $"ERROR: not numeric value for plugin id in line: {originalLine}");

            return true;
        }

        // Check for duplicate ID
        if (_pluginDefines.ContainsValue(pluginId))
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "ERROR: it has been used twice the same Id for plugin in #define @plugin .. line. Perhaps you forgot to clear previous #define line with [#define @plugins clear] line");

            return true;
        }

        // Check for duplicate name (warning only)
        if (_pluginDefines.ContainsKey(pluginName))
            Logger.LogWarning($"WARNING: same plugin name [{pluginName}] had already been defined. Perhaps you forgot a [#define @plugins clear] line to clear previous definition");

        _pluginDefines[pluginName] = pluginId;
        Logger.LogVerboseIf(_globals.Verbose, $"  #define @{pluginName} = {pluginId}");

        return true;
    }

    /// <summary>
    /// Try to get a user-defined constant value.
    /// </summary>
    public bool TryGetDefine(string name, out int value)
        => _userDefines.TryGetValue(name, out value);

    /// <summary>
    /// Try to get a plugin ID by name.
    /// </summary>
    public bool TryGetPluginId(string pluginName, out int pluginId)
        => _pluginDefines.TryGetValue(pluginName, out pluginId);
}
