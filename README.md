# TRNG Script Compiler (.NET)

Byte-faithful C# port of the original VB6 compiler for Tomb Raider Next Generation (TRNG)
scripts (`NG_Scripter`): a script VB6 compiled compiles to the same bytes, except where the
[parity decision log](docs/PARITY-DECISIONS.md) records a deliberate divergence, as the
[parity rules](CONVENTIONS.md#parity) state.
It compiles `Script.txt`, its includes and its language files into the `Script.dat` and the main
language `.dat` that the TRNG engine reads, as [Inputs and outputs](#inputs-and-outputs) details.

## Requirements

- The .NET SDK and runtime that [CONTRIBUTING.md](CONTRIBUTING.md#requirements) names
- Windows or Linux; [the testing guide](docs/TESTING.md) says how a test that holds on one system alone is
  skipped on the other. CI runs the suite on both ([`ci.yml`](.github/workflows/ci.yml))
- For a change, the Stanza checkout and Vale that [CONTRIBUTING.md](CONTRIBUTING.md#requirements) names

## Build and test

[CONTRIBUTING.md](CONTRIBUTING.md#build-and-test) owns the build, the formatting and the gates a change
passes, and [the testing guide](docs/TESTING.md) owns the test taxonomy,
the parity and coverage scripts and how to run a subset.

## Terms

The pages name the folders and the file of a compile as below, and the XML docs of
[`TRNGCompilerOptions`](TRNGScriptCompiler/Kernel/Runtime/TRNGCompilerOptions.cs) give what each option holds
and defaults to.

| Term | Option property |
| --- | --- |
| Engine folder | `EngineDirectory` |
| Script folder | `ScriptDirectory` |
| Plugin folder | `PluginDirectory` |
| Constants file | `ConstantsFilePath` |

## Inputs and outputs

A compile reads these files:

- **Engine folder:** `Objects.h` for the slot and static names, the images in `pix` that the `IF_CRYPTIC`
  flags of the script name, and the `ImportFile=` files.
  A path the engine folder lacks is also looked up beside the script file that names it,
  as [PD-83](docs/differences/imports-images-and-publication.md#pd-83-import-file-folders) records.
- **Script folder:** `Script.txt`, the `#INCLUDE` files it names and the language files its `[Language]`
  section lists, with the `@file` texts of those files, which are looked up beside the language file that
  names them.
- **Plugin folder:** the `Plugin*.script` files of the folders that the XML doc of
  [`PluginDirectory`](TRNGScriptCompiler/Kernel/Runtime/TRNGCompilerOptions.cs) lists.
- **Plugin library:** the `.dll` that a `Plugin=` line names. A compile checks that it exists in the engine
  folder, warning when it does not and the plugin folder holds no script of that name, and never reads it.
- **Constants file:** the NG command signatures and the global constants
  ([PD-28](docs/differences/constants-objects-h-and-plugins.md#pd-28-the-constants-file)).

A compile writes these files, all into the engine folder:

- **`Script.dat`** and the **`.dat` of the main language**, which is the first listed language unless the
  options name another.
- **The encrypted images** that `IF_CRYPTIC` names, in `pix`.

It writes nothing into the script folder.
[PD-23](docs/differences/imports-images-and-publication.md#pd-23-publishing-the-outputs) says how the outputs
replace the previous ones, which a failed compile leaves in place, and which files the cleanup deletes.
[PD-82](docs/differences/language-files.md#pd-82-the-language-files-a-compile-writes) says which language
files a compile writes.

## Run the console host

```powershell
dotnet build .\TRNGScriptCompiler.Cli -c Release
```

The build writes the console host, `NG_Scripter.exe` on Windows and `NG_Scripter` on Linux, to
`TRNGScriptCompiler.Cli\bin\Release\<target framework>\`, beside the `scripter_constants.txt` it reads.
The host is framework-dependent, so the `net8.0` build needs the .NET 8 runtime and the `net10.0` build
the .NET 10 runtime.

Pass the engine folder, which holds `Objects.h` and the `script` folder with `Script.txt`,
and add any option, such as the main language; `NG_Scripter --help` lists them all,
and `NG_Scripter --version` prints the version and exits:

```powershell
NG_Scripter.exe C:\TRLE --main-language English
```

- **Success:** the log goes to standard output, the last line reads `Compilation completed successfully`,
  and the engine folder holds the new `Script.dat` and main language `.dat`.
- **Failure:** each error goes to standard error as `ERROR: Line 190 of SCRIPT.TXT: <message>`, then
  `Compilation failed` follows, and the engine folder keeps the outputs of the last successful compile.
- **Interactive mode:** `NG_Scripter.exe` without arguments asks for the engine, script and plugin folders,
  and waits for a key at the end.

A compile exits with 0 when it succeeds, with 1 when it fails or when it succeeds but cannot write its `-Log`
file, and with 130 when Ctrl+C cancels it before the publication starts.
[The architecture guide](docs/ARCHITECTURE.md#hosts) says which lines go to standard error
and what Ctrl+C stops.

The console host also takes the path of `script.txt` and the switches of NG Center's command line,
so a batch file that passes that path and NG Center's switches runs with the port's executable in its place.
[PD-91](docs/differences/console-host.md#pd-91-ng-centers-command-line) lists the command lines
that compile differently:

```powershell
NG_Scripter.exe "C:\TRLE\script\script.txt" -NoMsgBox -Concise -Log -NoWait
```

## Releases

A `v*` tag publishes a GitHub release through [`release.yml`](.github/workflows/release.yml): a
framework-dependent zip for Windows and one for Linux, built from the `net8.0` target, so install the .NET 8
runtime. [CONTRIBUTING.md](CONTRIBUTING.md#release) says what a release runs.

## Embed the library

The library ships no package: reference the `TRNGScriptCompiler` project from yours,
which builds for the target frameworks of `Directory.Build.props`.
`CreateDefaults` expects `scripter_constants.txt` beside the application, in `AppContext.BaseDirectory`,
and the project reference copies the file there.

`TRNGCompiler.Compile` analyzes the script, publishes the outputs and returns the result, and
`TRNGCompiler.Analyze` checks a script without writing a file. `LogEntryProduced` streams each log line on the
thread that runs the call:

```csharp
using TRNGScriptCompiler;

TRNGCompilerOptions options = TRNGCompilerOptions.CreateDefaults(@"C:\TRLE", mainLanguage: "English");

var compiler = new TRNGCompiler();
compiler.LogEntryProduced += (_, e) => Console.WriteLine(e.Entry.FormattedMessage);

CompileResult result = compiler.Compile(options);
int errors = result.Diagnostics.Count(d => d.IsError);

Console.WriteLine(result.Success ? "Published" : $"Failed with {errors} error(s)");
```

The padding of the NG security block takes its seed from the timer, so the bytes of two compiles of one script
differ there unless `TRNGCompilerOptions.DeterministicSeed` is set ([PD-07](docs/PARITY-DECISIONS.md)).

An editor that analyzes unsaved buffers uses `CompilationHost`. Each request cancels the one in flight, and a
canceled result carries no diagnostics, so the editor drops it:

```csharp
using TRNGScriptCompiler;
using TRNGScriptCompiler.Hosting;

using var host = new CompilationHost();

TRNGCompilerOptions options = TRNGCompilerOptions.CreateDefaults(@"C:\TRLE");
var dirtyFiles = new Dictionary<string, string> { [@"C:\TRLE\script\Script.txt"] = "[Options]\r\n" };

DiagnosticsResult result = await host.GetDiagnosticsAsync(options, dirtyFiles);

if (!result.IsCanceled)
{
    foreach (CompilerDiagnostic diagnostic in result.Diagnostics)
    {
        string kind = diagnostic.Severity.ToString().ToLowerInvariant();
        Console.WriteLine($"{kind} line {diagnostic.LineNumber}: {diagnostic.Message}");
    }
}
```

[The architecture guide](docs/ARCHITECTURE.md#hosts) describes the hosts with the rest of the
[public surface](docs/ARCHITECTURE.md#public-surface).

## Documentation

- Read [the architecture guide](docs/ARCHITECTURE.md) and [the testing guide](docs/TESTING.md) for how the
  code and the suite are built.
- Read [the parity decision log](docs/PARITY-DECISIONS.md) and
  [the differences from VB6](docs/DIFFERENCES-FROM-VB6.md) for where the port departs from the VB6 compiler.
- Read [CONTRIBUTING.md](CONTRIBUTING.md) before a change, and [CONVENTIONS.md](CONVENTIONS.md)
  and [STYLEGUIDE.md](STYLEGUIDE.md) for the rules it keeps.
- Read [the fixtures readme](TRNGScriptCompiler.Tests/Fixtures/README.md) for the golden fixtures' provenance.

## License

See [LICENSE](LICENSE).
