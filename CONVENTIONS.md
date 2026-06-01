# Conventions

The rules the code keeps, so a change and its review are judged against one list.
A guard test or an analyzer enforces each rule whose **Kept by** column names it;
a reviewer checks each rule marked *review* and cites a file and line.

- A review blocks a change by citing a rule here that the change breaks.
- A rule changes only through an edit to this file.
- CSharpier owns layout, the Stanza analyzers own structure and doc-comment layout,
  and `.editorconfig` owns naming, so no rule here restates or fights them.
- Documentation follows [STYLEGUIDE.md](STYLEGUIDE.md).

## Parity

| Rule | Kept by |
| --- | --- |
| **P1** For every input the VB6 compiler compiled successfully, the produced bytes are identical, except where P2 records a divergence. | the `Parity` tests of [the testing guide](docs/TESTING.md) |
| **P2** A deliberate divergence is recorded in [the parity decision log](docs/PARITY-DECISIONS.md) with the VB6 lines it departs from, where VB6 has code to cite, and its enforcing test, and explained on [the differences page](docs/DIFFERENCES-FROM-VB6.md). | review |
| **P3** The port matches VB6 where the engine reads the bytes, or where scripts and language files written against VB6 compile and read as written. | review |
| **P4** The port rejects input VB6 accepts when it is malformed, contradictory or reads wrong, with an error that names the fix. | review |
| **P5** The port keeps a better result only where VB6's is an oversight that no working script depends on, and accepts a form VB6 rejects only where it reads right. | review |

## Architecture

