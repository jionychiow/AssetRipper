using AssetRipper.Export.Configuration;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Platforms;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Project;

public class StreamingAssetsPostExporter : IPostExporter
{
	private const string StreamingAssetsFolderName = "StreamingAssets";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, $"StreamingAssetsPostExporter: StreamingAssetsMode={settings.ImportSettings.StreamingAssetsMode}, SourceDataPath='{settings.SourceDataPath}'");
		if (settings.ImportSettings.StreamingAssetsMode == StreamingAssetsMode.Ignore)
		{
			Logger.Warning(LogCategory.Export, "StreamingAssetsPostExporter: mode is Ignore, skipping");
			return;
		}

		string? inputDirectory = FindStreamingAssetsPath(gameData, settings, fileSystem);
		Logger.Info(LogCategory.Export, $"StreamingAssetsPostExporter: FindStreamingAssetsPath returned '{inputDirectory}'");
		if (string.IsNullOrEmpty(inputDirectory) || !fileSystem.Directory.Exists(inputDirectory))
		{
			Logger.Warning(LogCategory.Export, $"StreamingAssets directory not found, skipping.");
			return;
		}

		Logger.Info(LogCategory.Export, $"Copying streaming assets from '{inputDirectory}'...");
		string outputDirectory = fileSystem.Path.Join(settings.AssetsPath, StreamingAssetsFolderName);

		fileSystem.Directory.Create(outputDirectory);

		foreach (string directory in fileSystem.Directory.EnumerateDirectories(inputDirectory, "*", SearchOption.AllDirectories))
		{
			string relativePath = fileSystem.Path.GetRelativePath(inputDirectory, directory);
			fileSystem.Directory.Create(fileSystem.Path.Join(outputDirectory, relativePath));
		}

		foreach (string file in fileSystem.Directory.EnumerateFiles(inputDirectory, "*", SearchOption.AllDirectories))
		{
			string relativePath = fileSystem.Path.GetRelativePath(inputDirectory, file);
			string newFile = fileSystem.Path.Join(outputDirectory, relativePath);

			using Stream readStream = fileSystem.File.OpenRead(file);
			using Stream writeStream = fileSystem.File.Create(newFile);
			readStream.CopyTo(writeStream);
		}

		Logger.Info(LogCategory.Export, "Finished copying streaming assets.");
	}

	private static string? FindStreamingAssetsPath(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		PlatformGameStructure? platform = gameData.PlatformStructure;
		if (platform is not null && !string.IsNullOrEmpty(platform.StreamingAssetsPath))
		{
			Logger.Info(LogCategory.Export, $"FindStreamingAssetsPath: platform.StreamingAssetsPath='{platform.StreamingAssetsPath}'");
			return platform.StreamingAssetsPath;
		}
		Logger.Info(LogCategory.Export, $"FindStreamingAssetsPath: platform is null={platform is null}, platform.StreamingAssetsPath is empty={platform is null || string.IsNullOrEmpty(platform.StreamingAssetsPath)}");

		if (!string.IsNullOrEmpty(settings.SourceDataPath))
		{
			string sourcePath = settings.SourceDataPath;
			Logger.Info(LogCategory.Export, $"FindStreamingAssetsPath: SourceDataPath='{sourcePath}', dir exists={fileSystem.Directory.Exists(sourcePath)}");
			if (fileSystem.Directory.Exists(sourcePath))
			{
				string directPath = Path.Join(sourcePath, StreamingAssetsFolderName);
				Logger.Info(LogCategory.Export, $"FindStreamingAssetsPath: directPath='{directPath}', exists={fileSystem.Directory.Exists(directPath)}");
				if (fileSystem.Directory.Exists(directPath))
				{
					return directPath;
				}
			}

			string? parentDir = Path.GetDirectoryName(sourcePath);
			if (parentDir is not null)
			{
				string folderName = Path.GetFileName(sourcePath);
				Logger.Info(LogCategory.Export, $"FindStreamingAssetsPath: parentDir='{parentDir}', folderName='{folderName}', endsWith_Data={folderName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)}");
				if (folderName.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
				{
					string dataPath = Path.Join(parentDir, folderName, StreamingAssetsFolderName);
					Logger.Info(LogCategory.Export, $"FindStreamingAssetsPath: dataPath='{dataPath}', exists={fileSystem.Directory.Exists(dataPath)}");
					if (fileSystem.Directory.Exists(dataPath))
					{
						return dataPath;
					}
				}
			}
		}
		else
		{
			Logger.Warning(LogCategory.Export, "FindStreamingAssetsPath: SourceDataPath is empty");
		}

		return null;
	}
}
