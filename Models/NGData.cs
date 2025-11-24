namespace TRNGScriptCompiler.Models;

/// <summary>
/// Represents an NG (Next Generation) command with its arguments.
/// </summary>
public class NGCommand
{
    public string CommandName { get; set; } = string.Empty;
    public NGCommandCode CommandCode { get; set; }
    public List<object> Arguments { get; set; } = [];
    public List<short> CompiledWords { get; set; } = []; // Compiled to word array
    public int LineNumber { get; set; }
    public string SourceFile { get; set; } = string.Empty;
}

/// <summary>
/// Group of NG commands for a section (Options or Level).
/// </summary>
public class NGCommandGroup
{
    public List<NGCommand> Commands { get; set; } = [];
    public NGMainFlags OptionFlags { get; set; }
    public NGLevelFlags LevelFlags { get; set; }
}

/// <summary>
/// Definition of an NG command (from scripter_constants.txt).
/// </summary>
public class NGCommandDefinition
{
    public string Name { get; set; } = string.Empty;
    public NGCommandCode Code { get; set; }
    public int ArgumentCount { get; set; }
    public List<NGArgumentType> ArgumentTypes { get; set; } = [];
    public bool IsOptionsOnly { get; set; } // Commands starting with *
    public int MaxOccurrences { get; set; } = -1; // -1 = unlimited
    public int BoolEnabledValue { get; set; }
    public int BoolDisabledValue { get; set; }
    public int OccurrenceCount { get; set; } // Runtime tracking
}

/// <summary>
/// Import file data (for ImportFile= commands).
/// </summary>
public class NGImportFile
{
    public int ImportId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public NGImportMode ImportMode { get; set; }
    public int FileType { get; set; }
    public byte[] Data { get; set; } = [];
}

/// <summary>
/// Import modes for NG import files.
/// </summary>
public enum NGImportMode
{
    Unknown = 0,
    Memory = 1,
    Temporary = 2
}

/// <summary>
/// Plugin information.
/// </summary>
public class NGPlugin
{
    public int PluginId { get; set; }
    public string PluginName { get; set; } = string.Empty;
    public int MainSettings { get; set; }
    public List<int> DisabledFeatures { get; set; } = [];
    public Dictionary<string, int> Constants { get; set; } = []; // Plugin constants
    public string ScriptFile { get; set; } = string.Empty; // Path to plugin*.script file
}

/// <summary>
/// Extended NG data for ScriptData.
/// </summary>
public class NGScriptData
{
    public NGCommandGroup OptionsCommands { get; set; } = new();
    public List<NGCommandGroup> LevelCommands { get; set; } = [];
    public List<NGImportFile> ImportFiles { get; set; } = [];
    public List<NGPlugin> Plugins { get; set; } = [];
    public Dictionary<string, NGCommandDefinition> CommandDefinitions { get; set; } = [];
    public bool EnableScriptEncryption { get; set; }
    public int NGSettings { get; set; }
}

/// <summary>
/// Trigger Group data (TriggerGroup= command).
/// </summary>
public class NGTriggerGroup
{
    public int GroupId { get; set; }
    public int ExportValue1 { get; set; } // Includes TGROUP_ flags
    public int ExportValue2 { get; set; }
    public int ExportValue3 { get; set; }
    public List<int> AdditionalValues { get; set; } = []; // Variable length array
}

/// <summary>
/// Global Trigger data (GlobalTrigger= command).
/// </summary>
public class NGGlobalTrigger
{
    public int TriggerId { get; set; }
    public int Flags { get; set; } // FGT_ flags
    public int TriggerType { get; set; } // GT_ type
    public int Parameter { get; set; }
    public int ConditionTriggerGroupId { get; set; }
    public int PerformTriggerGroupId { get; set; }
    public int PerformTriggerGroupOnFalseId { get; set; }
}

/// <summary>
/// Organizer data (Organizer= command).
/// </summary>
public class NGOrganizer
{
    public int OrganizerId { get; set; }
    public int Flags { get; set; }  // FO_ flags
    public int Parameter { get; set; }
    public List<(int Time, int TriggerGroupId)> TimeSequences { get; set; } = [];
}

/// <summary>
/// Customize data (Customize= command).
/// </summary>
public class NGCustomize
{
    public CustomizeType CustomizeType { get; set; }
    public List<object> Parameters { get; set; } = [];
}

/// <summary>
/// Test Position data (TestPosition= command).
/// </summary>
public class NGTestPosition
{
    public int TestId { get; set; }
    public int Flags { get; set; } // TPOS_ flags
    public int MoveableSlot { get; set; }
    public short XDistanceMin { get; set; }
    public short XDistanceMax { get; set; }
    public short YDistanceMin { get; set; }
    public short YDistanceMax { get; set; }
    public short ZDistanceMin { get; set; }
    public short ZDistanceMax { get; set; }
    public short HOrientDiffMin { get; set; }
    public short HOrientDiffMax { get; set; }
    public short VOrientDiffMin { get; set; }
    public short VOrientDiffMax { get; set; }
    public short ROrientDiffMin { get; set; }
    public short ROrientDiffMax { get; set; }
}
