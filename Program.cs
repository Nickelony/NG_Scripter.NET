using System.Text;
using TRNGScriptCompiler.Compiler;
using TRNGScriptCompiler.Utilities;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

Console.WriteLine("=========================================");
Console.WriteLine("   TRNG Script Compiler (C# Edition)");
Console.WriteLine("   Converted from VB6 to C#");
Console.WriteLine("=========================================");
Console.WriteLine();

try
{
	string trleFolderPath;
	bool waitForKey = false;

	// Parse command-line arguments
	if (args.Length == 0)
	{
		// Interactive mode
		Console.Write("Enter TRLE folder path: ");
		trleFolderPath = Console.ReadLine()?.Trim() ?? "";

		if (string.IsNullOrEmpty(trleFolderPath))
		{
			Console.WriteLine("Error: No TRLE folder specified.");
			return 1;
		}

		waitForKey = true;
	}
	else
	{
		// Command-line mode
		trleFolderPath = args[0];

		// Check for options
		for (int i = 1; i < args.Length; i++)
		{
			switch (args[i].ToLower())
			{
				case "-wait":
				case "/wait":
					waitForKey = true;
					break;

				case "-quiet":
				case "/quiet":
					Logger.SetConciseMode(true);
					break;

				case "-verbose":
				case "/verbose":
					Logger.SetConciseMode(false);
					break;
			}
		}
	}

	// Validate TRLE folder
	if (!Directory.Exists(trleFolderPath))
	{
		Logger.LogError($"TRLE folder does not exist: {trleFolderPath}");

		if (waitForKey)
		{
			Console.WriteLine("\nPress any key to exit...");
			Console.ReadKey();
		}

		return 1;
	}

	// Check for required files
	string tomb4Exe = Path.Combine(trleFolderPath, "tomb4.exe");
	string scriptFolder = Path.Combine(trleFolderPath, "script");

	if (!File.Exists(tomb4Exe))
		Logger.LogWarning("tomb4.exe not found. This may not be a valid TRLE folder.");

	if (!Directory.Exists(scriptFolder))
	{
		Logger.LogError($"Script folder not found: {scriptFolder}");

		if (waitForKey)
		{
			Console.WriteLine("\nPress any key to exit...");
			Console.ReadKey();
		}

		return 1;
	}

	Console.WriteLine();

	// Run the compiler
	var compiler = new TRNGCompiler();
	bool success = compiler.CompileAll(trleFolderPath, waitForKey);

	if (success)
	{
		Console.WriteLine();

		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine("✓ Compilation completed successfully!");

		Console.ResetColor();

		return 0;
	}
	else
	{
		Console.WriteLine();

		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine("✗ Compilation failed with errors.");

		Console.ResetColor();

		if (waitForKey)
		{
			Console.WriteLine("\nPress any key to exit...");
			Console.ReadKey();
		}

		return 1;
	}
}
catch (Exception ex)
{
	Console.ForegroundColor = ConsoleColor.Red;

	Console.WriteLine($"\nFatal error: {ex.Message}");
	Console.WriteLine($"Stack trace:\n{ex.StackTrace}");

	Console.ResetColor();

	if (args.Length == 0) // Interactive mode
	{
		Console.WriteLine("\nPress any key to exit...");
		Console.ReadKey();
	}

	return 1;
}
