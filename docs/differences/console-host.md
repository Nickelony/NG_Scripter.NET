# Differences in the console host

This page lists where the port differs from the VB6 compiler in the console host;
[the differences page](../DIFFERENCES-FROM-VB6.md) indexes every topic.

## PD-91: NG Center's command line

The console host accepts the command line a batch file gives `NG_Center.exe`: the path of `script.txt`,
quoted or not, before or after NG Center's four switches. The switches match as whole words and without regard
to case, as in VB6 (`Option Compare Text`, `Module1.bas:4`). NG Center takes the first quoted string of its
command line as the path, or the whole line when nothing is quoted,
and finds a switch anywhere (`Module1.bas:3258-3285`, `3740-3747`).

The engine folder is the folder of that path less a last `\script`.

| Command line | Before (VB6) | After (port) |
| --- | --- | --- |
| `"d:\trle\script\script.txt" -NoMsgBox -Concise -Log` | Compiles `d:\trle\script\SCRIPT.TXT`, appends the log to `d:\trle\LastCompilerLog.txt` and waits for Esc (`Module1.bas:3727-3747`, `8815`, `8978`) | The same, with the port's log lines, and waits for a key |
| The same with `-NoWait` | Exits when the compile ends | The same |
| `-NoWait "d:\trle\script\script.txt"` | Compiles `d:\trle`, because the quoted string can stand anywhere | The same |
| `d:\my trle\script\script.txt -Log` | Compiles `d:\my trle`: the whole line is the path, and the folder of the path ends before the switches | The same, while the words of the path are one space apart |
| `d:\TRLE - Copy\script\script.txt -Log` | Compiles `d:\TRLE - Copy`, because `-` is no switch | The same |
| `d:\my -Log folder\script\script.txt` | Compiles `d:\my -Log folder` and writes the log, because the switch is a word of the whole line | Reads `-Log` as the switch, so the path ends before it: an error unless the path is quoted |
| `-NoWait d:\trle\script\script.txt` | Compiles the engine folder NG Center saved at its last run, because the folder of the whole line, `-NoWait d:\trle\script`, does not exist | Compiles `d:\trle` |
| `"d:\trle\script\myscript.txt"` | Compiles `d:\trle\script\SCRIPT.TXT`, because any command line holding `script.txt` starts the compile (`Principale.frm:2927`) | An error that asks for the path of `script.txt` |
| A path whose folder does not exist | Compiles the engine folder NG Center saved at its last run (`Module1.bas:3729-3737`) | An error that the engine folder was not found |
| A word that is no switch, such as `-Quiet` | Ignored | An error that names the unknown option |
| A compile that fails | Exit code 0, and a message box unless `-NoMsgBox` (`Principale.frm:6792-6795`) | Exit code 1, and `Compilation failed` printed once |

- `-Log` replaces `LastCompilerLog.txt` in the engine folder with the run's log lines, in Windows-1252.
- NG Center deletes that file at every start, even without `-Log`,
  in the folder it saved (`Module1.bas:3722`, `15883-15894`).
  The port touches the file only when `-Log` asks for it.
- `-NoMsgBox` is accepted and does nothing, because the console shows no message box.
- The wait for a key is skipped when the input is redirected, so a launcher that hides the console and does
  not redirect the input passes `-NoWait`.

The port reads the path of the command line this way:

- The C runtime splits an unquoted path at its spaces before the port sees it,
  so the port joins the pieces with one space, and a path that holds two spaces in a row is quoted.
- A word of the path that starts with `-` stays in a path that names `script.txt`.
- The port reads a word that is one of NG Center's switches or one of its own options, such as `-c`, as that
  switch or option, so a path that holds one is quoted.

A file name other than `script.txt` would compile a file the command line does not name, and the port has no
saved folder to fall back on. A failing exit code lets the batch file stop on a failed compile.
