# Architecture

The compiler library is a stack of layers, one folder each, and a source file may reference only the layers
the rule table below allows, which `ArchitectureTests` checks on every run of the suite. The `TRNGCompiler`
facade runs each call as one compilation run: an analysis reads and compiles the script folder and composes
every output in memory, and a compile adds the publication, the only step that writes a file.

A host can pass `TRNGCompiler.Analyze` an overlay: the in-memory text of the files an editor has not saved,
which `VirtualFileSystem` holds and every read prefers to the file on disk.

## Layers

| Layer | Folder | Holds | May reference |
| --- | --- | --- | --- |
| Kernel | `Kernel/` | The run state, the diagnostics and the log, the options and the overlay, and the helpers the layers share: text, paths, numeric literals, the Windows-1252 codec, the NG payload words, the VB6 random generator and the VB6 cipher | Nothing outside the Kernel |
| Model | `Model/` | The data the formats hand on: the script as the parser collects it (`ParsedScript`, `NgLine`), the script model that pairs it with its NG compile, whose classic lines stay unencoded (`ScriptModel`, `NgScriptModel`), the language data, and `StringReferenceResolver` | Kernel |
| Plugins | `Plugins/` | Plugin discovery, and `PluginRegistry`: the plugin scripts, the `Plugin=` lines and the translation of exported-trigger plugin IDs, with `PluginDefineRegistry`, the positional `@plugin` define registry | Kernel, Model |
| Language | `Language/` | The language file parser, the escape codec of its strings and the `.dat` encoder | Kernel, Model |
| Constants | `Constants/` | The grammar of the constants file and the plugin scripts, `Objects.h`, the constant catalog and resolver, the `#DEFINE` scan and the numeric evaluator | Kernel, Model, Plugins |
| NG | `Ng/` | The NG command definitions and policies, the NG compile of the collected NG lines, and the NG header encoder with its section encoders and verifier | Kernel, Model, Constants, Plugins |
| Script | `Script/` | The script reader and parser with its section builders, the classic command catalog and encoder, the section compiler, the classic `Script.dat` payload encoder and `ScriptDatLanguageNameReader`, which lists the language names of an existing `Script.dat` for the publication's stale-file cleanup | Kernel, Model, Constants |
| Pipeline | `Pipeline/` | The facade, the analysis with its language set (`LanguageSet`), the composition of the outputs and their publication, with the set of stale language files (`StaleLanguageFiles`) | Kernel, Model, Plugins, Language, Constants, NG, Script |
| Hosting | `Hosting/` | `CompilationHost`, the diagnostics host of an editor, and its contract | Kernel, Pipeline |

The Script and NG layers never reference each other. The parser tells an NG line by a set of NG command names
it receives, and `ScriptDatComposer` in Pipeline joins the classic payload and the NG header. Each edge to
Plugins runs one way:

- Constants references Plugins because `ConstantCatalog` builds the plugin constants from the plugin scripts,
  and `ConstantResolver` gives a plugin constant the ID of its plugin's `Plugin=` line.
- NG references Plugins because `NgScriptCompiler` applies each `#DEFINE @plugin` line to the plugin
  registry's defines, and `NgTriggerGroupResolver` translates the plugin IDs of exported triggers through them.
  `NgParser` also passes over a `Plugin=` line whose ID the registry rejected.

The Kernel's folders follow a table of their own, so no cycle forms inside the leaf layer:

| Kernel folder | May reference |
| --- | --- |
| `Kernel/Text` | No other Kernel folder |
| `Kernel/Numerics` | No other Kernel folder |
| `Kernel/Diagnostics` | No other Kernel folder |
| `Kernel/Infrastructure` | `Kernel/Text` |
| `Kernel/Runtime` | `Kernel/Diagnostics`, `Kernel/Infrastructure`, `Kernel/Numerics`, `Kernel/Text` |

## How the rules are enforced

A file's layer is the first folder of its path under `TRNGScriptCompiler/`, and every `TRNGScriptCompiler.`
namespace its text names counts as a reference, in an XML doc cref or a comment too. A type of the root
namespace is visible to every file without a `using`, so the name of such a type counts as a reference to the
layer whose folder holds it. `ArchitectureTests` fails when:

