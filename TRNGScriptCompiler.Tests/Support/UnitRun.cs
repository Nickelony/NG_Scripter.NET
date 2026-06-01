using System.Collections.Frozen;
using TRNGScriptCompiler.Constants;
using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Kernel.Runtime;
using TRNGScriptCompiler.Language;
using TRNGScriptCompiler.Model;
using TRNGScriptCompiler.Ng;
using TRNGScriptCompiler.Plugins;
using TRNGScriptCompiler.Script;

namespace TRNGScriptCompiler.Tests.Support;

/// <summary>
/// Builds the per-run objects a direct test of one unit needs, from the shipped <c>scripter_constants.txt</c>,
/// as a compile does.
/// </summary>
/// <remarks>
/// <c>Objects.h</c> is read from memory, so nothing is written to disk, and every path lies below a temp folder
/// that no test creates.
/// </remarks>
internal sealed class UnitRun
{
	/// <summary>
	/// The <c>Objects.h</c> of a run, with the slots <c>LARA</c> 0, <c>PISTOLS_ANIM</c> 1 and <c>SLOT_TEST</c> 2.
	/// </summary>
	/// <remarks>
	/// Its statics are <c>PLANT0</c> 0 and <c>STATIC_TEST</c> 1, and <c>NUMBER_OBJECTS</c> stands at position 3,
	/// so the table holds three slots.
	/// </remarks>
	internal const string ObjectsHText =
		"enum object_types\r\n{\r\n\tLARA,\r\n\tPISTOLS_ANIM,\r\n\tSLOT_TEST,\r\n\tNUMBER_OBJECTS\r\n};\r\n\r\nenum static_types\r\n{\r\n\tPLANT0,\r\n\tSTATIC_TEST\r\n};\r\n";

	private static readonly ConstantsFile s_constants = ConstantsFile.Load(
		Path.Combine(AppContext.BaseDirectory, "scripter_constants.txt"),
		new DiagnosticSink(new CompilerLog(conciseLogging: true))
	);

	private readonly Dictionary<string, string> _files = [];

	/// <summary>Initializes the objects of a new run.</summary>
	/// <param name="objectsH">
	/// The text of <c>Objects.h</c>, or <see langword="null"/> for a run without the file.
	/// </param>
	/// <param name="pluginScripts">
	/// The plugin scripts of the run, whose constants the evaluator reads; none when <see langword="null"/>.
	/// </param>
	internal UnitRun(string? objectsH = ObjectsHText, IReadOnlyList<PluginScript>? pluginScripts = null)
	{
		pluginScripts ??= [];

		Root = Path.Combine(TestWorkspace.TempRoot, "unit-" + Guid.NewGuid().ToString("N"));
		Run = new CompilationRun(conciseLogging: true);
		ObjectsH = new ObjectsHCatalog(Run.Logger, Run.Diagnostics);

		if (objectsH is not null)
		{
			string objectsHPath = Path.Combine(Root, "Objects.h");
			ObjectsH.Load(objectsHPath, new VirtualFileSystem(new Dictionary<string, string> { [objectsHPath] = objectsH }));
		}

		Plugins = new PluginRegistry(
			scripts: pluginScripts,
			engineDirectory: Root,
			paths: Run.PathResolver,
			logger: Run.Logger,
			diagnostics: Run.Diagnostics
		);

		var catalog = new ConstantCatalog(constantsFile: s_constants, ObjectsH, pluginScripts, logger: Run.Logger, diagnostics: Run.Diagnostics);
		Evaluator = new ExpressionEvaluator(new ConstantResolver(Run.Defines, catalog, Plugins).Resolve);
	}

	/// <summary>Gets the NG command catalog of the shipped constants file.</summary>
	internal static NgCommandCatalog CommandCatalog { get; } =
		NgCommandCatalog.Parse(s_constants.Lines, s_constants.FilePath, new DiagnosticSink(new CompilerLog(conciseLogging: true)));

	/// <summary>Returns the definition of a stock NG command of the shipped constants file.</summary>
	/// <param name="name">The command name with its <c>=</c>, such as <c>Snow=</c>.</param>
	internal static NgCommandDefinition Stock(string name) => CommandCatalog.Definitions[name];

	/// <summary>
	/// Gets the names of the NG commands, with their <c>=</c>, matched without regard to case, as the parse
	/// receives them.
	/// </summary>
	internal static IReadOnlySet<string> NgCommandNames { get; } = CommandCatalog.Definitions.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>Gets the engine folder of the run, which holds <c>Objects.h</c> and the <c>script</c> folder.</summary>
	internal string Root { get; }

	/// <summary>Gets the script folder of the run, below its engine folder.</summary>
	internal string ScriptDirectory => Path.Combine(Root, "script");

	/// <summary>Gets the full path of the run's <c>Script.txt</c>, which the source locations of a test name.</summary>
	internal string ScriptFile => Path.Combine(ScriptDirectory, "Script.txt");

