using System.Collections.Frozen;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Infrastructure;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Script;

namespace TRNGScriptCompiler.Pipeline;

/// <summary>
/// Validates, loads the constants, parses, NG-compiles and composes the outputs of a compilation,
/// without writing a file.
/// </summary>
/// <remarks>
/// It builds the run's constant catalog, plugin registry and evaluator once; <see cref="TRNGCompiler"/> owns the run
/// lifecycle and publication.
/// </remarks>
internal static class ScriptAnalyzer
{
	/// <summary>Bundles the tables and readers that the parse, the NG compile and the classic compile share.</summary>
	private sealed record CompileServices(ExpressionEvaluator Evaluator, ObjectsHCatalog ObjectsH, PluginRegistry Plugins);

	/// <summary>Runs the analysis phase and returns a self-contained result.</summary>
	/// <returns>
	/// The success flag, the diagnostics and the log, and the composed outputs when no error was reported.
	/// </returns>
	/// <remarks>
	/// The script model is set once the NG lines compile, even when the languages then fail to parse. The language data
	/// and the main language file are set once the languages parse.
	/// </remarks>
	internal static AnalysisResult Analyze(CompilationRun run, TRNGCompilerOptions options, VirtualFileSystem? overlay, CancellationToken token)
	{
		// A canceled call throws whatever its options hold, so a caller never mistakes a cancellation for an invalid script folder.
		token.ThrowIfCancellationRequested();

		AnalysisPaths? paths = ValidatePrerequisites(run, options, overlay);

		if (paths is null)
			return CreateFailResult(run);

		var context = new AnalysisContext(run, options, paths, overlay, token);

		// Read scripter_constants.txt exactly once per run; both the NG command definitions
		// and the global constants are parsed from this shared snapshot.
		ConstantsFile constantsFile;

		try
		{
			constantsFile = ConstantsFile.Load(paths.ConstantsFilePath, run.Diagnostics, context.Overlay);
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			run.Diagnostics.Error($"Cannot read the constants file: {ex.Reason()}", SourceLocation.InFile(paths.ConstantsFilePath), ex);
			return CreateFailResult(run);
		}

		WarnAboutLanguageDatFiles(context);

		// The pipeline parses the definition table so the Constants format has no
		// dependency on the NG format.
		var ngCommandCatalog = NgCommandCatalog.Parse(constantsFile.Lines, paths.ConstantsFilePath, run.Diagnostics);

		if (ngCommandCatalog.Definitions.Count == 0)
		{
			run.Diagnostics.Error(
				$"No NG command definitions were found in: {paths.ConstantsFilePath}",
				SourceLocation.InFile(paths.ConstantsFilePath)
			);

			return CreateFailResult(run);
		}

		CompileServices services = BuildServices(context, constantsFile);

		ParsedScript? parsed = ParseScriptFile(context, services, ngCommandCatalog);

		if (parsed is null)
			return CreateFailResult(run);

		// NG string arguments resolve against the first language, so it is parsed, once and through the overlay, before the NG lines compile,
		// whatever section the [Language] section follows, as VB6 does (Module1.bas:5655).
		var languages = new LanguageSet(context);

		string firstLanguageFile = parsed.LanguageFiles[0];
		LanguageData? firstLanguageData = languages.ParseLanguageFile(firstLanguageFile, parsed.LanguageFileLocations[0]);

		if (firstLanguageData is null)
			return CreateFailResult(run);

		var ngCompiler = new NgScriptCompiler(
			run,
			commandCatalog: ngCommandCatalog,
			evaluator: services.Evaluator,
			objectsH: services.ObjectsH,
			plugins: services.Plugins,
			firstLanguage: firstLanguageData
		);

		var scriptModel = new ScriptModel(parsed, ngCompiler.Compile(parsed, token));

		LanguageSet.ParseResult? languageResult = languages.Parse(scriptModel, firstLanguageFile, firstLanguageData);

		if (languageResult is null)
			return CreateFailResult(run) with { ScriptModel = scriptModel };

		// The outputs compose here rather than in the compile, so an analysis reports every error a compile finds; the classic sections compile
		// with the evaluator that read the rest of the script.
		token.ThrowIfCancellationRequested();

		var composer = new OutputComposer(
			run,
			evaluator: services.Evaluator,
			deterministicSeed: options.DeterministicSeed,
			engineDirectory: paths.EngineDirectory,
			overlay
		);

		CompiledOutputs? outputs = composer.Compose(scriptModel, languageResult.AllLanguageData, languageResult.MainLanguageFile);

		// The last check, so a request canceled while the outputs compose returns canceled; nothing after it can be canceled.
		token.ThrowIfCancellationRequested();

		bool hasErrors = run.Diagnostics.HasErrors();

		run.Logger.Log(hasErrors ? "******** ANALYSIS FAILED ********" : "******** ANALYSIS COMPLETED ********");

		return BuildAnalysisResult(run, success: !hasErrors, scriptModel, languageResult) with
		{
			Outputs = hasErrors ? null : outputs,
		};
	}

