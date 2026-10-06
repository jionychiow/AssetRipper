using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Naninovel.Import;

public sealed class NaniScriptImporterPostExporter : IPostExporter
{
	private const string NaninovelRuntimeDllName = "Elringus.Naninovel.Runtime.dll";
	private const string EditorDirectoryName = "Editor";
	private const string ImporterScriptName = "NaniScriptImporter.cs";
	private const string PluginsDirectoryName = "Plugins";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		try
		{
			if (!ShouldGenerateImporter(settings.AssetsPath, fileSystem))
			{
				return;
			}

			string editorDir = fileSystem.Path.Join(settings.AssetsPath, EditorDirectoryName);
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			string editorPath = fileSystem.Path.Join(editorDir, ImporterScriptName);
			GenerateImporterScript(editorPath, fileSystem);
			Logger.Info(LogCategory.Export, $"Generated NaniScriptImporter at {editorPath}");
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"NaniScriptImporterPostExporter failed: {ex}");
		}
	}

	private static bool ShouldGenerateImporter(string assetsPath, FileSystem fileSystem)
	{
		string pluginsPath = fileSystem.Path.Join(assetsPath, PluginsDirectoryName, "GameLibs");
		string dllPath = fileSystem.Path.Join(pluginsPath, NaninovelRuntimeDllName);
		if (!fileSystem.File.Exists(dllPath))
		{
			Logger.Warning(LogCategory.Export, $"Naninovel runtime DLL not found at {dllPath}, skipping importer generation.");
			return false;
		}

		return true;
	}

	private static void GenerateImporterScript(string editorPath, FileSystem fileSystem)
	{
		string content = NaniScriptImporterTemplate.GetTemplate();

		if (fileSystem.File.Exists(editorPath))
		{
			string existingContent = File.ReadAllText(editorPath);
			if (string.Equals(existingContent, content, StringComparison.Ordinal))
			{
				Logger.Info(LogCategory.Export, "NaniScriptImporter.cs already exists with identical content, skipping.");
				return;
			}
		}

		File.WriteAllText(editorPath, content);
		GenerateMetaFile(editorPath, fileSystem);
	}

	private static void GenerateMetaFile(string csPath, FileSystem fileSystem)
	{
		string metaPath = csPath + ".meta";
		string guid = Guid.NewGuid().ToString("N");
		string metaContent = $$"""
fileFormatVersion: 2
guid: {{guid}}
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:

""";
		File.WriteAllText(metaPath, metaContent);
	}
}