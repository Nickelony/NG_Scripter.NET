# Testing Guide

The suite lives in `TRNGScriptCompiler.Tests` (MSTest, one assembly), and its parity tests hold the port to
the [parity rules](../CONVENTIONS.md#parity).

## Test layers

| Layer | Category | What it is | Runtime |
|---|---|---|---|
| Unit | `Fast` | Logic called directly rather than through a compile, with no workspace on disk: parsers, evaluators, encoders, CLI parsing, architecture rules. A unit test reads no file but those the build copies beside the suite, such as the constants file `UnitRun` loads, and the architecture rules read the library's sources. | ms |
| Integration | `IO` | Code that works on a workspace on disk: real compilations in disposable temp workspaces (`TestWorkspace`), and a unit that reads its files from one. | ms to s |
| Parity | `Parity` | Golden byte comparisons against the checked-in VB6 outputs, and bytes derived by hand from the VB6 source (`HandDerivedParityTests`). | s |
| Repository | `Repository` | Reads the repository's files without running the compiler. It checks the line ceilings and comment rules of STYLEGUIDE.md and that each Vale length rule keeps a `max`, the test categories of this table, and the ban on searches of the log text. It checks the ban on a `!` that an `Assert.IsNotNull` already covers, and the tables of the architecture guide against the literals of `ArchitectureTests`. It checks the tests and anchors that the parity log cites (`ParityLogGuardTests`). It also checks the shipped constants file against the VB6 resource, which is inconclusive where the VB6 sources are absent. | ms |

Every test carries exactly one of those categories, on the method or on its class, and `TestCategoryTests`
fails on a test with none, two, or one the table does not list. The test project builds for every target
framework of the library, and `dotnet test` runs the suite on each of them. Run a subset with:

```powershell
dotnet test TRNGScriptCompiler.Tests --filter "TestCategory=Fast"
dotnet test TRNGScriptCompiler.Tests --filter "TestCategory=Parity"
powershell -ExecutionPolicy Bypass -File .\Run-Parity.ps1   # parity only, prints PARITY PASSED
dotnet test TRNGScriptCompiler.Tests --filter "FullyQualifiedName~TRNGScriptCompiler.Tests.Ng."   # one folder
dotnet test TRNGScriptCompiler.Tests --framework net10.0   # one target framework
```

## Layout

Each test class lives in the folder of the feature it tests, and its namespace names that folder, such as
`TRNGScriptCompiler.Tests.Script`. A folder named after a library layer holds the tests of that layer's units,
whether a test calls a unit directly or reaches it through a compile.

A test lives in the class of the unit it tests, whether it calls the unit or compiles a whole script.
A class that mixes layers carries the category on each method.

| Folder | What its tests cover |
| --- | --- |
| `Kernel` | The kernel units, in a subfolder for each folder of the library's `Kernel`, which [the Kernel table](ARCHITECTURE.md#layers) lists. `Kernel/Diagnostics` also holds the cross-layer tests of the diagnostic shape, which compile whole scenarios, and `DiagnosticSpanTests` enforces rule D6 of [CONVENTIONS.md](../CONVENTIONS.md) |
| `Model` | The data the formats hand on, such as the split command line, the NG argument value and the string references that classic and NG commands share one resolver for |
| `Constants` | The constants file, `#DEFINE`, `Objects.h` and the numeric reader |
| `Plugins` | Plugin discovery, the plugin registry, and the constants and defines of a plugin |
| `Language` | Language files: their parsing, their `.dat` encoding and the string lookups |
| `Script` | Reading `Script.txt`, building its sections, the classic commands, the classic `Script.dat` payload and the matrix that compiles every classic command |
| `Ng` | The NG arguments, commands and header, and the matrix that compiles every NG command |
| `Pipeline` | A whole compile: its line ends, its text encoding, its publication, its failure modes and its cancellation |
| `Hosting` | `CompilationHost` and the analysis it reports |
| `Cli` | The console host |
| `Parity` | The goldens, the hand-derived bytes, and the VB6 behaviors a parity decision keeps or rejects |
| `Repository` | The guards that read the repository's files, and their helpers |
| `Support` | The shared helpers, such as `TestWorkspace`, `TestScripts`, `UnitRun` and `DiagnosticAssert`, and the guards of the shared inputs |

The fixtures live in `Fixtures`, which the build copies to the output folder.

## Conventions

### Naming

- **Names**: `MemberOrScenario_ExpectedBehavior` (for example
  `ExtraNgSentinel_StopsSectionAndMalformedLineFails`), and a test file for the unit or feature it tests.
- **Attributes**: `[TestMethod]` (+ `[DataRow]`/`[DynamicData]`); do not use `[DataTestMethod]`.
  Dynamic data whose rows are not plain values declares `DynamicDataDisplayName`,
  so failures show `Method(scenario)`.
- **Row names**: a `[DataRow]` whose arguments read badly in a report sets `DisplayName` to
  `nameof(Method) + " (scenario)"`, so a failure still names the method.

### Assertions

- **Assertions**: assert exact values: exact spans `(LineNumber, Column, Length)`, exact byte
  arrays, exact counts. Guards like `Count >= 0` are prohibited; a guard test asserts the exact
  content of its input (constants file, fixtures), so it fails when that input gains or loses content.
- **Diagnostics**: assert an error, a warning or a hint with `DiagnosticAssert` on the run's `Diagnostics`, which
  compares the full message, the line and, when the test gives one, the file. `NoErrorsOrWarnings` lets a hint
  stand, for a script whose `IGNORE` argument earns one.
- **Diagnostic files**: give `file:` wherever the test knows it, such as `workspace.ScriptFile` for the main
  script. `SingleError` has no default file, because it reads the diagnostics alone and a unit test names its
  own file.
- **No diagnostic**: a test that expects none says so with `DiagnosticAssert.NoDiagnostics`,
  on a workspace from `TestWorkspace.CreateClean` when the compile would warn of a missing `Objects.h` or
  extension section. A check that no message holds a marker word still passes when the message is reworded,
  so no test makes one.
- **Reading bytes**: read a field of `Script.dat` through `ScriptDatReader`,
  which gives the bytes of a classic section, the mini-tags of an NG level (`GetMiniTags`),
  the file of an import (`GetImportedBytes`) and the level paths. A search over the whole file could match the
  random security padding, so none remains.
- **Byte searches**: `ByteSearch` stays where no reader exists: the chunks of a language `.dat`,
  which holds no padding, and one classic command among others in a section,
  which it searches in that section's bytes alone.
- **Null-forgiving operator**: `Assert.IsNotNull` tells the compiler a name is not null,
  so `NullForgivingGuardTests` fails on a `!` after it
  on the same identifier or member access in the same method.

### Logs, console and guards

- **Log lines**: assert a log line that is not a diagnostic, such as a progress or a verbose line, by the
  severity and the exact message of its `LogEntry` (`TestCompileResult.Entries`).
- **Log text**: rule T3 of [CONVENTIONS.md](../CONVENTIONS.md#tests) bans a search of the log text,
  and `LogAssertionGuardTests` catches a substring, a regex match or a line found in the text,
  read directly or through a variable or a field that holds it.
- **Console text**: the text and the lines that a test's captured console records count as log text,
  so a search of them fails the guard too. A test pins a line by its index,
  or compares the lines it expects in full.
- **Console members**: `LogAssertionGuardTests` keeps the members and calls that the guard reads as the
  console text.
- **Log comparisons**: an exact comparison of a log line, such as `Assert.AreEqual` on the last line of a
  `Split`, is no search and is allowed, and so is the log text passed as a failure message. The guard reads
  syntax alone, so it misses the log text a method returns.
- **Guard controls**: a Repository guard keeps its scan in a function over its input,
  such as text or a test's categories.
  It runs that function on a small sample that breaks the rule, so a guard that stops catching its breach fails.

### Units and seams

- **Units**: a test of one unit builds it with the real per-run objects of `UnitRun`,
  and cites the VB6 lines it follows, such as `Module1.bas:5711-5733` for the classic command sort.
- **Unit scripts**: `UnitRun` holds the in-memory files of a script (`ForScript`, `With`),
  and builds the source loader (`CreateLoader`) and the script parser (`ParseScript`) over them.
- **Test seams**: the library holds an internal seam only where a test must hold or fix timing,
  fault a file move or delete, read a count or list it keeps to itself, or build a log without a callback.
  Each seam is documented as one.
- **Seams in use**: the `CompilationHost(TRNGCompiler)` constructor lets a test hold an analysis
  at a log line, and `TRNGCompiler.TimerSeconds` lets a test fix the clock that `Vb6Random` reads in an unseeded compile.
- **Staging seam**: the `OutputStaging` constructor takes a move and a delete, so a test can fault one and pin
  the error or the warning that follows (`OutputStagingTests`).
- **One level per scenario**: the test that pins a rule sits at the level that owns the rule,
  such as the loader for which file an `#INCLUDE` opens or the generator for a seeded sequence.
- **Compile beside unit**: a whole-compile test of a scenario stays only where it asserts
  what the unit cannot show.
  Examples are the line and file of the diagnostic, an I/O fault the compile attaches,
  and the option that hands a seed to the encoder.

### Workspaces and script text

- **Workspaces**: use the shared `TestWorkspace` builder. It creates a unique directory,
  writes files immediately (`WithScript`, `WithLanguageFile`, `WithBinaryFile`, `WithTextFile`),
  and exposes `Compile`/`Analyze` helpers plus `ReadScriptDat`/`ReadOutput`.
  Clean up with `using`; `TestAssembly` removes the shared root after the run.
- **Temp root**: `TestWorkspace.TempRoot` lies below the test output folder, in a folder unique to the run,
  and no test uses the system temp folder, `Path.GetTempPath()`.
- **Why not the system temp folder**: a compile lists each folder above the engine folder,
  and the system temp folder is often crowded, which slows every compile that runs below it.
- **Checkout location**: the temp root follows the checkout, so a checkout below a crowded folder still pays
  that cost.
- **Script text**: `TestScripts` holds the `[Language]` section that lists `English.txt`,
  the smallest `[Level]` section that compiles and its `LoadCamera=` line,
  and a test joins them to the lines it is about.
- **Own script text**: a test keeps its own copy of a block when the exact text is the point.
  Examples are the columns of a diagnostic, the line numbers a continuation joins, a line ending,
  the bytes of a hand-derived layout, and a variant such as another level path.
- **Locks and denied folders**: `TestWorkspace.WithFileLocked` runs an action while a handle holds a file open
  without sharing, and `WithListingDenied` runs one while a folder denies its listing,
  which only Windows offers.
- **Case-sensitive folders**: a test of the case-sensitive path handling builds its workspace with
  `TestWorkspace.CreateCaseSensitive`, which marks the folder case-sensitive on Windows and makes the test
  inconclusive where the file system refuses.

### Platforms

- **Platforms**: the suite runs on Windows and on Linux, and CI runs it on each. A test that cannot hold on
  Linux carries `[OSCondition(OperatingSystems.Windows)]` and a comment that says why,
  as rule T6 of [CONVENTIONS.md](../CONVENTIONS.md#tests) requires of a test that holds an output open.
- **Linux names**: a test that needs a name only Linux allows, such as a folder name that holds a backslash,
  carries `[OSCondition(OperatingSystems.Linux)]` and a comment that says why.
- **Symbolic links**: a test that creates a symbolic link is inconclusive on a Windows machine without the right
  to create one. The Linux job always runs it, as it does
  `PathUtilitiesTests.EnumerateFilesInsensitive_ListsASymbolicLinkToAFile`, which pins part of [PD-29](PARITY-DECISIONS.md).

### Parity and fixtures

- **Byte comparisons**: the builder's `CreateOptions` pins by default the seed that rule T4 of
  [CONVENTIONS.md](../CONVENTIONS.md#tests) asks for.
  [PD-07](PARITY-DECISIONS.md) lists the tests that compile without a seed or pin the timer seed.
- **Shipped constants**: the test that compares the stock constants file with the resource of the VB6 build
  ([PD-28](PARITY-DECISIONS.md)) stays inconclusive without `Original Compiler/`,
  which a clone lacks ([CONTRIBUTING.md](../CONTRIBUTING.md#repository-layout)), so no CI run compares the file.
- **Fixtures**: golden outputs are never regenerated. New byte coverage is hand-derived from the VB6
  format and lives in `HandDerivedParityTests`, or is a new workspace of
  [the golden corpus](../TRNGScriptCompiler.Tests/Fixtures/Corpus/README.md), whose VB6 outputs
  the owner produces.

### Build and run

- **Parallelization**: every test may run beside any other (`[assembly: Parallelize]` at method level, in
  `Support/TestAssembly.cs`), the parity tests included, so a test writes only below its own temp directory,
  builds its own compiler and changes no process-wide setting.
- **Warnings**: the test project builds with `TreatWarningsAsErrors`,
  and a compiler or MSTest analyzer warning is fixed rather than suppressed.
  The recommended CA rule set does not apply to tests, so test names keep their underscores.

## Architecture rules

`ArchitectureTests` (category `Fast`) checks the library's sources against the layer rules that
[ARCHITECTURE.md](ARCHITECTURE.md#how-the-rules-are-enforced) owns. A new layer folder or edge fails the suite
until the test's tables hold it, and the guide's tables change in the same edit.
`ArchitectureGuideTests` (category `Repository`) reads the guide's tables and fails on each layer,
edge and public type that the guide and the test's tables do not share.

## Coverage

```powershell
powershell -ExecutionPolicy Bypass -File .\Run-Coverage.ps1                     # the default floor
powershell -ExecutionPolicy Bypass -File .\Run-Coverage.ps1 -LineThreshold 0.95  # a stricter floor, for example
```

The script runs the suite once per target framework with `--collect:"XPlat Code Coverage"` and a TRX log, and
writes both to `artifacts\test-results\<framework>`. It measures line coverage of the `TRNGScriptCompiler`
assembly from the Cobertura report of each framework, and fails when a framework fails a test or falls below
the floor. `Run-Coverage.ps1` owns the default floor and the time after which it ends a hung test run,
and CI runs the script with both.

## Fixtures

See [Fixtures/README.md](../TRNGScriptCompiler.Tests/Fixtures/README.md) for golden provenance (instrumented
VB6 build, seed injection), the compared outputs (`Script.dat` + `English.dat`),
and the hand-derivation methodology used for new byte-level coverage.

The seeded goldens of `Fixtures/Workspace` use the seeds 12345, 54321 and 99999, and
`GoldenParityTests.SeededCompile_MatchesTheVb6Outputs` compiles the workspace once with each.
The fixtures readme says [what the seed is and which build injects it](../TRNGScriptCompiler.Tests/Fixtures/README.md#golden-provenance-the-instrumented-vb6-build).

`Fixtures/Stock/Objects.h` is the stock `Objects.h` that tests compile against,
`TestFixtures.StockObjectsHPath`, a copy of the one the corpus workspaces hold,
so a change to a golden workspace never changes what those tests read.
`StockObjectsHGuardTests` fails when a corpus copy differs from it.
