using TRNGScriptCompiler.Models;
using TRNGScriptCompiler.Parsers;
using TRNGScriptCompiler.Writers;
using TRNGScriptCompiler.Utilities;

namespace TRNGScriptCompiler.Compiler;

public class TRNGCompiler
{
    private readonly CompilerGlobals _globals = new();

    /// <summary>
    /// Compiles all TRNG script files (script.txt and language files) to binary .dat files.
    /// </summary>
    public bool CompileAll(string trleFolderPath, bool waitForKey = false)
    {
        try
        {
            Logger.Log("**** START COMPILING ****");
            Logger.Log($"TRNG Script Compiler - TRLE Folder: {trleFolderPath}");

            string scriptFolder = Path.Combine(trleFolderPath, "script");
            string scriptFile = Path.Combine(scriptFolder, "SCRIPT.TXT");

            if (!File.Exists(scriptFile))
            {
                Logger.LogError($"Cannot find SCRIPT.TXT in: {scriptFolder}");
                return false;
            }

            // Delete old .dat files
            Logger.Log($"Remove old .dat files from folder: {scriptFolder}");
            DeleteDatFiles(scriptFolder);

            Logger.Log($"Remove old language.dat files from base TRLE folder: {trleFolderPath}");
            DeleteLanguageDatFiles(trleFolderPath);

            // Parse the main script file
            var scriptParser = new ScriptParser(_globals);

            if (!scriptParser.ParseScriptFile(scriptFile, trleFolderPath, out ScriptData? scriptData) || scriptData is null)
            {
                Logger.LogError("Failed to parse SCRIPT.TXT");
                ErrorHandler.DisplayErrors(_globals.Errors);
                return false;
            }

            // Parse and compile language files
            Logger.Log("SAVE LANGUAGES.DAT ...");

            var compiledLanguages = new List<(string fileName, LanguageData data)>();
            LanguageData? mainLanguageData = null;
            string mainLanguageFile = string.Empty;

            foreach (var languageFile in scriptData.LanguageFiles)
            {
                string languagePath = Path.Combine(scriptFolder, languageFile);

                if (!File.Exists(languagePath))
                {
                    Logger.LogWarning($"Missing language file: {Path.GetFileName(languagePath)}");
                    continue;
                }

                // Parse language file
                var languageParser = new LanguageParser();

                if (!languageParser.ParseLanguageFile(languagePath, out LanguageData? languageData) || languageData is null)
                {
                    Logger.LogError($"Failed to parse {Path.GetFileName(languagePath)}");
                    continue;
                }

                compiledLanguages.Add((languageFile, languageData));

                // Save the first language as main
                if (mainLanguageData is null)
                {
                    mainLanguageData = languageData;
                    mainLanguageFile = languageFile;
                }
            }

            if (mainLanguageData is null)
            {
                Logger.LogError("No language files were successfully parsed");
                return false;
            }

            // Write script.dat
            string scriptDatPath = Path.Combine(scriptFolder, "script.dat");
            var scriptWriter = new ScriptWriter(_globals);
            scriptWriter.SetLanguageData(mainLanguageData);

            if (!scriptWriter.WriteScriptDat(scriptDatPath, scriptData))
            {
                Logger.LogError("Failed to write script.dat");
                return false;
            }

            // Write all language.dat files
            foreach (var (fileName, data) in compiledLanguages)
            {
                string languageDatPath = Path.Combine(scriptFolder,
                    Path.ChangeExtension(fileName, ".dat"));

                Logger.Log($"\tSaving: {Path.GetFileName(languageDatPath)}");

                if (!LanguageWriter.WriteLanguageDat(languageDatPath, data))
                    Logger.LogError($"Failed to write {Path.GetFileName(languageDatPath)}");
            }

            // Copy main files to TRLE root folder
            string mainScriptDat = Path.Combine(trleFolderPath, "script.dat");
            string mainLanguageDat = Path.Combine(trleFolderPath,
                Path.ChangeExtension(mainLanguageFile, ".dat"));

            if (File.Exists(scriptDatPath))
            {
                File.Copy(scriptDatPath, mainScriptDat, true);
                Logger.Log($"Created: {Path.GetFileName(mainScriptDat)}");
            }

            string sourceLanguageDat = Path.Combine(scriptFolder,
                Path.ChangeExtension(mainLanguageFile, ".dat"));

            if (File.Exists(sourceLanguageDat))
            {
                File.Copy(sourceLanguageDat, mainLanguageDat, true);
                Logger.Log($"Created: {Path.GetFileName(mainLanguageDat)}");
            }

            // Check for errors
            if (ErrorHandler.HasFatalErrors(_globals.Errors))
            {
                Logger.LogError("Compilation failed with errors.");
                ErrorHandler.DisplayErrors(_globals.Errors);
                return false;
            }

            Logger.Log("******** COMPLETED ********");

            if (waitForKey)
            {
                Logger.Log("PRESS ANY KEY TO CLOSE THE WINDOW ...");
                Console.ReadKey();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Compilation error: {ex.Message}");
            Logger.LogError($"Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    private static void DeleteDatFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.GetFiles(folder, "*.dat"))
        {
            try
            {
                File.Delete(file);
                Logger.LogVerbose($"      Removed: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not delete {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    private static void DeleteLanguageDatFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.GetFiles(folder, "*.dat"))
        {
            // Keep script.dat, delete only language.dat files
            if (!file.EndsWith("script.dat", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(file);
                    Logger.LogVerbose($"      Removed: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Could not delete {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }
}
