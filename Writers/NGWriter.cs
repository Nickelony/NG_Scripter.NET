using System.Text;
using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Writers;

/// <summary>
/// Writer for TRNG Next Generation header data.
/// Appends NG header to existing script.dat file.
/// </summary>
public sealed class NGWriter
{
    private readonly CompilerGlobals _globals;

    public NGWriter(CompilerGlobals globals)
        => _globals = globals;

    /// <summary>
    /// Append NG header to script.dat file.
    /// </summary>
    public bool WriteNGHeader(string scriptDatPath, ScriptData scriptData)
    {
        try
        {
            Logger.LogVerbose("Saving Header NG Data...");

            var ngData = scriptData.NGData;
            var allWords = new List<short>();

            // Reserve space for options section word count
            int firstSectionWordCountIndex = allWords.Count;
            allWords.Add(0);

            // Write [Options] section
            if (!WriteOptionsSection(allWords, ngData))
                return false;

            // Update options section word count (does NOT include security header)
            allWords[firstSectionWordCountIndex] = (short)(allWords.Count - firstSectionWordCountIndex);

            // Write security header (encrypted copy of options + settings)
            // This has its own separate word count
            WriteSecurityHeader(allWords, scriptData);

            // Write [Level] sections (each has its own word count)
            for (int i = 0; i < ngData.LevelCommands.Count; i++)
            {
                Logger.LogVerbose("");
                Logger.LogVerbose("========== START LEVEL PARSING ==========");
                Logger.LogVerbose($"[LEVEL] : Level {i}");

                if (!WriteLevelSection(allWords, ngData.LevelCommands[i], i))
                    return false;

                Logger.LogVerbose("========= END LEVEL PARSING =========");
            }

            // Write import files if any (each has its own word count)
            if (ngData.ImportFiles.Count > 0)
            {
                Logger.LogVerbose("");
                Logger.LogVerbose($"Files to import: {ngData.ImportFiles.Count}");
                Logger.LogVerbose("---------------------------------------------------------");

                foreach (var importFile in ngData.ImportFiles)
                {
                    if (!WriteImportFile(allWords, importFile))
                        return false;
                }
            }

            // Now append all words to the script.dat file
            Logger.LogVerbose("");
            Logger.LogVerbose($"Saving script.dat file in folder: {Path.GetDirectoryName(scriptDatPath)}");

            return AppendWordsToFile(scriptDatPath, allWords);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error writing NG header: {ex.Message}");
            return false;
        }
    }

    private bool WriteOptionsSection(List<short> allWords, NGScriptData ngData)
    {
        Logger.LogVerbose("[OPTIONS] Section ...");

        var optionsWords = new List<short>();

        // Add tag for options section
        allWords.Add(unchecked((short)NGTagType.ScriptOptions));

        // Compile all option commands
        foreach (var command in ngData.OptionsCommands.Commands)
        {
            Logger.LogVerboseIf(_globals.Verbose, $"\tScanning: {command.CommandName}");
            optionsWords.AddRange(command.CompiledWords);
        }

        // Add flags command (cnt_FlagsOption)
        const int flagsHeader = (200 * 256) + 1;  // cnt_FlagsOption * 256 + 1 word

        optionsWords.Add(unchecked((short)flagsHeader));
        optionsWords.Add((short)ngData.OptionsCommands.OptionFlags);

        // Add terminating 0
        optionsWords.Add(0);

        // Add all options words
        allWords.AddRange(optionsWords);

        return true;
    }

