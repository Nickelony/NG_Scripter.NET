using System.Text.RegularExpressions;

namespace TRNGScriptCompiler.Utilities;

/// <summary>
/// Parses Objects.h file to extract slot constants.
/// </summary>
public class ObjectsHParser
{
    private readonly Dictionary<string, int> _slotConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _staticConstants = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load slot and static constants from Objects.h.
    /// </summary>
    public bool LoadObjectsH(string objectsHPath)
    {
        try
        {
            if (!File.Exists(objectsHPath))
            {
                Logger.LogWarning($"Objects.h not found: {objectsHPath}");
                return false;
            }

            Logger.LogVerbose($"Loading slot constants from: {objectsHPath}");

            var content = File.ReadAllText(objectsHPath);

            // Parse enum object_types
            ParseEnum(content, "object_types", _slotConstants);

            // Parse enum static_types
            ParseEnum(content, "static_types", _staticConstants);

            Logger.LogVerbose($"Loaded {_slotConstants.Count} slot constants and {_staticConstants.Count} static constants");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading Objects.h: {ex.Message}");
            return false;
        }
    }

    private static void ParseEnum(string content, string enumName, Dictionary<string, int> targetDict)
    {
        // Find enum declaration
        var enumPattern = $@"enum\s+{enumName}\s*\{{([^}}]+)\}}";
        var match = Regex.Match(content, enumPattern, RegexOptions.Singleline);

        if (!match.Success)
        {
            Logger.LogWarning($"Could not find enum {enumName} in Objects.h");
            return;
        }

        var enumBody = match.Groups[1].Value;
        var lines = enumBody.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        int currentValue = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//"))
                continue;

            // Remove trailing comma and comments
            trimmed = trimmed.TrimEnd(',', ' ', '\t');
            var commentIndex = trimmed.IndexOf("//");

            if (commentIndex >= 0)
                trimmed = trimmed[..commentIndex].Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Check if there's an explicit value assignment
            var parts = trimmed.Split('=');
            var constantName = parts[0].Trim();

            if (parts.Length > 1)
            {
                // Explicit value
                var valueStr = parts[1].Trim();

                if (int.TryParse(valueStr, out int explicitValue))
                {
                    currentValue = explicitValue;
                }
                else if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(valueStr[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexValue))
                        currentValue = hexValue;
                }
            }

            if (!string.IsNullOrEmpty(constantName))
            {
                targetDict[constantName] = currentValue;
                currentValue++;
            }
        }
    }

    /// <summary>
    /// Try to get a slot constant value.
    /// </summary>
    public bool TryGetSlotConstant(string name, out int value)
        => _slotConstants.TryGetValue(name, out value);

    /// <summary>
    /// Try to get a static constant value.
    /// </summary>
    public bool TryGetStaticConstant(string name, out int value)
        => _staticConstants.TryGetValue(name, out value);

    /// <summary>
    /// Get all slot constants.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetSlotConstants()
        => _slotConstants;

    /// <summary>
    /// Get all static constants.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetStaticConstants()
        => _staticConstants;
}
