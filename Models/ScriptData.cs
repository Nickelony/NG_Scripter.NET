namespace TRNGScriptCompiler.Models;

public sealed class ScriptLine
{
    public string Text { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class ScriptSection
{
    public ScriptSectionType SectionType { get; set; }
    public ScriptLevelFlags Flags { get; set; }
    public string LevelName { get; set; } = string.Empty; // Display name from Name= command
    public string LevelPath { get; set; } = string.Empty; // File path from Level= command
    public int CD { get; set; }
    public List<ScriptLine> Lines { get; set; } = [];
    public List<byte> CompiledBytes { get; set; } = [];
    public NGCommandGroup NGCommands { get; set; } = new();
}

public sealed class ScriptOptions
{
    public ScriptMainFlags Flags { get; set; }
    public int InputTimeOut { get; set; }
    public int Security { get; set; }
    public short NGSettings { get; set; }
}

public sealed class FileInclude
{
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
}

public sealed class ScriptData
{
    public ScriptOptions Options { get; set; } = new();
    public List<ScriptSection> Sections { get; set; } = [];
    public List<string> LevelNames { get; set; } = [];
    public List<string> PSXExtensions { get; set; } = [.. new string[4]];
    public List<string> PCExtensions { get; set; } = [.. new string[4]];
    public List<string> LanguageFiles { get; set; } = [];
    public bool EncryptScript { get; set; }
    public int PersonalKey { get; set; }

    // NG (Next Generation) data
    public NGScriptData NGData { get; set; } = new();

    // File include tracking
    public List<FileInclude> IncludeStack { get; set; } = [];
}
