using System.Text;
using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Writers;

public class LanguageWriter
{
    private readonly CompilerGlobals _globals;
    
    public LanguageWriter(CompilerGlobals globals)
    {
        _globals = globals;
    }
    
    /// <summary>
    /// Writes a language.dat binary file
    /// </summary>
    public bool WriteLanguageDat(string outputPath, LanguageData languageData)
    {
        try
        {
            Logger.LogVerbose($"Writing {Path.GetFileName(outputPath)}...");
            
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fileStream, Encoding.GetEncoding(1252));
            
            // Write header
            writer.Write((short)languageData.TotalStrings);
            writer.Write((short)languageData.TotalPSXStrings);
            writer.Write((short)languageData.TotalPCStrings);
            
            // Write section sizes
            for (int i = 0; i < languageData.TotalSectionSizes; i++)
            {
                writer.Write((short)languageData.SectionSizes[i]);
            }
            
            // Write string offsets
            for (int i = 0; i < languageData.TotalAllStrings; i++)
            {
                writer.Write((short)languageData.Offsets[i]);
            }
            
            // Write strings (encrypted)
            for (int i = 0; i < languageData.TotalAllStrings; i++)
            {
                string text = languageData.Strings[i];
                
                // Convert C-style escapes to actual characters BEFORE encryption
                // This matches VB6: Call StringaC_To_Abs(VetStringhe(i), TagSpeciale, False, TestAnalisi)
                string dummy = string.Empty;
                StringUtilities.ConvertCStyleToActual(ref text, out dummy, false);
                
                byte[] encryptedBytes = StringUtilities.EncryptLanguageString(text);
                writer.Write(encryptedBytes);
                writer.Write((byte)0); // Null terminator
            }
            
            // Write extra NG strings if any
            if (languageData.TotalNGExtra > 0)
            {
                Logger.LogVerbose($"\tWriting extra NG header ({languageData.TotalNGExtra} strings)");
                
                // Build extra NG header
                var extraWords = new List<short>();
                
                // Add count of extra strings
                extraWords.Add((short)languageData.TotalNGExtra);
                
                foreach (var extraString in languageData.ExtraStrings)
                {
                    // Add index
                    extraWords.Add((short)extraString.Index);
                    
                    // Convert text to words
                    string text = extraString.Text;
                    
                    // Handle file references (starting with @)
                    if (text.StartsWith("@"))
                    {
                        string fileName = text.Substring(1);
                        string scriptDir = Path.GetDirectoryName(outputPath) ?? "";
                        string fullPath = Path.Combine(scriptDir, fileName);
                        
                        if (File.Exists(fullPath))
                        {
                            text = File.ReadAllText(fullPath, Encoding.GetEncoding(1252));
                            text = text.Replace("\r\n", "\n");
                        }
                    }
                    
                    // Convert string to C-style
                    string dummy = string.Empty;
                    StringUtilities.ConvertCStyleToActual(ref text, out dummy, true);
                    
                    // Convert to words
                    var words = ConvertStringToWords(text, true);
                    
                    // Add word count
                    extraWords.Add((short)words.Count);
                    
                    // Add words
                    extraWords.AddRange(words);
                }
                
                // Write NG header
                WriteNGHeader(writer, extraWords);
            }
            
            Logger.Log($"Created: {Path.GetFileName(outputPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error writing language.dat: {ex.Message}");
            return false;
        }
    }
    
    private List<short> ConvertStringToWords(string text, bool encrypt)
    {
        var words = new List<short>();
        
        // VB6 Logic:
        // If ((Len(Testo) Mod 2) = 0) Then
        //     Testo = Testo & Chr(0)
        // End If
        // Testo = Testo & Chr(0)
        
        // In C#, we work with bytes.
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);
        var byteList = new List<byte>(bytes);
        
        if (byteList.Count % 2 == 0)
        {
            byteList.Add(0);
        }
        byteList.Add(0);
        
        bytes = byteList.ToArray();
        
        // Convert bytes to words and encrypt
        for (int i = 0; i < bytes.Length; i += 2)
        {
            byte b1 = bytes[i];
            byte b2 = bytes[i + 1];
            
            if (encrypt)
            {
                if (b1 != 0) b1 = (byte)(b1 ^ 0xA5);
                if (b2 != 0) b2 = (byte)(b2 ^ 0xA5);
            }
            
            short word = (short)(b1 | (b2 << 8));
            words.Add(word);
        }
        
        return words;
    }
    
    private void WriteNGHeader(BinaryWriter baseWriter, List<short> words)
    {
        // Prepare the chunk first
        using var chunkStream = new MemoryStream();
        using var chunkWriter = new BinaryWriter(chunkStream);
        
        // Calculate chunk size (words count + 2 for size and tag)
        int nWords = words.Count + 2;
        if (nWords > 0x7FFF) nWords++;
        
        // Write Size
        if (nWords > 0x7FFF)
        {
            short high = (short)((nWords >> 16) | 0x8000);
            short low = (short)(nWords & 0xFFFF);
            chunkWriter.Write(high);
            chunkWriter.Write(low);
        }
        else
        {
            chunkWriter.Write((short)nWords);
        }
        
        // Write Tag
        const short NGTAG_LANGUAGE_STRINGS = unchecked((short)0x800A);
        chunkWriter.Write(NGTAG_LANGUAGE_STRINGS);
        
        // Write Data
        foreach (short word in words)
        {
            chunkWriter.Write(word);
        }
        
        byte[] chunkBytes = chunkStream.ToArray();
        
        // Now write the full NG Header to baseWriter
        
        // 1. "NG" Marker
        baseWriter.Write((short)0x474E);
        
        // 2. Chunk
        baseWriter.Write(chunkBytes);
        
        // 3. Two zero words
        baseWriter.Write((short)0);
        baseWriter.Write((short)0);
        
        // 4. EndHeader
        // EndCheck = &H454C474E ("NGLE")
        // SizeNGHeader = TotWords * 2 + Len(EndHeader)
        // TotWords here includes: "NG" marker (1), Chunk words, Zero words (2).
        
        int chunkWordCount = chunkBytes.Length / 2;
        int totalWords = 1 + chunkWordCount + 2;
        int sizeNGHeader = totalWords * 2 + 8; // 8 bytes for EndHeader struct
        
        baseWriter.Write((int)0x454C474E); // EndCheck
        baseWriter.Write((int)sizeNGHeader); // SizeNGHeader
    }
}
