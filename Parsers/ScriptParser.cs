using System.Reflection;
using System.Text;
using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Parsers;

public sealed class ScriptParser
{
    private readonly CompilerGlobals _globals;
    private readonly ScriptData _scriptData = new();
    private readonly NGParser _ngParser;
    private readonly PluginManager _pluginManager;
    private readonly ConstantsLoader _constantsLoader;
    private readonly ObjectsHParser _objectsHParser;
    private readonly DefineManager _defineManager;
    private readonly ExpressionEvaluator _expressionEvaluator;
    private bool _languageFileLoaded;

    private readonly List<string> _sectionNames =
    [
        "",
        "[PSXExtensions]",
        "[PCExtensions]",
        "[Language]",
        "[Options]",
        "[Title]",
        "[Level]"
    ];

    public ScriptParser(CompilerGlobals globals)
    {
        _globals = globals;
        _ngParser = new NGParser(globals);
        _pluginManager = new PluginManager(globals);
        _constantsLoader = new ConstantsLoader(globals);
        _objectsHParser = new ObjectsHParser();

        // Create expression evaluator with constant resolver
        _expressionEvaluator = new ExpressionEvaluator(ResolveConstantForExpression);
        _defineManager = new DefineManager(globals, _expressionEvaluator);

        // Pass components to NG parser
        _ngParser.SetExpressionEvaluator(_expressionEvaluator);
    }

