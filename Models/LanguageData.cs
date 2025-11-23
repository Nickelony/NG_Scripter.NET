namespace TRNGScriptCompiler.Models;

public sealed class ExtraNGString
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class LanguageData
{
    public const int MaxLanguageSections = 5;

    public int TotalStrings { get; set; }
    public int TotalPSXStrings { get; set; }
    public int TotalPCStrings { get; set; }
    public int TotalNGExtra { get; set; }

    public List<string> Strings { get; set; } = [];
    public List<ExtraNGString> ExtraStrings { get; set; } = [];

    public int[] SectionSizes { get; set; } = new int[MaxLanguageSections];
    public int[] SectionStartIndexes { get; set; } = new int[MaxLanguageSections];
    public int TotalSectionSizes { get; set; }

    public List<string> SpecialNames { get; set; } = [];
    public List<int> SpecialIndexes { get; set; } = [];

    public int TotalSpecials { get; set; }
    public int TotalAllStrings { get; set; }
    public List<int> Offsets { get; set; } = [];

    public bool UseCCodes { get; set; } = true;
    public string LanguageFile { get; set; } = string.Empty;
}
