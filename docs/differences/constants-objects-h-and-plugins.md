# Differences in the constants file, `Objects.h` and plugins

This page lists where the port differs from the VB6 compiler in the constants file, `Objects.h` and plugins;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-28: the constants file

The port reads the NG command definitions and constants from one `scripter_constants.txt` file that a host can
point at, instead of from data built into the program.

| | Source of the NG definitions and constants |
| --- | --- |
| Before (VB6) | A resource inside `NG_Center.exe`, read with `LoadResData(101, "CUSTOM")` (`Module1.bas:2497`); new definitions need a new NG Center build |
| After (port) | `TRNGCompilerOptions.ConstantsFilePath`, which `TRNGCompilerOptions.CreateDefaults` sets to the copy next to the application (`AppContext.BaseDirectory`) |

A missing or unreadable file, or one without command definitions, is fatal.
One file feeds both the definitions and the constants, so they cannot disagree.
The port reads the file once per run.

## PD-49: plugins without a `Plugin=` line

The port compiles the script and the language files as they are, and never adds a `Plugin=` line or an ExtraNG
string to them.

| | `Plugin_X.script` installed, and no `Plugin=` line for it in `script.txt` |
| --- | --- |
| Before (VB6) | At startup NG Center inserts `Plugin= N, Plugin_X, IGNORE` after `[Options]` in `script.txt` and appends `N: Plugin_X` to the language files (`Module1.bas:15105-15274`), then compiles the rewritten files |
| After (port) | The files compile as written, without the plugin's `Plugin=` entry or ExtraNG string |

A compiler that rewrites its inputs changes files under the author's version control without asking. A script
that uses a plugin declares it with `Plugin=`.

## PD-52: reading `Objects.h`

Without `Objects.h`, a `#define` name still resolves, and the port warns that the file is missing.
A `//` comment inside an enum is skipped,
and the slot names come from `enum object_types` wherever it sits in the file.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `#define MY_ID 5` and `Customize= MY_ID,1,2`, in an engine folder without `Objects.h` | `MY_ID` is unknown, because VB6 stops looking after the constants file when `Objects.h` is missing (`Module1.bas:10131-10134`) | 5 |
| `SLOT_REAL, // a note, with a comma` and `SLOT_NEXT,` on the next line | VB6 reads `//anote` as item 1 and `withacommaSLOT_NEXT` as item 2, because it ends an item at every comma and drops spaces, tabs and line breaks (`:9052-9068`) | `SLOT_NEXT` is slot 1 |
| An `enum floor_types` above `enum object_types` | The floor types are the slot names, because VB6 takes the first `enum` in the file (`:9036`, `10138`) | The `object_types` names |

Slot and static names come from `Objects.h`, so without it they stay unknown in both. The stock `Objects.h`
has no comment inside an enum and lists `object_types` first, so it reads the same in both. The items of an
enum otherwise follow VB6, warnings aside (PD-81).

## PD-61: a plugin constant without `Plugin=`

Using a plugin constant fails when the script has no `Plugin=` line for its plugin,
and the error names the line to add.

| | `Customize= PLUGIN_X_CONST,1,2`, with `Plugin_X.script` installed and no `Plugin=` line for it |
| --- | --- |
| Before (VB6) | NG Center's editor refuses to compile until the line exists (`Principale.frm:4389-4402`), and its startup inserts the line into `script.txt` (PD-49) |
| After (port) | An error that names the `Plugin=` line for `Plugin_X` |

Without the line the constant has no plugin ID, so the engine cannot tell which plugin the value belongs to.

## PD-62: the constants file and plugin scripts

The constants file and the plugin scripts share one grammar, and a line the port cannot read the way its
author meant fails, naming the file and the line.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `CUST_SETUP:1` in `Plugin_A.script` and `CUST_SETUP:2` in `Plugin_B.script`, used by `Customize=` | At startup NG Center merges and sorts the plugin constants, and a message box offers to rename one copy `CUST_SETUP_2` and rewrite its `.script` file (`Module1.bas:4276-4318`) | An error where the script uses `CUST_SETUP`, naming both plugins |
| `VIEW_USE_YARDS<TAB>:$0080` in the stock `Plugin_Drones.script` | The tab stays in the name, so `VIEW_USE_YARDS` never resolves (`:3094`, `3126`) | `VIEW_USE_YARDS` is 128 |
| `MY_VALUE: $10` | 0, because VB6 reads `$` hex only right after the colon (`:3129-3135`) | 16 |
| `MY_VALUE:12abc` | 12 | An error naming the file and the line |
| Two definitions of `Snow=` in a custom constants file | The first wins (`:5320-5322`) | An error |

