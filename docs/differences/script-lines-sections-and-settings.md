# Differences in script lines, sections and settings

This page lists where the port differs from the VB6 compiler in script lines, sections and settings;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-02: a classic command repeated in a section

A classic command that a `[Level]` or `[Title]` section repeats with a different value fails, and an identical
repeat warns. Two lines give the same setting when VB6 writes the same tag for them, from resolved values: one
tag per `ResidentCut=` slot and per inventory item and piece, so `Key= 1` and `Key= $1` give one setting.
`AnimatingMIP=` adds its slot, and a level flag writes no tag, so it keys on its command.

| Lines | Before (VB6) | After (port) |
| --- | --- | --- |
| `Fog= 1,2,3`, then `Fog= 4,5,6` | Both written, with a warning that the last one is used (`Module1.bas:6409-6419`) | An error naming both lines |
| `Key= 1,...`, then `Key= $1,...` with other values | Both written, with a warning, because both write tag 0x93 (`:6253-6255`) | An error naming both lines |
| `Horizon= ENABLED`, then `Horizon= DISABLED` | The horizon is on, without a warning, because VB6 only ever sets a flag (`:6370-6377`) | An error naming both lines |
| `Fog= 1,2,3` twice | Both written, with a warning | Both written, with a warning |
| `ResidentCut= 1,5` and `ResidentCut= 2,6` | Two slots, without a warning | The same |

VB6 counts a repeat by the tag it writes,
so it never counts one of `FMV=`, `AnimatingMIP=`, `Name=` or a level flag.

`LoadCamera=` and `FMV=` keep VB6's behavior: both lines are written,
in the order VB6's sort gives them (PD-32). The help's own `[Level]` example gives two `LoadCamera=` lines,
of which the engine uses the one written last, and each `FMV=` line is a movie of its own.

Two different values leave the script unclear about which one the game uses,
and the one written last depends on VB6's sort rather than on the order of the lines.

## PD-05: a level without `Name=`

A `[Level]` without a `Name=` line fails the compile.

VB6 starts every level with the name `0`, because it stores the number 0 in a text field (`Module1.bas:5250`),
so its check for an empty name never fires (`:5839`) and it looks up the string `0` instead (`:5845`).

| | A `[Level]` without `Name=` |
| --- | --- |
| Before (VB6) | A missing-string error, unless the first language file holds the string `0`; then the level compiles under that name |
| After (port) | `Missing 'Name=' command in [Level] section` |

A level named after a stray `0` in the language file is never what the author meant.

## PD-26: an extension list longer than its field

An extension list longer than its fixed 20-byte field fails the compile,
instead of shifting every field after it.

```
[PSXExtensions]
Level= .AAAAAAAAAAAAAAAAAAAAAAAA
```

| | Result |
| --- | --- |
| Before (VB6) | All 26 bytes are written, so the PC extension table and every later field start 6 bytes late |
| After (port) | An error that the list is longer than 20 bytes |

The engine reads the table at a fixed offset, so the VB6 output was unreadable,
and a list cut to 20 bytes would not hold what the author wrote.

## PD-31: a setting given two values

A setting that holds one value fails when a section gives it a second, different value,
and an identical repeat warns. `Level=` fails whenever a section repeats it (PD-42).
The rule covers a `#DEFINE` in one file,
the `ENABLED`/`DISABLED` options of `[Options]` and of a level,
`InputTimeOut=`, `Security=`, an extension line, `Name=` and `Level=`.

| Lines | Before (VB6) | After (port) |
| --- | --- | --- |
| `LoadSave= ENABLED`, then `LoadSave= DISABLED` | Saving is enabled, because VB6 only ever sets the flag (`Module1.bas:5402-5425`) | An error naming both lines |
| `Title= DISABLED`, then `Title= ENABLED`, with a `[Title]` section | The compile fails: the ignore flag the first line sets stays (`:5406-5411`), and VB6 cannot write an ignored `[Title]` (PD-54) | An error naming both lines |
| `#DEFINE SPEED 1`, then `#DEFINE SPEED 2` | 1, with a warning (`:13691-13698`) | An error naming both lines |
| `Level= data\first,0`, then `Level= data\second,0` | Two level headers and two paths, so every later level's index points one path early (`:5811-5814`, `5861`) | An error naming both lines |

VB6 picks one of two contradictory values, or writes both, so the script does not say which one the game uses.
It keeps the last value of the other settings.

## PD-36: spaces before `=`

A command name followed by spaces before its `=` is the same command.

| | `Fog = 1,2,3` |
| --- | --- |
| Before (VB6) | An unknown command, which is fatal (`Module1.bas:4749-4750`) |
| After (port) | `Fog= 1,2,3` |

## PD-40: a `>` continuation

A `>` continuation is legal only next to a comma or right after the command's `=`,
where both compilers join the two lines with nothing between. A continuation anywhere else fails,
and so does text after the `>`; a comment after it stays legal.

| Lines | Before (VB6) | After (port) |
| --- | --- | --- |
| `Fog= 1,2,>`, then `3` | `Fog= 1,2,3` | `Fog= 1,2,3` |
| `LoadCamera= 0,0,0,0,0,1>`, then `2,0` | The sixth value is 12, because VB6 trims both pieces and joins them with nothing between (`Module1.bas:5155-5167`) | An error: the continuation splits a value |
| `Name= Test>`, then `Level` | Looks up `TestLevel` | An error: the continuation splits a word |
| `Legend= Press,> to go on` | Drops ` to go on` and joins the next line to `Press,` (`:5160`) | An error that names `to go on` as the text the `>` would drop |
| `Fog= 1,2,3 // fog -> red` | Joins the next line into the comment and drops it, because only a `;` comment stops a continuation | An error: a `>` inside a comment |

