namespace TRNGScriptCompiler.Tests.Support;

/// <summary>The blocks of <c>Script.txt</c> text that tests share, so a test writes only what it is about.</summary>
/// <remarks>
/// A block ends in a line feed, so blocks and further lines join by concatenation. A test keeps its own copy of a block
/// when the exact text is the point, such as a line number, a line ending or a variant spelling.
/// </remarks>
public static class TestScripts
{
	/// <summary>The <c>LoadCamera=</c> line that every level needs, without its line end.</summary>
	public const string LoadCamera = "LoadCamera= 0,0,0,0,0,0,0";

	/// <summary>The <c>[Language]</c> section that lists <c>English.txt</c> as language 0.</summary>
	public const string Language = "[Language]\nFile= 0, English.txt\n";

	/// <summary>
	/// The smallest <c>[Level]</c> section that compiles: the name <c>Test Level</c>, the level path <c>data\test</c>
	/// and the <see cref="LoadCamera"/> line.
	/// </summary>
	public const string Level = "[Level]\nName= Test Level\nLevel= data\\test,0\n" + LoadCamera + "\n";

	/// <summary>The <see cref="Language"/> section followed by the <see cref="Level"/> section.</summary>
	public const string LanguageAndLevel = Language + Level;

	/// <summary>
	/// The <see cref="Language"/> section, an <c>[Options]</c> section that disables the title and the security,
	/// and a level named <c>Image Level</c>, which takes an <c>Image=</c> line after it.
	/// </summary>
	public const string ImageLevel =
		Language + "[Options]\nTitle= DISABLED\nSecurity= 0\n" + "[Level]\nName= Image Level\nLevel= data\\image,0\n" + LoadCamera + "\n";

	/// <summary>The <c>Image=</c> line that encrypts <c>pix\image4.bmp</c>, with its line end.</summary>
	public const string CrypticImage = "Image= 1,4,IF_CRYPTIC,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE,IGNORE\n";
}