| Rule | Kept by |
| --- | --- |
| **A1** A library source lives in a layer of [the architecture guide](docs/ARCHITECTURE.md), and a reference follows its edge tables. Read the guide before adding a namespace, a project reference or an `InternalsVisibleTo` grant. | `ArchitectureTests`, `ArchitectureGuideTests` |
| **A2** Implementation types are `internal`, and the public surface is the documented embedding surface, in the namespaces [the architecture guide](docs/ARCHITECTURE.md#public-surface) gives it. | `ArchitectureTests`, `ArchitectureGuideTests` |
| **A3** Inside an internal type, a member is `internal` or `private` unless it implements an interface or overrides a member. | review |
| **A4** A type that builds and drives its collaborators is an orchestration service and takes the `CompilationRun`, as `ScriptAnalyzer`, `ScriptParser` and `NgParser` do. A unit that records a file it reads takes the run too, as `DefineRecorder` does, and one that only reads the define registry may take the registry, as `ConstantResolver` does. | review |
| **A5** A leaf parser or encoder takes the per-run `DiagnosticSink` for its errors, warnings and hints, and the per-run `CompilerLog` only when it writes verbose text, as `LanguageParser` and `LanguageDatEncoder` do; `NgArgumentParser` and `NgCommandEncoder` take the sink alone. | review |
| **A6** Encoders compose bytes in memory and touch no file, so the analysis composes every output and reports every error a compile reports. | review |
| **A7** `OutputPublisher` is the only writer: it encrypts the images in memory through `ImageEncryptor`, and through `OutputStaging` it stages the files and moves them over the old ones. | review |
| **A8** The library never writes to the console. A host shows the log by subscribing to `TRNGCompiler.LogEntryProduced`, as the console host does. | review |

## Bytes

| Rule | Kept by |
| --- | --- |
| **B1** Byte assembly uses `BinaryWriter` for `.dat` streams, `List<byte>` with `BinaryUtilities.WriteWord` and `WriteDWord` for classic command payloads, or a `List<short>` word list with `NgWords` for the NG payload, its command words, chunks and header included, and never a fourth idiom. | review |
| **B2** A static byte lookup table is a `private static readonly byte[] s_...Data` array behind a `private static ReadOnlySpan<byte> ...` accessor, as `LegacyCipher` does, or a C# 12 `static ReadOnlySpan<byte> ... => [...]` property, which needs no array. | review |
| **B3** A flag enum uses `1 << n` bit literals, so the bit index is readable. A value that mirrors a VB6 constant keeps a comment that points at `scripter_constants.txt`. | review |

## Names

| Rule | Kept by |
| --- | --- |
| **N1** Types, members, the accessibility-gated field prefixes, locals and parameters follow the naming rules of `.editorconfig`. A thread-static field keeps the static `s_` prefix, without an extra `t_` segment. | IDE1006 |
| **N2** An identifier spells an acronym as a word (`Id`, `Json`, `Uri`, `Ng`), and prose writes it uppercase. A brand keeps its own casing, and so do `TRNG` as the product name (`TRNGCompiler`) and `NGCenter` where an identifier names the NG Center program. | review |
| **N3** A member does not repeat the containing type's whole name, as `ParsedScript.ParsedScriptSections` would. A word the two share is allowed, as in `NgCommand.CommandName`. | review |
| **N4** A `Try...` member returns `bool`, and its value, when it has one, through `out`. A bare `bool` is allowed, as `Dictionary.TryAdd` does. A member that returns nothing takes no `Try` prefix. A step that reports its own diagnostics and returns a result object returns `null` after an error and takes no `Try` prefix either, as `ScriptParser.Parse` and `LanguageParser.Parse` do. | review |
| **N5** An event is `EventHandler` when it carries no payload and `EventHandler<TEventArgs>` otherwise, with `TEventArgs` named `{EventName}EventArgs`, and never an `Action`. | review |
| **N6** The suffix of a type names its role. A `...Catalog` is a lookup built once, or a static one, a `...Registry` holds state that fills while the run reads the script, and a static table of entries is a plural noun, as `CompilerFileNames` is. An encoder writes the bytes or words of one unit, a compiler drives the encoders or the parsers of a section's commands over parsed sections, as `ClassicSectionCompiler` and `NgScriptCompiler` do, and a composer joins encoded parts into a whole file. A writer writes a file, so only publication has one. | review |
| **N7** A line carrier holds its text as `Text` and its file and line as one `SourceLocation Location`, and a list of arguments is an `IReadOnlyList<string>`. A carrier whose unit marks a term also holds `SourceText`, the first physical line as the file holds it, up to its comment, beside the normalized `Text`. | review |

## Contracts

| Rule | Kept by |
| --- | --- |
| **K1** A public or protected non-nullable reference parameter is guarded with `ArgumentNullException.ThrowIfNull` as the first statement. An internal or private member takes no guard, because in-assembly callers are trusted. | review |
| **K2** A public payload collection is typed as `IReadOnlyList<T>`. | review |
| **K3** An expected failure is reported through the operation's result, not an exception, and the caught exception is attached to the failure so the evidence survives. | review |
| **K4** An exception is caught only where the code can recover from it, translate it, or add context. A catch that only logs and continues hides the fault from the caller and from the log. | review |
| **K5** A file holds one top-level type where it can, and unrelated types never share a file. | review |
| **K6** A public member ships with a consumer, either an in-repo caller or a named external adopter, plus a test. | review |
| **K7** The null-forgiving operator `!` marks only an invariant that is proven but out of the compiler's reach. Flow analysis, an explicit check or a nullability attribute comes first, because `!` hides a real null until far from its cause. | review, `NullForgivingGuardTests` for a `!` after `Assert.IsNotNull` |
| **K8** An options bag is a `sealed record` with an init-only property per member. Each property validates itself, unless the values are checked against the file system or each other, as the paths of `TRNGCompilerOptions` are: then a `Validate()` reports every finding at once. The bag offers a factory such as `CreateDefaults` for its defaults, and a consumer takes them rather than re-deriving them. | review |

## Diagnostics

| Rule | Kept by |
| --- | --- |
| **D1** `DiagnosticSink.Error`, `Warning` and `Hint` are the only way to report an error, a warning or a hint. Each records a diagnostic; an error and a warning write a log line too, and an error fails the run. A hint is a suggestion about input the compiler accepts, so it writes no log line and takes no part in the count of warnings. `CompilerLog` carries progress and verbose text only. | review |
| **D2** `DiagnosticSink.FormatLocation` is the one location formatter. It renders `Line N of name.txt` for a 1-based line and its file, `Line N` for a line alone, and `General` for a diagnostic whose line number is `null`. A diagnostic tied to no line reads so, and no sentinel such as -1 stands for that case. | review |
| **D3** A diagnostic message is a full sentence without trailing punctuation, and it numbers arguments from 1, as VB6 does. It carries no `ERROR:` or `WARNING:` prefix and never names its own line, because the logger and the sink add both. | review |
| **D4** A diagnostic about a source line passes a `SourceLocation` that names the line and the full path of its file, as the source loader resolved it, so an error inside an `#INCLUDE` file can be found. | review |
| **D5** Verbose text goes through `CompilerLog.LogVerbose`, which emits `LogSeverity.Verbose`, so `ConciseLogging` suppresses it, and which builds an interpolated message only when it logs it. | review |
| **D6** A diagnostic's column and length count UTF-16 units in the line as its file holds it, up to its comment, or in the first physical line of a line a `>` continues, and never in the normalized text. A term the source line does not hold as written marks the whole line (column 0, length 0). | review, `DiagnosticSpanTests` |

## Tests

| Rule | Kept by |
| --- | --- |
| **T1** A test pins behavior a user or a caller sees, and fails when that behavior breaks. A new guard or contract test has been seen failing. | review |
| **T2** A test carries exactly one category of the test layers table of [the testing guide](docs/TESTING.md). | `TestCategoryTests` |
| **T3** A test never searches the log text, because a search pins neither the line nor the severity. | `LogAssertionGuardTests` |
| **T4** A byte-comparison test pins `TRNGCompilerOptions.DeterministicSeed`, because of the NG security-block padding that [PD-07](docs/PARITY-DECISIONS.md) records. Only a test that leaves the security block out of the comparison, as the unseeded golden test does, runs unseeded. | review |
| **T5** A test of file-system fault handling makes a move fail by putting a folder where an output goes, or hands the `OutputStaging` constructor a move or a delete that throws. It makes staging fail by turning the engine folder into a file once the analysis ends. It cannot block the staged temp file instead, because that file takes a name unique to the compile. | review |
| **T6** A test that holds the existing output open without delete sharing to fail its move runs on Windows alone, because Linux moves a file whatever holds it open. | review |
| **T7** A test needs no XML doc, its name states what it checks, and a test class or file is named for what it tests. | review |
| **T8** A test asserts behavior, never a constant or a fact of the platform. | review |

## Complexity

| Rule | Kept by |
| --- | --- |
| **C1** A rule that would otherwise be duplicated or negated at several call sites has one home, such as a named predicate, a probe or a shared internal core, and each call site keeps its own reaction. A rule that two layers need, which may not reference each other, lives in the lowest layer both reach: `Kernel/Text` for text alone, as `EnabledState` is, and `Model` for anything that names a model type. | review |
| **C2** A member that only forwards a call adds validation, an abstraction or meaningful domain terminology. | review |
| **C3** A type can be described in one sentence. When that sentence needs "and", the second responsibility moves to a collaborator behind the facade. | review |
| **C4** An internal abstract class or interface has at least two implementations. A public one is part of the embedding surface, where an embedder may hold or implement it. | review |
| **C5** Nothing exists that nothing sets or reads: no member, parameter or option, unless a comment names it as a test seam. | review |
| **C6** A type, layer, option or parameter serves a need the code has today. | review |
| **C7** A method with more than 80 lines of code, or with more than 6 parameters, does one job a split would not make clearer. A line that is blank, or holds only a comment or only a brace, is no line of code. | review |
