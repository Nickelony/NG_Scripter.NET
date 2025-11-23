namespace TRNGScriptCompiler.Models;

/// <summary>
/// NG Command Tag Codes.
/// </summary>
public enum NGCommandCode
{
    AssignSlot = 1,
    Settings = 16,
    Customize = 20,
    TriggerGroup = 21,
    GlobalTrigger = 22,
    Parameters = 27,
    Image = 34,
    DefaultWindowsFont = 41,
    Plugin = 43,
    TriggerGroupWord = 46, // Legacy word-sized version
    FlagsOption = 200,
    FlagsLevel = 201,
    ImportFiles = 202
}

/// <summary>
/// NG Argument Types.
/// </summary>
[Flags]
public enum NGArgumentType
{
    None = 0,
    String = 1 << 0,          // String argument
    ArrayByte = 1 << 1,       // Byte array
    Word = 1 << 2,            // 16-bit word
    Integer = 1 << 3,         // 16-bit integer
    Long = 1 << 4,            // 32-bit long
    ItemSlot = 1 << 5,        // Item slot (0-464)
    Bool = 1 << 6,            // Boolean (ENABLED/DISABLED)
    Array = 1 << 7,           // Variable length word array
    ArrayNybble = 1 << 8,     // 4-bit nibble array
    Import = 1 << 9,          // Import file ID
    ArrayLong = 1 << 10       // Variable length long array
}

/// <summary>
/// NG Tag Types (appears in binary header).
/// </summary>
public enum NGTagType
{
    ScriptOptions = 0x800B,   // NGTAG_SCRIPT_OPTIONS
    ScriptLevel = 0x800C,     // NGTAG_SCRIPT_LEVEL
    ImportFile = 0x801F,      // NGTAG_IMPORT_FILE
    LevelNames = 0x10,        // NGTAG_LEVEL_NAMES (TO VERIFY)
    NGHubHeaders = 0x11,      // NGTAG_NG_HUB_HEADERS (TO VERIFY)
    VarDataLara = 0x12        // NGTAG_VAR_DATA_LARA (TO VERIFY)
}

/// <summary>
/// NG Main Script Flags (for [Options] section).
/// </summary>
[Flags]
public enum NGMainFlags
{
    None = 0,
    NoFloor = 0x0001,
    Bar = 0x0002,
    // Add more as discovered from VB6 code
}

/// <summary>
/// NG Level Flags.
/// </summary>
[Flags]
public enum NGLevelFlags
{
    None = 0,
    // Flags added based on VB6 code analysis
}

/// <summary>
/// Customize Types (CUST_* constants).
/// </summary>
public enum CustomizeType
{
    DisableScreamingHead = 1,
    SetSecretNumber = 2,
    SetCreditsLevel = 3,
    DisableForcingAnim96 = 4,
    RollingballPushing = 5,
    NewSoundEngine = 6,
    SpeedMoving = 7,
    ShatterRange = 8,
    Weapon = 9,
    Ammo = 10,
    ShowAmmoCounter = 11,
    SetInvItem = 12,
    // More customize types from scripter_constants.txt
}
