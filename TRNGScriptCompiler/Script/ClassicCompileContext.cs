using TRNGScriptCompiler.Kernel.Diagnostics;
using TRNGScriptCompiler.Model;

namespace TRNGScriptCompiler.Script;

/// <summary>Carries one command's mutable compilation state through the encoding of a classic command.</summary>
/// <remarks>
/// Every encoding of a classic command takes this one context, so the methods of the encoder share one shape.
/// </remarks>
internal sealed class ClassicCompileContext
{
	private readonly ScriptCommandLine _line;

	internal ClassicCompileContext(List<byte> bytes, int cdSlot, ScriptCommandLine line)
	{
		Bytes = bytes;
		CdSlot = cdSlot;
		Command = line.Command;
		_line = line;
	}

	/// <summary>Gets the output byte buffer the handler appends to.</summary>
	internal List<byte> Bytes { get; }

	internal ScriptLevelFlags Flags { get; set; }

	/// <summary>Gets or sets the CD slot of the <c>Level=</c> line, which the section header stores.</summary>
	internal int CdSlot { get; set; }

	/// <summary>Gets or sets the command name; the encoder overwrites it with its canonical casing.</summary>
	internal string Command { get; set; }

	/// <summary>Gets the normalized text of the line, whose remainder the commands that take it read.</summary>
	/// <remarks>
	/// <c>Legend=</c> reads the text after its command as one string, because a comma there is no separator.
	/// </remarks>
	internal string Text => _line.Text;

	/// <summary>Gets the 1-based line of the command and the full path of the source file that declared it.</summary>
	internal SourceLocation Location => _line.Location;

	/// <summary>
	/// Returns the location of an argument in the source line, or of the whole line when it is not written as is.
	/// </summary>
	/// <param name="index">The zero-based index of the argument.</param>
	internal SourceLocation ArgumentLocation(int index) => _line.ArgumentLocation(index);
}
