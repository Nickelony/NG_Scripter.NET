using TRNGScriptCompiler.Models;

namespace TRNGScriptCompiler.Parsers;

/// <summary>
/// Hardcoded NG command definitions from scripter_constants.txt
/// Format: CommandCode#MaxOccurrences#CommandName=:ArgType1:ArgType2:...#BoolEnabled#BoolDisabled
/// </summary>
public static class NGCommandDefinitions
{
    public static Dictionary<string, NGCommandDefinition> GetAllDefinitions()
    {
        var definitions = new Dictionary<string, NGCommandDefinition>(StringComparer.OrdinalIgnoreCase);
        
        // Parse all command definitions
        var rawDefinitions = new[]
        {
            "1#400#AssignSlot=:Slot:Long",
            "2#1#Snow=:Word",
            "3#1#LevelFarView=:Word",
            "4#1#FogRange=:Word:Word",
            "5#1#*WorldFarView=:Word",
            "6#1#TextFormat=:Word:Word:Word:Word",
            "7#1#Rain=:Word",
            "8#2#Damage=:Word:Word:Word:Long:String:Word",
            "9#100#Enemy=:Slot:Word:Word:Word:Word:Array",
            "10#256#Animation=:Word:Word:Word:Word:Word:Word:Word:Array",
            "11#100#MirrorEffect=:Word:Word:Array",
            "12#20#Elevator=:Word:Word:Word:Word:Word:Word:Word:Array",
            "13#1#KeyPad=:Word:Word:Word:Word",
            "14#99#AddEffect=:Word:Word:Word:Word:Word:Word:Word:Word:Word:Array",
            "15#1#Detector=:Word:Word:Word:Array",
            "16#1#*Settings=:Word",
            "17#99#TextureSequence=:Word:Word:Word:ArrayNybble",
            "18#100#Equipment=:Slot:Word",
            "19#255#MultEnvCondition=:Word:Word:Word:Word:Array",
            "20#-1#Customize=:Long:Array",
            "21#999#TriggerGroup=:Word:Long:Long:Long:ArrayLong",
            "22#499#GlobalTrigger=:Word:Word:Word:Long:Word:Word:Word",
            "23#499#Organizer=:Word:Word:Word:Word:Word:Array",
            "24#1#*SoundSettings=:Word:Word:Word",
            "25#99#ItemGroup=:Word:Word:Array",
            "26#199#ColorRGB=:Word:Word:Word:Word",
            "27#-1#Parameters=:Long:Word:Array",
            "28#1#Turbo=:Word:Word",
            "29#1#WindowTitle=:String",
            "30#99#TestPosition=:Word:Word:Slot:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word",
            "31#1#*LogItem=:Word:Word",
            "32#99#WindowsFont=:Word:String:Word:Word:Word:Word",
            "33#99#Diary=:Word:Word:Word:Word:Word:String:Word:Word",
            "34#199#Image=:Word:Word:Word:Word:Word:Word:Word:Word:Word",
            "35#1#SavegamePanel=:Word:Word:Word:Word:Word:String:Word:Word:Word",
            "36#1#*DiagnosticType=:Word:Word",
            "37#127#Switch=:Word:Word:Word:Array",
            "38#92#CombineItems=:Word:Word:Word",
            "39#49#StandBy=:Word:Word:Word:Word:String:Word:Word:Word:Word:Word:Word:Word",
            "40#511#AnimationSlot=:Slot:Word:Word:Word:Word:Word:Word:Word:Word:Array",
            "41#1#DefaultWindowsFont=:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word:Word",
            "42#1#Demo=:Word:Word:String:String:Word:Array",
            "43#255#*Plugin=:Word:String:Long:Array",
            "44#1#LaraStartPos=:Word:Word",
            "45#160#StaticMIP=:Word:Word:Word:Word:Word",
            "46#999#TriggerGroupWord=:Word:Word:Word:Word:Array",  // Word-sized version (legacy) - Re-enabled for optimization
            "47#1#*CryptPersonalKey=:Long",
            "200#1#*FlagsOption=:Word",  // Internal - main flags
            "201#1#FlagsLevel=:Word",     // Internal - level flags
        };
        
        foreach (var def in rawDefinitions)
        {
            var parsed = ParseDefinition(def);
            if (parsed is not null)
            {
                definitions[parsed.Name] = parsed;
            }
        }
        
        return definitions;
    }
    
    private static NGCommandDefinition? ParseDefinition(string definition)
    {
        // Format: Code#MaxOccurrences#[*]Name=:Type1:Type2:...#BoolEnabled#BoolDisabled
        var parts = definition.Split('#');
        if (parts.Length < 3) return null;
        
        if (!int.TryParse(parts[0], out int code)) return null;
        if (!int.TryParse(parts[1], out int maxOccurrences)) return null;
        
        var commandPart = parts[2];
        var colonIndex = commandPart.IndexOf(':');
        if (colonIndex == -1) return null;
        
        var nameWithEquals = commandPart.Substring(0, commandPart.IndexOf('=') + 1);
        var isOptionsOnly = nameWithEquals.StartsWith("*");
        var name = isOptionsOnly ? nameWithEquals.Substring(1) : nameWithEquals;
        
        // Parse argument types
        var typesPart = commandPart.Substring(colonIndex + 1);
        var typeStrings = typesPart.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var argumentTypes = new List<NGArgumentType>();
        
        foreach (var typeStr in typeStrings)
        {
            argumentTypes.Add(ParseArgumentType(typeStr));
        }
        
        // Parse bool flags if present
        int boolEnabled = 0, boolDisabled = 0;
        if (parts.Length >= 4 && TryParseHexOrDec(parts[3], out boolEnabled))
        {
            if (parts.Length >= 5)
            {
                TryParseHexOrDec(parts[4], out boolDisabled);
            }
        }
        
        return new NGCommandDefinition
        {
            Name = name,
            Code = (NGCommandCode)code,
            ArgumentCount = argumentTypes.Count,
            ArgumentTypes = argumentTypes,
            IsOptionsOnly = isOptionsOnly,
            MaxOccurrences = maxOccurrences,
            BoolEnabledValue = boolEnabled,
            BoolDisabledValue = boolDisabled
        };
    }
    
    private static NGArgumentType ParseArgumentType(string typeStr)
    {
        return typeStr.ToUpperInvariant() switch
        {
            "SLOT" => NGArgumentType.ItemSlot,
            "STRING" => NGArgumentType.String,
            "WORD" => NGArgumentType.Word,
            "ARRAYBYTE" => NGArgumentType.ArrayByte,
            "INTEGER" => NGArgumentType.Integer,
            "LONG" => NGArgumentType.Long,
            "BOOL" => NGArgumentType.Bool,
            "ARRAY" => NGArgumentType.Array,
            "ARRAYNYBBLE" => NGArgumentType.ArrayNybble,
            "IMPORT" => NGArgumentType.Import,
            "ARRAYLONG" => NGArgumentType.ArrayLong,
            _ => NGArgumentType.None
        };
    }
    
    private static bool TryParseHexOrDec(string value, out int result)
    {
        if (value.StartsWith("$") || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hexValue = value.StartsWith("$") ? value.Substring(1) : value.Substring(2);
            return int.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out result);
        }
        return int.TryParse(value, out result);
    }
}
