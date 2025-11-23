using System.Text;
using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Parsers;

public sealed class LanguageParser
{
    private readonly LanguageData _languageData = new();

    /// <summary>
    /// Parses a language.txt file.
    /// </summary>
    public bool ParseLanguageFile(string filePath, out LanguageData? languageData)
    {
        languageData = null;

        if (!File.Exists(filePath))
        {
            Logger.LogError($"Cannot find language file: {Path.GetFileName(filePath)}");
            return false;
        }

        Logger.LogVerbose($"Parsing {Path.GetFileName(filePath)}");

        _languageData.LanguageFile = filePath;
        _languageData.TotalPCStrings = 0;
        _languageData.TotalPSXStrings = 0;
        _languageData.TotalStrings = 0;
        _languageData.TotalAllStrings = 0;
        _languageData.TotalSpecials = 0;
        _languageData.TotalNGExtra = 0;
        _languageData.UseCCodes = true;

        var lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1252));
        int currentSection = -1;
        int sectionSize = 0;
        bool foundExtraNG = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string normalizedLine = StringUtilities.ConvertActualToCStyle(line);

            // Check for section headers
            if (normalizedLine.StartsWith('[') && normalizedLine.EndsWith(']'))
            {
                // Save previous section size
                if (currentSection >= 0)
                {
                    _languageData.SectionSizes[currentSection] = sectionSize;
                    _languageData.TotalSectionSizes++;
                }

                // Start new section
                string sectionName = normalizedLine.Trim('[', ']').ToLower();

                if (sectionName == "strings")
                {
                    currentSection = 0;
                    sectionSize = 0;
                }
                else if (sectionName is "psxstrings" or "psx strings")
                {
                    currentSection = 1;
                    sectionSize = 0;
                }
                else if (sectionName is "pcstrings" or "pc strings")
                {
                    currentSection = 2;
                    sectionSize = 0;
                }
                else if (sectionName is "extrang" or "extra_ng")
                {
                    foundExtraNG = true;
                    currentSection = -1;
                }

                continue;
            }

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(normalizedLine) || normalizedLine.TrimStart().StartsWith(';'))
                continue;

            // Process string entries
            if (foundExtraNG)
            {
                // Parse extra NG string: index: string
                int colonIndex = normalizedLine.IndexOf(':');

                if (colonIndex > 0 && int.TryParse(normalizedLine[..colonIndex].Trim(), out int index))
                {
                    string text = normalizedLine[(colonIndex + 1)..].Trim();
                    StringUtilities.ConvertCStyleToActual(ref text, out _, true);

                    _languageData.ExtraStrings.Add(new ExtraNGString
                    {
                        Index = index,
                        Text = text
                    });

                    _languageData.TotalNGExtra++;
                }
            }
            else if (currentSection >= 0)
            {
                // Regular string entry
                string text = normalizedLine;
                StringUtilities.ConvertCStyleToActual(ref text, out _);

                _languageData.Strings.Add(text);

                // Section size is in bytes (length of string + null terminator)
                sectionSize += Encoding.GetEncoding(1252).GetByteCount(text) + 1;

                if (currentSection == 0)
                    _languageData.TotalStrings++;
                else if (currentSection == 1)
                    _languageData.TotalPSXStrings++;
                else if (currentSection == 2)
                    _languageData.TotalPCStrings++;
            }
        }

        // Save last section size
        if (currentSection >= 0)
        {
            _languageData.SectionSizes[currentSection] = sectionSize;
            _languageData.TotalSectionSizes++;
        }

        _languageData.TotalAllStrings = _languageData.Strings.Count;

        // Calculate offsets
        int offset = 0;

        for (int i = 0; i < _languageData.TotalAllStrings; i++)
        {
            _languageData.Offsets.Add(offset);
            offset += Encoding.GetEncoding(1252).GetByteCount(_languageData.Strings[i]) + 1;
        }

        Logger.LogVerbose($"\tFound {_languageData.TotalAllStrings} standard strings");
        Logger.LogVerbose($"\tFound {_languageData.TotalNGExtra} extra NG strings");

        languageData = _languageData;
        return true;
    }

    /// <summary>
    /// Finds the index of a string in the language data.
    /// </summary>
    public static int GetStringIndex(LanguageData languageData, string text)
    {
        for (int i = 0; i < languageData.Strings.Count; i++)
        {
            if (string.Equals(languageData.Strings[i], text, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
