using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Deletes DLLs from the export project that are superseded by source compilation.
/// </summary>
public static class SupersededDllCleaner
{
	/// <summary>
	/// Removes decompiled DOTween module files that duplicate DOTween.dll.
	/// Does NOT remove superseded DLLs (they are retained in DLL replacement mode).
	/// </summary>
	public static ExtractionReport Clean(string assetsPath, FileSystem fileSystem)
	{
		ExtractionReport report = new();
		RemoveDecompiledDotweenModules(assetsPath, fileSystem, report);
		return report;
	}

	/// <summary>
	/// Removes the DG/Tweening folder from decompiled Assembly-CSharp scripts.
	/// These classes duplicate DOTween.dll and cause CS0121 ambiguous call errors.
	/// </summary>
	private static void RemoveDecompiledDotweenModules(string assetsPath, FileSystem fileSystem, ExtractionReport report)
	{
		string dgPath = fileSystem.Path.Join(assetsPath, "Scripts", "Assembly-CSharp", "DG");

		if (fileSystem.Directory.Exists(dgPath))
		{
			try
			{
				Directory.Delete(dgPath, recursive: true);
				Logger.Info(LogCategory.Export, $"[NaninovelPackageExtractor] Removed decompiled DOTween modules: {dgPath}");

				string metaPath = dgPath + ".meta";
				if (fileSystem.File.Exists(metaPath))
				{
					File.Delete(metaPath);
				}

				report.Add(new ExtractionRecord("DG", dgPath, ExtractionStatus.Succeeded));
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"[NaninovelPackageExtractor] Failed to remove DG folder: {ex.Message}");
				report.Add(new ExtractionRecord("DG", dgPath, ExtractionStatus.Failed, null, ex.Message));
			}
		}
	}
}