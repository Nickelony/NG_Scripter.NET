# Differences in numbers, names and arguments

This page lists where the port differs from the VB6 compiler in numbers, names and arguments;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-35: a `#define` cannot reuse a name

A `#define` whose name is already a constant, a plugin constant, a slot or a static fails the compile.

```
#define SNOW_ALL_OUTSIDE 1
Snow= SNOW_ALL_OUTSIDE
```

| | Result |
| --- | --- |
| Before (VB6) | `Snow=` gets 2, the constant's value, because VB6 looks up constants, plugin constants and slots before defines (`Module1.bas:10117-10170`), and the define changes nothing, without a warning |
| After (port) | An error that `#DEFINE SNOW_ALL_OUTSIDE` reuses the name of a TRNG constant |

A define that reuses a static name wins in VB6, because VB6 looks up statics after defines,
so the static stops resolving in that file. Letting the define win would change a value every VB6 build was
tested with, and ignoring it hides the author's mistake.

## PD-46: names in NG arrays

An element of an NG byte or nybble array can be a constant, a `#define` or a sum, like any other NG number.

```
#define FRAMES 4
TextureSequence= 1,2,3,FRAMES
```

| | Result |
| --- | --- |
| Before (VB6) | A fatal error: "not decimal numeric value in 4th argument", because VB6 reads array elements as literals only (`Module1.bas:7201-7227`) |
| After (port) | The array holds the element 4 |

Every other NG argument accepts a name, and a name says what a bare number does not.

## PD-51: numbers that read wrong

A number the author could misread fails, and the error names the fix. Such a number holds an operator with no
value after it, an `&H` or `&O` value that VB reads as negative, a fraction or an exponent, or `0x`.
A `$` with a space, a sum past 32 bits and a `\x` escape with a minus sign fail too.
One reader serves classic and NG arguments, `#DEFINE` values, `InputTimeOut=`, `Security=`, `ImportFile=`,
the `File=` index and the `!NNN` index.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `Fog= 5+-3,0,0` | 7, because VB6 reuses the previous value for an empty piece (`Module1.bas:4857-4911`) | An error that names `5-3` |
| `LevelFarView= -` | 0, by the same rule | An error |
| `LevelFarView= &HFFFF` | -1, because VB reads an `&H` value up to `&HFFFF` as a 16-bit value, while `$FFFF` is 65535 | An error that names `$FFFF` and `-1` |
| `Fog= 1.5,0,0` | 2, through `IsNumeric`, whose decimal separator follows the Windows locale | An error |
| `Mirror= 0,2147483647+1` | No output: the sum overflows VB's `Long` and raises error 6 | An error that names the overflow |
| `Hello \x-4` in a language file | Decoded, because VB6's hexadecimal check accepts `-` (`MioModulo.bas:4986-5011`) | An invalid-escape error |
| `$82: Text` in `[ExtraNG]` | Index 0, because `Val` does not read `$` (`:2118`) | Index 130, as `$82` reads in a script |

Each is a typo or a VB detail the author did not mean, and VB6 turns it into a value the author did not write.
An `&H` or `&O` value that reads the same as its `$` form, such as `&H7400` or `&O17`, compiles as in VB6,
and `0x10` and `$ 12` fail in both compilers. An ExtraNG index takes one number,
because a language file has no `#DEFINE` scope: a sum, a name and `#` fail there.

VB6 tests a script number, an ExtraNG index and a command definition field with `IsNumeric` before `Val` reads
it (`Module1.bas:4887`, `2109`, `2696`). There, an `&H` or `&O` value that ends in VB's `&` Long suffix,
such as `&H10&`, fails with an error that names the suffix and the `$` form.
The constants file and the plugin scripts read a value with `Val` alone, so `&HFFFF&` is 65535 there,
as in VB6 (PD-62).

## PD-56: values out of range

