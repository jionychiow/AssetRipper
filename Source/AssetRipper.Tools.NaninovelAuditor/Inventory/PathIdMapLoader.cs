using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;

namespace AssetRipper.Tools.NaninovelAuditor.Inventory;

/// <summary>
/// 存量 path_id_map.json 读取与交叉校验（PathID/Name/Collection 三字段一致率）。
/// 注意：该映射只记录 Mesh/Texture/AudioClip/TextAsset（PathIdMapExporter 的类型过滤），
/// 不含 MonoBehaviour，交叉校验仅对 TextAsset 清单条目有效。
/// </summary>
internal static class PathIdMapLoader
{
	public static PathIdMap? TryLoad(string exportedRoot, out string loadError)
	{
		string mapPath = Path.Combine(exportedRoot, InventoryScope.PathIdMapRelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(mapPath))
		{
			loadError = $"path_id_map.json not found at {mapPath}";
			return null;
		}
		try
		{
			PathIdMap map = ArtifactWriter.ReadJson<PathIdMap>(mapPath);
			loadError = string.Empty;
			return map;
		}
		catch (Exception ex)
		{
			loadError = ex.Message;
			return null;
		}
	}

	public static PathIdMapCrossCheckResult CrossCheck(PathIdMap map, IReadOnlyList<SourceInventoryEntry> textAssetEntries)
	{
		Dictionary<string, HashSet<long>> pathIdsByFile = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, HashSet<string>> namesByFile = new(StringComparer.OrdinalIgnoreCase);
		foreach (PathIdMapFile file in map.Files)
		{
			HashSet<long> pathIds = [];
			HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
			foreach (PathIdMapEntry asset in file.Assets)
			{
				pathIds.Add(asset.PathID);
				names.Add(asset.Name);
			}
			pathIdsByFile[file.Name] = pathIds;
			namesByFile[file.Name] = names;
		}

		int checkedCount = 0;
		int matchedCount = 0;
		List<string> mismatches = [];
		foreach (SourceInventoryEntry entry in textAssetEntries)
		{
			string collectionName = GetCollectionName(entry.DataSourcePath);
			if (collectionName is null || !pathIdsByFile.TryGetValue(collectionName, out HashSet<long>? pathIds))
			{
				continue;
			}
			checkedCount++;
			if (pathIds.Contains(entry.PathId))
			{
				matchedCount++;
			}
			else
			{
				mismatches.Add($"{entry.DataSourcePath} ({entry.AssetIdentifier})");
			}
		}

		return new PathIdMapCrossCheckResult(checkedCount, matchedCount, mismatches);
	}

	private static string? GetCollectionName(string dataSourcePath)
	{
		int hashIndex = dataSourcePath.LastIndexOf('#');
		return hashIndex > 0 ? dataSourcePath[..hashIndex] : null;
	}
}

internal sealed record PathIdMapCrossCheckResult(int CheckedCount, int MatchedCount, IReadOnlyList<string> Mismatches)
{
	public double MatchRate => CheckedCount == 0 ? 1.0 : (double)MatchedCount / CheckedCount;
}