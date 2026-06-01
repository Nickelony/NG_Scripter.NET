using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;

namespace TRNGScriptCompiler;

/// <summary>
/// The options of a TRNG compiler run: its folders, its constants file, its main language and its random seed.
/// </summary>
/// <remarks>
/// A blank path, a missing required path or a path of the wrong kind fails the run, and a missing plugin
/// folder only warns. <see cref="CreateDefaults(string, MainLanguageSelector?)"/> builds the options for an engine
/// folder from the standard layout around it.
/// </remarks>
public sealed record TRNGCompilerOptions
{
	/// <summary>Gets the engine folder, which holds <c>Objects.h</c>, <c>pix/</c> and plugin DLLs.</summary>
	/// <remarks>
	/// A compile publishes <c>Script.dat</c> and the main language's <c>.dat</c> here, and no other language's.
	/// </remarks>
	public required string EngineDirectory { get; init; }

	/// <summary>Gets the path to the <c>scripter_constants.txt</c> file that defines NG command signatures.</summary>
	public required string ConstantsFilePath { get; init; }

	/// <summary>
	/// Gets the script folder, which holds <c>Script.txt</c>, the language files and <c>#INCLUDE</c> targets.
	/// </summary>
	/// <remarks>
	/// A compile publishes its outputs to <see cref="EngineDirectory"/>, not to this folder.
	/// </remarks>
	public required string ScriptDirectory { get; init; }

	/// <summary>
	/// Gets the plugin folder, whose <c>Plugin*.script</c> files, and those of its immediate subfolders,
	/// the compile reads.
	/// </summary>
	/// <remarks>
	/// A script at the top level wins over a subfolder copy of its name.
	/// <see cref="ResolvePluginDirectory"/> gives the default.
	/// </remarks>
	public required string PluginDirectory { get; init; }

	/// <summary>
	/// Gets the main language, whose <c>.dat</c> alone is published, as an index, a file name or a base name.
	/// </summary>
	/// <remarks>
	/// Every language is still compiled and checked, and a <see langword="null"/> selector,
	/// which blank text converts to, picks the first listed language with a warning.
	/// </remarks>
	public MainLanguageSelector? MainLanguage { get; init; }

	/// <summary>
	/// When set, to any value including 0, seeds the random padding of the NG security block once, so a full
	/// compilation writes the same bytes every time.
	/// </summary>
	/// <remarks>
	/// <see langword="null"/> means timer entropy.
	/// </remarks>
	public double? DeterministicSeed { get; init; }

	/// <summary>
	/// Resolves the script folder as the engine folder's <c>script</c> folder, or its parent's
	/// when only that one exists.
	/// </summary>
	/// <remarks>
	/// The engine path may be relative or full, with or without a trailing separator, and the returned path
	/// may not exist on disk.
	/// </remarks>
	public static string ResolveScriptDirectory(string engineDirectoryPath)
	{
		ArgumentNullException.ThrowIfNull(engineDirectoryPath);

		// The parent is taken from the full path, because the parent of "." is empty and the parent of "C:\TRLE\" is "C:\TRLE" itself.
		engineDirectoryPath = Path.TrimEndingDirectorySeparator(PathResolver.Uncached.ResolveFullPath(engineDirectoryPath));

		string scriptPath = PathResolver.Uncached.ResolveFullPath(Path.Combine(engineDirectoryPath, "script"));

		if (!Directory.Exists(scriptPath))
		{
			string? parentDir = Path.GetDirectoryName(engineDirectoryPath);

			if (parentDir is not null)
			{
				string parentScriptPath = PathResolver.Uncached.ResolveFullPath(Path.Combine(parentDir, "script"));

				if (Directory.Exists(parentScriptPath))
					scriptPath = parentScriptPath;
			}
		}

		return scriptPath;
	}

	/// <summary>
	/// Resolves the plugin folder of an engine folder as a full path: its <c>NG_Center</c> folder when it exists,
	/// else the engine folder.
	/// </summary>
	/// <remarks>
	/// NG Center keeps the installed plugin scripts in its own folder, and a TombIDE project passes its <c>Plugins</c>
	/// folder instead.
	/// </remarks>
	public static string ResolvePluginDirectory(string engineDirectoryPath)
	{
		ArgumentNullException.ThrowIfNull(engineDirectoryPath);

		string ngCenterPath = PathResolver.Uncached.ResolveFullPath(Path.Combine(engineDirectoryPath, "NG_Center"));
		return Directory.Exists(ngCenterPath) ? ngCenterPath : PathResolver.Uncached.ResolveFullPath(engineDirectoryPath);
	}

