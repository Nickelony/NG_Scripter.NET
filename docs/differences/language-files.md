# Differences in language files

This page lists where the port differs from the VB6 compiler in language files;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-21: language files that write the same output

Two `[Language]` entries whose `.dat` names differ only in case fail the compile,
instead of one output silently replacing the other.

```
[Language]
File= 1, English2.txt
File= 2, ENGLISH2.TXT
```

| | Result |
| --- | --- |
| Before (VB6) | Both compile to the same `English2.DAT` on Windows, and the second overwrites the first |
| After (port) | `Multiple outputs map to the same file name 'ENGLISH2.dat'; give each [Language] File= entry a name of its own` |

The rule holds in any position, the first language's file included: `File= 0, English.txt` followed by
`File= 1, ENGLISH.TXT` fails, naming `ENGLISH.dat`.

VB6 names the `.DAT` after the `File=` entry in `CambiaEst` and writes it in `ScriveLanguageDat`.

## PD-33: language sections are matched by name

`[Strings]`, `[PSXStrings]` and `[PCStrings]` fill their own tables whatever order the file lists them in.

```
[PCStrings]
Pc
[Strings]
Classic
```

| | `Pc` is written to |
| --- | --- |
| Before (VB6) | The `[Strings]` table, and `Classic` to the PSX table, because VB6 numbers the sections in the order they appear (`Module1.bas:2015-2031`) |
| After (port) | The PC table |

VB6 also writes one size word for each section it saw (`:1792-1793`),
so the header of this file is one word short and every offset after it is read one slot off.
The port always writes three counts and three sizes, and it writes the strings grouped by table in the order
`[Strings]`, `[PSXStrings]`, `[PCStrings]`.

The game reads each table for its platform, so the VB6 output loses the PC strings whenever a file skips or
reorders a section.

## PD-34: the main-language ExtraNG check

The main language must hold every ExtraNG index the first language holds,
unless the main language is the first one.

| | `[Language]` lists French, then English; English is the main language and lacks an ExtraNG index |
| --- | --- |
| Before (VB6) | Compiles, because VB6 skips the check when the main language's file is named `english` (`Module1.bas:8903`) |
| After (port) | Fails, naming the missing index |

The check exists so the shipped language shows every ExtraNG string, and the file name says nothing about
whether it does.

## PD-44: a language file with bare line feeds

A language file saved with bare line feeds compiles like the same file saved with CRLF.

| | `English.txt` saved with bare line feeds |
| --- | --- |
| Before (VB6) | The whole file is one line, so the `.dat` holds almost no strings |
| After (port) | Every line is its own string |

## PD-48: the spaces around a classic string

A classic language string is written without its surrounding spaces,
so the data matches the offsets in the header.

VB6 sizes every string from its trimmed text (`Module1.bas:2002`, `2060`),
but it trims the text it writes only when the line holds a colon (`:2246-2266`).

| | `Pistols ; note`, followed by `Shotgun`, in `[Strings]` |
| --- | --- |
| Before (VB6) | Sized as `Pistols` and written as `Pistols ` with its space, so the offset of `Shotgun` and of every later string points one byte early |
| After (port) | `Pistols`, and every offset points at its string |

A trailing `\x20` escape gives the same VB6 result, because the escape is decoded after the trim.

## PD-50: an error in a language file

An error in any language file fails the whole compile, and the port publishes no output.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `5: Bad \xZZ` in `[ExtraNG]` | Reports the error and writes the text undecoded (`Module1.bas:1874`) | Fails the compile |
| `5: @missing.txt` | Reports the error after writing the classic part of that language's `.dat`, so the `.dat` lacks its ExtraNG strings (`:1781-1830`, `1858-1864`), and copies it to the engine folder when it is the main language (`:8968`) | Fails the compile |
| A listed language file that does not exist | A warning (`:8891-8894`) | Fails the compile |

VB6 reports these errors and still publishes, so the game runs with a raw escape in a string, without the
ExtraNG strings of a language, or without one of its languages.

## PD-58: language-file checks

