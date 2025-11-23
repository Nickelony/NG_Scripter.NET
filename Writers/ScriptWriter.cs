using System.Text;
using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Writers;

public class ScriptWriter
{
    private readonly CompilerGlobals _globals;
    private LanguageData? _languageData;
    
    public ScriptWriter(CompilerGlobals globals)
    {
        _globals = globals;
    }
    
    public void SetLanguageData(LanguageData languageData)
    {
        _languageData = languageData;
    }
    
    /// <summary>
    /// Writes a script.dat binary file
    /// </summary>
    public bool WriteScriptDat(string outputPath, ScriptData scriptData)
    {
        if (_languageData is null)
        {
            Logger.LogError("Language data not set. Call SetLanguageData() first.");
            return false;
        }
        
        try
        {
            Logger.LogVerbose("Writing script.dat header...");
            
            // First compile all sections to binary
            if (!CompileSections(scriptData))
            {
                return false;
            }
            
            // Write classic script.dat in a separate scope to ensure file is closed
            {
                using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                using var writer = new BinaryWriter(fileStream, Encoding.GetEncoding(1252));
                
                // Write options
                writer.Write((int)scriptData.Options.Flags);
                writer.Write(scriptData.Options.InputTimeOut);
                writer.Write((byte)scriptData.Options.Security);
                
                // Build level paths from sections
                var levelPaths = scriptData.Sections
                    .Where(s => !string.IsNullOrEmpty(s.LevelPath))
                    .Select(s => s.LevelPath)
                    .ToList();
                
                // Write section count and level count
                writer.Write((byte)scriptData.Sections.Count);
                writer.Write((short)levelPaths.Count);
                
                Logger.LogVerbose($"\tNumber of Levels: {levelPaths.Count}");
                
                // Calculate total size of level paths
                int levelNamesSize = levelPaths.Sum(path => 
                    Encoding.GetEncoding(1252).GetByteCount(path) + 1);
                writer.Write((short)levelNamesSize);
                
                // Calculate total size of sections
                int sectionsSize = scriptData.Sections.Sum(s => s.CompiledBytes.Count);
                writer.Write((short)sectionsSize);
                
                // Write extensions
                WriteExtensions(writer, scriptData.PSXExtensions);
                WriteExtensions(writer, scriptData.PCExtensions);
                
                // Write level name offsets
                var levelOffsets = new List<short>();
                int offset = 0;
                foreach (var levelPath in levelPaths)
                {
                    levelOffsets.Add((short)offset);
                    offset += Encoding.GetEncoding(1252).GetByteCount(levelPath) + 1;
                }
                foreach (var levelOffset in levelOffsets)
                {
                    writer.Write(levelOffset);
                }
                
                // Write level paths
                foreach (var levelPath in levelPaths)
                {
                    Logger.LogVerbose($"\tLevel: \"{levelPath}\"");
                    BinaryUtilities.WriteNullTerminatedString(writer, levelPath);
                }
                
                // Write section offsets
                var sectionOffsets = new List<short>();
                offset = 0;
                foreach (var section in scriptData.Sections)
                {
                    sectionOffsets.Add((short)offset);
                    offset += section.CompiledBytes.Count;
                }
                foreach (var sectionOffset in sectionOffsets)
                {
                    writer.Write(sectionOffset);
                }
                
                // Write sections
                foreach (var section in scriptData.Sections)
                {
                    if (section.CompiledBytes.Count > 0)
                    {
                        writer.Write(section.CompiledBytes.ToArray());
                    }
                }
                
                // Write language file names
                foreach (var langFile in scriptData.LanguageFiles)
                {
                    string datName = Path.ChangeExtension(langFile, ".DAT");
                    BinaryUtilities.WriteNullTerminatedString(writer, datName);
                }
                
                Logger.Log($"Created: {Path.GetFileName(outputPath)}");
            } // File is closed here
            
            // Always append NG header for TRNG compatibility, even if no NG commands are used
            // VB6 compiler always writes it (with security header and flags)
            var ngWriter = new NGWriter(_globals);
            if (!ngWriter.WriteNGHeader(outputPath, scriptData))
            {
                return false;
            }
            
            // Encrypt script.dat if enabled
            if (scriptData.NGData.EnableScriptEncryption)
            {
                ngWriter.EncryptScriptDat(outputPath);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error writing script.dat: {ex.Message}");
            return false;
        }
    }
    
    private bool HasNGCommands(ScriptData scriptData)
    {
        return scriptData.NGData.OptionsCommands.Commands.Count > 0 ||
               scriptData.NGData.LevelCommands.Any(l => l.Commands.Count > 0) ||
               scriptData.NGData.ImportFiles.Count > 0;
    }
    
    private void WriteExtensions(BinaryWriter writer, List<string> extensions)
    {
        // Match VB6 logic: concatenate all extensions with null separator, then pad to 20 bytes
        var buffer = new List<byte>();
        
        // Add extensions (up to 4 supported by VB6 logic, though loop goes to TotPsxExtension-1)
        // VB6: Testo = Testo & .VetPsxExtension(i) & Chr(0)
        foreach (var ext in extensions)
        {
            if (!string.IsNullOrEmpty(ext))
            {
                buffer.AddRange(Encoding.GetEncoding(1252).GetBytes(ext));
                buffer.Add(0);
            }
        }
        
        // Pad with zeros up to 20 bytes
        // VB6: For i = Len(Testo) To 19 ... Testo = Testo & Chr(0)
        while (buffer.Count < 20)
        {
            buffer.Add(0);
        }
        
        // If we somehow exceeded 20 bytes (too many long extensions), truncate
        if (buffer.Count > 20)
        {
            Logger.LogWarning("Extensions list too long, truncating to 20 bytes");
            writer.Write(buffer.Take(20).ToArray());
        }
        else
        {
            writer.Write(buffer.ToArray());
        }
    }
    
    private bool CompileSections(ScriptData scriptData)
    {
        Logger.LogVerbose("Compiling script sections...");
        
        foreach (var section in scriptData.Sections)
        {
            if (section.SectionType == ScriptSectionType.Level || 
                section.SectionType == ScriptSectionType.Title)
            {
                if (!CompileSection(scriptData, section))
                {
                    return false;
                }
            }
        }
        
        return true;
    }
    
    private bool CompileSection(ScriptData scriptData, ScriptSection section)
    {
        // Sort lines according to original script.exe order
        var tagOrder = new[] {
            "Name=", "Key=", "KeyCombo=", "Puzzle=", "PuzzleCombo=", "Pickup=", 
            "PickupCombo=", "Examine=", "Fmv=", "UVRotate=", "Layer1=", "Layer2=", 
            "LensFlare=", "Fog=", "Mirror=", "AnimatingMIP=", "Legend=", "YoungLara=", 
            "ResidentCut=", "Cut=", "Horizon=", "StarField=", "Lightning=", "Train=", 
            "Pulse=", "ColAddHorizon=", "Timer=", "RemoveAmulet=", "LoadCamera=", 
            "ResetHub=", "NoLevel=", "Level="
        };
        
        // Calculate order values for sorting
        for (int i = 0; i < section.Lines.Count; i++)
        {
            var (command, arguments) = StringUtilities.ParseCommandLine(section.Lines[i]);
            int orderValue = Array.IndexOf(tagOrder, command);
            
            if (orderValue < 0)
            {
                orderValue = 9999;
            }
            else
            {
                orderValue *= 1000;

                // Match VB6 sorting logic for inventory items
                // Indices 1-7 are: Key, KeyCombo, Puzzle, PuzzleCombo, Pickup, PickupCombo, Examine
                if (orderValue >= 1000 && orderValue <= 7000)
                {
                    // Add item number * 10
                    if (arguments.Count > 0)
                    {
                        int itemNumber = ParseNumericArgument(arguments[0]);
                        if (itemNumber >= 0)
                        {
                            orderValue += itemNumber * 10;
                        }
                    }
                    
                    // If combo, add piece number
                    if (command.Contains("Combo") && arguments.Count > 1)
                    {
                        int pieceNumber = ParseNumericArgument(arguments[1]);
                        if (pieceNumber >= 0)
                        {
                            orderValue += pieceNumber;
                        }
                    }
                }
            }
            
            section.LineOrder.Add(orderValue);
        }
        
        // Sort lines by order
        var sortedIndices = Enumerable.Range(0, section.Lines.Count)
            .OrderBy(i => section.LineOrder[i])
            .ToList();
        
        // Compile each command
        foreach (int index in sortedIndices)
        {
            string line = section.Lines[index];
            var (command, arguments) = StringUtilities.ParseCommandLine(line);
            
            if (!CompileCommand(scriptData, section, command, arguments))
            {
                return false;
            }
        }
        
        // Validate LoadCamera is present for levels
        if (section.SectionType == ScriptSectionType.Level)
        {
            bool hasLoadCamera = section.Lines.Any(line => 
                StringUtilities.ParseCommandLine(line).command == "LoadCamera=");
            
            if (!hasLoadCamera)
            {
                Logger.LogError($"ERROR: LoadCamera= command is required in level section '{section.LevelName}'");
                return false;
            }
        }
        
        // Add final Level= or Title= tag (BEFORE terminator!)
        if (!string.IsNullOrEmpty(section.LevelName) || section.SectionType == ScriptSectionType.Title)
        {
            byte tag = (byte)(section.SectionType == ScriptSectionType.Title ? 0x82 : 0x81);
            section.CompiledBytes.Add(tag);
            
            if (section.SectionType == ScriptSectionType.Level)
            {
                // Check if Name= command was present
                if (string.IsNullOrEmpty(section.LevelName))
                {
                    Logger.LogError("ERROR: missing 'Name=' command in [Level] section");
                    return false;
                }
                
                // Add level name index
                int nameIndex = GetStringIndex(section.LevelName);
                if (nameIndex < 0)
                {
                    Logger.LogError($"Cannot find level name in language file: {section.LevelName}");
                    return false;
                }
                section.CompiledBytes.Add((byte)nameIndex);
            }
            
            // Add flags
            BinaryUtilities.AddWord(section.CompiledBytes, (short)section.Flags);
            
            // Add level index
            int levelIndex = scriptData.Sections.IndexOf(section);
            section.CompiledBytes.Add((byte)levelIndex);
            
            // Add CD number
            section.CompiledBytes.Add((byte)section.CD);
        }
        
        // Add terminating tag 0x83 (AFTER the section tag!)
        section.CompiledBytes.Add(0x83);
        
        return true;
    }

    
    private bool CompileCommand(ScriptData scriptData, ScriptSection section, string command, List<string> arguments)
    {
        try
        {
            switch (command)
            {
                case "Name=":
                    // Name is handled separately
                    return true;
                    
                case "FMV=":
                    return CompileFMV(section, arguments);
                    
                case "Level=":
                    return CompileLevel(scriptData, section, arguments);
                    
                case "Cut=":
                    return CompileCut(section, arguments);
                    
                case "ResidentCut=":
                    return CompileResidentCut(section, arguments);
                    
                case "Layer1=":
                case "Layer2=":
                    return CompileLayer(section, command, arguments);
                    
                case "UVRotate=":
                    return CompileUVRotate(section, arguments);
                    
                case "Legend=":
                    return CompileLegend(section, arguments);
                    
                case "LensFlare=":
                    return CompileLensFlare(section, arguments);
                    
                case "Mirror=":
                    return CompileMirror(section, arguments);
                    
                case "Fog=":
                    return CompileFog(section, arguments);
                    
                case "AnimatingMIP=":
                    return CompileAnimatingMIP(section, arguments);
                    
                case "LoadCamera=":
                    return CompileLoadCamera(section, arguments);
                    
                case "ResetHUB=":
                    return CompileResetHub(section, arguments);
                    
                case "Key=":
                case "Puzzle=":
                case "Pickup=":
                case "Examine=":
                    return CompileInventoryItem(section, command, arguments);
                    
                case "KeyCombo=":
                case "PuzzleCombo=":
                case "PickupCombo=":
                    return CompileInventoryCombo(section, command, arguments);
                    
                case "YoungLara=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.YoungLara;
                    return true;
                    
                case "Horizon=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.Horizon;
                    return true;
                    
                case "Starfield=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.StarField;
                    return true;
                    
                case "Lightning=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.Lightning;
                    return true;
                    
                case "Train=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.Train;
                    return true;
                    
                case "Pulse=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.Pulse;
                    return true;
                    
                case "Timer=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.Timer;
                    return true;
                    
                case "RemoveAmulet=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.RemoveAmulet;
                    return true;
                    
                case "NoLevel=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.NoLevel;
                    return true;
                    
                case "ColAddHorizon=":
                    if (IsEnabled(arguments)) section.Flags |= ScriptLevelFlags.ColAddHorizon;
                    return true;
                    
                default:
                    Logger.LogWarning($"Unhandled command in compilation: {command}");
                    return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error compiling command {command}: {ex.Message}");
            return false;
        }
    }
    
    private bool CompileFMV(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 2)
        {
            Logger.LogError("Wrong number of arguments for FMV command");
            return false;
        }
        
        if (!int.TryParse(arguments[0], out int index) || !int.TryParse(arguments[1], out int trigger))
        {
            Logger.LogError("Invalid arguments for FMV command");
            return false;
        }
        
        if (trigger == 1)
            index |= 128;
        
        section.CompiledBytes.Add(0x80); // Tag
        section.CompiledBytes.Add((byte)index);
        return true;
    }
    
    private bool CompileLevel(ScriptData scriptData, ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 2)
        {
            Logger.LogError("Wrong number of arguments for Level command");
            return false;
        }
        
        // Level= command provides the file path, not the display name
        // LevelName should already be set from the Name= command
        section.LevelPath = arguments[0];
        
        if (!int.TryParse(arguments[1], out int cd) || cd < 0 || cd > 255)
        {
            Logger.LogError("CD value out of range (0-255) for Level command");
            return false;
        }
        
        section.CD = cd;
        
        // Add level path to global list if not there
        if (!scriptData.LevelNames.Contains(arguments[0]))
        {
            scriptData.LevelNames.Add(arguments[0]);
        }
        
        return true;
    }
    
