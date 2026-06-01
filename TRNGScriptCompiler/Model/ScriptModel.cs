namespace TRNGScriptCompiler.Model;

/// <summary>The script after the parse and the NG compile, which the <c>Script.dat</c> encoders read.</summary>
/// <param name="Parsed">The script as the parser collected it.</param>
/// <param name="NgModel">The NG compile of the script's NG lines.</param>
/// <remarks>
/// The NG commands are compiled, in <see cref="NgModel"/>, whose level groups follow the sections of the parsed script.
/// The classic lines of each section are the lines the parser collected, because the composition of <c>Script.dat</c>
/// encodes them later.
/// </remarks>
internal sealed record ScriptModel(ParsedScript Parsed, NgScriptModel NgModel);
