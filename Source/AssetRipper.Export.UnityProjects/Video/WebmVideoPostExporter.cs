using System.IO;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Video;

public sealed class WebmVideoPostExporter : IPostExporter
{
	private const string ResourceFileName = "resources.resource";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists(settings.AssetsPath))
		{
			return;
		}

		string? resourceFilePath = LocateResourceFile(gameData, settings, fileSystem);
		if (resourceFilePath is null)
		{
			Logger.Warning(LogCategory.Export, "WebM repair: could not locate resources.resource file, skipping .webm repair");
			return;
		}

		try
		{
			int fixedCount = WebmEbmlRepairer.RepairFromResourceFile(settings.AssetsPath, resourceFilePath);
			Logger.Info(LogCategory.Export, $"WebM video repair complete: fixed {fixedCount} .webm file(s) from {resourceFilePath}");
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"WebM video repair failed: {ex.Message}");
		}
	}

	private static string? LocateResourceFile(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		if (!string.IsNullOrEmpty(settings.SourceDataPath))
		{
			string directPath = Path.Join(settings.SourceDataPath, ResourceFileName);
			if (File.Exists(directPath))
			{
				return directPath;
			}
		}


		return null;
	}
}