	/// <summary>Gets the run, whose log and diagnostics every unit built from these objects writes to.</summary>
	internal CompilationRun Run { get; }

	/// <summary>Gets the <c>Objects.h</c> table of the run.</summary>
	internal ObjectsHCatalog ObjectsH { get; }

	/// <summary>
	/// Gets the plugin registry of the run, which holds its plugin scripts and the <c>Plugin=</c> lines
	/// a test declares.
	/// </summary>
	internal PluginRegistry Plugins { get; }

	/// <summary>
	/// Gets the evaluator of the run, which reads the constants, the slot and static names
	/// and the <c>#DEFINE</c> values.
	/// </summary>
	internal ExpressionEvaluator Evaluator { get; }

	/// <summary>Gets the errors, warnings and hints the run has reported.</summary>
	internal IReadOnlyList<CompilerDiagnostic> Diagnostics => Run.Diagnostics.Recorded;

	/// <summary>Creates a run whose <c>Script.txt</c> holds the text given, in memory.</summary>
	/// <param name="script">The text of <c>Script.txt</c>.</param>
	/// <returns>The run, which more files join through <see cref="With"/>.</returns>
	internal static UnitRun ForScript(string script) => new UnitRun().With("Script.txt", script);

	/// <summary>Reads and parses a script held in memory with the parser itself, without a compile.</summary>
	/// <param name="script">The text of <c>Script.txt</c>.</param>
	/// <param name="includes">The files below the script folder that the script includes.</param>
	/// <returns>
	/// The source and the parsed script, each <see langword="null"/> where its step failed, and the run.
	/// </returns>
	internal static ScriptParse ParseScript(string script, params (string Name, string Content)[] includes)
	{
		UnitRun unit = ForScript(script);

		foreach ((string name, string content) in includes)
			unit.With(name, content);

		ScriptParser parser = unit.CreateScriptParser();

		ScriptSource? source = parser.ReadSource(
			scriptPath: unit.ScriptFile,
			scriptDirectory: unit.ScriptDirectory,
			overlay: unit.Files,
			token: CancellationToken.None
		);

		return new ScriptParse(source, source is null ? null : parser.Parse(source, unit.Root, CancellationToken.None), unit);
	}

	/// <summary>Gets the in-memory files of the run, which <see cref="With"/> fills.</summary>
	internal VirtualFileSystem Files => new(_files);

	/// <summary>Gets the full path of a file below the script folder.</summary>
	/// <param name="parts">The folders and the file name, below the script folder.</param>
	/// <returns>The full path.</returns>
	internal string PathOf(params string[] parts) => Path.Combine([ScriptDirectory, .. parts]);

	/// <summary>Adds a file below the script folder to the in-memory files of the run, and returns the run.</summary>
	/// <param name="relativePath">The path of the file below the script folder.</param>
	/// <param name="content">The text of the file.</param>
	/// <returns>This run.</returns>
	internal UnitRun With(string relativePath, string content)
	{
		_files[PathOf(relativePath)] = content;
		return this;
	}

	/// <summary>Creates the script parser of the run.</summary>
	/// <returns>The parser, which reads the in-memory files only when <see cref="Files"/> goes to it.</returns>
	internal ScriptParser CreateScriptParser() => new(Run, Evaluator, NgCommandNames);

	/// <summary>Creates the source loader of the run over its in-memory files, at <c>Script.txt</c>.</summary>
	/// <returns>The loader.</returns>
	internal ScriptSourceLoader CreateLoader()
	{
		var loader = new ScriptSourceLoader(Run, scriptPath: ScriptFile, ScriptDirectory, overlay: Files);
		return loader;
	}

	/// <summary>Records the <c>#DEFINE</c> lines of a file, as the parse does before it reads any command.</summary>
	/// <param name="file">The full path of the file that holds the lines.</param>
	/// <param name="lines">The lines of the file, the first being line 1.</param>
	internal void Define(string file, params string[] lines) => new DefineRecorder(Run, Evaluator).Scan(file, lines);

	/// <summary>Returns the location of a line of the run's <c>Script.txt</c>.</summary>
	/// <param name="line">The 1-based line.</param>
	/// <returns>The location.</returns>
	internal SourceLocation At(int line) => new(line, File: ScriptFile);

	/// <summary>
	/// Splits a line of the run's <c>Script.txt</c> into its command and arguments, as the parse does.
	/// </summary>
	/// <param name="line">The 1-based line.</param>
	/// <param name="text">The normalized text of the line.</param>
	/// <param name="sourceText">
	/// The line as the file holds it, or <see langword="null"/> for the normalized text.
	/// </param>
	/// <returns>The command line.</returns>
	internal ScriptCommandLine CommandLine(int line, string text, string? sourceText = null)
	{
		(string command, List<string> arguments) = ScriptLineLexer.ParseCommandLine(text);
		return new ScriptCommandLine(text, SourceText: sourceText ?? text, command, arguments, Location: At(line));
	}