Two plugins that give a name one value still give it two plugin IDs, so a use of that name fails too,
and a name the script does not use compiles. A plugin constant that hides a built-in one with a different
value warns at load, naming its line in the plugin script, and the built-in value keeps winning, as in VB6.
The stock constants file and plugin scripts compile the same in both, apart from the tab.

Both kinds of file read the same way:

- Only `<END>` closes a section, so `<END_CONSTANTS>` is a line of the section, as in VB6.
- An `&H` or `&O` value ending in VB's `&` Long suffix reads as `Val` reads it, so `&HFFFF&` is 65535 and
  `&H8000&` is 32768. Another value with the suffix, such as `65535&` or `$FF&`, fails.
- A line without one `:`, or whose name does not start with a letter or `_` or holds a space,
  is skipped with a warning, where VB6 shows a message box.
- A name given twice keeps its first value with a warning.

A command definition fails, naming its line, in these cases:

- Its name is defined twice, the two `TriggerGroup=` forms apart.
- It has an unknown or empty argument type.
- It gives a `Bool` among other arguments, or a `Bool` whose values are both 0.
- Its code lies outside 0 to 255, or its limit is below -1.
- It has an array type that is not its last argument type, or more than one array type.

The last two cases are definitions that the compile could not encode.
The code, limit and flag values of a definition take `$`, as in VB6.
VB6 takes an unknown argument type as a word.

## PD-87: plugin scripts and `Plugin=` lines

The port reads the plugin scripts of one folder and of its immediate subfolders,
and a plugin takes the ID of the `Plugin=` line of the script being compiled, wherever that line stands.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `Plugin_X.script` in `NG_Center\` and an installer copy in `NG_Center\Plugin_X\` | The top-level copy (`TrovaFiles`, `MioModulo.bas:2056-2075`) | The top-level copy, with a verbose note when the two differ |
| `Plugin_X.script` only in `NG_Center\Plugin_X\` | Not read | Read |
| `Customize= PLUGIN_X_CONST,1,2` after the editor changes `Plugin= 2,Plugin_X,IGNORE` to ID 3 | High word 2, the ID NG Center read at startup (`Module1.bas:4266`), until a restart | High word 3 |
| `Plugin= 6,Plugin_X,IGNORE` in an `#INCLUDE` file, and an exported trigger of `Plugin_X` | An error, because VB6 reads the `Plugin=` lines of `script.txt` alone (`:15024-15104`) | The trigger takes ID 6 |
| `Plugin= MY_ID,Plugin_X,IGNORE` | A message box, and the plugin stays out of the table (`:15081-15083`), so its exported triggers fail | An error that asks for the ID as a number |

The default plugin folder is `<engine>\NG_Center` when it exists, otherwise the engine folder;
TombIDE passes the `Plugins` folder of its project, which holds one subfolder per plugin.
A plugin folder or subfolder that cannot be read is skipped with a warning that names it.

A script deeper in the tree, such as a backup, belongs to no installed plugin, so its constants would resolve
names that no plugin of the game defines. The ID of the compile's own `Plugin=` line is the ID `Script.dat`
gives the plugin, so the high word names the plugin the engine loads.
A `Plugin=` ID that is not a number writes its value into `Script.dat` while the table misses the plugin,
so the port asks for the number.

## PD-96: a plugin define ID of 32768 or more

An exported trigger names its plugin by the ID of its `#DEFINE @PluginName` line in the high word of a
`TriggerGroup=` value, and the compile replaces that ID with the one of the `Plugin=` line. A define ID of 32768 or
more cannot take part, so the port fails the line.

| | `#DEFINE @Plugin_X 32769`, `Plugin= 2,Plugin_X,IGNORE` and `TriggerGroup= 1,$80012000,406,1` |
| --- | --- |
| Before (VB6) | Reads the high word as a signed 16-bit `Integer`, which is negative, so it skips the conversion without a word (`Module1.bas:7044, 7551, 7563-7565`) and writes the define ID where the plugin ID belongs |
| After (port) | An error that asks for a define ID below 32768 |

A high word of 32768 or more that no `#DEFINE @PluginName` line names stays as written, as in VB6.