    /// <summary>
    /// Parses the main SCRIPT.TXT file.
    /// </summary>
    public bool ParseScriptFile(string scriptPath, string trleFolderPath, out ScriptData? scriptData)
    {
        scriptData = null;

        if (!File.Exists(scriptPath))
        {
            ErrorHandler.AddError(_globals.Errors, -1, true,
                $"Cannot find script file: {scriptPath}");

            return false;
        }

        Logger.Log($"Parsing script file: {Path.GetFileName(scriptPath)}");

        // Load global constants from scripter_constants.txt
        // Look for it in the compiler's directory first, then in NG_Scripter directory
        string? constantsPath = null;
        var compilerDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (compilerDir is not null)
        {
            var localConstantsPath = Path.Combine(compilerDir, "scripter_constants.txt");

            if (File.Exists(localConstantsPath))
                constantsPath = localConstantsPath;
        }

        if (constantsPath is not null)
            _constantsLoader.LoadConstants(constantsPath);
        else
            Logger.LogWarning("scripter_constants.txt not found - global constants will not be available");

        // Load Objects.h for slot and static constants
        var objectsHPath = Path.Combine(trleFolderPath, "Objects.h");
        _objectsHParser.LoadObjectsH(objectsHPath);

        // Discover and load plugins from the TRLE root directory
        _pluginManager.DiscoverPlugins(trleFolderPath);

        foreach (var plugin in _pluginManager.GetDiscoveredPlugins())
            _pluginManager.LoadPluginConstants(plugin);

        // Initialize script data
        _scriptData.Options.Flags = ScriptMainFlags.None;
        _scriptData.Options.InputTimeOut = 0;
        _scriptData.Options.Security = 0;

        // Start parsing from the main script file
        _scriptData.IncludeStack.Add(new FileInclude
        {
            FileName = scriptPath,
            LineNumber = 0
        });

        _globals.CurrentSourceFile = scriptPath;

        var currentSection = ScriptSectionType.None;
        const bool ignoreTitle = false;
        int lineNumber = 0;

        // Process all files in the include stack
        while (_scriptData.IncludeStack.Count > 0)
        {
            var currentFile = _scriptData.IncludeStack[^1];
            var lines = File.ReadAllLines(currentFile.FileName, Encoding.GetEncoding(1252));

            Logger.LogVerbose($"------------ PARSING FILE <{Path.GetFileName(currentFile.FileName)}> ----------");

            for (int i = 0; i < lines.Length; i++)
            {
                lineNumber++;
                _globals.CurrentLineNumber = lineNumber;

                // Handle line continuation with '>' character
                string rawLine = lines[i];

                while (true)
                {
                    // Check if line ends with '>' (not in a comment)
                    int commentPos = rawLine.IndexOf(';');
                    int continuationPos = rawLine.IndexOf('>');

                    if (continuationPos >= 0 && (commentPos < 0 || continuationPos < commentPos))
                    {
                        // Remove the '>' and trim, then append next line
                        rawLine = rawLine[..continuationPos].Trim();

                        if (i + 1 < lines.Length)
                        {
                            i++;
                            lineNumber++;
                            rawLine += " " + lines[i].Trim();
                        }
                        else
                        {
                            break; // No more lines to continue with
                        }
                    }
                    else
                    {
                        break; // No continuation character
                    }
                }

                // Normalize the line
                string line = StringUtilities.NormalizeLine(rawLine);

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Process #define directives (can appear anywhere)
                if (line.StartsWith("#DEFINE", StringComparison.OrdinalIgnoreCase))
                {
                    _defineManager.ProcessDefineLine(line, lineNumber, rawLine);
                    continue;
                }

                // Check if it's a new section
                var newSection = GetSectionType(line);

                if (newSection != ScriptSectionType.None)
                {
                    // Load language file before [Options] section if not already loaded
                    if (newSection == ScriptSectionType.Options && !_languageFileLoaded)
                        LoadFirstLanguageFile(Path.GetDirectoryName(scriptPath) ?? string.Empty);

                    if (newSection is ScriptSectionType.Level or ScriptSectionType.Title)
                    {
                        // Reset command occurrence counts for new level
                        _ngParser.ResetOccurrenceCounts();

                        // Start new level/title section
                        if (newSection != ScriptSectionType.Title || !ignoreTitle)
                        {
                            _scriptData.Sections.Add(new ScriptSection
                            {
                                SectionType = newSection,
                                Flags = ScriptLevelFlags.None,
                                LevelName = string.Empty,
                                CD = 0
                            });
                        }

                        // Also add NG command group for this level
                        _scriptData.NGData.LevelCommands.Add(new NGCommandGroup());
                    }

                    currentSection = newSection;
                    continue;
                }

                // Process the command based on current section
                if (currentSection == ScriptSectionType.None)
                {
                    ErrorHandler.AddError(_globals.Errors, lineNumber, false,
                        $"Command out of context: {line}");

                    continue;
                }

                // Parse command and arguments
                var (command, arguments) = StringUtilities.ParseCommandLine(line);

                if (string.IsNullOrEmpty(command))
                {
                    ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                        "Syntax error: missing command. Cannot find '=' character");

                    return false;
                }

                // Handle different sections
                if (currentSection == ScriptSectionType.Options)
                {
                    if (!ProcessOptionsCommand(command, arguments, lineNumber))
                        return false;
                }
                else if (currentSection == ScriptSectionType.Language)
                {
                    if (!ProcessLanguageCommand(command, arguments, lineNumber))
                        return false;
                }
                else if (currentSection is ScriptSectionType.PCExtensions or ScriptSectionType.PSXExtensions)
                {
                    if (!ProcessExtensionCommand(currentSection, command, arguments, lineNumber))
                        return false;
                }
                else if (currentSection is ScriptSectionType.Level or ScriptSectionType.Title)
                {
                    if (currentSection != ScriptSectionType.Title || !ignoreTitle)
                    {
                        var section = _scriptData.Sections[^1];

                        // Check if this is an NG command
                        if (IsNGCommand(command))
                        {
                            // Parse as NG command and add to section's NG commands
                            var ngCommand = _ngParser.ParseNGCommand(command, arguments, lineNumber, false);

                            if (ngCommand is not null)
                                section.NGCommands.Commands.Add(ngCommand);
                        }
                        else
                        {
                            // Classic command - add to lines
                            section.Lines.Add(line);
                            section.LineNumbers.Add(lineNumber);
                            section.SourceFiles.Add(Path.GetFileName(currentFile.FileName));
                        }

                        // Extract level name if this is a Name= command
                        if (command == "Name=" && arguments.Count >= 1)
                            section.LevelName = arguments[0];

                        // Extract level path if this is a Level= command
                        if (command == "Level=" && arguments.Count >= 1)
                            section.LevelPath = arguments[0];
                    }
                }
            }

            // Remove current file from stack
            _scriptData.IncludeStack.RemoveAt(_scriptData.IncludeStack.Count - 1);

            Logger.LogVerbose($"------------ END FILE <{Path.GetFileName(currentFile.FileName)}> ----------");
        }

        // Collect NG level commands from all sections
        CollectNGLevelCommands();

