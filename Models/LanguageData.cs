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

    public int TotalAllStrings => Strings.Count;
    public List<int> Offsets { get; set; } = [];

    public string LanguageFile { get; set; } = string.Empty;

    /// <summary>
    /// Finds the index of a string using ordinal comparison.
    /// Returns -1 if not found.
    /// </summary>
    public int FindStringIndex(string text)
    {
        for (int i = 0; i < Strings.Count; i++)
        {
            if (string.Equals(Strings[i], text, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Finds the index of an extra NG string using ordinal comparison.
    /// Returns -1 if not found. On success, returns 32768 + extraString.Index.
    /// </summary>
    public int FindExtraStringIndex(string text)
    {
        for (int i = 0; i < ExtraStrings.Count; i++)
        {
            if (string.Equals(ExtraStrings[i].Text, text, StringComparison.Ordinal))
                return 32768 + ExtraStrings[i].Index;
        }

        return -1;
    }
}