	/// <summary>Builds the <c>#DEFINE @plugin</c> line that the parse hands to the NG compile.</summary>
	/// <param name="text">The normalized text of the line, which is also its source text.</param>
	/// <param name="location">The location of the line.</param>
	/// <returns>The line.</returns>
	internal static NgLine PluginDefineLine(string text, SourceLocation location) => NgLine.PluginDefine(text, sourceText: text, location);

	/// <summary>Builds the <c>Plugin=</c> line that the script analysis hands to the plugin registry.</summary>
	/// <param name="id">The ID argument.</param>
	/// <param name="name">The plugin name argument.</param>
	/// <param name="location">The location of the line.</param>
	/// <returns>The line.</returns>
	internal static NgLine PluginLine(string id, string name, SourceLocation location)
	{
		string text = $"Plugin= {id},{name},IGNORE";
		return new NgLine(new ScriptCommandLine(text, SourceText: text, Command: "Plugin=", Arguments: [id, name, "IGNORE"], location));
	}

	/// <summary>Builds a first language whose <c>[Strings]</c> section holds the lines given.</summary>
	/// <param name="strings">The lines, the first at index 0.</param>
	/// <returns>The language.</returns>
	internal static LanguageData Language(params string[] strings) => Language(strings, extraStrings: []);

	/// <summary>
	/// Builds a first language whose <c>[Strings]</c> section holds the lines given, with ExtraNG strings.
	/// </summary>
	/// <param name="strings">The lines, the first at index 0.</param>
	/// <param name="extraStrings">The ExtraNG strings.</param>
	/// <returns>The language.</returns>
	internal static LanguageData Language(string[] strings, ExtraNgString[] extraStrings)
	{
		return new()
		{
			TotalSectionStrings = strings.Length,
			TotalPsxStrings = 0,
			TotalPcStrings = 0,
			Strings = strings,
			ExtraStrings = extraStrings,
			LanguageFile = "English.txt",
		};
	}

	/// <summary>
	/// Encodes a language into the bytes of its <c>.dat</c>, and fails the test when the encoder fails.
	/// </summary>
	/// <param name="language">The language to encode.</param>
	/// <param name="extraStringFileLoader">
	/// Reads the file an <c>@file</c> ExtraNG string names, taking the path of the language file and the name
	/// of the file; none when <see langword="null"/>.
	/// </param>
	/// <returns>The bytes of the <c>.dat</c>.</returns>
	internal static byte[] EncodeLanguage(LanguageData language, Func<string, string, string?>? extraStringFileLoader = null)
	{
		LanguageEncoding encoding = RunLanguageEncoder(language, extraStringFileLoader);

		Assert.IsTrue(encoding.Success, encoding.Log);
		return encoding.Bytes;
	}

	/// <summary>
	/// Encodes a language into the bytes of its <c>.dat</c> with a fresh encoder, and reports how it went.
	/// </summary>
	/// <param name="language">The language to encode.</param>
	/// <param name="extraStringFileLoader">
	/// Reads the file an <c>@file</c> ExtraNG string names, taking the path of the language file and the name
	/// of the file; none when <see langword="null"/>.
	/// </param>
	/// <returns>The outcome, with the diagnostics and the log of the encoder.</returns>
	internal static LanguageEncoding RunLanguageEncoder(LanguageData language, Func<string, string, string?>? extraStringFileLoader = null)
	{
		var logger = new CompilerLog(conciseLogging: false);
		var diagnostics = new DiagnosticSink(logger);
		var encoder = new LanguageDatEncoder(logger, diagnostics);

		bool success = encoder.TryEncode(languageData: language, extraStringFileLoader: extraStringFileLoader ?? ((_, _) => null), out byte[] bytes);

		return new LanguageEncoding(success, bytes, Diagnostics: diagnostics.Recorded, Log: logger.GetText());
	}

	/// <summary>The outcome of <see cref="ParseScript"/>.</summary>
	/// <param name="Source">The source the loader read, or <see langword="null"/> when reading failed.</param>
	/// <param name="Parsed">The parsed script, or <see langword="null"/> when reading or parsing failed.</param>
	/// <param name="Unit">The run, whose diagnostics hold the failure.</param>
	internal sealed record ScriptParse(ScriptSource? Source, ParsedScript? Parsed, UnitRun Unit);

	/// <summary>The outcome of <see cref="RunLanguageEncoder"/>.</summary>
	/// <param name="Success">Whether the encoder succeeded.</param>
	/// <param name="Bytes">The bytes of the <c>.dat</c>.</param>
	/// <param name="Diagnostics">The errors, warnings and hints the encoder recorded.</param>
	/// <param name="Log">The text of the encoder's log.</param>
	internal sealed record LanguageEncoding(bool Success, byte[] Bytes, IReadOnlyList<CompilerDiagnostic> Diagnostics, string Log);
}
