using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using System.Security.Cryptography;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Extracts DOTween DLLs and module source files from the official zip package
/// into the export project if DOTween dependency is detected.
/// </summary>
public static class DotTweenDependencyExporter
{
	/// <summary>
	/// Extracts DOTween files from the zip entries into the export project if dependency is detected.
	/// </summary>
	public static ExtractionReport Export(IReadOnlyDictionary<string, byte[]> zipEntries, string assetsPath, FileSystem fileSystem)
	{
		ExtractionReport report = new();

		if (!HasDotTweenDependency(assetsPath, fileSystem))
		{
			Logger.Info(LogCategory.Export, "[NaninovelPackageExtractor] No DOTween dependency detected, skipping DOTween extraction.");
			return report;
		}

		Logger.Info(LogCategory.Export, "[NaninovelPackageExtractor] DOTween dependency detected, extracting DOTween files...");

		string dotweenPath = fileSystem.Path.Join(assetsPath, "Plugins", "GameLibs", "DOTween");
		string dotweenEditorPath = fileSystem.Path.Join(dotweenPath, "Editor");

		foreach (KeyValuePair<string, byte[]> entry in zipEntries)
		{
			string entryName = entry.Key;
			byte[] data = entry.Value;

			string? targetPath = MapDotweenEntry(entryName, dotweenPath, dotweenEditorPath);
			if (targetPath is null)
			{
				continue;
			}

			ExtractionRecord record = ExtractFile(entryName, data, targetPath);
			report.Add(record);
		}

		Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] DOTween extraction: {report.SucceededCount} succeeded, {report.SkippedCount} skipped, {report.FailedCount} failed");
		return report;
	}

	private static bool HasDotTweenDependency(string assetsPath, FileSystem fileSystem)
	{
		string pluginsPath = fileSystem.Path.Join(assetsPath, "Plugins", "GameLibs");

		string dotweenDll = fileSystem.Path.Join(pluginsPath, "DOTween.dll");
		if (fileSystem.File.Exists(dotweenDll))
		{
			return true;
		}

		string runtimeDir = fileSystem.Path.Join(assetsPath, "Naninovel", "Runtime");
		if (fileSystem.Directory.Exists(runtimeDir))
		{
			foreach (string csFile in fileSystem.Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.AllDirectories))
			{
				try
				{
					string content = fileSystem.File.ReadAllText(csFile);
					if (content.Contains("DG.Tweening", StringComparison.Ordinal))
					{
						return true;
					}
				}
				catch
				{
				}
			}
		}

		return false;
	}

	private static string? MapDotweenEntry(string entryName, string dotweenPath, string dotweenEditorPath)
	{
		string fileName = Path.GetFileName(entryName);
		string ext = Path.GetExtension(fileName);

		if (ext == ".dll")
		{
			if (fileName == "DOTween.dll")
			{
				return null;
			}
			if (fileName == "DOTweenEditor.dll" || fileName == "DOTweenUpgradeManager.dll")
			{
				return null;
			}
			return null;
		}

		if (ext == ".cs")
		{
			return Path.Join(dotweenPath, fileName);
		}

		return null;
	}

	private static ExtractionRecord ExtractFile(string entryName, byte[] data, string targetPath)
	{
		try
		{
			string? dir = Path.GetDirectoryName(targetPath);
			if (dir is not null && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			using MD5 md5 = MD5.Create();
			string hash = Convert.ToHexString(md5.ComputeHash(data));

			if (File.Exists(targetPath))
			{
				using FileStream existingStream = File.OpenRead(targetPath);
				string existingHash = Convert.ToHexString(md5.ComputeHash(existingStream));
				if (existingHash == hash)
				{
					return new ExtractionRecord(entryName, targetPath, ExtractionStatus.Skipped, hash);
				}
			}

			File.WriteAllBytes(targetPath, data);
			Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Extracted DOTween file: {entryName} -> {targetPath}");
			return new ExtractionRecord(entryName, targetPath, ExtractionStatus.Succeeded, hash);
		}
		catch (Exception ex)
		{
			return new ExtractionRecord(entryName, targetPath, ExtractionStatus.Failed, null, ex.Message);
		}
	}
}