    private static void WriteSecurityHeader(List<short> allWords, ScriptData scriptData)
    {
        // Create NGTAG_CONTROLLO_OPTIONS section for anti-tampering
        // This section contains encrypted verification data

        var random = new Random();
        var ngData = scriptData.NGData;

        // XOR encryption table (matches VB6 VetCrypt array)
        byte[] xorTable = [27, 48, 38, 153, 58, 77, 42, 58, 66, 45, 55, 22, 55];

        // Random data size: 20-28 words (40-56 bytes)
        // VB6: TotDati = Casuale(9) + 20
        int totalWords = random.Next(20, 29);
        int totalBytes = totalWords * 2;
        byte[] dataBytes = new byte[totalBytes];

        // Fill with pseudo-random pattern
        for (int i = 0; i < totalBytes; i++)
        {
            if (i % 2 == 0)
            {
                // Even positions: 4 or 6
                // VB6: If (Casuale(2) = 1) Then 6 Else 4
                dataBytes[i] = (byte)(random.Next(2) == 1 ? 6 : 4);
            }
            else
            {
                // Odd positions: random value 0-15
                // VB6: Casuale(16) returns 0-15
                dataBytes[i] = (byte)random.Next(16);
            }
        }

        // Embed verification data at specific positions:
        // Index 5: number of levels (from main script data)
        // VB6: VetBytes(5) = BaseScriptDat.TotLivelli
        dataBytes[5] = (byte)scriptData.LevelNames.Count;

        // Index 12: copy of script options flags (from main script data)
        // VB6: VetBytes(12) = BaseScriptDat.Options.Flags
        dataBytes[12] = (byte)scriptData.Options.Flags;

        // Index 19: copy of NG settings
        // VB6: VetBytes(19) = BaseScriptDat.Options.NG_Settings
        dataBytes[19] = (byte)ngData.NGSettings;

        // Calculate checksum (sum of all bytes except first)
        int checksum = 0;

        for (int i = 1; i < totalBytes; i++)
            checksum += dataBytes[i];

        dataBytes[0] = (byte)(checksum & 0xFF);

        // XOR encrypt all bytes except the first (checksum)
        int xorIndex = 0;

        for (int i = 1; i < totalBytes; i++)
        {
            if (xorIndex == xorTable.Length)
                xorIndex = 0;

            dataBytes[i] ^= xorTable[xorIndex];
            xorIndex++;
        }

        // Write to output: NWords (total words including tag + data), then tag
        const int securityTag = 32790; // NGTAG_CONTROLLO_OPTIONS = 0x8016 = 32790
        int nWords = totalWords + 2; // Total words = wordCount word + tag word + data words

        allWords.Add((short)nWords);
        allWords.Add(unchecked((short)securityTag));

        // Convert bytes to words (little-endian pairs)
        for (int i = 0; i < totalBytes; i += 2)
        {
            short word = (short)(dataBytes[i] | (dataBytes[i + 1] << 8));
            allWords.Add(word);
        }
    }

    private bool WriteLevelSection(List<short> allWords, NGCommandGroup levelCommands, int levelIndex)
    {
        var levelWords = new List<short>();
        int wordCountIndex = allWords.Count;

        // Reserve space for word count
        allWords.Add(0);

        // Add tag for level section
        allWords.Add(unchecked((short)NGTagType.ScriptLevel));

        // Compile all level commands
        foreach (var command in levelCommands.Commands)
        {
            Logger.LogVerboseIf(_globals.Verbose, $"\tScanning: {command.CommandName}");
            levelWords.AddRange(command.CompiledWords);
        }

        // Add flags command (cnt_FlagsLevel)
        const int flagsHeader = (201 * 256) + 1;  // cnt_FlagsLevel * 256 + 1 word

        levelWords.Add(unchecked((short)flagsHeader));
        levelWords.Add((short)levelCommands.LevelFlags);

        // Add terminating 0
        levelWords.Add(0);

        // Check size limit
        if (levelWords.Count > 32767)
        {
            Logger.LogError($"ERROR: level section {levelIndex} too big. Unrecoverable error");
            return false;
        }

        // Add all level words
        allWords.AddRange(levelWords);

        // Update word count
        allWords[wordCountIndex] = (short)(allWords.Count - wordCountIndex);

        Logger.LogVerbose($"====== TOT WORD PER SCRIPT_LEVEL SECTION = {levelWords.Count}");

        return true;
    }

    private static bool WriteImportFile(List<short> allWords, NGImportFile importFile)
    {
        // Calculate total words for this chunk
        // VB6: TotWords = 49 + Numero / 2 (where Numero is file size rounded up to even)
        // 49 words = 2 (Size) + 1 (Tag) + 1 (Id) + 1 (TypeImport) + 1 (TypeFile) + 1 (NumeroId) + 40 (Name) + 2 (FileSize)
        int fileSize = importFile.Data.Length;
        int dataWords = (fileSize + 1) / 2;
        int totalChunkWords = 49 + dataWords;

        // 1. Chunk Size (2 words: High|0x8000, Low)
        // VB6: AddDwordSizeInVet(TotWords, VetCommand, TotCommand)
        short sizeLow = (short)(totalChunkWords & 0xFFFF);
        short sizeHigh = (short)((totalChunkWords >> 16) & 0xFFFF);
        sizeHigh |= unchecked((short)0x8000); // Set bit 15 to indicate DWORD size

        allWords.Add(sizeHigh);
        allWords.Add(sizeLow);

        // 2. Tag
        allWords.Add(unchecked((short)NGTagType.ImportFile)); // 0x801F

        // 3. ID
        allWords.Add((short)importFile.ImportId);

        // 4. TypeImport
        allWords.Add((short)importFile.ImportMode);

        // 5. TypeFile
        allWords.Add((short)importFile.FileType);

        // 6. NumeroId (extracted from filename)
        int numeroId = GetFileNumber(importFile.FileName);
        allWords.Add((short)numeroId);

        // 7. Name (40 words = 80 bytes)
        // VB6 appends Chr(0) to name before converting
        string name = importFile.FileName + "\0";
        var nameWords = ConvertStringToWords(name, 40);
        allWords.AddRange(nameWords);

        // 8. File Size (2 words: Low, High)
        allWords.Add((short)(fileSize & 0xFFFF));
        allWords.Add((short)((fileSize >> 16) & 0xFFFF));

        // 9. Data
        for (int i = 0; i < fileSize; i += 2)
        {
            byte low = importFile.Data[i];
            byte high = (i + 1 < fileSize) ? importFile.Data[i + 1] : (byte)0;
            allWords.Add((short)(low | (high << 8)));
        }

        Logger.LogVerbose($"\tImport file: {importFile.FileName} ({fileSize} bytes)");

        return true;
    }

