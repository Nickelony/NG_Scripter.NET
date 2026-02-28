using System.Globalization;

namespace TRNGScriptCompiler.Utilities;

/// <summary>
/// Shared utility for parsing numeric values in decimal or hexadecimal format.
/// Supports $ prefix, 0x prefix, and plain decimal.
/// </summary>
public static class NumberParser
{
    /// <summary>
    /// Parses a string as a decimal or hexadecimal integer.
    /// Accepts $FFFF, 0xFFFF, and plain decimal formats.
    /// </summary>
    public static bool TryParseHexOrDec(string value, out int result)
    {
        value = value.Trim();

        // Handle hex values with $ prefix
        if (value.StartsWith('$'))
            return int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, null, out result);

        // Handle hex values with 0x prefix
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, null, out result);

        // Handle plain decimal
        return int.TryParse(value, out result);
    }
}
