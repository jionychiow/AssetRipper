using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;

namespace AssetRipper.Tools.ConsoleExporter;

internal static class Program
{
	public static int Main(string[] args)
	{
		Logger.Add(new ConsoleLogger(false));

		if (args.Length < 2)
		{
			Console.Error.WriteLine($"Usage: {Path.GetFileName(Environment.ProcessPath)} <input game path> <output path> [skip-plugins]");
			return 1;
		}

		string inputPath = args[0];
		string outputPath = args[1];

		Console.WriteLine($"Input:  {inputPath}");
		Console.WriteLine($"Output: {outputPath}");

		FullConfiguration settings = new();
		settings.LoadFromDefaultPath();

		if (args.Length >= 3 && args[2] == "skip-plugins")
		{
			settings.ExportSettings.PluginExportMode = PluginExportMode.Skip;
			Console.WriteLine("PluginExportMode: Skip");
		}

		ExportHandler handler = new(settings);
		handler.LoadProcessAndExport([inputPath], outputPath, LocalFileSystem.Instance);

		Console.WriteLine("Done!");
		return 0;
	}
}
