using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects.Naninovel.Package;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Project;

public sealed class UniTaskPlayerLoopPostExporter : IPostExporter
{
	private const string UniRxAsyncDllName = "UniRx.Async.dll";
	private const string EditorDirectoryName = "Editor";
	private const string PluginsDirectoryName = "Plugins";
	private const string GameLibsDirectoryName = "GameLibs";

	private readonly UniTaskPlayerLoopSettings _settings;

	public UniTaskPlayerLoopPostExporter() : this(new UniTaskPlayerLoopSettings()) { }

	public UniTaskPlayerLoopPostExporter(UniTaskPlayerLoopSettings settings)
	{
		_settings = settings;
	}

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		try
		{
			if (!_settings.EnableEditorScriptGeneration)
			{
				Logger.Info(LogCategory.Export, "UniTaskPlayerLoopPostExporter: Editor script generation disabled, skipping.");
				return;
			}

			if (!NaninovelProjectDetector.IsNaninovelProject(settings.AssetsPath, fileSystem))
			{
				Logger.Info(LogCategory.Export, "UniTaskPlayerLoopPostExporter: Not a Naninovel project, skipping.");
				return;
			}

			string pluginsPath = fileSystem.Path.Join(settings.AssetsPath, PluginsDirectoryName, GameLibsDirectoryName);
			string dllPath = fileSystem.Path.Join(pluginsPath, UniRxAsyncDllName);
			if (!fileSystem.File.Exists(dllPath))
			{
				Logger.Warning(LogCategory.Export, $"UniTaskPlayerLoopPostExporter: {UniRxAsyncDllName} not found at {dllPath}, skipping.");
				return;
			}

			string editorDir = fileSystem.Path.Join(settings.AssetsPath, EditorDirectoryName);
			if (!fileSystem.Directory.Exists(editorDir))
			{
				Directory.CreateDirectory(editorDir);
			}

			string editorPath = fileSystem.Path.Join(editorDir, _settings.EditorScriptFileName);
			GenerateEditorScript(editorPath, fileSystem);
			GenerateMetaFile(editorPath, fileSystem);
			RunDiagnostic(assetsPath: settings.AssetsPath, dllPath: dllPath, fileSystem: fileSystem);
			RunVerification(editorPath: editorPath, dllPath: dllPath, fileSystem: fileSystem);
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"UniTaskPlayerLoopPostExporter failed: {ex}");
		}
	}

	private void GenerateEditorScript(string editorPath, FileSystem fileSystem)
	{
		string content = UniTaskPlayerLoopInitializerTemplate.GetTemplate();

		if (fileSystem.File.Exists(editorPath))
		{
			string existingContent = File.ReadAllText(editorPath);
			if (string.Equals(existingContent, content, StringComparison.Ordinal))
			{
				Logger.Info(LogCategory.Export, $"UniTaskPlayerLoopInitializer.cs already exists with identical content, skipping.");
				return;
			}
		}

		File.WriteAllText(editorPath, content);
		Logger.Info(LogCategory.Export, $"Generated UniTaskPlayerLoopInitializer at {editorPath}");
	}

	private static void GenerateMetaFile(string csPath, FileSystem fileSystem)
	{
		string metaPath = csPath + ".meta";
		if (fileSystem.File.Exists(metaPath))
		{
			return;
		}

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

	private static void RunDiagnostic(string assetsPath, string dllPath, FileSystem fileSystem)
	{
		string editorDir = fileSystem.Path.Join(assetsPath, EditorDirectoryName);
		string scriptPath = fileSystem.Path.Join(editorDir, "UniTaskPlayerLoopInitializer.cs");

		if (!fileSystem.File.Exists(scriptPath))
		{
			Logger.Error(LogCategory.Export, $"RunDiagnostic: UniTaskPlayerLoopInitializer.cs not found at {scriptPath}");
		}

		string metaPath = scriptPath + ".meta";
		if (!fileSystem.File.Exists(metaPath))
		{
			Logger.Warning(LogCategory.Export, $"RunDiagnostic: .meta file not found for {scriptPath}");
		}

		if (!fileSystem.File.Exists(dllPath))
		{
			Logger.Warning(LogCategory.Export, $"RunDiagnostic: {UniRxAsyncDllName} not found at {dllPath}");
		}
	}

	private static void RunVerification(string editorPath, string dllPath, FileSystem fileSystem)
	{
		if (fileSystem.File.Exists(editorPath))
		{
			string content = File.ReadAllText(editorPath);
			if (!content.Contains("using") || !content.Contains("class") || !content.Contains("[InitializeOnLoad]"))
			{
				Logger.Warning(LogCategory.Export, "RunVerification: Generated script missing basic syntax structure.");
			}
		}

		string metaPath = editorPath + ".meta";
		if (fileSystem.File.Exists(metaPath))
		{
			string metaContent = File.ReadAllText(metaPath);
			if (!metaContent.Contains("guid: ") || metaContent.Length < 50)
			{
				Logger.Warning(LogCategory.Export, "RunVerification: .meta file appears invalid.");
			}
		}
	}
}