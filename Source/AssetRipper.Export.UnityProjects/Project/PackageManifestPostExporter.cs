using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Project;

public class PackageManifestPostExporter : IPostExporter
{
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "Creating Packages/manifest.json...");
		string packagesDirectory = fileSystem.Path.Join(settings.ProjectRootPath, "Packages");
		fileSystem.Directory.Create(packagesDirectory);
		string path = fileSystem.Path.Join(packagesDirectory, "manifest.json");
		using Stream stream = fileSystem.File.Create(path);
		CreateManifest(settings.Version).Save(stream);
		Logger.Info(LogCategory.Export, $"Created manifest.json at {path}");
	}

	protected virtual PackageManifest CreateManifest(UnityVersion version)
	{
		return PackageManifest.CreateDefault(version);
	}
}
