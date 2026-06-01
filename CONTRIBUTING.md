# Contributing

How to build, test and format the repository, and what a change needs before it is ready.

## Requirements

- Install the .NET SDK that [`global.json`](global.json) names, or a later feature band of that version,
  which its roll-forward accepts.
- Install the .NET 8 runtime, because the suite runs on each target framework `Directory.Build.props` lists.
- Install PowerShell, which runs the scripts at the root.
- Install [Vale](https://vale.sh/docs/install), which checks the prose of the pages and the comments.
- Clone [Stanza](https://github.com/Nickelony/Stanza) beside this repository, as `..\Stanza`.
  The build loads its analyzers from that checkout, and `format.ps1` builds its tools there.

The build takes another checkout location from `-p:StanzaRepoRoot=<path>` or an environment variable of the
same name, and `format.ps1` takes it from the environment variable alone.
Without a checkout the build still passes, but the `ZA` pins in `.editorconfig` have no effect and
`format.ps1` stops.

## Build and test

```powershell
dotnet build .\TRNGScriptCompiler.slnx
dotnet test .\TRNGScriptCompiler.slnx
```

- [The testing guide](docs/TESTING.md) owns the test taxonomy, the parity and coverage scripts,
  and how to run a subset.
- The repository guards read the repository's files rather than run the compiler:
  `dotnet test .\TRNGScriptCompiler.slnx --filter TestCategory=Repository`.

## Repository layout

- `TRNGScriptCompiler/` holds the compiler library, which [the architecture guide](docs/ARCHITECTURE.md) maps
  to its layers.
- `TRNGScriptCompiler.Cli/` holds the console host, which builds as `NG_Scripter.exe`.
- `TRNGScriptCompiler.Tests/` holds the MSTest suite,
  including the golden byte-parity tests and their fixtures.
- `Original Compiler/` is where the VB6 sources go, as the parity reference outside the build. Git ignores it,
  so a clone lacks it.
- To get the sources, download `VB Progetti_TRNG.rar` from
  [the TRNG source page](http://www.trlevelmanager.eu/trng_source.htm) and unpack it into
  `Original Compiler/`, so that `Original Compiler/MioModulo.bas` and
  `Original Compiler/Progetti/NG_Scripter/Module1.bas` exist.
  The citations of [the parity decision log](docs/PARITY-DECISIONS.md) number the lines of the maintainer's
  instrumented copy, so a pristine download needs [the offsets below](#vb6-citations).
- The tests that read the sources, such as the one for
  [PD-28](docs/differences/constants-objects-h-and-plugins.md#pd-28-the-constants-file),
  are inconclusive without them.
- `docs/` holds the guides.
- `.vale/` holds the Vale style that checks the prose.

## Where parity lives

Byte parity with VB6 is the contract.
[The P rules](CONVENTIONS.md#parity) say where the port may depart from VB6,
[the parity decision log](docs/PARITY-DECISIONS.md) records each behavior on which the two differ,
and [the differences pages](docs/DIFFERENCES-FROM-VB6.md) show each intentional departure.
A row rests on [seeded goldens](docs/TESTING.md#fixtures),
on owner-produced [corpus outputs](TRNGScriptCompiler.Tests/Fixtures/Corpus/README.md) from NG Center,
or on [bytes derived by hand](TRNGScriptCompiler.Tests/Fixtures/README.md#adding-new-edge-case-coverage)
from the VB6 source.

## VB6 citations

The citations of the pages and the code number the lines of the maintainer's instrumented copy of
`Original Compiler/`, which adds [the fixed seed](TRNGScriptCompiler.Tests/Fixtures/README.md#golden-provenance-the-instrumented-vb6-build)
of [PD-07](docs/PARITY-DECISIONS.md) and drops one handler.
A pristine download matches every cited file but these three:

- `MioModulo.bas` gains three lines after line 7, the declarations of `g_DeterministicSeed` and
  `m_DeterministicSeeded` and a blank line, so cited line N from 11 to 848 is pristine line N - 3.
- `MioModulo.bas` also replaces pristine line 846, `Randomize`, with cited lines 849 to 857,
  so cited line N from 858 on is pristine line N - 11.
- `Module1.bas` gains line 8414, the assignment of `g_DeterministicSeed`, so cited line N from 8415 on is
  pristine line N - 1.
- `Principale.frm` lacks pristine lines 5005 to 5012, the handler `Pul_MetaCabinet_Click` and the blank line
  after it, so cited line N from 5005 on is pristine line N + 8.

## The build

- The build runs the compiler, the .NET analyzers and the Stanza `ZA` rules,
  whose severities `.editorconfig` sets, and `TreatWarningsAsErrors` makes each finding an error.
- The build runs the IDE style and naming rules, such as IDE1006, only when `EnforceCodeStyleInBuild` is set,
  and this tree does not set it. `format.ps1` applies the fixes those rules offer.
- The recommended CA rule set applies to the library and the console host only,
  so test names keep their underscores. The SDK sets its level from each target framework,
  so a newer target can bring new findings.
- To separate a behavior failure from a `ZA` layout one, build with `-p:StanzaRepoRoot=none`,
  which drops the Stanza rules.

## Formatting

[`format.ps1`](format.ps1) owns the layout of this repository.
It builds [`stanza-format`](https://github.com/Nickelony/Stanza/wiki/Format-command-reference) and
`stanza-markup` from the Stanza checkout, and that build builds the Stanza analyzers and their code fixes for
`dotnet format` to load. It then runs `stanza-format` over the solution,
and `stanza-markup` over the XML markup files, which are the project, `Directory.Build` and solution files.

```powershell
./format.ps1            # write the changes, changed C# files and every XML markup file
./format.ps1 -All       # write the changes, whole solution
./format.ps1 -Check     # report only, whole solution, exit non-zero when something is off
```

- CSharpier owns layout, the Stanza analyzers own structure and doc-comment layout,
  and `.editorconfig` owns naming and pins each rule's severity.
- `stanza-format` runs the passes in the order the Stanza rules need,
  so no tool undoes the lines another one wrote.
- Markdown, JSON and `.editorconfig` are hand-kept: no formatter rewrites them,
  but `-Check` checks their line endings.
- `dotnet build .\TRNGScriptCompiler.slnx -t:StanzaMarkupCheck` runs the strict markup scan
  once the tree is formatted.

## Verification

A change is ready when these gates pass, in this order:

```powershell
./format.ps1
dotnet build .\TRNGScriptCompiler.slnx
dotnet test .\TRNGScriptCompiler.slnx
vale --glob='!{**/bin/**,**/obj/**,**/TestResults/**,artifacts/**,Original Compiler/**,.codebase-memory/**,.vs/**}' .
```

- `./format.ps1` is the maintainer's gate and takes a long time. An agent or a reviewer runs the other gates
  and reports that it did not format.
- `.github/workflows/ci.yml` builds, tests and checks the coverage floor and the parity category on every
  target framework, on each runner of its matrix. One runner also runs `dotnet format --verify-no-changes`,
  and a separate job runs Vale.
- Each runner also builds `-c Release` and smoke-tests the console host: it runs the built host of each target
  framework on a copy of `TRNGScriptCompiler.Tests/Fixtures/Workspace`, and the step fails unless the host exits
  with 0 and writes `Script.dat`.
- CI runs no Stanza rule, because the runners have no Stanza checkout.
- A docs-only change runs Vale and the `Repository` guards, because no build step reads a page or a readme.
- A review judges the change against [CONVENTIONS.md](CONVENTIONS.md).

## Release

A `v*` tag runs [`release.yml`](.github/workflows/release.yml). The tag must be `v` plus the `Version` of
[`Directory.Build.props`](Directory.Build.props), which the job checks before it publishes anything.
The workflow builds Release, runs the suite on Windows and on Linux, and attaches a framework-dependent zip
of the `net8.0` host for each of `win-x64` and `linux-x64` to the GitHub release.
`NG_Scripter --version` prints that same version.

The version, the authors, the copyright, the repository URL and the description live in the build files alone:
`Directory.Build.props` for the shared identity, and each project file for its own description.

## Adding a command or a parity row

Each checklist names what the change touches, and the guards named in it fail when a step is missing.

### A new NG command

- **Definition:** add its line under `<START_NEW_COMMANDS>` in `TRNGScriptCompiler/scripter_constants.txt`,
  which `NgCommandCatalog` reads. The shipped file is the resource VB6 builds in,
  and `ConstantsResourceTests` compares the two where the VB6 sources are present.
- **Code and policy:** add a member to `NgCommandCode` only when the code names the command,
  and a row to `NgCommandPolicies` only when
  its duplicate rule, `[Title]`-only rule, word form or array groups differ from the default.
- **Sample:** add a line to the options, title or level list of `NgCommandCoverageTests`, with the words
  VB6 writes for it; `Samples_CoverEveryMatrixDrivenCommand` fails without one.
- **Pin:** replace `ConstantsHash` in `GoldenParityTests`, which pins the shipped file.
- **Golden fixtures:** add the line to the `NgOptions` or `NgLevelCommands` workspace of
  [the golden corpus](TRNGScriptCompiler.Tests/Fixtures/Corpus/README.md),
  whose VB6 outputs the owner produces. The seeded goldens never change.
- **Parity row:** a behavior that differs from VB6 needs a row, as the last checklist describes.

### A new classic command

- **Catalog:** add a `ClassicCommandInfo` to `ClassicCommandCatalog` with its sort order, tag, and layout or
  `ClassicHandler`. The parser, the encoder, the canonical spelling, the sort key and the repeat check read that table.
- **Handler:** a new handler adds a `ClassicHandler` value, a method that `ClassicCommandEncoder` dispatches to,
  and its argument count in `GetArgumentCount`, which `TryEncode` checks before it dispatches.
- **Name constant:** a command that other code singles out by name takes a name constant on `ClassicCommandCatalog`.
- **Other sections:** a classic `[Options]`, `[Language]` or extension command belongs to
  `OptionsSectionBuilder`, `LanguageSectionBuilder` or `ExtensionsSectionBuilder` instead.
- **VB6 tables:** keep the command and order tables that `ClassicCommandCatalogTests` holds equal to VB6's.
- **Sample:** add a row to `GetSamples` in `ClassicCommandCoverageTests`, with the bytes VB6 writes;
  `ClassicLevelRows_CoverEveryClassicLevelCommand` compares the rows with `ClassicCommandCatalog.Names`
  and fails without the row.
- **Derivation:** derive the bytes of its ranges and errors by hand from the VB6 source in
  `ClassicCommandEncoderTests`, and cite the lines.
- **Golden fixtures:** add the line to the `ClassicLevelCommands` or `ClassicOptionsExtensions` workspace of
  [the golden corpus](TRNGScriptCompiler.Tests/Fixtures/Corpus/README.md). The seeded goldens never change.
- **Parity row:** a behavior that differs from VB6 needs a row, as the last checklist describes.

### A new parity row

- **Row:** append the row to [the parity decision log](docs/PARITY-DECISIONS.md) under the next free ID,
  with its topic, its decision (`Match VB6` or `Intentional`), its VB6 citation and its enforcing tests.
- **Section:** an `Intentional` row links a `## PD-NN: title` section on a topic page below
  `docs/differences/`, which gives a Before and After example. A new topic page takes a line in
  [the index](docs/DIFFERENCES-FROM-VB6.md).
- **Test:** the row names a test that exists and pins the behavior.
  A byte-level test is hand-derived from the VB6 source, as
  [the fixtures readme](TRNGScriptCompiler.Tests/Fixtures/README.md#adding-new-edge-case-coverage) describes.
- **Guard:** `ParityLogGuardTests` checks each named test and each linked heading, and
  `dotnet test .\TRNGScriptCompiler.slnx --filter TestCategory=Repository` runs it.
