# Differences in includes and paths

This page lists where the port differs from the VB6 compiler in includes and paths;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-16: include search folders

The port also finds an `#INCLUDE` file next to the file that includes it,
so an include tree can use subfolders.

VB6 drops the name's folder up to its last `\` or `:`, as `SoloNome` does,
and looks in `<engine>\script\` (`Module1.bas:5031`, `MioModulo.bas:805-818`),
so a folder written with `/` survives. The port falls back to exactly that path.
For `#INCLUDE "x\a.txt"` in `script\Script.txt`:

| | Opens |
| --- | --- |
| Before (VB6) | `<engine>\script\a.txt`; a missing file is an error |
| After (port) | `<engine>\script\x\a.txt`; when that file is missing, `<engine>\script\a.txt` |

When both files exist and differ, the compile fails and names both,
because either choice could surprise the author.

A `/` in the name survives the fallback as it survives in VB6: `#INCLUDE "sub/b.txt"` in `script\x\a.txt`
opens `script\x\sub\b.txt`, and when that file is missing, VB6's `script\sub\b.txt`.

## PD-17: include cycles

An include cycle fails with a diagnostic that names the cause, instead of a runtime error.

VB6 holds open files in a 21-frame stack (`Mod_ScriptDat.bas:131`): `script.txt` and 20 nested includes.
A file that includes itself overflows it. For `Self.txt` holding `#INCLUDE "Self.txt"`:

| | Result |
| --- | --- |
| Before (VB6) | A runtime error that the handler of `CompilaTutto` logs and shows as "Internal error: ...", which ends the compile (`Module1.bas:8813`, `8991-8994`) |
| After (port) | `Too many nested #INCLUDE files, past the limit of 20; check for an include cycle` |

The limit is VB6's 20 nested includes, so every include tree VB6 compiled still compiles.

## PD-29: path casing

Every file the compiler opens resolves on a case-sensitive file system too: the port matches each folder and
file name to the casing on disk. That covers the paths the script names:
a `File=` language file, an `#INCLUDE` file, an `ImportFile=` file,
an `@file` text and the DLL of a `Plugin=` line.

| | `ImportFile= 1,EXTRAS\DATA.BIN,FTYPE_USERFILE,IMPORT_TEMPORARY`, with the file on disk as `extras/Data.bin` |
| --- | --- |
| Before (VB6) | Found, because Windows ignores case (`Module1.bas:7635`) |
| After (port) | Found on a case-sensitive file system too, and `Script.dat` names the file `EXTRAS\DATA.BIN`, as VB6 does (`:7645`) |

- A backslash in a path the script names separates folders on every system,
  because a script is written for the Windows compiler. A backslash in the name of the engine folder itself,
  which Linux allows, stays part of that name.
- When a folder holds two spellings of one name, the spelling asked for wins. Without it,
  the first spelling in ordinal order wins, so the choice does not depend on the order in which the file
  system lists the folder.
- A name the port writes into an output keeps the script's spelling, so `Script.dat` names the language and
  import files as written. The main language `.dat` takes the casing of its `File=` line unless it replaces a
  file of another casing.
- Each output replaces a file whose name differs only in case and keeps that file's name. The port writes
  `Script.dat` where VB6 writes `script.dat` (`Module1.bas:8958`), so on a case-sensitive file system the new
  file would otherwise stand beside the old one.

On Windows too, a kept name differs from the VB6 output, because VB6 deletes the old file before it copies the
new one (`CopiaFile`, `MioModulo.bas:1283-1296`), so its outputs always take the names it composes:

| | An engine folder that holds `script.dat` and `ENGLISH.DAT`, compiled with `English` as the main language |
| --- | --- |
| Before (VB6) | `script.dat` and `English.dat`, the main language name with `.dat` (`Module1.bas:8881`) |
| After (port) | `script.dat` and `ENGLISH.DAT`, and the `Created:` lines and `CompileResult.PublishedFiles` name those two |

A folder scan lists one folder and enters no subfolder, so a link that points at a parent folder cannot loop it,
and a symbolic link to a file counts as the file.

Some behaviors follow the host or compare paths without regard to case, so a script can read differently on a
case-sensitive system than on Windows. The port keeps each of them,
and each subsection below says what happens on both systems and why it stays.

### Path identity without regard to case

- **Windows:** two names that differ only in case name one file, because the file system says so.
- **Case-sensitive system:** the library still counts them as one file. `VirtualFileSystem` rejects a set that
  holds both, and a run records them as one file read. The `#DEFINE` values of the spelling it loads first stand
  for both, and only that spelling warns about its encoding.
- **Why it stays:** script paths resolve without regard to case, so a script cannot tell two spellings apart,
  and a file keeps one identity whichever spelling names it.

### Absolute paths

- **Windows:** in an `ImportFile=` line, `\data.bin` and `/data.bin` join the engine folder,
  as in VB6 (`Module1.bas:7635`), and `C:\data.bin` is an error.
  In any other path a script names, `\data.bin` and `/data.bin` root at the current drive.
