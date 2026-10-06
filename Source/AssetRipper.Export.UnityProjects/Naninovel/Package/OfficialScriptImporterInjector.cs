using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Injects the official Naninovel ScriptImporter by removing the hand-written NaniScriptImporter.
/// The official ScriptImporter.cs is already extracted by NaninovelSourceCompileExporter.
/// </summary>
public static class OfficialScriptImporterInjector
{
	/// <summary>
	/// Removes the hand-written NaniScriptImporter.cs from the export project.
	/// </summary>
	public static ExtractionReport Inject(string assetsPath, FileSystem fileSystem)
	{
		ExtractionReport report = new();

		string officialImporterPath = fileSystem.Path.Join(assetsPath, "Naninovel", "Editor", "Script", "ScriptImporter.cs");
		if (!fileSystem.File.Exists(officialImporterPath))
		{
			Logger.Error(LogCategory.Export, "[NaninovelPackageExtractor] Official ScriptImporter.cs not found. Ensure NaninovelSourceCompileExporter has executed.");
			report.Add(new ExtractionRecord("ScriptImporter.cs", officialImporterPath, ExtractionStatus.Failed, null, "Official ScriptImporter not found"));
			return report;
		}

		string handWrittenPath = fileSystem.Path.Join(assetsPath, "Editor", "NaniScriptImporter.cs");
		if (fileSystem.File.Exists(handWrittenPath))
		{
			File.Delete(handWrittenPath);
			Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Deleted hand-written importer: {handWrittenPath}");
			report.Add(new ExtractionRecord("NaniScriptImporter.cs", handWrittenPath, ExtractionStatus.Succeeded));
		}
		else
		{
			Logger.Info(LogCategory.Export, "[NaninovelPackageExtractor] Hand-written NaniScriptImporter.cs not found, nothing to clean up.");
			report.Add(new ExtractionRecord("NaniScriptImporter.cs", handWrittenPath, ExtractionStatus.Skipped));
		}

		string metaPath = handWrittenPath + ".meta";
		if (fileSystem.File.Exists(metaPath))
		{
			File.Delete(metaPath);
			Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Deleted hand-written importer meta: {metaPath}");
		}

		return report;
	}
}