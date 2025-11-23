using System.Globalization;

namespace TRNGScriptCompiler.Utilities;

/// <summary>
/// Evaluates numeric expressions with + and - operators
/// </summary>
public class ExpressionEvaluator
{
    // Resolver returns (success, value, pluginId)
    private readonly Func<string, (bool success, int value, int pluginId)> _constantResolver;
    
    /// <summary>
    /// The Plugin ID of the last resolved constant in the expression.
    /// This is used for encoding plugin IDs in certain commands.
    /// </summary>
    public int LastPluginId { get; private set; }
    
    public ExpressionEvaluator(Func<string, (bool, int, int)> constantResolver)
    {
        _constantResolver = constantResolver;
    }
    
    /// <summary>
    /// Evaluate an expression like "CONST1 + CONST2 - 5"
    /// </summary>
    public bool TryEvaluate(string expression, out int result)
    {
        result = 0;
        LastPluginId = 0; // Reset before evaluation
        
        if (string.IsNullOrWhiteSpace(expression))
            return false;
        
        expression = expression.Trim().ToUpperInvariant();
        
        // Split by + operator
        var addParts = expression.Split('+');
        int total = 0;
        
        foreach (var addPart in addParts)
        {
            var trimmedAdd = addPart.Trim();
            if (string.IsNullOrEmpty(trimmedAdd))
                continue;
            
            // Split by - operator
            var subtractParts = trimmedAdd.Split('-');
            
            for (int i = 0; i < subtractParts.Length; i++)
            {
                var part = subtractParts[i].Trim();
                if (string.IsNullOrEmpty(part))
                    continue;
                
                if (!TryParseValue(part, out int value))
                    return false;
                
                // First part is added, rest are subtracted
                if (i == 0)
                    total += value;
                else
                    total -= value;
            }
        }
        
        result = total;
        return true;
    }
    
    private bool TryParseValue(string value, out int result)
    {
        result = 0;
        value = value.Trim();
        
        if (string.IsNullOrEmpty(value))
            return false;
        
        // Try hex with $
        if (value.StartsWith("$"))
        {
            return int.TryParse(value.Substring(1), NumberStyles.HexNumber, null, out result);
        }
        
        // Try hex with 0x
        if (value.StartsWith("0X"))
        {
            return int.TryParse(value.Substring(2), NumberStyles.HexNumber, null, out result);
        }
        
        // Try hex with #
        if (value.StartsWith("#"))
        {
            return int.TryParse(value.Substring(1), NumberStyles.HexNumber, null, out result);
        }
        
        // Try decimal number
        if (int.TryParse(value, out result))
        {
            return true;
        }
        
        // Try as constant name
        var resolved = _constantResolver(value);
        if (resolved.success)
        {
            result = resolved.value;
            
            // Track plugin ID if present
            if (resolved.pluginId > 0)
            {
                LastPluginId = resolved.pluginId;
            }
            
            return true;
        }
        
        return false;
    }
}