        scriptData = _scriptData;
        return true;
    }

    private ScriptSectionType GetSectionType(string line)
    {
        for (int i = 1; i < _sectionNames.Count; i++)
        {
            if (string.Equals(line, _sectionNames[i], StringComparison.OrdinalIgnoreCase))
                return (ScriptSectionType)i;
        }

        return ScriptSectionType.None;
    }

    private bool ProcessOptionsCommand(string command, List<string> arguments, int lineNumber)
    {
        // Check if this is an NG command (not a classic command)
        if (IsNGCommand(command))
        {
            // Special handling for Plugin= command to register plugin IDs
            if (command.Equals("Plugin=", StringComparison.OrdinalIgnoreCase))
                RegisterPluginCommand(arguments, lineNumber);

            // Parse as NG command
            var ngCommand = _ngParser.ParseNGCommand(command, arguments, lineNumber, true);

            if (ngCommand is not null)
                _scriptData.NGData.OptionsCommands.Commands.Add(ngCommand);

            return true;
        }

        // Classic Options commands
        switch (command)
        {
            case "LoadSave=":
                if (IsEnabled(arguments))
                    _scriptData.Options.Flags |= ScriptMainFlags.LoadSave;
                break;

            case "Title=":
                if (IsEnabled(arguments))
                    _scriptData.Options.Flags |= ScriptMainFlags.Title;
                break;

            case "PlayAnyLevel=":
                if (IsEnabled(arguments))
                    _scriptData.Options.Flags |= ScriptMainFlags.PlayAnyLevel;
                break;

            case "FlyCheat=":
                if (IsEnabled(arguments))
                    _scriptData.Options.Flags |= ScriptMainFlags.FlyCheat;
                break;

            case "DemoDisc=":
                if (IsEnabled(arguments))
                    _scriptData.Options.Flags |= ScriptMainFlags.DemoDisk;
                break;

            case "InputTimeOut=":
                if (arguments.Count > 0 && int.TryParse(arguments[0], out int timeout))
                    _scriptData.Options.InputTimeOut = timeout;
                break;

            case "Security=":
                if (arguments.Count > 0 && int.TryParse(arguments[0], out int security))
                    _scriptData.Options.Security = security;
                break;

            default:
                ErrorHandler.AddError(_globals.Errors, lineNumber, false,
                    $"Unknown command for [Options] section: {command}");
                break;
        }

        return true;
    }

    private bool ProcessLanguageCommand(string command, List<string> arguments, int lineNumber)
    {
        if (command != "File=")
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "Unknown command for [Language] section");

            return false;
        }

        if (arguments.Count != 2)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "Wrong number of arguments for 'File=' command");

            return false;
        }

        if (!int.TryParse(arguments[0], out int index) || index != _scriptData.LanguageFiles.Count)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "Unexpected index for language file");

            return false;
        }

        if (!arguments[1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "Missing '.txt' extension for language file");

            return false;
        }

        _scriptData.LanguageFiles.Add(arguments[1]);

        if (_scriptData.LanguageFiles.Count > 8)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                "Too many language files. Maximum is 8");

            return false;
        }

        return true;
    }

    private bool ProcessExtensionCommand(ScriptSectionType sectionType, string command,
        List<string> arguments, int lineNumber)
    {
        if (arguments.Count != 1)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                $"Wrong number of arguments for '{command}' command");

            return false;
        }

        int index = command switch
        {
            "Level=" => 0,
            "FMV=" => 1,
            "Cut=" => 2,
            _ => -1
        };

        if (index < 0)
        {
            ErrorHandler.AddError(_globals.Errors, lineNumber, true,
                $"Unknown command for extensions section: {command}");

            return false;
        }

        if (sectionType == ScriptSectionType.PSXExtensions)
            _scriptData.PSXExtensions[index] = arguments[0];
        else
            _scriptData.PCExtensions[index] = arguments[0];

        return true;
    }

    private static bool IsEnabled(List<string> arguments)
    {
        if (arguments.Count == 0)
            return false;

        return string.Equals(arguments[0], "ENABLED", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a command is an NG command (not a classic TRLE command).
    /// </summary>
    private static bool IsNGCommand(string command)
    {
        // Classic Options commands
        if (command
            is "LoadSave="
            or "Title="
            or "PlayAnyLevel="
            or "FlyCheat="
            or "DemoDisc="
            or "InputTimeOut="
            or "Security=")
        {
            return false;
        }

        // Classic Language/Extension commands
        if (command
            is "File="
            or "Level="
            or "Cut="
            or "FMV="
            or "Name=")
        {
            return false;
        }

        // Check if command exists in NG command definitions
        return NGCommandDefinitions.GetAllDefinitions()
            .Any(def => def.Value.Name.Equals(command, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Registers a Plugin= command and assigns the plugin ID.
    /// Format: Plugin= ID, PluginName, Flags, ArrayOfStrings
    /// </summary>
    private void RegisterPluginCommand(List<string> arguments, int lineNumber)
    {
        if (arguments.Count < 2)
        {
            Logger.LogWarning($"Line {lineNumber}: Plugin= command requires at least 2 arguments (ID, Name)");
            return;
        }

        // Parse plugin ID
        if (!int.TryParse(arguments[0].Trim(), out int pluginId))
        {
            Logger.LogWarning($"Line {lineNumber}: Invalid plugin ID: {arguments[0]}");
            return;
        }

        // Get plugin name
        string pluginName = arguments[1].Trim();

        // Find the plugin in discovered plugins, or check if DLL exists
        var plugin = _pluginManager.FindPlugin(pluginName);

        if (plugin is null)
        {
            // Plugin not discovered - check if DLL exists
            string trlePath = Path.GetDirectoryName(Path.GetDirectoryName(_scriptData.IncludeStack[0].FileName)) ?? string.Empty;
            string dllPath = Path.Combine(trlePath, $"{pluginName}.dll");

            if (!File.Exists(dllPath))
            {
                Logger.LogWarning($"Line {lineNumber}: Plugin '{pluginName}' not found (missing {pluginName}.dll)");
                return;
            }

            // DLL exists but no .script file - create plugin entry without constants
            plugin = new PluginInfo
            {
                Name = pluginName,
                ScriptPath = string.Empty
            };

            _pluginManager.GetDiscoveredPlugins().Add(plugin);
        }

        plugin.PluginId = pluginId;
        Logger.LogVerbose($"Registered plugin: {pluginName} with ID {pluginId}");

        // Add to script data plugins
        _scriptData.NGData.Plugins.Add(new NGPlugin
        {
            PluginId = pluginId,
            PluginName = pluginName
        });
    }

    /// <summary>
    /// Loads the first language file to make strings available for NG command parsing.
    /// </summary>
    private void LoadFirstLanguageFile(string scriptFolder)
    {
        if (_scriptData.LanguageFiles.Count == 0)
        {
            Logger.LogWarning("No language files defined in [Language] section");
            return;
        }

        string firstLanguageFile = Path.Combine(scriptFolder, _scriptData.LanguageFiles[0]);

        if (!File.Exists(firstLanguageFile))
        {
            Logger.LogWarning($"First language file not found: {firstLanguageFile}");
            return;
        }

        var languageParser = new LanguageParser();

        if (languageParser.ParseLanguageFile(firstLanguageFile, out LanguageData? languageData) && languageData is not null)
        {
            _ngParser.SetLanguageData(languageData);
            _languageFileLoaded = true;

            Logger.LogVerbose($"Loaded language file for NG command parsing: {Path.GetFileName(firstLanguageFile)}");
        }
        else
        {
            Logger.LogWarning($"Failed to load language file: {firstLanguageFile}");
        }
    }

    /// <summary>
    /// Collects NG level commands from all sections into NGScriptData.LevelCommands.
    /// </summary>
    private void CollectNGLevelCommands()
    {
        _scriptData.NGData.LevelCommands.Clear();

        foreach (var section in _scriptData.Sections)
        {
            // Each section (Level or Title) gets its own NGCommandGroup
            _scriptData.NGData.LevelCommands.Add(section.NGCommands);
        }
    }

    /// <summary>
    /// Resolves a constant name for expression evaluation.
    /// Returns (success, value, pluginId) tuple.
    /// </summary>
    private (bool success, int value, int pluginId) ResolveConstantForExpression(string constantName)
    {
        // Try user defines first
        if (_defineManager.TryGetDefine(constantName, out int value))
            return (true, value, 0);

        // Try global constants
        if (_constantsLoader.TryGetConstant(constantName, out value))
            return (true, value, 0);

        // Try slot constants
        if (_objectsHParser.TryGetSlotConstant(constantName, out value))
            return (true, value, 0);

        // Try static constants
        if (_objectsHParser.TryGetStaticConstant(constantName, out value))
            return (true, value, 0);

        // Try plugin constants
        if (_pluginManager is not null)
        {
            foreach (var plugin in _pluginManager.GetDiscoveredPlugins())
            {
                if (_pluginManager.TryResolvePluginConstant(plugin.Name, constantName, out value))
                    return (true, value, plugin.PluginId);
            }
        }

        return (false, 0, 0);
    }
}