A language file fails on the lines below, where VB6 writes a file whose strings the game reads at the wrong
index or not at all.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `   ; note` in `[Strings]` | An empty string, because VB6 tests the first character of the line (`Module1.bas:2012`), so every later index moves | An error that names both fixes: remove the indent, or write a line of spaces for an empty string |
| A tab, or spaces and a tab, before the `;` in `[Strings]` | A string that holds a tab, because VB6 has turned the tab into `\t` before the test (`:1993`), so the comment survives it (`:2012`) and every later index moves | The same error as for spaces |
| `[PCStrings] ` with a trailing space, or a tab at either edge | A string of the previous table, which moves every later table (`:2015-2018`) | An error |
| `[PSXStrings] ; psx` or `[ExtraNG] ; ng` | A string of the previous table, so the section never starts. VB6 compares the whole line with the header names (`:2015-2018`) and cuts the comment only to size the text (`:1997`, `2188`; sized at `:2060`). Above the first header it drops the line (`:2049`) | An error that says to move the comment to a line of its own |
| A second `[Strings]` header | Numbered as the next table (`:2028-2031`) | An error |
| `100: One` and `100: Two` in `[ExtraNG]` | Both written, and lookups find the first (`:2133-2138`) | An error; an identical repeat warns |
| `40000: Text` in `[ExtraNG]` | An error that VB6 reports and passes over: the index is written cut to 16 bits (`:2119-2126`, `1849`) | An error, also for a number past 32 bits, such as `99999999999` |
| `12a: Text` in `[ExtraNG]` | An error, because `IsNumeric` rejects the index (`:2109-2116`); `&H82: Text` passes `IsNumeric`, and `Val` reads it as 130 (`:2118`) | An error naming the index; `&H82` is 130 as in VB6 (PD-51) |
| A `;` in an `@file` text | Everything after it in the file is dropped (`:1874`) | An error that names `\x3B` |
| A main language whose `[Strings]` count differs from the first language's | A warning (`:8941-8951`) | An error; another language warns |
| `File= 0, lang\English.txt` | `Script.dat` names `lang\English.DAT`, which VB6 never copies to the engine folder (`:6768`, `8968`) | An error: the language file must sit in the script folder |
| Classic strings whose section size or offset passes 65,535 bytes | Sizes and offsets cut to 16 bits (`:1784-1798`) | An error |
| An ExtraNG string of 65,536 words or more, about 128 KiB | The word count cut to 16 bits (`:1879`), so the count differs from the words that follow it | An error that says to shorten the string or split it into two |
| `¥` (byte 0xA5) or `\x00` in a string | The XOR with 0xA5 (`Mod_ScriptDat.bas:246-288`) leaves a zero there, where the game ends the string | An error naming the character |

`Script.dat` refers to strings by their index in the first language, so a moved index shows the wrong text in
the game.

## PD-59: language-file warnings

The port warns, without changing the bytes, about language lines VB6 compiles without a word but that are easy
to misread:

- a line of spaces or a tag alone, which counts as an empty string;
- a bracketed line that is not one of the four section headers, which is a string;
- a tag that is not upper-case letters, digits and `_` with at least one letter, such as `Note:` in
  `Note: text`, which VB6 removes from the written text (`Module1.bas:2244-2263`). The warning names the fix:
  a dummy tag in front, as in `T:Note: text`.

## PD-64: the default main language

Without a main-language option, the first language that `[Language]` lists ships, and the log names it.

| | `[Language]` lists Italian, then English, and no main language is given |
| --- | --- |
| Before (VB6) | NG Center selects the last listed language whose `.dat` already sits in the engine folder, else `english`, else the first (`Module1.bas:9332-9434`) |
| After (port) | Italian, with a warning that names it |

The choice follows the script rather than files left from an earlier compile.

## PD-82: the language files a compile writes

A compile writes `Script.dat` and the main language's `.dat` into the engine folder and nothing into
`script\`, so the engine folder ends as an NG Center compile leaves it.

VB6 writes `script.dat` and every language `.dat` into `script\`, then copies `script.dat` and the main
language `.dat` into the engine folder (`Module1.bas:8884-8973`). For a `[Language]` section that lists
`English.txt` and `French.txt`, with French as the main language:

| | Engine folder | `script\` |
| --- | --- | --- |
| Before (VB6) | `script.dat`, `French.dat` | `script.dat`, `English.DAT`, `French.DAT` |
| After (port) | `Script.dat`, `French.dat` | Nothing written |

The game does not read the copies in `script\`, and NG Center's language switch copies one of them into the
engine folder, so a copy left from an earlier compile can ship a stale language.
The port still compiles and checks every language, so an error in a language that does not ship fails the
compile. The stale-file cleanup of [PD-23](imports-images-and-publication.md#pd-23-publishing-the-outputs)
removes from the engine folder each other language `.dat` that the old or the new `Script.dat` names.