	/// <summary>
	/// Builds the tables and readers of the run once: <c>Objects.h</c>, the plugins, the constant catalog
	/// and the evaluator.
	/// </summary>
	private static CompileServices BuildServices(AnalysisContext context, ConstantsFile constantsFile)
	{
		CompilationRun run = context.Run;

		var objectsH = new ObjectsHCatalog(run.Logger, run.Diagnostics);
		objectsH.Load(run.PathResolver.ResolveFullPath(Path.Combine(context.Paths.EngineDirectory, CompilerFileNames.ObjectsH)), context.Overlay);

		IReadOnlyList<PluginScript> pluginScripts = PluginDiscovery.Discover(
			folder: context.Paths.PluginDirectory,
			overlay: context.Overlay,
			logger: run.Logger,
			diagnostics: run.Diagnostics
		);

		var plugins = new PluginRegistry(
			scripts: pluginScripts,
			engineDirectory: context.Paths.EngineDirectory,
			paths: run.PathResolver,
			logger: run.Logger,
			diagnostics: run.Diagnostics
		);

		var catalog = new ConstantCatalog(constantsFile, objectsH, pluginScripts, logger: run.Logger, diagnostics: run.Diagnostics);
		var evaluator = new ExpressionEvaluator(new ConstantResolver(run.Defines, catalog, plugins).Resolve);

		return new CompileServices(evaluator, objectsH, plugins);
	}

	/// <summary>
	/// Reports the findings of <see cref="TRNGCompilerOptions.Validate()"/> and checks that <c>Script.txt</c> exists,
	/// after resolving the paths segment by segment to their on-disk casing.
	/// </summary>
	/// <returns>The resolved paths, or <see langword="null"/> when a check fails.</returns>
	private static AnalysisPaths? ValidatePrerequisites(CompilationRun run, TRNGCompilerOptions options, VirtualFileSystem? overlay)
	{
		bool optionsValid = true;

		foreach (CompilerDiagnostic finding in options.Validate(overlay))
		{
			if (finding.IsError)
			{
				run.Diagnostics.Error(finding.Message);
				optionsValid = false;
			}
			else
			{
				run.Diagnostics.Warning(finding.Message);
			}
		}

		if (!optionsValid)
			return null;

		// Resolve the engine folder and constants path segment by segment so mixed-case
		// paths work on case-sensitive file systems.
		string engineDirectory = run.PathResolver.ResolveFullPath(options.EngineDirectory);
		string constantsFilePath = run.PathResolver.ResolveFullPath(options.ConstantsFilePath);
		string scriptFile = run.PathResolver.ResolveFullPath(Path.Combine(options.ScriptDirectory, CompilerFileNames.ScriptTxt));

		if (!VirtualFileSystem.FileExists(scriptFile, overlay))
		{
			run.Diagnostics.Error($"Cannot find Script.txt in: {options.ScriptDirectory}");
			return null;
		}

		return new AnalysisPaths(
			engineDirectory,
			PluginDirectory: run.PathResolver.ResolveFullPath(options.PluginDirectory),
			ScriptDirectory: run.PathResolver.ResolveFullPath(options.ScriptDirectory),
			scriptFile,
			constantsFilePath
		);
	}

