using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Post-Exporter that extracts official Naninovel package resources (source code, ScriptImporter, DOTween)
/// into the export project, replacing AssetRipper's modified DLLs with source compilation mode.
/// </summary>
public sealed class NaninovelPackageExtractorPostExporter : IPostExporter
{
	private const string NaninovelPackageName = "Naninovel - Visual Novel Engine v1.18.1.unitypackage";
	private const string DotTweenZipName = "DOTween_1_2_335.zip";

	/// <inheritdoc/>
	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		if (!NaninovelProjectDetector.IsNaninovelProject(settings.AssetsPath, fileSystem))
		{
			Logger.Info(LogCategory.Export, "[NaninovelPackageExtractor] Non-Naninovel project, skipping official package extraction.");
			return;
		}

		Logger.Info(LogCategory.Export, "[NaninovelPackageExtractor] Naninovel project detected, starting official package extraction...");

		string packageDir = LocatePackageDirectory();
		string unitypackagePath = Path.Join(packageDir, NaninovelPackageName);
		string dotweenZipPath = Path.Join(packageDir, DotTweenZipName);

		if (!File.Exists(unitypackagePath))
		{
			string errorMsg = $"[NaninovelPackageExtractor] Official package file not found: {unitypackagePath}";
			Logger.Error(LogCategory.Export, errorMsg);
			throw new FileNotFoundException(errorMsg, unitypackagePath);
		}

		bool dotTweenEnabled = File.Exists(dotweenZipPath);
		if (!dotTweenEnabled)
		{
			Logger.Warning(LogCategory.Export, $"[NaninovelPackageExtractor] DOTween zip not found: {dotweenZipPath}, skipping DOTween extraction.");
		}

		Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Reading unitypackage: {unitypackagePath}");
		IReadOnlyDictionary<string, UnityPackageEntry> entries = UnityPackageReader.Read(unitypackagePath);
		Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Unitypackage contains {entries.Count} entries.");


		IReadOnlyDictionary<string, byte[]>? zipEntries = null;
		if (dotTweenEnabled)
		{
			Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Reading DOTween zip: {dotweenZipPath}");
			zipEntries = DotTweenZipReader.Read(dotweenZipPath);
		}

		ExtractionManifest manifest = ExtractionManifest.CreateDefault();

		// DLL replacement mode: skip source compilation and DLL cleaning.
		// Original DLLs are synced from the game's Managed directory by NaninovelResourcePatcherPostExporter.
		// Only inject official ScriptImporter (delete hand-written one) and extract DOTween.
		ExtractionReport importerReport = OfficialScriptImporterInjector.Inject(settings.AssetsPath, fileSystem);

		ExtractionReport? dotTweenReport = null;
		if (dotTweenEnabled && zipEntries is not null)
		{
			dotTweenReport = DotTweenDependencyExporter.Export(zipEntries, settings.AssetsPath, fileSystem);
		}

		ExtractionReport cleanerReport = SupersededDllCleaner.Clean(settings.AssetsPath, fileSystem);

		ExtractionReport merged = dotTweenReport is not null
			? ExtractionReport.Merge(importerReport, dotTweenReport, cleanerReport)
			: ExtractionReport.Merge(importerReport, cleanerReport);

		Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Extraction complete: {merged.SucceededCount} succeeded, {merged.SkippedCount} skipped, {merged.FailedCount} failed.");

		if (merged.FailedCount > 0)
		{
			foreach (ExtractionRecord record in merged.Records)
			{
				if (record.Status == ExtractionStatus.Failed)
				{
					Logger.Warning(LogCategory.Export, $"[NaninovelPackageExtractor] Failed: {record.PathName} -> {record.TargetPath}: {record.ErrorMessage}");
				}
			}
		}
	}

	private static string LocatePackageDirectory()
	{
		string assemblyDir = AppContext.BaseDirectory;
		string? current = assemblyDir;

		while (current is not null)
		{
			string candidate = Path.Join(current, "Naninovel", "Packages");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			candidate = Path.Join(current, "Source", "AssetRipper.Export.UnityProjects", "Naninovel", "Packages");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			current = Directory.GetParent(current)?.FullName;
		}

		return Path.Join(assemblyDir, "Naninovel", "Packages");
	}
}