# Quirks golden

This fixture pins the VB6 behaviors the main `Workspace/` fixture does not reach.

- Its inputs are in `Workspace/`, and the VB6 outputs go in `VB6 Outputs/99999/`.
- Every line of `Workspace/script/SCRIPT.TXT` compiles in the VB6 NG_Scripter, and each group of lines follows
  a `;` comment that names its quirk.
- The workspace leaves out every input VB6 rejects, because one rejected line produces no output at all.
- It also leaves out every behavior the port changes on purpose, which
  [the parity decision log](../../../docs/PARITY-DECISIONS.md) records.
- A line whose VB6 result the source leaves open sits in its own workspace of
  [the golden corpus](../Corpus/README.md).

## What the workspace covers

The configuration differs from the main fixture:

- The script is not encrypted, because `Settings=` leaves out `SET_CRYPT_SCRIPT`.
- `[Language]` lists `English.txt`, `French.txt` and `German.txt`, and the main language is French.
- The sections run `[Language]`, `[PCExtensions]`, `[Options]`, `[PSXExtensions]`, `[Title]`, three `[Level]`
  sections and a second `[Options]`.

Script lexing and sections:

- Two `[Options]` sections share one duplicate-key table and repeat no NG option key.
- A `>` continuation ends a `LoadCamera=` line after a comma, where both compilers join the pieces with
  nothing between.

Classic commands:

- `Fog=` precedes the lower-sorting `UVRotate=`.
- Two `ResidentCut=` slots and two `FMV=` lines share a sort key, and lower-key lines follow them, so VB6's
  exchange sort writes each pair reversed.
- A later `[Level]` reuses the first level's path.
- Negative values appear only in the speeds the engine reads as signed: `Layer1=`, `Layer2=` and `UVRotate=`.
- Item names hold double quotes and an inch mark, such as `The "Eye" of Horus` and `12" Ruler`.
- `Legend= :Hello` looks up the string whose leading `:` tag VB6 strips when it writes the language file.

NG commands and numbers:

- `&H7400` is a VB hex literal that reads the same as `$7400`.
- `WindowsFont=` takes `!120` as its `STRING` argument, and `WindowTitle=` takes the hexadecimal index `!$82`.
- `StaticMIP= PLANT1,4,PLANT2,8,PLANT3` names static slots that `Objects.h` lists ten to a line.
- `TextureSequence= 1,2,3` has an empty nybble array.
- `AssignSlot= 470,1234` uses a slot above 464 that the stock `Objects.h` defines, and `TestPosition=` names
  its slot as `LARA`.
- `TriggerGroup=` with `IGNORE` takes the word form, because its text holds no plugin ID.

Plugins:

- `Customize= PLUGIN_DEMO_CONST,1,2` ORs the plugin ID of `Plugin= 2,PluginDemo,IGNORE` into the high word.
- No `@plugin` define names plugin ID 9, so `TriggerGroup= 3,$90001,...` keeps it.
- A positional `@plugin` block inside a `[Level]` converts plugin ID 7 into ID 2,
  and `@plugins clear` ends it.

Language files:

- Strings hold `\x80`-`\x9F` escapes of the bytes Windows-1252 defines.
- Each language holds ExtraNG indexes 100, 120 and 130, so any of them passes the main-language check.
  `English.txt` writes index 130 as `&H82`.

Imports and images:

- `ImportFile=` embeds files of 46, 48 and 49 bytes with import modes 0, 1 and 2.
- `Image=` with `IF_CRYPTIC` encrypts the source `pix/image4.bmp` into `pix/@image4.bmp`.

## Producing the golden

`..\Run-VB6Corpus.ps1` compiles this workspace after the corpus workspaces, and
[the procedure](../../../docs/PRODUCING-VB6-OUTPUTS.md) gives the steps of every workspace. These apply to this workspace alone:

- The script copies `Workspace/PluginDemo.script` into NG Center's own folder for the compile, because VB6
  reads plugin scripts from there, and removes that copy afterward.
- The script writes an empty `French.dat` into the scratch engine folder, so NG Center's command line picks
  French as the main language; NG Center deletes the placeholder before it compiles.
- The script collects `pix/@image4.bmp` next to `script.dat` and the language `.dat` files.

To compile this workspace alone:

```powershell
.\Run-VB6Corpus.ps1 -NGCenterPath 'C:\NG_Center\NG_Center.exe' -Workspace Quirks
```
