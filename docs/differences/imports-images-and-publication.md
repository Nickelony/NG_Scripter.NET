# Differences in imports, images and publication

This page lists where the port differs from the VB6 compiler in imports, images and publication;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-20: the import-file number

The number of an `ImportFile=` file comes from the digits before the last dot of the name as written.

VB6 reads the digits with `Val` into an Integer, which overflows above 32767, and the port fails there.

| | `ImportFile= 10,extras\data,...` in the engine folder `C:\TRLE1.5` |
| --- | --- |
| Before (VB6) | VB6 searches the full path `C:\TRLE1.5\extras\data` for its last dot (`Module1.bas:6943-6967`, `7648`), so it reads the digit `1` from the folder's name |
| After (port) | -1, the number of a name without a dot, as VB6 gives it in a folder without one |

A folder's name says nothing about the file.

## PD-22: encrypting a `Script.dat` shorter than 64 bytes

The header encryption refuses a `Script.dat` shorter than the 64 bytes it scrambles,
instead of reading past its end. VB6's `CriptaScriptDat` always reads and writes 64 bytes.

| | Result |
| --- | --- |
| Before (VB6) | The missing bytes are read as zeros and written back, so the file grows to 64 bytes |
| After (port) | The scramble refuses the bytes and leaves them unchanged |

A compiled `Script.dat` is always longer than 64 bytes, so this guards against a defect rather than a script:
the compile would fail with an error before it writes a file.

## PD-23: publishing the outputs

A compile that fails leaves the previous outputs in the engine folder, so the game keeps working.

VB6 deletes every `.dat` in `script\` and every `.dat` except `script.dat` in the engine folder before it
reads `SCRIPT.TXT` (`Module1.bas:8823-8836`). It then writes every output into `script\` and copies
`script.dat` and the main language `.dat` into the engine folder (`:8884-8973`).
An `IF_CRYPTIC` image is encrypted while `script.dat` is written: VB6 deletes `pix\imageN.bmp`,
then writes `@imageN.bmp` (`:8161`, `8183`, `8505-8508`).

| | After a compile that fails |
| --- | --- |
| Before (VB6) | The engine folder has no language `.dat`, an output can be half written, and an `IF_CRYPTIC` image can be encrypted although nothing else was written |
| After (port) | The previous `Script.dat`, language `.dat` files and images are unchanged |

The port publishes the outputs as one unit, after the analysis composed every output without an error:

1. It encrypts the `IF_CRYPTIC` images in memory.
2. It writes each output, the encrypted images included, to a temp file next to it whose name is unique to the
   compile, and flushes each to disk.
3. It moves the temp files over the outputs, `Script.dat` last, keeping each replaced output as a backup.
4. When a move fails, or throws an exception of any type, it moves every replaced output back from its backup
   and deletes the temp files, so the engine folder holds the previous outputs; an exception then reaches the
   caller. Otherwise, it deletes the backups and `imageN.bmp`.
5. It deletes the stale language `.dat` files: those the old or the new `Script.dat` names,
   and never a file the compile read.

A read-only backup or `imageN.bmp` loses that attribute before the port deletes it, because the new output is
a new file that is not read-only.

VB6 deletes every `.dat` in the engine folder but `script.dat`, so an `ImportFile=` named `custom7.dat` there,
or another tool's `.dat`, is lost; the port keeps both. A failure while moving a backup back is reported with
the backup's name, so the author can restore it by hand. The host's log handler receives the lines of these
steps after the last of them, so a handler that throws cannot stop a move or a rollback halfway.

A process killed between the two moves of an output cannot roll back. It leaves the previous output as its backup,
named `<output>.<id>.bak`, such as `Script.dat.1a2b3c4d.bak`, and nothing at the output's own path.
To restore it, rename the backup to the output's name.

The next compile does not read, replace or delete a backup or a `.tmp` file left behind,
because each compile tags its own files with a new id. Once the outputs are right, delete those files by hand.

## PD-45: rewriting an encrypted image

`Image=` with `IF_CRYPTIC` writes `@imageN.bmp` as a new file.

| | A 300-byte `image4.bmp` and a 400-byte `@image4.bmp` left from an earlier compile |
| --- | --- |
| Before (VB6) | The 300 new bytes followed by bytes 300-399 of the old file, because VB6 opens it without truncating (`Module1.bas:8182`) |
| After (port) | The 300 new bytes |

The VB6 result depends on a file left from an earlier compile, and its tail is not part of the image.

## PD-53: encrypting a short image

`Image=` with `IF_CRYPTIC` refuses an `imageN.bmp` shorter than the 256 bytes the cipher reverses,
and leaves the file in place.

| | A 58-byte `image4.bmp` |
| --- | --- |
| Before (VB6) | Deletes `image4.bmp`, then stops with a runtime error before it writes `@image4.bmp` (`Module1.bas:8161-8170`), so the bitmap is lost |
| After (port) | `image4.bmp is too small to encrypt: expected at least 256 bytes, found 58 bytes`, and `image4.bmp` stays |

## PD-83: import file folders

An `ImportFile=` path that the engine folder does not hold is also looked up in the folder of the script file
that names it.

VB6 prefixes the engine folder to the path as written (`Module1.bas:7635-7640`), as NG documents import paths.
The port looks there first, so a file both folders hold is embedded from the engine folder, as in VB6,
and an absolute path fails, as in VB6. For `ImportFile= 1,data.bin,FTYPE_USERFILE,IMPORT_TEMPORARY` with
`data.bin` only in `script\`:

| | Result |
| --- | --- |
| Before (VB6) | A fatal error: VB6 cannot find the file to import |
| After (port) | `script\data.bin` is embedded |

The fallback only accepts a script VB6 rejects, so no script VB6 compiled embeds other bytes.

## PD-93: the compile banner

The log of a compile shows `**** START COMPILING ****` and the engine folder where publication starts,
after an analysis that succeeded.

VB6's `CompilaTutto` logs the banner as the first step of its compile, when logging is on,
before it deletes the old `.dat` files and reads `SCRIPT.TXT` (`Module1.bas:8817-8842`).

| | The log of a compile whose script has an error |
| --- | --- |
| Before (VB6) | Opens with `**** START COMPILING ****`, then shows the steps up to the error |
| After (port) | Opens with `**** START ANALYZING ****` and logs no compile banner |

The port reads and checks the script in memory before it writes a file,
so a failed analysis starts no compile, and its log does not announce one.

## PD-95: import IDs

An `ImportFile=` ID and an `IMPORT` argument compare by the word the import chunk writes,
so two IDs collide when they write one word.

VB6 compares a repeated ID as written, after it reads -1 as 0 (`Module1.bas:6883`, `7597-7604`).
It compares an `IMPORT` argument as written with the signed word it stored for each import
(`:7467-7484`, `7644`), so an argument from 32768 to 65535 names no import.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `ImportFile= -1,...` and `ImportFile= 65535,...` | Two imports, whose chunks both carry the ID word -1 | An error: the two IDs write one word |
| `ImportFile= -1,...` and `ImportFile= 0,...` | An error: the ID is repeated | Two imports, with the ID words -1 and 0 |
| `ImportFile= 65534,...` or `ImportFile= -2,...`, and an `IMPORT` argument of 65534 | An error: no import has the ID | The argument names the import |

The import chunk holds the ID as a word, so the port compares the IDs as the chunks carry them.
