using System.Globalization;
using TRNGScriptCompiler.Models;

namespace TRNGScriptCompiler.Utilities;

/// <summary>
/// Loads global TRNG constants from scripter_constants.txt.
/// </summary>
public sealed class ConstantsLoader
{
    private readonly Dictionary<string, int> _constants = new(StringComparer.OrdinalIgnoreCase);
    private readonly CompilerGlobals _globals;

    public ConstantsLoader(CompilerGlobals globals)
        => _globals = globals;

    /// <summary>
    /// Load constants from scripter_constants.txt file.
    /// </summary>
    public bool LoadConstants(string constantsFilePath)
    {
        try
        {
            if (!File.Exists(constantsFilePath))
            {
                Logger.LogWarning($"Constants file not found: {constantsFilePath}");
                return false;
            }

            Logger.LogVerbose($"Loading constants from: {constantsFilePath}");

            var lines = File.ReadAllLines(constantsFilePath);
            bool inConstantsSection = false;
            int loadedCount = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Check for start/end markers
                if (trimmed.Equals("<START_CONSTANTS>", StringComparison.OrdinalIgnoreCase))
                {
                    inConstantsSection = true;
                    continue;
                }

                if (trimmed.Equals("<END>", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("<END_CONSTANTS>", StringComparison.OrdinalIgnoreCase))
                {
                    inConstantsSection = false;
                    continue;
                }

                // Skip if not in constants section
                if (!inConstantsSection)
                    continue;

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';'))
                    continue;

                // Parse constant definition: NAME:VALUE  ;comment
                // Skip lines that look like command definitions (contain #)
                if (trimmed.Contains('#'))
                    continue;

                // Remove comment first
                int commentIndex = trimmed.IndexOf(';');

                if (commentIndex > 0)
                    trimmed = trimmed[..commentIndex].Trim();

                // Split by colon
                int colonIndex = trimmed.IndexOf(':');

                if (colonIndex <= 0)
                    continue;

                string constantName = trimmed[..colonIndex].Trim();
                string valueStr = trimmed[(colonIndex + 1)..].Trim();

                if (string.IsNullOrEmpty(constantName) || string.IsNullOrEmpty(valueStr))
                    continue;

                // Skip lines that don't look like valid constant definitions
                // Valid constants start with letter/underscore and contain no spaces
                if (!char.IsLetter(constantName[0]) && constantName[0] != '_')
                    continue;

                if (constantName.Contains(' ') || constantName.Contains('\t'))
                    continue;

                // Parse value (can be decimal or hex with $)
                if (TryParseConstantValue(valueStr, out int value))
                {
                    _constants[constantName] = value;
                    loadedCount++;

                    Logger.LogVerboseIf(_globals.Verbose, $"  {constantName} = {value}");
                }
                else
                {
                    Logger.LogWarning($"Failed to parse constant value: {constantName}:{valueStr}");
                }
            }

            Logger.Log($"Loaded {loadedCount} global constants");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading constants: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Try to resolve a constant name to its value.
    /// </summary>
    public bool TryGetConstant(string name, out int value)
        => _constants.TryGetValue(name, out value);

    /// <summary>
    /// Get all loaded constants.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetAllConstants()
        => _constants;

    private static bool TryParseConstantValue(string valueStr, out int value)
    {
        valueStr = valueStr.Trim().ToUpperInvariant();

        // Handle hex values with $
        if (valueStr.StartsWith('$'))
            return int.TryParse(valueStr[1..], NumberStyles.HexNumber, null, out value);

        // Handle hex values with 0x
        if (valueStr.StartsWith("0X"))
            return int.TryParse(valueStr[2..], NumberStyles.HexNumber, null, out value);

        // Handle decimal
        return int.TryParse(valueStr, out value);
    }
}
