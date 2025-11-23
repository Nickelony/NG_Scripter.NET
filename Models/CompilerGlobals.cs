namespace TRNGScriptCompiler.Models;

public sealed class DefineRecord
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public sealed class NewTag
{
    public string Name { get; set; } = string.Empty;
    public int ArgumentCount { get; set; }
    public int BoolEnabled { get; set; }
    public int BoolDisabled { get; set; }
    public List<ArgumentType> ArgumentTypes { get; set; } = [];
    public bool OptionsOnly { get; set; }
    public NewTagCode TagCode { get; set; }
    public int MaxOccurrences { get; set; } = -1; // -1 = unlimited
    public int Occurrences { get; set; }
}

public sealed class CompilerError
{
    public int LineNumber { get; set; }
    public bool IsFatal { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceLine { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
}

public sealed class CompilerGlobals
{
    public string ScriptName { get; set; } = string.Empty;
    public int CurrentLineNumber { get; set; }
    public string LanguageName { get; set; } = string.Empty;
    public string LanguageText { get; set; } = string.Empty;
    public int LoadingLastTime { get; set; }
    public List<CompilerError> Errors { get; set; } = [];
    public int ErrorIndex { get; set; }
    public bool CompileOnly { get; set; }
    public bool NoWait { get; set; }
    public bool EnableLog { get; set; }
    public bool ConciseLog { get; set; }
    public bool NoMessageBox { get; set; }
    public string OldTRLEFolder { get; set; } = string.Empty;
    public bool IsInclude { get; set; }
    public bool IsIncludeScan { get; set; }
    public string CurrentSourceLine { get; set; } = string.Empty;
    public string CurrentSourceFile { get; set; } = string.Empty;
    public string LogOutput { get; set; } = string.Empty;
    public List<DefineRecord> Defines { get; set; } = [];
    public List<NewTag> NewTags { get; set; } = [];
    public bool Verbose { get; set; }
}