	/// <summary>
	/// Creates standard-layout options for an engine folder, with the <c>scripter_constants.txt</c>
	/// beside the application.
	/// </summary>
	/// <remarks>
	/// <see cref="ResolveScriptDirectory"/> and <see cref="ResolvePluginDirectory"/> give the folders.
	/// </remarks>
	public static TRNGCompilerOptions CreateDefaults(string engineDirectoryPath, MainLanguageSelector? mainLanguage = null)
	{
		ArgumentNullException.ThrowIfNull(engineDirectoryPath);
		return CreateDefaults(engineDirectoryPath, mainLanguage, out _);
	}

	/// <summary>
	/// Creates standard-layout options for an engine folder and reports where the layout conventions had to guess.
	/// </summary>
	/// <remarks>
	/// <paramref name="notes"/> holds a non-fatal diagnostic for each guess, such as a script folder or constants file
	/// that does not exist.
	/// </remarks>
	public static TRNGCompilerOptions CreateDefaults(
		string engineDirectoryPath,
		MainLanguageSelector? mainLanguage,
		out IReadOnlyList<CompilerDiagnostic> notes
	)
	{
		ArgumentNullException.ThrowIfNull(engineDirectoryPath);

		string constantsPath = Path.Combine(AppContext.BaseDirectory, CompilerFileNames.ScripterConstants);
		string scriptPath = ResolveScriptDirectory(engineDirectoryPath);

		var collectedNotes = new List<CompilerDiagnostic>();

		if (!Directory.Exists(scriptPath))
		{
			collectedNotes.Add(
				new CompilerDiagnostic
				{
					Severity = DiagnosticSeverity.Warning,
					Message = $"Script folder was guessed because neither 'script' candidate exists: {scriptPath}",
				}
			);
		}

		if (!File.Exists(constantsPath))
		{
			collectedNotes.Add(
				new CompilerDiagnostic
				{
					Severity = DiagnosticSeverity.Warning,
					Message = $"Default constants file does not exist next to the application: {constantsPath}",
				}
			);
		}

		notes = collectedNotes;

		return new TRNGCompilerOptions
		{
			EngineDirectory = engineDirectoryPath,
			ScriptDirectory = scriptPath,
			PluginDirectory = ResolvePluginDirectory(engineDirectoryPath),
			ConstantsFilePath = constantsPath,
			MainLanguage = mainLanguage,
		};
	}

	/// <summary>Validates the option values and returns every problem found, in path order.</summary>
	/// <remarks>
	/// A blank or missing required path is fatal, and so is a path of the wrong kind. A missing plugin folder warns,
	/// and each run checks first, so a host calls this only to check ahead.
	/// </remarks>
	public IReadOnlyList<CompilerDiagnostic> Validate() => Validate(overlay: null);

	/// <summary>
	/// Validates the option values against the disk and the overlay of the run, if any, whose files and folders count
	/// as existing.
	/// </summary>
	/// <returns>The diagnostics describing the option problems, in path order.</returns>
	internal IReadOnlyList<CompilerDiagnostic> Validate(VirtualFileSystem? overlay)
	{
		var diagnostics = new List<CompilerDiagnostic>();

		AddPathDiagnostic(diagnostics, overlay, path: EngineDirectory, description: "Engine folder", mustExist: true, isFolder: true);
		AddPathDiagnostic(diagnostics, overlay, path: ScriptDirectory, description: "Script folder", mustExist: true, isFolder: true);
		AddPathDiagnostic(diagnostics, overlay, path: PluginDirectory, description: "Plugin folder", mustExist: false, isFolder: true);
		AddPathDiagnostic(diagnostics, overlay, path: ConstantsFilePath, description: "Constants file", mustExist: true, isFolder: false);

		return diagnostics;
	}

	private static void AddPathDiagnostic(
		List<CompilerDiagnostic> diagnostics,
		VirtualFileSystem? overlay,
		string path,
		string description,
		bool mustExist,
		bool isFolder
	)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Message = $"{description} path is not set" });
			return;
		}

		bool isFile = VirtualFileSystem.FileExists(path, overlay);
		bool isDirectory = VirtualFileSystem.DirectoryExists(path, overlay);

		if (isFolder ? isDirectory : isFile)
			return;

		// A path of the wrong kind fails whether or not the option may be missing, because the run cannot read it.
		if (isFile || isDirectory)
		{
			string found = isFolder ? "a file" : "a folder";
			string expected = isFolder ? "a folder" : "a file";

			diagnostics.Add(
				new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Message = $"{description} is {found}, not {expected}: {path}" }
			);

			return;
		}

		diagnostics.Add(
			new CompilerDiagnostic
			{
				Severity = mustExist ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
				Message = $"{description} was not found: {path}",
			}
		);
	}
}
