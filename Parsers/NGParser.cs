using System.Globalization;
using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Parsers;

/// <summary>
/// Parser for TRNG Next Generation script commands.
/// </summary>
public sealed class NGParser
{
    private readonly CompilerGlobals _globals;
    private readonly Dictionary<string, NGCommandDefinition> _commandDefinitions = [];

    private LanguageData? _languageData;
    private ExpressionEvaluator? _expressionEvaluator;
    private int _lastPluginId; // Track plugin ID for Customize/Parameters commands

    public NGParser(CompilerGlobals globals)
    {
        _globals = globals;
        _commandDefinitions = NGCommandDefinitions.GetAllDefinitions();
    }

    public void SetExpressionEvaluator(ExpressionEvaluator expressionEvaluator)
        => _expressionEvaluator = expressionEvaluator;

    public void SetLanguageData(LanguageData languageData)
        => _languageData = languageData;

    /// <summary>
    /// Reset occurrence counts for all commands (called when starting a new level).
    /// </summary>
    public void ResetOccurrenceCounts()
    {
        foreach (var definition in _commandDefinitions.Values)
            definition.OccurrenceCount = 0;
    }

    /// <summary>
    /// Parse an NG command from command name and arguments (overload for ScriptParser).
    /// </summary>
    public NGCommand? ParseNGCommand(string commandName, List<string> arguments, int lineNumber, bool isOptionsCommand)
    {
        if (!_commandDefinitions.TryGetValue(commandName, out var definition))
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, false, $"Unknown NG command: {commandName}");
            return null;
        }

        // Check if command is options-only and we're in correct section
        if (definition.IsOptionsOnly && !isOptionsCommand)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                $"{commandName} can only be used in [Options] section");

            return null;
        }

        // Check occurrence limits
        if (definition.MaxOccurrences != -1 && definition.OccurrenceCount >= definition.MaxOccurrences)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                $"Too many {commandName} commands. Maximum {definition.MaxOccurrences} allowed.");

            return null;
        }

        definition.OccurrenceCount++;

        var command = new NGCommand
        {
            CommandCode = definition.Code,
            CommandName = definition.Name,
            LineNumber = lineNumber
        };

        // Parse arguments based on definition
        if (!ParseArguments(definition, arguments, command))
            return null;

        // OPTIMIZATION: TriggerGroup (21) -> TriggerGroupWord (46)
        // If all arguments fit in 16 bits, use the legacy Word-sized command to save space
        // and match VB6 behavior (which might be required by the engine for some triggers)
        if ((int)command.CommandCode == 21 && TryOptimizeTriggerGroup(command, out var optimizedDefinition))
            definition = optimizedDefinition;

        // Compile to binary words
        if (!CompileCommand(definition, command))
            return null;

        return command;
    }

    /// <summary>
    /// Parse an NG command from full command line (legacy overload).
    /// </summary>
    public bool ParseNGCommand(string commandLine, int lineNumber, string sourceFile, out NGCommand? command)
    {
        command = null;

        // Extract command name and arguments
        var (commandName, arguments) = StringUtilities.ParseCommandLine(commandLine);

        if (!_commandDefinitions.TryGetValue(commandName, out var definition))
        {
            Logger.LogError($"Unknown NG command: {commandName} at line {lineNumber}");
            return false;
        }

        // Check occurrence limits
        if (definition.MaxOccurrences != -1 && definition.OccurrenceCount >= definition.MaxOccurrences)
        {
            Logger.LogError($"Too many {commandName} commands. Maximum {definition.MaxOccurrences} allowed.");
            return false;
        }

        definition.OccurrenceCount++;

        command = new NGCommand
        {
            CommandName = commandName,
            CommandCode = definition.Code,
            LineNumber = lineNumber,
            SourceFile = sourceFile
        };

        // Parse arguments based on definition
        if (!ParseArguments(definition, arguments, command))
            return false;

        // Compile to binary words
        if (!CompileCommand(definition, command))
            return false;

        return true;
    }

    private bool ParseArguments(NGCommandDefinition definition, List<string> arguments, NGCommand command)
    {
        // Handle Bool type commands (ENABLED/DISABLED)
        if (definition.ArgumentTypes.Count == 1 && definition.ArgumentTypes[0] == NGArgumentType.Bool)
        {
            if (arguments.Count != 1)
            {
                Logger.LogError($"Wrong number of arguments for {definition.Name}. Expected 1, got {arguments.Count}");
                return false;
            }

            var value = arguments[0].ToUpperInvariant();

            if (value == "ENABLED")
            {
                command.Arguments.Add(true);
                return true;
            }
            else if (value == "DISABLED")
            {
                command.Arguments.Add(false);
                return true;
            }
            else
            {
                Logger.LogError($"Expected ENABLED or DISABLED, got: {arguments[0]}");
                return false;
            }
        }

        // Handle variable-length array types
        bool hasVariableArray = definition.ArgumentTypes.Any(t => t
            is NGArgumentType.Array
            or NGArgumentType.ArrayByte
            or NGArgumentType.ArrayNybble
            or NGArgumentType.ArrayLong);

        int fixedArgCount = hasVariableArray ? definition.ArgumentTypes.Count - 1 : definition.ArgumentTypes.Count;

        if (!hasVariableArray && arguments.Count != definition.ArgumentTypes.Count)
        {
            Logger.LogError($"Wrong number of arguments for {definition.Name}. Expected {definition.ArgumentTypes.Count}, got {arguments.Count}");
            return false;
        }

        if (hasVariableArray && arguments.Count < fixedArgCount)
        {
            Logger.LogError($"Too few arguments for {definition.Name}. Expected at least {fixedArgCount}, got {arguments.Count}");
            return false;
        }

        // Parse fixed arguments
        for (int i = 0; i < fixedArgCount; i++)
        {
            // Reset plugin ID before parsing each argument (VB6 does this in the loop)
            _lastPluginId = 0;

            if (!ParseArgument(definition.ArgumentTypes[i], arguments[i], command))
                return false;

            // For Customize/Parameters first Long argument, encode plugin ID in high word
            if (i == 0 && ((int)definition.Code == 20 || (int)definition.Code == 27) && definition.ArgumentTypes[i] == NGArgumentType.Long && _lastPluginId > 0)
            {
                int originalValue = (int)command.Arguments[0];
                command.Arguments[0] = originalValue | (_lastPluginId << 16);
            }

            // For AssignSlot second argument (Arg 1), encode plugin ID in high word
            if (i == 1 && (int)definition.Code == 1 && definition.ArgumentTypes[i] == NGArgumentType.Long && _lastPluginId > 0)
            {
                int originalValue = (int)command.Arguments[1];
                command.Arguments[1] = originalValue | (_lastPluginId << 16);
            }
        }

        // Parse variable array if present
        if (hasVariableArray)
        {
            var arrayType = definition.ArgumentTypes[fixedArgCount];
            var arrayArgs = new List<object>();

            for (int i = fixedArgCount; i < arguments.Count; i++)
            {
                var tempCommand = new NGCommand();
                var elementType = arrayType == NGArgumentType.ArrayLong ? NGArgumentType.Long : NGArgumentType.Word;

                if (arrayType == NGArgumentType.ArrayNybble)
                {
                    // Nibbles are 4-bit values (0-15)
                    if (!ParseArgument(NGArgumentType.Word, arguments[i], tempCommand))
                        return false;

                    var value = (int)tempCommand.Arguments[0];

                    if (value is < 0 or > 15)
                    {
                        Logger.LogError($"Nibble value out of range (0-15): {value}");
                        return false;
                    }

                    arrayArgs.Add(value);
                }
                else
                {
                    if (!ParseArgument(elementType, arguments[i], tempCommand))
                        return false;

                    arrayArgs.Add(tempCommand.Arguments[0]);
                }
            }

            command.Arguments.Add(arrayArgs);
        }

        return true;
    }

    private bool ParseArgument(NGArgumentType type, string argument, NGCommand command)
    {
        try
        {
            switch (type)
            {
                case NGArgumentType.Word:
                case NGArgumentType.Integer:
                    if (!TryParseNumber(argument, out int wordValue))
                    {
                        Logger.LogError($"Invalid number: {argument}");
                        return false;
                    }

                    command.Arguments.Add(wordValue);
                    return true;

                case NGArgumentType.Long:
                    if (!TryParseNumber(argument, out int longValue))
                    {
                        Logger.LogError($"Invalid number: {argument}");
                        return false;
                    }

                    command.Arguments.Add(longValue);
                    return true;

                case NGArgumentType.String:
                    // Remove quotes if present
                    var str = argument.Trim();

                    if (str.StartsWith('"') && str.EndsWith('"'))
                        str = str[1..^1];

                    command.Arguments.Add(str);
                    return true;

                case NGArgumentType.ItemSlot:
                    // Can be number or slot name
                    if (!TryParseNumber(argument, out int slotValue))
                    {
                        // Try to resolve slot name from constants
                        Logger.LogError($"Invalid slot: {argument}");
                        return false;
                    }

                    if (slotValue is < 0 or > 464)
                    {
                        Logger.LogError($"Slot value out of range (0-464): {slotValue}");
                        return false;
                    }

                    command.Arguments.Add(slotValue);
                    return true;

                case NGArgumentType.Import:
                    if (!TryParseNumber(argument, out int importId))
                    {
                        Logger.LogError($"Invalid import ID: {argument}");
                        return false;
                    }

                    command.Arguments.Add(importId);
                    return true;

                default:
                    Logger.LogError($"Unsupported argument type: {type}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error parsing argument '{argument}': {ex.Message}");
            return false;
        }
    }

    private bool TryParseNumber(string value, out int result)
    {
        value = value.Trim().ToUpperInvariant();

        // Handle IGNORE constant
        if (value == "IGNORE")
        {
            result = -1;
            return true;
        }

        // Use ExpressionEvaluator for everything else (handles hex, decimal, constants, expressions)
        if (_expressionEvaluator is not null && _expressionEvaluator.TryEvaluate(value, out result))
        {
            _lastPluginId = _expressionEvaluator.LastPluginId;
            return true;
        }

        result = 0;
        return false;
    }

    private bool CompileCommand(NGCommandDefinition definition, NGCommand command)
    {
        // Format: (CommandCode * 256 + ArgumentWordCount) as first word
        // Then all arguments as words/longs

        var words = new List<short>();

        // For bool commands, don't add command bytes - they become flags
        if (definition.ArgumentTypes.Count == 1 && definition.ArgumentTypes[0] == NGArgumentType.Bool)
        {
            // Bool commands don't generate bytes, they set flags
            command.CompiledWords.Clear();
            return true;
        }

        // Count total words needed
        int totalWords = 0;

        for (int i = 0; i < command.Arguments.Count; i++)
        {
            var arg = command.Arguments[i];
            var argType = i < definition.ArgumentTypes.Count ? definition.ArgumentTypes[i] : NGArgumentType.Word;

            totalWords += GetArgumentWordCount(arg, argType);
        }

        // Add header word (tag * 256 + word count)
        int headerWord = ((int)definition.Code * 256) + totalWords;
        words.Add((short)headerWord);

        // Add argument words
        for (int i = 0; i < command.Arguments.Count; i++)
        {
            var arg = command.Arguments[i];
            var argType = i < definition.ArgumentTypes.Count ? definition.ArgumentTypes[i] : NGArgumentType.Word;

            AddArgumentWords(words, arg, argType);
        }

        command.CompiledWords = words;
        return true;
    }

    private bool TryOptimizeTriggerGroup(NGCommand command, out NGCommandDefinition optimizedDefinition)
    {
        optimizedDefinition = default!;

        // Check if all arguments fit in 16 bits (unsigned Word 0-65535)
        // Arg 0 is ID (Word) - already checked
        // Arg 1, 2, 3 are Longs in Code 21
        // Arg 4 is ArrayLong in Code 21

        // Check fixed args
        for (int i = 1; i <= 3; i++)
        {
            if (i < command.Arguments.Count)
            {
                int val = (int)command.Arguments[i];

                // Check if it fits in 16 bits (signed or unsigned)
                // 0 to 65535 OR -32768 to -1
                if ((val & 0xFFFF0000) != 0 && (val & 0xFFFF0000) != unchecked((int)0xFFFF0000))
                    return false;
            }
        }

        // Check array args
        if (command.Arguments.Count > 4 && command.Arguments[4] is List<object> array)
        {
            foreach (var item in array)
            {
                int val = (int)item;

                if ((val & 0xFFFF0000) != 0 && (val & 0xFFFF0000) != unchecked((int)0xFFFF0000))
                    return false;
            }
        }

        // If we get here, all values fit in 16 bits.
        // Switch to Code 46
        command.CommandCode = (NGCommandCode)46;

        // Find definition for TriggerGroupWord
        if (_commandDefinitions.TryGetValue("TriggerGroupWord=", out var def))
        {
            optimizedDefinition = def;
            return true;
        }

        return false;
    }

    private static int GetArgumentWordCount(object arg, NGArgumentType type)
    {
        switch (type)
        {
            case NGArgumentType.Word:
            case NGArgumentType.Integer:
            case NGArgumentType.ItemSlot:
            case NGArgumentType.Import:
                return 1;

            case NGArgumentType.Long:
                return 2;

            case NGArgumentType.String:
                // String index reference = 1 word
                return 1;

            case NGArgumentType.Array:
                if (arg is List<object> array)
                    return array.Count;

                return 0;

            case NGArgumentType.ArrayByte:
                if (arg is List<object> byteArray)
                {
                    // Count byte + data bytes, rounded up to word boundary
                    int totalBytes = 1 + byteArray.Count;

                    if ((totalBytes & 1) != 0)
                        totalBytes++; // Pad to even

                    return totalBytes / 2;
                }

                return 0;

            case NGArgumentType.ArrayNybble:
                if (arg is List<object> nibbleArray)
                {
                    // Count byte + nibbles (2 per byte), rounded up to word boundary
                    int nibbleCount = nibbleArray.Count;
                    int nibbleBytes = (nibbleCount + 1) / 2; // 2 nibbles per byte
                    int totalBytes = 1 + nibbleBytes; // Count byte + nibble bytes

                    if ((totalBytes & 1) != 0)
                        totalBytes++; // Pad to even

                    return totalBytes / 2;
                }

                return 0;

            case NGArgumentType.ArrayLong:
                if (arg is List<object> longArray)
                    return longArray.Count * 2;

                return 0;

            default:
                return 0;
        }
    }

    private void AddArgumentWords(List<short> words, object arg, NGArgumentType type)
    {
        switch (type)
        {
            case NGArgumentType.Word:
            case NGArgumentType.Integer:
            case NGArgumentType.ItemSlot:
            case NGArgumentType.Import:
                words.Add((short)(int)arg);
                break;

            case NGArgumentType.Long:
                int longVal = (int)arg;
                words.Add((short)(longVal & 0xFFFF));
                words.Add((short)((longVal >> 16) & 0xFFFF));
                break;

            case NGArgumentType.String:
                // Get string index from language data
                int strIndex = GetStringIndex((string)arg);
                words.Add((short)strIndex);
                break;

            case NGArgumentType.Array:
                if (arg is List<object> array)
                {
                    foreach (var item in array)
                        words.Add((short)(int)item);
                }

                break;

            case NGArgumentType.ArrayByte:
                if (arg is List<object> byteArray)
                {
                    // Pack bytes into words: first byte is count, then data bytes
                    var bytes = new List<byte>
                    {
                        (byte)byteArray.Count // Count first
                    };

                    foreach (var item in byteArray)
                        bytes.Add((byte)(int)item);

                    // Pad to even number of bytes
                    if ((bytes.Count & 1) != 0)
                        bytes.Add(0);

                    // Pack into words (little-endian)
                    for (int i = 0; i < bytes.Count; i += 2)
                        words.Add((short)(bytes[i] | (bytes[i + 1] << 8)));
                }

                break;

            case NGArgumentType.ArrayNybble:
                if (arg is List<object> nibbleArray)
                {
                    // Pack nibbles into bytes, then into words
                    var bytes = new List<byte>
                    {
                        (byte)nibbleArray.Count // Count first
                    };

                    // Pack nibbles 2 per byte
                    for (int i = 0; i < nibbleArray.Count; i += 2)
                    {
                        int low = (int)nibbleArray[i];
                        int high = (i + 1 < nibbleArray.Count) ? (int)nibbleArray[i + 1] : 0;
                        bytes.Add((byte)(low | (high << 4)));
                    }

                    // Pad to even number of bytes
                    if ((bytes.Count & 1) != 0)
                        bytes.Add(0);

                    // Pack into words (little-endian)
                    for (int i = 0; i < bytes.Count; i += 2)
                        words.Add((short)(bytes[i] | (bytes[i + 1] << 8)));
                }

                break;

            case NGArgumentType.ArrayLong:
                if (arg is List<object> longs)
                {
                    foreach (var item in longs)
                    {
                        int val = (int)item;
                        words.Add((short)(val & 0xFFFF));
                        words.Add((short)((val >> 16) & 0xFFFF));
                    }
                }

                break;
        }
    }

    private int GetStringIndex(string text)
    {
        if (_languageData is null)
        {
            // Language data not yet loaded - return 0 as default
            // This is normal during Options section parsing
            return 0;
        }

        // Handle special string index formats
        if (text.StartsWith('#') && int.TryParse(text[1..], out int directIndex))
            return directIndex;

        if (text.StartsWith('!') && int.TryParse(text[1..], out int ngIndex))
            return 32768 + ngIndex;

        if (text.StartsWith('&') && int.TryParse(text[1..], NumberStyles.HexNumber, null, out int binIndex))
            return binIndex;

        // Search in language strings
        for (int i = 0; i < _languageData.Strings.Count; i++)
        {
            if (string.Equals(_languageData.Strings[i], text, StringComparison.Ordinal))
                return i;
        }

        // Search in NG extra strings
        for (int i = 0; i < _languageData.ExtraStrings.Count; i++)
        {
            if (string.Equals(_languageData.ExtraStrings[i].Text, text, StringComparison.Ordinal))
                return 32768 + _languageData.ExtraStrings[i].Index;
        }

        Logger.LogWarning($"String not found in language file: {text}");
        return 0;
    }

    /// <summary>
    /// Reset occurrence counters for all commands.
    /// </summary>
    public void ResetOccurrenceCounters()
    {
        foreach (var definition in _commandDefinitions.Values)
            definition.OccurrenceCount = 0;
    }
}
