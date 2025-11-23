using System.Text;

namespace TRNGScriptCompiler.Utilities;

public static class BinaryUtilities
{
    /// <summary>
    /// Converts a long value to a 16-bit word (short).
    /// </summary>
    public static short LongToWord(long value)
        => (short)(value & 0xFFFF);

    /// <summary>
    /// Splits a long into two 16-bit words (low and high).
    /// </summary>
    public static (short low, short high) LongToTwoWords(long value)
    {
        short low = (short)(value & 0xFFFF);
        short high = (short)((value >> 16) & 0xFFFF);

        return (low, high);
    }

    /// <summary>
    /// Combines two bytes into a word.
    /// </summary>
    public static short TwoBytesToWord(byte lowByte, byte highByte)
        => (short)(lowByte | (highByte << 8));

    /// <summary>
    /// Writes a byte to a binary writer.
    /// </summary>
    public static void WriteByte(BinaryWriter writer, byte value)
        => writer.Write(value);

    /// <summary>
    /// Writes a 16-bit word to a binary writer.
    /// </summary>
    public static void WriteWord(BinaryWriter writer, short value)
        => writer.Write(value);

    /// <summary>
    /// Writes a 32-bit dword to a binary writer.
    /// </summary>
    public static void WriteDWord(BinaryWriter writer, int value)
        => writer.Write(value);

    /// <summary>
    /// Reads a null-terminated string from a binary reader.
    /// </summary>
    public static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;

        while ((b = reader.ReadByte()) != 0)
            bytes.Add(b);

        return Encoding.GetEncoding(1252).GetString([.. bytes]);
    }

    /// <summary>
    /// Writes a null-terminated string to a binary writer.
    /// </summary>
    public static void WriteNullTerminatedString(BinaryWriter writer, string text)
    {
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(text);

        writer.Write(bytes);
        writer.Write((byte)0);
    }

    /// <summary>
    /// Adds bytes to a byte array.
    /// </summary>
    public static void AddBytes(List<byte> target, params byte[] bytes)
        => target.AddRange(bytes);

    /// <summary>
    /// Adds a word (16-bit) to a byte array.
    /// </summary>
    public static void AddWord(List<byte> target, short value)
    {
        target.Add((byte)(value & 0xFF));
        target.Add((byte)((value >> 8) & 0xFF));
    }

    /// <summary>
    /// Adds a dword (32-bit) to a byte array.
    /// </summary>
    public static void AddDWord(List<byte> target, int value)
    {
        target.Add((byte)(value & 0xFF));
        target.Add((byte)((value >> 8) & 0xFF));
        target.Add((byte)((value >> 16) & 0xFF));
        target.Add((byte)((value >> 24) & 0xFF));
    }
}
