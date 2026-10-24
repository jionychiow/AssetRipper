using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Inventory;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// 任务4入口：构建原始权威清单，与 path_id_map.json 交叉校验，
/// 输出 source_inventory.json + source_inventory.csv + source_inventory_summary.md。
/// </summary>
public sealed class SourceInventoryModule : INaninovelAuditModule
{
	public string ModuleName => "source-inventory";

	public AuditRunResult Execute(AuditSession session)
	{
		Logger.Info(LogCategory.System, "[source-inventory] Building source inventory...");
		SourceInventoryBuilder builder = new();
		SourceInventory inventory = builder.Build(session.GameData, session.Options);

		PathIdMap? pathIdMap = PathIdMapLoader.TryLoad(session.Options.ExportedRoot, out string mapLoadError);
		PathIdMapCrossCheckResult? crossCheck = null;
		if (pathIdMap is not null)
		{
			// path_id_map 只记录 Mesh/Texture/AudioClip/TextAsset（不含 MonoBehaviour），
			// 交叉校验仅对 TextAsset 来源的条目（ManagedText 与 .nani 命名的 TextAsset）有效。
			IReadOnlyList<SourceInventoryEntry> textAssetEntries = inventory.Entries
				.Where(e => e.AssetType == NaninovelAssetType.ManagedText && e.PathId != 0)
				.ToList();
			crossCheck = PathIdMapLoader.CrossCheck(pathIdMap, textAssetEntries);
		}

		string jsonPath = Path.Combine(session.Options.ReportDirectory, "source_inventory.json");
		string csvPath = Path.Combine(session.Options.ReportDirectory, "source_inventory.csv");
		string mdPath = Path.Combine(session.Options.ReportDirectory, "source_inventory_summary.md");

		ArtifactWriter.WriteJson(jsonPath, inventory);
		ArtifactWriter.WriteCsv(csvPath,
			["AssetType", "AssetIdentifier", "DataSourcePath", "ByteLength", "Sha256", "IsAuthoritative", "AuthorityBasis", "PathId"],
			inventory.Entries.Select(e => new[]
			{
				e.AssetType.ToString(),
				e.AssetIdentifier,
				e.DataSourcePath,
				e.ByteLength.ToString(),
				e.Sha256,
				e.IsAuthoritative.ToString(),
				e.AuthorityBasis,
				e.PathId.ToString(),
			}));

		WriteSummary(mdPath, inventory, pathIdMap, mapLoadError, crossCheck);

		int naniCount = inventory.Entries.Count(e => e.AssetType == NaninovelAssetType.NaniScript);
		int configCount = inventory.Entries.Count(e => e.AssetType == NaninovelAssetType.Configuration);
		int managedTextCount = inventory.Entries.Count(e => e.AssetType == NaninovelAssetType.ManagedText);
		Logger.Info(LogCategory.System, $"[source-inventory] Done: nani={naniCount}, configuration={configCount}, managedText={managedTextCount}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = $"nani={naniCount}; configuration={configCount}; managedText={managedTextCount}; " +
				$"pathIdMapCrossCheck={(crossCheck is null ? $"unavailable ({mapLoadError})" : $"{crossCheck.MatchedCount}/{crossCheck.CheckedCount} matched")}",
			ProducedArtifacts = [jsonPath, csvPath, mdPath],
		};
	}

	private static void WriteSummary(string path, SourceInventory inventory, PathIdMap? pathIdMap, string mapLoadError, PathIdMapCrossCheckResult? crossCheck)
	{
		StringBuilder sb = new();
		sb.AppendLine("# Source Inventory Summary");
		sb.AppendLine();
		sb.AppendLine($"- Total entries: {inventory.Entries.Count}");
		foreach (Models.NaninovelAssetType type in Enum.GetValues<Models.NaninovelAssetType>())
		{
			int count = inventory.Entries.Count(e => e.AssetType == type);
			if (count > 0)
			{
				sb.AppendLine($"- {type}: {count}");
			}
		}
		sb.AppendLine();

		sb.AppendLine("## Data source distribution");
		sb.AppendLine();
		sb.AppendLine("| Source | Count |");
		sb.AppendLine("|--------|-------|");
		foreach (IGrouping<string, SourceInventoryEntry> group in inventory.Entries.GroupBy(e => e.DataSourcePath.Split('#')[0]))
		{
			sb.AppendLine($"| {group.Key} | {group.Count()} |");
		}
		sb.AppendLine();

		sb.AppendLine("## Duplicate identifiers (same-type variants)");
		sb.AppendLine();
		sb.AppendLine("| Key | Variants |");
		sb.AppendLine("|-----|----------|");
		foreach (IGrouping<string, SourceInventoryEntry> group in inventory.Entries
			.GroupBy(e => e.AssetType == Models.NaninovelAssetType.NaniScript ? e.AssetIdentifier : $"{e.AssetType}:{e.AssetIdentifier}", StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1))
		{
			sb.AppendLine($"| {group.Key} | {group.Count()} |");
		}
		sb.AppendLine();

		sb.AppendLine("## path_id_map.json cross-check (TextAsset entries)");
		sb.AppendLine();
		if (crossCheck is null)
		{
			sb.AppendLine($"- Map unavailable: {mapLoadError}");
		}
		else
		{
			sb.AppendLine($"- Checked: {crossCheck.CheckedCount}, matched: {crossCheck.MatchedCount}, rate: {crossCheck.MatchRate:P1}");
			if (crossCheck.Mismatches.Count > 0)
			{
				sb.AppendLine("- Mismatches:");
				foreach (string mismatch in crossCheck.Mismatches)
				{
					sb.AppendLine($"  - {mismatch}");
				}
			}
		}
		if (pathIdMap is not null)
		{
			sb.AppendLine($"- Map files: {pathIdMap.Files.Count}, total mapped assets: {pathIdMap.Files.Sum(f => f.Assets.Count)}");
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}