A join without a space reads as one word where the author wrote two lines,
and a join with a space would change the text VB6 looked up, so the port accepts neither.

## PD-41: once-only options

A command the constants file allows once, such as `DiagnosticType=` or `LogItem=`, fails when `[Options]`
repeats it with a different first or second argument.
A repeat with the same arguments fails in both, as a duplicate.

| | `DiagnosticType= 1,2` followed by `DiagnosticType= 3,2` |
| --- | --- |
| Before (VB6) | Both are written, because VB6 counts occurrences only in levels (`Module1.bas:8480`, `8491`) |
| After (port) | `Too many DiagnosticType= commands; a section holds at most 1` |

The engine holds one value for each of these settings, so VB6 leaves it to pick one of two contradictory
values. The limit counts across every `[Options]` section of the script, because they form one list.

## PD-43: a bare line feed in `script.txt`

A line feed without a carriage return ends a line in the script, as CRLF does.

| | `; comment` ending in a bare line feed, followed by `Fog= 1,2,3` and CRLF |
| --- | --- |
| Before (VB6) | One line, because `Line Input` ends lines only at a carriage return (`Module1.bas:5150`), so the comment swallows `Fog=` |
| After (port) | Two lines, so `Fog= 1,2,3` is compiled |

Editors and version control tools save files with bare line feeds,
and the VB6 result drops a command without a message.

## PD-54: a disabled `[Title]`

A script whose `[Title]` is disabled with `Title= DISABLED` compiles with its `[Title]` section as written,
and only the title flag is clear. The lines of that section are checked like any other.

VB6 gives every `[Level]` and `[Title]` an NG block, the ignored `[Title]` included (`Module1.bas:5232-5236`),
but records a level name only for a stored section (`:5812`).
Its NG writer then reads one name past the end (`:8427`),
and the compile fails with "ERROR writting script.dat".

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `Title= DISABLED` and a `[Title]` section | The compile fails | `Script.dat` holds the title section and the levels, with the title flag clear |
| A line without `=` in that `[Title]` | Skipped unparsed (`:5269`), before the compile fails as above | An error |

VB6 numbers the NG blocks with the title included even when it is disabled, so keeping the section keeps every
level at the index its NG block has. Disabling the title is a reasonable way to test the levels.

## PD-55: input outside any section

A command or a `#define @plugin` above the first section of `script.txt` fails,
and so does a line of text above the first section header of a language file. Comments,
blank lines and the directives `#DEFINE`, `#INCLUDE` and `#FIRST_ID` stay legal there.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `Fog= 1,2,3` above the first section | A warning that the command is out of context, and no effect (`Module1.bas:5272-5276`) | An error |
| `#define @PluginDemo 9` above the first section | The same warning, and no effect | An error |
| `Hello` above `[Strings]` in a language file | Dropped without a word (`:2049`) | An error |

A line that has no effect is a mistake the author would otherwise find only in the game.

## PD-57: malformed directives and lines

A directive word with extra letters, text after a Ctrl-Z byte, and extra arguments to a one-value option fail.
A grouped array that ends in a partial group warns.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `#INCLUDEX "a.txt"` or `#DEFINEX FOO 1` | Not a directive: a warning above the first section (`Module1.bas:5272-5276`), an error inside one (`:5293-5299`) | An error |
| Text after a Ctrl-Z byte | Dropped, because VB6 reads Chr(26) as the end of the file | An error |
| `Security= 1,2` | 1, and the extra argument is ignored (`:5427-5430`) | An error |
| `[PCExtensions]` with `Level= .TR4` and `Cut= .TR4` but no `FMV=` | The `Cut=` slot is dropped, because VB6 writes as many slots as the section has commands (`:5545-5562`) | An error: the `FMV=` slot is missing (PD-25) |
| `TriggerGroup= 1,$2000,406,1,2` | Written with a partial triplet, without a word (`:7154-7172`) | Written the same, with a warning that the array ends in a partial group |
| `Plugin= 1,Plugin_X,IGNORE` and `Plugin= 2,Plugin_X,IGNORE` | Both written; exported triggers take the first ID (`:6993-6995`) | An error: the plugin is declared twice |

VB6 ignores an extra argument of `InputTimeOut=` too.

A Ctrl-Z at the very end of `script.txt`, an include or a language file stays legal, as old editors write one
there. `Objects.h` and `@file` texts keep the byte, because VB6 reads them whole.

The help reads `TriggerGroup=` in triplets, `MultEnvCondition=` in triples and `Organizer=` in pairs after its
third value. A partial group only warns, because scripts VB6 compiled hold them.

## PD-63: input VB6 rejects that reads right

The port accepts a few inputs that VB6 rejects, where the input reads right.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `Fog=` followed by a no-break space and `1,2,3` | Not a number, because `Trim$` strips spaces only | `Fog= 1,2,3` |
| `Name= Big Room` with the language line `Big Room ` (a trailing space) | A missing-string error, because VB6 looks strings up by the raw line (`Module1.bas:2055`, `6591-6596`) | The string `Big Room `, with a warning that names the language line |
| Two `AnimationSlot=` lines for one slot, with different animations | A duplicate-command error, because the duplicate check uses the slot alone (`:7594-7605`) | Both animations, because the check keys on the slot and the animation index |

A no-break space, a vertical tab or a form feed at the edge of an argument stays in VB6 and fails,
and the port trims each of them.

The text of `Name=` and `Legend=` loses the same edge characters as an argument, so `Name=` followed by a
no-break space and `Big Room` names `Big Room`.

A name lookup tries the exact language line first, so every string VB6 found is still found. The second lookup
compares the language lines with their comment cut and their spaces collapsed and trimmed, as a script line's
are. A single match is used with a warning that names the language line, and more than one fails, naming them.
