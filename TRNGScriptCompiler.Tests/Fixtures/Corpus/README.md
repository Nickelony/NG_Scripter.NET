# Golden corpus

The golden corpus is a set of small workspaces that
[the instrumented VB6 build](../README.md#golden-provenance-the-instrumented-vb6-build) compiles once, so
`GoldenCorpusTests` can compare the port's bytes with VB6's for input the port is meant to match.
[The procedure](../../../docs/PRODUCING-VB6-OUTPUTS.md) gives the command that produces the outputs.

Each folder holds `Workspace/`, an engine folder with `script/script.txt`, the language files, the stock
`Objects.h` and any include, `@file` or import input, and `VB6 Outputs/99999/`, which holds the VB6 outputs.
The text files are Windows-1252 with CRLF line ends, as VB6 reads them. Every workspace lists `English.txt`,
which NG Center's command line picks as the main language.

`GoldenCorpusTests` compiles each workspace with the `DeterministicSeed` of the instrumented build,
once per language golden with that language as the main one, because the port publishes only the main
language `.dat`. Each compile compares `Script.dat` and the main language `.dat` with the golden.
The test runs in the suite, in the `Parity` category, and a workspace without a `script.dat` golden fails it,
because a line VB6 rejects belongs in a hand-derived test.

## What the corpus covers

By the VB6 source, every line of these workspaces compiles, and each group of lines follows a `;` comment that
names what it checks.

| Workspace | Covers |
| --- | --- |
| `ClassicLevelCommands` | Every classic `[Level]` command at the ends of its range, `Name=` in `[Title]`, and each level flag set and cleared, in VB6's sort order |
| `ClassicSortOrder` | Lines that share a sort key followed by lower-key lines, the `[Level]` example of the VB6 help, and a section in reverse key order |
| `ClassicOptionsExtensions` | The classic `[Options]` switches in mixed letter case, hexadecimal `InputTimeOut=` and `Security=`, a lower-case extension section in reverse slot order that fills 20 bytes, and a section with one extension |
| `ScriptLexing` | `>` continuations next to a comma or right after `=`, `;` and `//` comments, tabs, runs of spaces, the letter case of sections, commands and keywords, and a comma in `Name=` |
| `NumbersAndDefines` | `$`, `#`, `&H` and `&O` numbers, sums, constants, slot and static names and `#DEFINE` values in classic and NG arguments, `File=` and `!NNN` through the numeric reader, a define used above its line, and a `#FIRST_ID` line the compile skips |
| `Includes` | An include above the first section, nested includes, an include that opens a section, one include used by two levels, and defines that each file keeps to itself |
| `IncludeDepth` | A chain of 20 nested includes, the most VB6 holds open |
| `SectionOrder` | `[Options]` in three parts and `[Language]` after the levels, with NG string arguments that name its strings |
| `IdenticalRepeats` | Options, extensions, classic commands, a `#DEFINE` and an ExtraNG index given twice with the same value, which the port accepts with a warning |
| `NgOptions` | Every NG `[Options]` command but `ImportFile=`, over three `[Options]` sections, with `Plugin=` lines for plugins that are not installed |
| `NgLevelCommands` | The NG level commands without notable arrays or strings, at the ends of their ranges, and the NG switches in `[Title]` and `[Level]` |
| `NgArrays` | Empty arrays, odd and even nybble counts, 255 nybbles, whole groups, the commands VB6 never counts as repeats, and a command of 255 words |
| `NgStrings` | Every string argument with classic and ExtraNG strings, `!NNN`, `*`, `IGNORE` and `_`, and `DefaultWindowsFont=` in `[Title]` |
| `NgTriggers` | `TriggerGroup=` in word form and long form up to 253 words, `GlobalTrigger=` with six and seven arguments, and each form of the `TestPosition=` slot |
| `PluginDefines` | Positional `@plugin` define blocks against `Plugin=` lines of plugins that are not installed: held across levels, cleared, and given a new ID |
| `ImportFiles` | Imports of 1 to 5, 7, 51 and 256 bytes, every file type and import mode, the import-file numbers VB6 reads from names, a subfolder, the engine folder itself and a name with spaces |
| `EncryptedScript` | `SET_CRYPT_SCRIPT` with level paths whose joined length is odd |
| `LanguageFeatures` | Comments above the first header, tags, escapes, raw Windows-1252 characters, an empty `[PSXStrings]`, ExtraNG index forms and bounds, `@file` texts, an empty ExtraNG text, the sentinel, and lookups by raw line, without regard to case and by first match |
| `LanguageMulti` | Eight languages, the most `[Language]` holds, with `File=` indexes written as a number, `$`, a sum, `&H` and a define |
| `LanguageLargeTable` | 300 strings, with `Name=` and `Legend=` at indexes 254 and 255 and word-wide indexes past 255 |
| `LanguageLongExtraNG` | An ExtraNG chunk of more than 32767 words, which VB6 sizes with two words |

## What the corpus leaves out

Input that the port rejects, improves, extends, warns about with other bytes or fixes stays out, because the
port differs there on purpose, as [the parity decision log](../../../docs/PARITY-DECISIONS.md) records.
Every line VB6 rejects stays out too, because one rejected line costs its workspace all its outputs,
and so do these inputs:

- Plugin scripts stay out, because VB6 reads them from NG Center's folder, where they would apply to every
  workspace; the quirks golden covers plugins.
- An include never holds `[Language]`, because NG Center reads the `File=` lines of `script.txt` to pick the
  main language before it compiles.

## Lines the VB6 source does not settle

Reading the VB6 source leaves open what VB6 writes for each of these lines, so each sits in a workspace of its
own, where a VB6 rejection would cost no other line its outputs. VB6 compiled every workspace below, so its
bytes settle each line, and the port writes the same bytes.

| Workspace | Line | VB6's bytes show |
| --- | --- | --- |
| `ScriptCtrlZ` | A Ctrl-Z byte after the last line of `script.txt` (PD-57) | The file ends at the Ctrl-Z, which adds no line |
| `LanguageSpacesString` | A `[Strings]` line of three spaces, which VB6 writes through a routine that fails on an empty string (PD-39) | An empty string: the terminating zero alone |
| `LanguageTagOnlyString` | A `[Strings]` line that holds the tag `NOTE:` alone (PD-39) | An empty string: the terminating zero alone |
| `LanguageEscapeX81`, `LanguageEscapeX8D`, `LanguageEscapeX8F`, `LanguageEscapeX90`, `LanguageEscapeX9D` | The escape of a byte Windows-1252 leaves undefined, in a classic string and an ExtraNG text | The escaped byte itself |
| `LanguageRawUndefinedBytes` | Those bytes raw, as a language file saved in another code page holds them | The raw byte itself |
| `LanguageAtFileEdgeSpaces` | An `@file` text with spaces at the start of a line and at the end of the file | Only the spaces at the two ends of the whole text trimmed |
| `LanguageAtFileTrailingTab` | An `@file` line that ends with a tab | The tab kept |
| `LanguageExtraNGComment` | An ExtraNG text that ends in a `;` comment | The comment cut and the space before it kept |

VB6 rejected two `AnimationSlot=` lines for one slot as a duplicate, so that line has no workspace; the port
writes both (PD-63), and
`NgDuplicateCommandTests.AnimationSlotLinesForOneSlotWithDifferentAnimations_AreBothWritten` pins its bytes.

## Adding a workspace

- Check each line against the VB6 source, which must compile it, and against
  [the parity decision log](../../../docs/PARITY-DECISIONS.md), which must record no intentional difference
  for it.
- Write the text files as Windows-1252 with CRLF line ends, copy the stock
  `Original Compiler/Progetti/NG_Scripter/Objects.h` and list `English.txt` in `script.txt`;
  the script creates `VB6 Outputs/99999/`.
- Give a line whose VB6 result the source does not settle a workspace of its own, named for what the line
  holds, and list it under [Lines the VB6 source does not settle](#lines-the-vb6-source-does-not-settle).
