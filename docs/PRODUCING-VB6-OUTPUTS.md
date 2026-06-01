# Producing the VB6 outputs

The owner produces the VB6 outputs of [the golden corpus](../TRNGScriptCompiler.Tests/Fixtures/Corpus/README.md)
and of [the quirks golden](../TRNGScriptCompiler.Tests/Fixtures/Quirks/README.md) with the instrumented NG Center
build, which [the fixtures readme](../TRNGScriptCompiler.Tests/Fixtures/README.md#golden-provenance-the-instrumented-vb6-build)
describes. One command compiles every workspace, run from `TRNGScriptCompiler.Tests\Fixtures`:

```powershell
powershell -ExecutionPolicy Bypass -File .\Run-VB6Corpus.ps1 -NGCenterPath 'C:\NG_Center\NG_Center.exe'
```

## Preparing NG Center

- Start NG Center once without arguments and choose an engine folder,
  because the command-line compile refuses to run before NG Center has saved settings.
- Move every `Plugin*.script` out of NG Center's folder; the script refuses to start while one is there.
- Close every NG Center window.
- Copy `LastCompilerLog.txt` out of the engine folder that NG Center's settings name if you want to keep it,
  because NG Center deletes that file there each time it starts.
- Empty the `VB6 Outputs/99999/` folders of an earlier run,
  because the script deletes no file it did not create.
- Leave room in the temp folder: NG Center copies `tomb4.exe`, `ngle.exe` and its other tools into each
  scratch engine folder.

## Running the script

Add `-WhatIf` to print the command lines without running NG Center, and `-Workspace <name>` to compile some
workspaces only; `Quirks` names the quirks workspace. For each workspace, the script:

1. Copies the workspace into a new scratch engine folder and hashes its files.
2. Adds the `sound\LevelSFX Creator\sounds.txt` that NG Center needs at startup, and gives `Objects.h` the
   current time, because NG Center copies its own `objects.h` over an older one.
3. Runs `NG_Center.exe "<scratch>\<workspace>\script\script.txt" -Log -NoWait -NoMsgBox` in a fresh process
   and waits for it to exit.
4. Hashes the inputs again and reads the log.
5. Copies `LastCompilerLog.txt` into `VB6 Outputs/99999/`, unless an input changed; after a compile, it also
   copies the `.dat` files VB6 wrote into `script\`, any new `pix\@imageN.bmp` and a `run-info.txt` with the
   NG Center version, the ANSI code page and the locale.

The summary gives one result per workspace:

| Result | Meaning | What to do |
| --- | --- | --- |
| `compiled` | The log holds "Completed compilation" and no error line, and `script\script.dat` exists | Commit the workspace's `VB6 Outputs/99999/` |
| `failed` | The log holds an error line, which the summary prints | Commit nothing from the workspace; move the line VB6 rejected to a hand-derived test that cites the log, and delete it from the workspace, or delete the workspace when that line is all it holds |
| `inputs changed` | NG Center rewrote an input, and the outputs stay in the scratch folder | Look for a plugin file in NG Center's folder, then compile the workspace again with a new `-ScratchRoot` |
| `not run` | The workspace has no `script\script.txt`, so the script skipped it | Add the script file, or delete the workspace |
| `timed out` | NG Center waited on a message box that `-NoMsgBox` does not cover | Compile the workspace alone with a long `-TimeoutSeconds` and read the box |

A compiled log can still end with "Failed the compiling, there are 2 errors", because VB6 counts each warning
as an error there (`Module1.bas:9444-9457`, `Principale.frm:6792-6795`); the script reads the error lines
instead. The `Main` column names the language NG Center copied to the engine folder,
which is English for every corpus workspace and French for the quirks workspace.
The scratch folders stay in place for inspection; delete them once the outputs are committed.

## NG Center's command line

The script relies on these behaviors of `NG_Center.exe` when its command line holds `script.txt`:

- NG Center takes the engine folder from the `script.txt` path, without its final `\script`, and compiles
  `script\script.txt` there in place of the folder its settings name.
- `-Log` appends the compile messages to `LastCompilerLog.txt` in that engine folder;
  NG Center also appends its own trace to `log.txt` in its own folder.
- `-NoMsgBox` keeps most message boxes closed, and `-Log` writes their text to the log; `-NoWait` makes
  NG Center exit by itself after the compile.
- NG Center picks the last listed language whose `.dat` sits in the engine folder as the main language,
  else the one named English, else the first; a fresh scratch folder holds no `.dat`.
- NG Center deletes every `.dat` in `script\` and every `.dat` but `script.dat` in the engine folder,
  then writes `script.dat` and each language `.dat` into `script\`.
  It copies `script.dat` and the main language `.dat` into the engine folder,
  and the script reports a copy that differs from its source.
- On exit NG Center saves its settings with the engine folder it had before the compile, unless its startup
  failed, in which case the saved folder is the scratch folder. The script backs up `center_settings.bin`,
  reports a change and prints the command that restores the backup.
