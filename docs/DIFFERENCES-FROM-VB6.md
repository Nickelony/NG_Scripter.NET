# Differences from the VB6 compiler

This page lists the topic pages that hold every place where the port deliberately behaves differently from
the VB6 compiler.
Each section of a topic page gives the difference with a Before (VB6) and After (port) example and the reason
the port's behavior is better. Everywhere else, an input VB6 compiled produces the same bytes in the port.

The [parity rules](../CONVENTIONS.md#parity) say where the port may depart from VB6.
[The parity decision log](PARITY-DECISIONS.md) holds the one-line decision for each row, its VB6 source
citation and the tests that enforce it; each section names its row.

- [Script lines, sections and settings](differences/script-lines-sections-and-settings.md) covers how the
  lines, directives, sections and settings of a script are read, including repeated,
  malformed and disabled ones.
- [Numbers, names and arguments](differences/numbers-names-and-arguments.md) covers how numbers, `#define`
  names, NG array names, `SLOT` arguments, the `_` discard and binary string references are read and range
  checked.
- [Includes and paths](differences/includes-and-paths.md) covers the folders `#INCLUDE` searches, include
  cycles, path casing and the input limits.
- [Text and its encoding](differences/text-and-its-encoding.md) covers byte-order marks, characters
  Windows-1252 cannot hold and the locale independence of text comparison.
- [Language files](differences/language-files.md) covers how a compile matches, checks, defaults and writes
  the language files.
- [The constants file, `Objects.h` and plugins](differences/constants-objects-h-and-plugins.md) covers how a
  compile reads and checks the constants file, `Objects.h`, plugin scripts and `Plugin=` lines.
- [Imports, images and publication](differences/imports-images-and-publication.md) covers the import-file
  number read from an import's file name, image encryption, how a compile publishes its outputs,
  the compile banner and the ID argument of an import.
- [The console host](differences/console-host.md) covers NG Center's command line.

The random padding of the NG security block is not a difference;
[PD-07](PARITY-DECISIONS.md) records how it is seeded.