A value outside the range its field holds fails, where VB6 writes it cut or wrapped.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `Fog= -1,0,0` | 255 for red, because VB6 only checks that the value fits a byte, and -1 does (`Module1.bas:4915-4937`) | `Argument 1 of Fog= is out of range: -1 lies outside 0 to 255, the values a color holds` |
| `UVRotate= -200` | 56, because -200 passes the byte check only by wrapping | An error: a byte holds -128 to 255 |
| `AnimatingMIP= 1,20`, followed by another command | The distance times 16 cut to one byte, because the next command clears the error (`:5746`, `6141-6148`) | An error: the distance holds 0-15 |
| `FMV= 128,0` | Movie 0 with the trigger set, because bit 7 is the trigger flag (`:5789-5790`) | An error: the index holds 0-127 |
| `LensFlare= 8388608,0,0,0,0,0` | The coordinate divided by 256, 32768, wraps to -32768 in its word (`:6033-6034`) | An error |
| `AssignSlot= 491,1234` with the stock `Objects.h` | Slot 491, which does not exist (`:7432-7436`) | An error: the slots end at 490 |
| `Enemy= -1,1,2,3,4` | Slot -1, because -1 passes the word check | An error: a slot is never negative |
| `ItemGroup= 1,2,3,-40000` | 25536, because -40000 passes the word check only by wrapping | An error: a word holds -32768 to 65535 |
| `Name= Far String`, where `Far String` is string 256 | String 0, because the index is written as one byte (`:5851`, `5994`) | An error: the string must be among the first 256 |
| An `ImportFile=` name of 80 characters or more | Cut to the 80-byte field, with no terminator (`:7645`) | An error |

VB6 width-checks a byte field to -256..255, so an unsigned field takes a negative value.

A signed field, such as the `Layer1=` speed or `UVRotate=`, takes -128 to 255,
so both of its readings compile. A word field of an inventory command or an NG command takes -32768 to 65535
for the same reason. A slot is checked against `NUMBER_OBJECTS` in `Objects.h`,
except the slot field of `TestPosition=`, which keeps the word range because with `TPOS_TEST_ITEM_INDEX` it
holds an item index.

## PD-60: `SLOT` arguments

A `SLOT` argument accepts a number, a `$` literal, a slot name, a `#define` and sums of them. VB6 accepts the
first three (`Module1.bas:7432-7436`, `7499-7506`).

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `AssignSlot= MY_SLOT,1234` with `#define MY_SLOT 100` | An error | Slot 100 |
| `AssignSlot= PLANT1,1234` | An error | An error: `PLANT1` is a static, not a slot |

A define reads as clearly as a slot name, while a static or a TRNG constant in a slot field is a mistake.
A plugin constant and the `#` and `0x` forms fail too,
and the error for a hexadecimal form names its `$` literal.

## PD-92: a binary string reference

A string argument that starts with `&`, in a classic or an NG command, names a binary string: `&` and pairs of
the upper-case hexadecimal digits `0-9` and `A-F`. VB6 checks the form before it looks the language line up,
for the classic and the NG commands alike (`Module1.bas:6579-6585`, `7529`), and so does the port. The port
also rejects a lone `&` and a `-` among the digits, which VB6 takes.

VB6 checks the form in `GetIndiceStringa` with `IsStringaBinariaOk`,
and the port checks it in one resolver, `StringReferenceResolver`, for both kinds of command.

| Script line, with a language line of the same text | Before (VB6) | After (port) |
| --- | --- | --- |
| `Legend= &` | The index of the language line `&`, because the check has no digit to reject (`Module1.bas:14127-14154`) | An error that names the form, with `&0A1B` as an example |
| `Damage= 1,2,3,4,&-1,5` | The index of the language line `&-1`, because the check reads each digit alone with `isNumericHex`, which takes a leading `-` (`MioModulo.bas:5003`) | An error that names the form |
| `Legend= &12ab` | An error, because a digit is lower-case (`Module1.bas:14144-14150`) | An error that names the form |

A binary string gives bytes, so a lone `&` gives none, and `-` is no hexadecimal digit. VB6's check finds no
digit to reject in a lone `&` and tests each digit alone, so it passes a reference that reads wrong as a
binary string.

## PD-97: the `_` discard

A numeric or `SLOT` argument takes `_` as the discard `IGNORE` names, the value -1. VB6 looks `_` up as a constant
and fails (`Module1.bas:4887-4896`). The port accepts it and suggests it: `IGNORE` still compiles, and the
compiler reports a hint, "Replace IGNORE with the _ discard", on the argument.

| Input | Before (VB6) | After (port) |
| --- | --- | --- |
| `KeyPad= _,0,0,0` | An error: `_` is neither a number nor a known name | The same bytes as `KeyPad= IGNORE,0,0,0` |
| `KeyPad= IGNORE,0,0,0` | Compiles | Compiles, with a hint on `IGNORE` |
| `WindowTitle= _` | The language line `_` (`Module1.bas:7521-7529`) | The language line `_`, as in VB6 |

A `STRING` argument keeps its VB6 reading: `_` is text that the language file holds, and a hint would have no
better spelling to give. A numeric or `SLOT` argument that fails its range gets
no hint.

A hint is the lowest severity of a diagnostic. It reaches the diagnostics of a result, which an editor shows,
and writes no log line.
