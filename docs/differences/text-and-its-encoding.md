# Differences in text and its encoding

This page lists where the port differs from the VB6 compiler in text and its encoding;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-27: text files with a byte-order mark

A text input saved with a UTF-8, UTF-16 or UTF-32 byte-order mark compiles: the port detects the mark,
decodes the file accordingly and warns about the file.

| | A `SCRIPT.TXT` saved as UTF-8 with a byte-order mark |
| --- | --- |
| Before (VB6) | The mark's bytes `EF BB BF` are read as text in front of `[Language]`, and the compile fails |
| After (port) | The file compiles as if it were saved without the mark |

A language file saved with a mark fails in VB6 the same way.

A file without a mark is read as Windows-1252, as VB6 reads it.
When such a file is valid UTF-8 with a character outside ASCII, the port warns and names the fix,
because an editor that saves `é` as UTF-8 without a mark ships it as `Ã©`.
The rule covers the script, language, constants and plugin files,
`Objects.h` and the `@file` texts of ExtraNG strings.

VB6 reads an `@file` text as raw bytes (`Module1.bas:1865`), so it compiles one with a mark,
and the mark and every UTF-8 sequence become Windows-1252 text: a UTF-8 `Café` is written as `ï»¿CafÃ©`.
The port writes `Café`.

## PD-30: characters Windows-1252 cannot hold

A character that Windows-1252 cannot represent fails the compile. Only a file with a byte-order mark can carry
such a character (PD-27), because a file without one is read as Windows-1252 byte by byte.
The rule covers language strings, ExtraNG strings, level paths, language file names, extensions of the
`[PCExtensions]` and `[PSXExtensions]` sections, and `ImportFile=` file names.

| | `Café →` in a UTF-16 language file |
| --- | --- |
| Before (VB6) | The file does not compile, because VB6 reads it as Windows-1252 |
| After (port) | An error naming the value that holds `→` |

Windows-1252 writes a character it lacks as a look-alike, such as `A` for `Ā`, or as `?`,
which would put text in the game that the author never wrote.

## PD-38: text comparison does not depend on the Windows locale

String lookups compare text without regard to case, character by character, on every machine,
with `OrdinalIgnoreCase`.

| | `Pickup= 1,Straße,...` with `Strasse` listed before `Straße` |
| --- | --- |
| Before (VB6) | Compares through the Windows locale (`Option Compare Text`, `Module1.bas:4` and `6592`), where `ß` can equal `ss`, so the result can depend on the machine |
| After (port) | Finds `Straße` on every machine |

A compiler whose output changes with the Windows locale cannot be reproduced on a build server or another
author's machine.