    private static int GetFileNumber(string fileName)
    {
        // Extract number at the end of filename (before extension)
        // Example: MyFile123.dat -> 123
        string name = Path.GetFileNameWithoutExtension(fileName);
        string numberStr = "";

        for (int i = name.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(name[i]))
                numberStr = name[i] + numberStr;
            else
                break;
        }

        if (string.IsNullOrEmpty(numberStr))
            return 0;

        return int.Parse(numberStr);
    }

    private static List<short> ConvertStringToWords(string text, int totalWords)
    {
        var words = new List<short>();
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);

        int totalBytes = totalWords * 2;
        byte[] paddedBytes = new byte[totalBytes];

        Array.Copy(bytes, paddedBytes, Math.Min(bytes.Length, totalBytes));

        for (int i = 0; i < totalBytes; i += 2)
        {
            short word = (short)(paddedBytes[i] | (paddedBytes[i + 1] << 8));
            words.Add(word);
        }

        return words;
    }

    private static bool AppendWordsToFile(string filePath, List<short> words)
    {
        try
        {
            // Append NG header signature and words to existing script.dat file
            using var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write);
            using var writer = new BinaryWriter(fileStream, Encoding.GetEncoding(1252));

            // Write NG header signature "NG" as a 16-bit word (0x474E in little-endian = "NG")
            // VB6: NumeroW = &H474E
            writer.Write((short)0x474E);

            // Track total words written (including NG signature = 1 word)
            int totalWords = 1;

            // Write all NG data words
            foreach (var word in words)
            {
                writer.Write(word);
                totalWords++;
            }

            // Write two terminating zero words
            writer.Write((short)0);
            writer.Write((short)0);

            totalWords += 2;

            // Write footer: "NGLE" signature (0x454C474E) + total size
            // EndHeader.EndCheck = &H454C474E
            writer.Write(0x454C474E);

            // EndHeader.SizeNGHeader = TotWords * 2 + Len(EndHeader)
            // Len(EndHeader) = 8 bytes (4 for signature + 4 for size)
            int ngHeaderSize = (totalWords * 2) + 8;
            writer.Write(ngHeaderSize);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error appending NG header to file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Encrypt script.dat file if encryption is enabled.
    /// </summary>
    public void EncryptScriptDat(string scriptDatPath)
    {
        try
        {
            // Read first 64 bytes
            var cryptBuffer = new byte[64];

            using (var fs = new FileStream(scriptDatPath, FileMode.Open, FileAccess.Read))
                fs.Read(cryptBuffer, 0, 64);

            // Scramble order
            var cryptOrder = new int[]
            {
                57, 49, 1, 7, 36, 37, 0, 17, 45, 13, 40, 44, 46,
                33, 30, 34, 20, 41, 26, 19, 59, 53, 43, 2, 22, 6, 23, 9, 31, 10,
                21, 15, 5, 8, 42, 24, 55, 14, 48, 56, 47, 60, 12, 39, 28, 32, 16,
                27, 52, 35, 62, 58, 63, 11, 18, 38, 4, 54, 50, 61, 51, 25, 29, 3
            };

            var scrambled = new byte[64];

            for (int i = 0; i < 64; i++)
                scrambled[i] = cryptBuffer[cryptOrder[i]];

            // XOR with key table
            var xorTable = new byte[]
            {
                239, 85, 225, 248, 61, 111, 214, 25, 218, 151,
                29, 139, 133, 15, 180, 10, 196
            };

            int xorIndex = 0;

            for (int i = 0; i < 64; i++)
            {
                if (xorIndex >= xorTable.Length)
                    xorIndex = 0;

                scrambled[i] ^= xorTable[xorIndex];
                xorIndex++;
            }

            // Write back encrypted bytes
            using (var fs = new FileStream(scriptDatPath, FileMode.Open, FileAccess.Write))
                fs.Write(scrambled, 0, 64);

            Logger.LogVerbose("Script.dat encrypted successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error encrypting script.dat: {ex.Message}");
        }
    }
}