    private bool CompileCut(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 1)
        {
            Logger.LogError("Wrong number of arguments for Cut command");
            return false;
        }
        
        if (!int.TryParse(arguments[0], out int cutNumber) || cutNumber < 0 || cutNumber > 31)
        {
            Logger.LogError("Cut value out of range (0-31)");
            return false;
        }
        
        section.CompiledBytes.Add(0x84);
        section.CompiledBytes.Add((byte)cutNumber);
        return true;
    }
    
    private bool CompileResidentCut(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 2)
        {
            Logger.LogError("Wrong number of arguments for ResidentCut command");
            return false;
        }
        
        if (!int.TryParse(arguments[0], out int slot) || slot < 1 || slot > 4)
        {
            Logger.LogError("Slot value out of range (1-4) for ResidentCut");
            return false;
        }
        
        if (!int.TryParse(arguments[1], out int cutNumber))
        {
            Logger.LogError("Invalid cut number for ResidentCut");
            return false;
        }
        
        byte tag = (byte)(0x85 + (slot - 1));
        section.CompiledBytes.Add(tag);
        section.CompiledBytes.Add((byte)cutNumber);
        return true;
    }
    
    private bool CompileLayer(ScriptSection section, string command, List<string> arguments)
    {
        if (arguments.Count != 4)
        {
            Logger.LogError($"Wrong number of arguments for {command} command");
            return false;
        }
        
        byte tag = (byte)(command == "Layer1=" ? 0x89 : 0x8A);
        section.CompiledBytes.Add(tag);
        
        if (command == "Layer1=")
            section.Flags |= ScriptLevelFlags.Layer1;
        else
            section.Flags |= ScriptLevelFlags.Layer2;
        
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(arguments[i], out int value))
            {
                Logger.LogError($"Invalid argument {i} for {command}");
                return false;
            }
            section.CompiledBytes.Add((byte)value);
        }
        
        return true;
    }
    
    private bool CompileUVRotate(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 1)
        {
            Logger.LogError("Wrong number of arguments for UVRotate command");
            return false;
        }
        
        if (!int.TryParse(arguments[0], out int value))
        {
            Logger.LogError("Invalid argument for UVRotate");
            return false;
        }
        
        section.CompiledBytes.Add(0x8B);
        section.CompiledBytes.Add((byte)value);
        return true;
    }
    
    private bool CompileLegend(ScriptSection section, List<string> arguments)
    {
        // Legend takes the entire rest of line as text
        // Rejoin with comma+space since ParseCommandLine trims each argument
        if (arguments.Count == 0)
        {
            Logger.LogError("Missing text for Legend command");
            return false;
        }
        
        string text = string.Join(", ", arguments);
        int index = GetStringIndex(text);
        
        if (index < 0)
        {
            Logger.LogError($"Cannot find legend text in language file: {text}");
            return false;
        }
        
        section.CompiledBytes.Add(0x8C);
        section.CompiledBytes.Add((byte)index);
        return true;
    }
    
    private bool CompileLensFlare(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 6)
        {
            Logger.LogError("Wrong number of arguments for LensFlare command");
            return false;
        }
        
        section.CompiledBytes.Add(0x8D);
        section.Flags |= ScriptLevelFlags.LensFlare;
        
        // First 3 arguments are coordinates (divide by 256)
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(arguments[i], out int value))
            {
                Logger.LogError($"Invalid coordinate {i} for LensFlare");
                return false;
            }
            
            value /= 256;
            BinaryUtilities.AddWord(section.CompiledBytes, (short)value);
        }
        
        // Last 3 arguments are RGB colors
        for (int i = 3; i < 6; i++)
        {
            if (!int.TryParse(arguments[i], out int value))
            {
                Logger.LogError($"Invalid color {i} for LensFlare");
                return false;
            }
            section.CompiledBytes.Add((byte)value);
        }
        
        return true;
    }
    
    private bool CompileMirror(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 2)
        {
            Logger.LogError("Wrong number of arguments for Mirror command");
            return false;
        }
        
        section.CompiledBytes.Add(0x8E);
        section.Flags |= ScriptLevelFlags.Mirror;
        
        if (!int.TryParse(arguments[0], out int room))
        {
            Logger.LogError("Invalid room number for Mirror");
            return false;
        }
        section.CompiledBytes.Add((byte)room);
        
        int surface = ParseNumericArgument(arguments[1]);
        if (surface < 0)
        {
            Logger.LogError("Invalid surface value for Mirror");
            return false;
        }
        BinaryUtilities.AddDWord(section.CompiledBytes, surface);
        
        return true;
    }
    
    private bool CompileFog(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 3)
        {
            Logger.LogError("Wrong number of arguments for Fog command");
            return false;
        }
        
        section.CompiledBytes.Add(0x8F);
        
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(arguments[i], out int value))
            {
                Logger.LogError($"Invalid color {i} for Fog");
                return false;
            }
            section.CompiledBytes.Add((byte)value);
        }
        
        return true;
    }
    
    private bool CompileAnimatingMIP(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 2)
        {
            Logger.LogError("Wrong number of arguments for AnimatingMIP command");
            return false;
        }
        
        if (!int.TryParse(arguments[0], out int slot) || slot < 1 || slot > 16)
        {
            Logger.LogError("First argument out of range (1-16) for AnimatingMIP");
            return false;
        }
        
        if (!int.TryParse(arguments[1], out int distance) || distance < 0 || distance > 15)
        {
            Logger.LogError("Second argument out of range (0-15) for AnimatingMIP");
            return false;
        }
        
        section.CompiledBytes.Add(0x90);
        
        int value = distance * 16 + (slot - 1);
        section.CompiledBytes.Add((byte)value);
        
        return true;
    }
    
    private bool CompileLoadCamera(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 7)
        {
            Logger.LogError("Wrong number of arguments for LoadCamera command");
            return false;
        }
        
        section.CompiledBytes.Add(0x91);
        
        // First 6 arguments are 32-bit integers
        for (int i = 0; i < 6; i++)
        {
            if (!int.TryParse(arguments[i], out int value))
            {
                Logger.LogError($"Invalid argument {i} for LoadCamera");
                return false;
            }
            BinaryUtilities.AddDWord(section.CompiledBytes, value);
        }
        
        // Last argument is a byte
        if (!int.TryParse(arguments[6], out int lastValue))
        {
            Logger.LogError("Invalid last argument for LoadCamera");
            return false;
        }
        section.CompiledBytes.Add((byte)lastValue);
        
        return true;
    }
    
    private bool CompileResetHub(ScriptSection section, List<string> arguments)
    {
        if (arguments.Count != 1)
        {
            Logger.LogError("Wrong number of arguments for ResetHUB command");
            return false;
        }
        
        if (!int.TryParse(arguments[0], out int value))
        {
            Logger.LogError("Invalid argument for ResetHUB");
            return false;
        }
        
        section.CompiledBytes.Add(0x92);
        section.Flags |= ScriptLevelFlags.ResetHub;
        section.CompiledBytes.Add((byte)value);
        
        return true;
    }
    
    private bool CompileInventoryItem(ScriptSection section, string command, List<string> arguments)
    {
        if (arguments.Count != 8)
        {
            Logger.LogError($"Wrong number of arguments for {command} command");
            return false;
        }
        
        // Determine tag and max count
        byte baseTag;
        int maxCount;
        
        switch (command)
        {
            case "Key=":
                baseTag = 0x93;
                maxCount = 12;
                break;
            case "Puzzle=":
                baseTag = 0x9F;
                maxCount = 12;
                break;
            case "Pickup=":
                baseTag = 0xAB;
                maxCount = 4;
                break;
            case "Examine=":
                baseTag = 0xAF;
                maxCount = 3;
                break;
            default:
                Logger.LogError($"Internal error: unknown inventory command {command}");
                return false;
        }
        
        if (!int.TryParse(arguments[0], out int itemNumber) || itemNumber < 1 || itemNumber > maxCount)
        {
            Logger.LogError($"Item number out of range (1-{maxCount}) for {command}");
            return false;
        }
        
        byte tag = (byte)(baseTag + itemNumber - 1);
        section.CompiledBytes.Add(tag);
        
        // Get string index for item name
        int stringIndex = GetStringIndex(arguments[1]);
        if (stringIndex < 0)
        {
            Logger.LogError($"Cannot find string '{arguments[1]}' in language file");
            return false;
        }
        BinaryUtilities.AddWord(section.CompiledBytes, (short)stringIndex);
        
        // Add 6 word arguments
        for (int i = 2; i < 8; i++)
        {
            int value = ParseNumericArgument(arguments[i]);
            if (value < 0)
            {
                Logger.LogError($"Invalid argument {i} for {command}");
                return false;
            }
            BinaryUtilities.AddWord(section.CompiledBytes, (short)value);
        }
        
        return true;
    }
    
    private bool CompileInventoryCombo(ScriptSection section, string command, List<string> arguments)
    {
        if (arguments.Count != 9)
        {
            Logger.LogError($"Wrong number of arguments for {command} command");
            return false;
        }
        
        // Determine tag and max count
        byte baseTag;
        int maxCount;
        
        switch (command)
        {
            case "KeyCombo=":
                baseTag = 0xB2;
                maxCount = 8;
                break;
            case "PuzzleCombo=":
                baseTag = 0xC2;
                maxCount = 8;
                break;
            case "PickupCombo=":
                baseTag = 0xD2;
                maxCount = 4;
                break;
            default:
                Logger.LogError($"Internal error: unknown combo command {command}");
                return false;
        }
        
        if (!int.TryParse(arguments[0], out int comboNumber) || comboNumber < 1 || comboNumber > maxCount)
        {
            Logger.LogError($"Combo number out of range (1-{maxCount}) for {command}");
            return false;
        }
        
        if (!int.TryParse(arguments[1], out int piece) || piece < 1 || piece > 2)
        {
            Logger.LogError($"Piece number out of range (1-2) for {command}");
            return false;
        }
        
        byte tag = (byte)(baseTag + (comboNumber - 1) * 2 + (piece - 1));
        section.CompiledBytes.Add(tag);
        
        // Get string index for item name
        int stringIndex = GetStringIndex(arguments[2]);
        if (stringIndex < 0)
        {
            Logger.LogError($"Cannot find string '{arguments[2]}' in language file");
            return false;
        }
        BinaryUtilities.AddWord(section.CompiledBytes, (short)stringIndex);
        
        // Add 6 word arguments
        for (int i = 3; i < 9; i++)
        {
            int value = ParseNumericArgument(arguments[i]);
            if (value < 0)
            {
                Logger.LogError($"Invalid argument {i} for {command}");
                return false;
            }
            BinaryUtilities.AddWord(section.CompiledBytes, (short)value);
        }
        
        return true;
    }
    
    private int ParseNumericArgument(string arg)
    {
        // Handle hexadecimal values starting with $
        if (arg.StartsWith("$"))
        {
            string hexValue = arg.Substring(1);
            if (int.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out int result))
            {
                return result;
            }
            return -1;
        }

        // Handle hexadecimal values starting with &H
        if (arg.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            string hexValue = arg.Substring(2);
            if (int.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out int result))
            {
                return result;
            }
            return -1;
        }
        
        // Handle regular decimal values
        if (int.TryParse(arg, out int value))
        {
            return value;
        }

        // Handle constants
        var define = _globals.Defines.FirstOrDefault(d => d.Name.Equals(arg, StringComparison.OrdinalIgnoreCase));
        if (define is not null)
        {
            return define.Value;
        }
        
        return -1;
    }
    
    private bool IsEnabled(List<string> arguments)
    {
        if (arguments.Count == 0)
            return false;
            
        return string.Equals(arguments[0], "ENABLED", StringComparison.OrdinalIgnoreCase);
    }
    
    private int GetStringIndex(string text)
    {
        // Handle immediate index (#123)
        if (text.StartsWith("#"))
        {
            if (int.TryParse(text.Substring(1), out int index))
            {
                return index;
            }
            Logger.LogError($"Invalid index format: {text}");
            return -1;
        }
        
        // Handle NG Extra String index (!123)
        if (text.StartsWith("!"))
        {
            if (int.TryParse(text.Substring(1), out int ngIndex))
            {
                // NG strings use 32768 + index (0x8000 bit set)
                return 32768 + ngIndex;
            }
            Logger.LogError($"Invalid NG index format: {text}");
            return -1;
        }
        
        // Handle binary string (&HEXVALUE)
        if (text.StartsWith("&"))
        {
            if (int.TryParse(text.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out int binValue))
            {
                return binValue;
            }
            Logger.LogError($"Invalid binary format: {text}");
            return -1;
        }
        
        if (_languageData is null)
        {
            Logger.LogError("Language data not loaded");
            return -1;
        }
        
        // Search in normal strings
        for (int i = 0; i < _languageData.Strings.Count; i++)
        {
            if (string.Equals(_languageData.Strings[i], text, StringComparison.Ordinal))
            {
                return i;
            }
        }
        
        // Search in NG Extra strings
        for (int i = 0; i < _languageData.ExtraStrings.Count; i++)
        {
            if (string.Equals(_languageData.ExtraStrings[i].Text, text, StringComparison.Ordinal))
            {
                return 32768 + _languageData.ExtraStrings[i].Index;
            }
        }
        
        Logger.LogWarning($"String not found in language file: {text}");
        return -1;
    }
}