1. a source file lives outside the layer folders, or declares a namespace other than its folder's, or other
   than the root for a public type outside `Hosting/` ([Public surface](#public-surface)
   and `LibrarySources_LiveInKnownLayers_WithMatchingNamespaces`);
2. a file names a namespace outside the layer map,
   or a layer its own may not reference (`LibraryReferences_RespectTheLayerEdgeTable`);
3. a Kernel file lives outside the Kernel folders,
   or names a Kernel folder its own may not reference (`KernelReferences_RespectTheKernelEdgeTable`);
4. either table holds a cycle (`AllowedEdges_FormAnAcyclicGraph`);
5. a file of the publication stage, `OutputPublisher`, `OutputStaging` or `TRNGCompiler`, names `PathResolver`
   other than as `PathResolver.Uncached` ([Run state](#run-state)
   and `PublicationStage_NamesPathResolverOnlyAsUncached`).

For example, a `using TRNGScriptCompiler.Script;` in `Ng/NgParser.cs` fails the second check on Windows with
`Ng\NgParser.cs: NG must not reference Script ('TRNGScriptCompiler.Script').` A `global using` of a library
namespace would reach every file while the scan counts it against the layer of one,
so the library declares none.

`ArchitectureGuideTests` reads the layer table, the Kernel table and the public surface table of this page.
It fails on each folder, edge or public type that the page states and the literals of `ArchitectureTests`
lack, and on each that they hold and the page lacks, so the two cannot differ.

A new layer folder takes an entry in `s_folderLayers` and `s_allowedEdges` of the test and a row in the table
above, and a new Kernel folder takes an entry in `s_kernelEdges` and a row in the Kernel table.

## A compile, step by step

1. `ScriptAnalyzer` reports the findings of `TRNGCompilerOptions.Validate()` as the run's first diagnostics,
   and an error among them stops the run. It then stops the run when the script folder holds no `Script.txt`.
2. It reads the constants file once, warns about the language `.dat` files in the script folder, and builds
   the NG command definitions from that read.
3. It loads `Objects.h`, discovers the plugin scripts and builds the constant catalog
   from the constants file, `Objects.h` and the plugin scripts,
   with the `PluginRegistry`, the `ConstantResolver` and the `ExpressionEvaluator`, each once per run.
   It is therefore the composition root: every parser receives its collaborators through its constructor.
4. `ScriptParser.ReadSource` reads `Script.txt` with includes and continuations, normalizes and splits
   each command line once, and collects the `Plugin=` lines of every file. The registry records them before
   the parse reads a number, so a plugin constant finds its plugin wherever the line stands, and Script holds
   no reference to Plugins.
5. `ScriptParser.Parse` records every file's `#DEFINE` values, then collects the sections into a
   `ParsedScript` through its section builders. It tells a classic command by `ClassicCommandCatalog` and an
   NG command by the NG command names, without regard to case. It fails on any other command where it
   stands, and compiles nothing.
6. `LanguageSet` parses the first language once, through the overlay, and `NgScriptCompiler` then compiles the
   collected NG lines against it, so a string argument resolves whatever section the `[Language]` section
   follows. The `ScriptModel` pairs the result with the parsed script, whose classic lines stay unencoded.
7. `LanguageSet` chooses the main language, parses the other languages and checks them against the first.
8. `OutputComposer` composes `Script.dat` in memory through `ScriptDatComposer`, which compiles the classic
   sections through `ClassicSectionCompiler`, encodes the classic payload and the NG header, and applies the
   header scramble the script asks for.
9. It checks the `IF_CRYPTIC` images without changing them, and encodes every language `.dat`, of which it
   keeps the main language's. Because the analysis composes every output, it reports every error a compile
   reports but a failure to write a file.
10. `Compile` runs the analysis and, when it succeeds, publishes in the same run. `OutputPublisher` encrypts
    the images through `ImageEncryptor` and moves every output into place through `OutputStaging`;
    [PD-23](differences/imports-images-and-publication.md#pd-23-publishing-the-outputs) gives the order of the
    moves, the restore after a failed move and the cleanup of the stale files.

## Trusted input

A script and the files it names are trusted input. A path in a script may be absolute or a network path,
and the compiler reads what it names;
[Absolute paths](differences/includes-and-paths.md#absolute-paths) says how each system reads a root.
[PD-94](differences/includes-and-paths.md#pd-94-input-limits) gives the input limits, which guard against runaway
input.

## Run state

The facade builds a `CompilationRun` for each call and keeps none, so overlapping analyses never share
defines, diagnostics or log lines. A compile uses one run for its analysis and its publication, so the
publication knows the files the analysis read.

| `CompilationRun` member | Holds |
| --- | --- |
| `Diagnostics`, a `DiagnosticSink` | The run's errors, warnings and hints; an error or a warning writes its line to the log, a hint writes none, and an error fails the run |
| `Logger`, a `CompilerLog` | The log text, handed line by line to `TRNGCompiler.LogEntryProduced` |
| `Defines`, a `DefineRegistry` | The `#DEFINE` values, each visible only in the file that declares it, keyed on the file's full path (PD-85 in [the parity log](PARITY-DECISIONS.md)) |
| `PathResolver` | The folder listings the analysis reads, each kept for the rest of the run, which is safe because the analysis writes no file; publication resolves through `PathResolver.Uncached` instead, which `ArchitectureTests` enforces for the files of the publication stage |
| `RecordFileRead`, `WasFileRead` | The files the run read, which the stale-file cleanup never deletes |

## Single owners

Each concern below has one owner, so the parts that share it cannot disagree.

| Concern | Owner | Consequence |
| --- | --- | --- |
| Windows-1252, the code page VB6 reads and writes | `Vb6Ansi`, and for the console host's own `-Log` file the encoder in `CliLogFile` | Every output string is encoded through `Vb6Ansi`, and a `\xNN` escape decodes through its byte table. The library keeps `Vb6Ansi` internal, and the host writes only its own log, so the host gets the code page from the framework |
| Section headers | `ScriptSectionHeaders`, keyed by `ScriptSectionType` | The parser, the `Script.dat` encoder and `LevelSectionBuilder` read one table, so reordering the enum cannot break a lookup |
| Text inputs | `TextInputReader`, over `VirtualFileSystem.ReadAllText`, and `TextFileUtilities.SplitLines` | Every text input, `@file` texts included, is read through the overlay and decoded by its byte-order mark ([PD-27](differences/text-and-its-encoding.md#pd-27-text-files-with-a-byte-order-mark)). `TextInputReader` warns about the encoding note at the file, and each reader keeps its own reaction to a failed read. A host's unsaved text splits into the lines of the saved file |
| Path casing | `PathResolver` | Every path the compiler opens resolves to its on-disk casing, and a backslash in a path the script names separates folders on every system ([PD-29](differences/includes-and-paths.md#pd-29-path-casing)). The run's `PathResolver` caches its listings ([run state](#run-state)); the option helpers also list afresh through `PathResolver.Uncached` |
| Numeric literals | `NumericLiteral` | A literal reads one way in a script, a language file, the constants file and a plugin script, and one that VB6 reads differently fails ([PD-51](differences/numbers-names-and-arguments.md#pd-51-numbers-that-read-wrong)) |
| Numeric expressions | `ExpressionEvaluator` | Every numeric argument and setting of a script evaluates by one grammar of literals and names joined by `+` and `-`, and classic and NG fields take one width check |
| Classic commands | `ClassicCommandCatalog`, with a `ClassicCommandInfo` per command | The parser's classification, the encoder's handler and layout, the canonical spelling and name constants, the sort key and the repeat check read one table |
| NG command rules beyond the constants file | `NgCommandPolicies` | The duplicate rule, the `[Title]`-only rule, the word form and the array groups of a command come from one table |
| A layout size that Script and NG both write or check | The lowest layer both reach, such as `NgImportFile` and `ParsedScript` in Model | The import name field and the extension slot count are each defined once, because Script and NG may not reference each other |
| NG payload words | `NgWords` | Every encoder of the NG payload packs dwords, bytes, text, section counts and chunks the same way, and the NG header and the ExtraNG header of a language `.dat` share its framing |
| String references | `StringReferenceResolver` | Classic and NG commands share this resolver for an `&HEX` or text reference against the first language ([PD-92](differences/numbers-names-and-arguments.md#pd-92-a-binary-string-reference)), and it rejects `!NNN` in a classic command ([PD-11](PARITY-DECISIONS.md)) |
| The `Script.dat` header scramble and the image encryption | `LegacyCipher` | Both transforms change a buffer in memory and touch no file |

## Public surface

The types below are the library's whole public surface. `ArchitectureTests` holds the same list and fails when
a public type is added or removed without an edit to it, and `ArchitectureGuideTests` fails when the list and
this table differ. Every other type is `internal`,
and only the test assembly sees the internals, through `InternalsVisibleTo`.

A public type declares the root namespace, whichever layer folder holds it,
so an embedder that compiles a script imports `TRNGScriptCompiler` alone.
The editor host keeps the namespace of its folder, `TRNGScriptCompiler.Hosting`,
which an editor imports beside the root.

| Area | Namespace | Public types |
| --- | --- | --- |
| Compile entry points | `TRNGScriptCompiler` | `TRNGCompiler`, `AnalysisResult`, `CompileResult`, `TRNGCompilerOptions`, `MainLanguageSelector`, `VirtualFileSystem` |
| Diagnostics and logging | `TRNGScriptCompiler` | `CompilerDiagnostic`, `DiagnosticSeverity`, `LogEntry`, `LogEntryProducedEventArgs`, `LogSeverity` |
| Editor hosting | `TRNGScriptCompiler.Hosting` | `CompilationHost`, `IDiagnosticsProvider`, `DiagnosticsResult` |

- A result carries its success flag, its diagnostics, its log and the files it chose or published.
  The parsed model and the composed outputs stay in internal members of `AnalysisResult`, which a compile
  publishes and the tests read.
- The XML docs of each public type own its contract, such as the thread a `LogEntryProduced` handler runs on,
  the `null` line of a diagnostic tied to no line,
  and the files `TRNGCompiler.Analyze` reads from its overlay.

## Hosts

- `CompilationHost` is the diagnostics host that an editor such as TombIDE embeds. It runs one analysis per
  request over the editor's unsaved buffers, with concise logging.
- A new request cancels the one in flight, which returns a canceled result unless its analysis already composed
  the outputs. Each result carries a rising generation number, so an editor drops a canceled result and any
  result older than the latest it has shown.
- The console host builds against the public surface alone and declares no public type. It takes the engine
  folder or NG Center's command line ([PD-91](differences/console-host.md#pd-91-ng-centers-command-line)),
  compiles, and prints each entry that `LogEntryProduced` streams. `--help` prints the help alone,
  `--version` prints the version alone, and a compile opens with the banner.
- `CliApplication` runs the command line and reports the compile. `InteractivePrompts` asks the folders of
  interactive mode, and `CliLogFile` writes the `-Log` file.
- The console host writes error lines, the line `Compilation failed` or `Compilation canceled` and the
  notice of the first Ctrl+C to standard error,
  and every other line, warnings and the log included, to standard output.
- The first Ctrl+C during a compile cancels the token that `TRNGCompiler.Compile` takes, which it checks until
  publication starts, so a publication that has started runs to its end and the engine folder keeps its last outputs.
- A second Ctrl+C ends the process at once, which can leave a publication half done. A Ctrl+C before a compile
  starts, such as at a prompt of interactive mode, ends the process at once too, as does one after the run ended,
  such as at the key prompt.
- `CliProcess` holds that handler and the last-resort handler of an unexpected fault, which prints its message and
  stack trace and exits with 1. `Program.cs` wires the console to `CliProcess` and `CliApplication`, and holds
  the wait for a key, which it skips when the input is redirected.