	/// <summary>
	/// Warns about the language <c>.dat</c> files VB6 leaves in the script folder, where this compiler writes none.
	/// </summary>
	/// <remarks>
	/// NG Center's language switch copies the selected one to an engine folder lacking it (Principale.frm:2654-2690),
	/// so a stale one can reach the game.
	/// </remarks>
	private static void WarnAboutLanguageDatFiles(AnalysisContext context)
	{
		string scriptDirectory = context.Paths.ScriptDirectory;

		if (!Directory.Exists(scriptDirectory))
			return;

		string[] names;

		try
		{
			names =
			[
				.. Directory
					.EnumerateFiles(scriptDirectory)
					.Select(Path.GetFileName)
					.OfType<string>()
					.Where(name => Path.GetExtension(name).IgnoreCaseEquals(".dat") && !name.IgnoreCaseEquals(CompilerFileNames.ScriptDat))
					.Order(StringComparer.OrdinalIgnoreCase),
			];
		}
		catch (Exception ex) when (ex.IsFileAccessFault())
		{
			context.Run.Diagnostics.Warning(
				$"Could not list the script folder, so it is not checked for language .dat files: {ex.Reason()}",
				exception: ex
			);

			return;
		}

		if (names.Length > 0)
		{
			context.Run.Diagnostics.Warning(
				$"The script folder holds {string.Join(", ", names)}, which NG Center's language switch copies into the engine folder, so a stale language can reach the game; delete them"
			);
		}
	}

	/// <summary>
	/// Parses <c>Script.txt</c>, or returns <see langword="null"/> when parsing fails or it lists no language file.
	/// </summary>
	/// <remarks>
	/// The registry records each <c>Plugin=</c> line before the parse reads a number, so a plugin constant finds
	/// its plugin wherever the line stands.
	/// </remarks>
	private static ParsedScript? ParseScriptFile(AnalysisContext context, CompileServices services, NgCommandCatalog commandCatalog)
	{
		var scriptParser = new ScriptParser(
			run: context.Run,
			evaluator: services.Evaluator,
			ngCommandNames: commandCatalog.Definitions.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
		);

		ScriptSource? source = scriptParser.ReadSource(
			scriptPath: context.Paths.ScriptFile,
			scriptDirectory: context.Paths.ScriptDirectory,
			overlay: context.Overlay,
			token: context.Token
		);

		if (source is null)
			return null;

		foreach (NgLine pluginLine in source.PluginLines)
			services.Plugins.Declare(pluginLine);

		ParsedScript? parsed = scriptParser.Parse(source, context.Paths.EngineDirectory, context.Token);

		if (parsed is null)
			return null;

		if (parsed.LanguageFiles.Count == 0)
		{
			context.Run.Diagnostics.Error("No language files were listed in Script.txt", SourceLocation.InFile(context.Paths.ScriptFile));
			return null;
		}

		return parsed;
	}

	/// <summary>Creates a failed analysis result that carries the run's diagnostics and its log text so far.</summary>
	/// <remarks>
	/// A caller adds its own context with a <c>with</c> expression.
	/// </remarks>
	internal static AnalysisResult CreateFailResult(CompilationRun run)
	{
		return new()
		{
			Success = false,
			Diagnostics = [.. run.Diagnostics.Recorded],
			Log = run.Logger.GetText(),
		};
	}

	private static AnalysisResult BuildAnalysisResult(
		CompilationRun run,
		bool success,
		ScriptModel scriptModel,
		LanguageSet.ParseResult languageResult
	)
	{
		return new()
		{
			Success = success,
			Diagnostics = [.. run.Diagnostics.Recorded],
			Log = run.Logger.GetText(),
			MainLanguageFile = languageResult.MainLanguageFile,
			ScriptModel = scriptModel,
			FirstLanguageData = languageResult.FirstLanguageData,
			AllLanguageData = languageResult.AllLanguageData,
		};
	}
}
