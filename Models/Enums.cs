namespace TRNGScriptCompiler.Models;

[Flags]
public enum ScriptLevelFlags
{
    None = 0,
    YoungLara = 1 << 0,
    Weather = 1 << 1, // Not used
    Horizon = 1 << 2,
    Layer1 = 1 << 3,
    Layer2 = 1 << 4,
    StarField = 1 << 5,
    Lightning = 1 << 6,
    Train = 1 << 7,
    Pulse = 1 << 8,
    ColAddHorizon = 1 << 9,
    ResetHub = 1 << 10,
    LensFlare = 1 << 11,
    Timer = 1 << 12,
    Mirror = 1 << 13,
    RemoveAmulet = 1 << 14,
    NoLevel = 1 << 15
}

[Flags]
public enum ScriptMainFlags
{
    None = 0,
    FlyCheat = 1 << 0,
    LoadSave = 1 << 1,
    Title = 1 << 2,
    PlayAnyLevel = 1 << 3,
    DemoDisk = 1 << 7
}

public enum ScriptSectionType
{
    None,
    PSXExtensions,
    PCExtensions,
    Language,
    Options,
    Title,
    Level
}


