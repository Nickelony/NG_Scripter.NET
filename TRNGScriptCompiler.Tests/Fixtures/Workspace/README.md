# Parity workspace

This workspace is the input of the seeded goldens that `GoldenParityTests` compares and
[the testing guide](../../../docs/TESTING.md#fixtures) lists, and [the fixture rules](../README.md) give their
provenance.
Its files never change, because the goldens never change.

- The workspace root is the engine folder of the compile.
- `script/` holds `SCRIPT.TXT`, `English.txt`, the `#INCLUDE` files `ng_variants.inc` and
  `exotic_commands.inc`, and `intro.txt`, the `@file` text of ExtraNG index 130 in `English.txt`.
- `ImportFile=` names `extras\demo007.bin`, relative to the engine folder.
- `Settings= SET_CRYPT_SCRIPT` makes the compile encrypt `Script.dat`.
- `Image= 1,4,IF_CRYPTIC,...` names `pix/image4.bmp`. The workspace holds its encrypted copy `pix/@image4.bmp`
  and no `image4.bmp`, as the inputs VB6 compiled did, so the compile finds the image encrypted and leaves it
  alone. No command names `pix/image1.bmp`.
- `PluginDemo.script` defines `PLUGIN_DEMO_CONST`, which the compile loads for the plugin of
  `Plugin= 2,PluginDemo,IGNORE`, and no line of the script names it.
