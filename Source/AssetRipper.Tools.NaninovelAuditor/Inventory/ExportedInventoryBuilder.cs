using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Inventory;

/// <summary>
/// 导出资产清单构建器（任务5）：文件系统扫描导出项目关键目录全集，
/// 按"剥离 _数字 后缀得基础ID"建立导出清单，统计副本数与内容哈希、读取 .meta GUID。
/// 纯文件系统操作，不需要 GameData 会话。
/// </summary>
internal sealed class ExportedInventoryBuilder
{
	/// <summary>扫描的关键目录全集（相对于导出项目根）。</summary>
	private static readonly string[] ScanRelativeDirs =
	[
		"Assets/Resources/nScripts",
		"Assets/Resources/naninovel",
		"Assets/StreamingAssets/text",
		"Assets/StreamingAssets/aa",
		"Assets/Resources/Localization",
		"Assets/Resources/sexdialog",
		"Assets/Naninovel",
		"Assets/NaninovelInventory",
	];

	private static readonly string[] ScanExtensions =
	[
		".nani", ".asset", ".txt", ".json", ".csv", ".tsv",
	];

	public ExportedInventory Build(string exportedRoot, out IReadOnlyList<string> missingScanDirs)
	{
		string projectRoot = InventoryScope.GetExportedProjectRoot(exportedRoot);
		List<ExportedInventoryEntry> entries = [];
		List<string> missingDirs = [];

		foreach (string relativeDir in ScanRelativeDirs)
		{
			string absDir = Path.Combine(projectRoot, relativeDir.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(absDir))
			{
				missingDirs.Add(relativeDir);
				continue;
			}
			foreach (string file in Directory.EnumerateFiles(absDir, "*", SearchOption.AllDirectories))
			{
				string extension = Path.GetExtension(file);
				if (!ScanExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
				{
					continue;
				}
				string fileName = Path.GetFileName(file);
				(string baseName, int? duplicateIndex) = SuffixStripper.Strip(fileName);
				// BaseAssetIdentifier 须与源清单口径一致：无扩展名的基础标识。
				string baseIdentifier = Path.GetFileNameWithoutExtension(baseName);
				string relativePath = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
				FileInfo info = new(file);
				entries.Add(new ExportedInventoryEntry
				{
					BaseAssetIdentifier = baseIdentifier,
					ExportedPath = relativePath,
					DuplicateIndex = duplicateIndex,
					ByteLength = (int)info.Length,
					Sha256 = ArtifactWriter.ComputeSha256File(file),
					DiffStatus = ExportDiffStatus.NotExported,
					MetaGuid = ReadMetaGuid(file + ".meta"),
				});
			}
		}

		missingScanDirs = missingDirs;
		return new ExportedInventory { Entries = entries };
	}

	private static string? ReadMetaGuid(string metaPath)
	{
		if (!File.Exists(metaPath))
		{
			return null;
		}
		foreach (string line in File.ReadLines(metaPath))
		{
			if (line.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
			{
				return line["guid:".Length..].Trim();
			}
		}
		return null;
	}
}