- **Case-sensitive system:** in an `ImportFile=` line, `/data.bin` is an error because it is absolute,
  `\data.bin` joins the engine folder, and `C:\data.bin` is an error too, because its path holds a colon.
- **Case-sensitive system, other paths:** in any other path a script names,
  `/data.bin` roots at the file system root,
  while `\data.bin` and `C:\data.bin` are no roots and resolve below the folder the path resolves from.
- **Why it stays:** the host decides what a root is, and a script that writes relative paths reads the same on
  every system.

### Two case variants of an output

- **Windows:** a folder cannot hold `script.dat` and `Script.dat` together.
- **Case-sensitive system:** when the engine folder holds both, the port replaces only the variant
  `PathUtilities.ChooseEntry` picks, which is the exact spelling asked for or else the first in ordinal order,
  and the other variant stays beside it.
- **Why it stays:** only a hand-made engine folder holds both, and the choice is fixed, so each compile
  replaces the same variant.

### Segments without backtracking

- **Windows:** a folder cannot hold `Sub` and `sub` together, so `SUB\a.txt` finds the file whichever way it
  is cased.
- **Case-sensitive system:** `PathResolver.ResolveFullPath` resolves each segment once. With the folders `Sub`
  and `sub` and the file only under `sub`, `SUB\a.txt` picks `Sub`, which sorts first, and misses the file.
- **Why it stays:** the spelling asked for wins, so writing the folder as `sub` finds the file,
  and only a folder that holds two spellings of one name reaches this.

### Trailing characters

- **Windows:** the system drops a trailing dot or space from a file name when it opens the file, so
  `English.txt.` opens `English.txt`.
- **Case-sensitive system:** a trailing dot or space is part of the name, so `English.txt.` opens no file.
- **Why it stays:** the file system owns this rule, and a trailing dot names a real file on Linux, so the port
  passes each name as written.

### The line ending of `-Log`

- **Windows:** the console host writes `LastCompilerLog.txt` with CRLF line endings.
- **Case-sensitive system:** it writes the file with LF line endings, the line ending of the host.
- **Why it stays:** the log is a text file for the tools of the host that wrote it,
  and the `.dat` outputs do not depend on it.

### A Windows path on Linux

- **Windows:** `NG_Scripter C:\TRLE\script\script.txt` names the file `script.txt` in the folder
  `C:\TRLE\script`, so the engine folder is `C:\TRLE`.
- **Linux:** the same text is one file name, because a backslash is no separator there, so the host fails with
  an error that says it names another file than `script.txt`.
- **Why it stays:** a backslash is a legal character in a Linux name,
  so the host cannot tell a Windows path from a name. The help shows its example paths with the separator of
  the host, so they read right where they run.

## PD-37: `#INCLUDE` names

`#INCLUDE` accepts a file name without quotes when the name holds no spaces, a quoted name that holds spaces,
and a quote right after `#INCLUDE`.

VB6 cuts the line at its spaces and reads the name from the second piece only (`Module1.bas:4999-5018`).

| Line | Before (VB6) | After (port) |
| --- | --- | --- |
| `#INCLUDE levels.txt` | A fatal error: "missing inverted commas" (`:5022`) | Includes `levels.txt` |
| `#INCLUDE "my levels.txt"` | A fatal error: "missing inverted commas" | Includes `my levels.txt` |
| `#INCLUDE"levels.txt"` | Not an include: ignored with a warning above the first section, fatal inside one | Includes `levels.txt` |
| `#INCLUDE "a.txt" "b.txt"` | Includes `a.txt` and ignores the rest (`:5016-5018`) | An error that names the quoted name and asks for one `#INCLUDE` line per file |
| `#INCLUDE "levels.txt` | A fatal error: "missing inverted commas" (`:5019-5026`) | An error that says the name opens a double quote that is never closed |
| `#INCLUDE "levels;1.txt"` | A fatal error: the comment cuts the line at the `;`, so the quote never closes (`:5019-5026`) | An error that says the name holds a `;` or `//`, where the comment starts, so an `#INCLUDE` name cannot hold either, and asks to rename the file |

An unquoted name that holds a space fails too, naming the quotes it needs.

## PD-94: input limits

A compile stops at two limits, so a script cannot make it read without end.

- Each input file is read to 256 MiB at most. A script, an include, a language file, an `@file` text, the constants
  file, `Objects.h`, a plugin script, an import file and an image all count.
- A run loads 10000 include files at most, and a file included twice counts twice.

VB6 has neither limit. It reads an import or an image whole into memory by its length (`Module1.bas:7648-7654`,
`8155-8160`), reads the script a line at a time (`:5127`), and counts no include besides the depth of 20 (PD-17).
Nested includes multiply, so a script of a few lines can name billions of loads.

| | A file of 300 MiB, or a script that loads 10001 includes |
| --- | --- |
| Before (VB6) | The compile reads until its memory runs out |
| After (port) | An error that names the file and its limit, or the limit of include files |

The port reads a file through a stream and stops at the cap, so a device such as `/dev/zero`, which reports a
length of 0, ends in the error too.
