using System.Text;
using AssetRipper.Import.Logging;
using AssetRipper.Tools.NaninovelAuditor.Inventory;
using AssetRipper.Tools.NaninovelAuditor.Models;
using AssetRipper.Tools.NaninovelAuditor.Reporting;
using AssetRipper.Tools.NaninovelAuditor.Session;

namespace AssetRipper.Tools.NaninovelAuditor.Modules;

/// <summary>
/// 任务5入口：扫描导出项目建立导出资产清单，统计副本数，
/// 输出 exported_inventory.json + exported_inventory.csv + exported_inventory_summary.md。
/// 纯文件系统操作，无需 GameData 加载（独立入口 Execute(AuditOptions)）。
/// </summary>
public sealed class ExportedInventoryModule : INaninovelAuditModule
{
	public string ModuleName => "exported-inventory";

	public AuditRunResult Execute(AuditSession session) => Execute(session.Options);

	public AuditRunResult Execute(AuditOptions options)
	{
		Logger.Info(LogCategory.System, "[exported-inventory] Scanning exported project...");
		ExportedInventoryBuilder builder = new();
		ExportedInventory inventory = builder.Build(options.ExportedRoot, out IReadOnlyList<string> missingScanDirs);

		string jsonPath = Path.Combine(options.ReportDirectory, "exported_inventory.json");
		string csvPath = Path.Combine(options.ReportDirectory, "exported_inventory.csv");
		string mdPath = Path.Combine(options.ReportDirectory, "exported_inventory_summary.md");

		ArtifactWriter.WriteJson(jsonPath, inventory);
		ArtifactWriter.WriteCsv(csvPath,
			["BaseAssetIdentifier", "ExportedPath", "DuplicateIndex", "ByteLength", "Sha256", "MetaGuid"],
			inventory.Entries.Select(e => new[]
			{
				e.BaseAssetIdentifier,
				e.ExportedPath,
				e.DuplicateIndex?.ToString() ?? "",
				e.ByteLength.ToString(),
				e.Sha256,
				e.MetaGuid ?? "",
			}));

		WriteSummary(mdPath, inventory, missingScanDirs);

		int naniTotal = inventory.Entries.Count(e => e.ExportedPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase));
		int naniBaseIds = inventory.Entries.Where(e => e.ExportedPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase))
			.Select(e => e.BaseAssetIdentifier).Distinct(StringComparer.OrdinalIgnoreCase).Count();
		int duplicateFiles = inventory.Entries.Count(e => e.DuplicateIndex is not null);
		Logger.Info(LogCategory.System, $"[exported-inventory] Done: total={inventory.Entries.Count}, nani={naniTotal} (baseIds={naniBaseIds}), duplicates={duplicateFiles}");

		return new AuditRunResult
		{
			ModuleName = ModuleName,
			Success = true,
			Summary = $"total={inventory.Entries.Count}; naniFiles={naniTotal}; naniBaseIds={naniBaseIds}; duplicateFiles={duplicateFiles}; missingScanDirs={missingScanDirs.Count}",
			ProducedArtifacts = [jsonPath, csvPath, mdPath],
		};
	}

	private static void WriteSummary(string path, ExportedInventory inventory, IReadOnlyList<string> missingScanDirs)
	{
		StringBuilder sb = new();
		sb.AppendLine("# Exported Inventory Summary");
		sb.AppendLine();
		sb.AppendLine($"- Total entries: {inventory.Entries.Count}");
		sb.AppendLine($"- Duplicate-suffix files (_数字): {inventory.Entries.Count(e => e.DuplicateIndex is not null)}");
		sb.AppendLine();

		sb.AppendLine("## Scanned directories");
		sb.AppendLine();
		sb.AppendLine("| Directory | Files |");
		sb.AppendLine("|-----------|-------|");
		foreach (IGrouping<string, ExportedInventoryEntry> group in inventory.Entries
			.GroupBy(e => string.Join('/', e.ExportedPath.Split('/')[..^1])))
		{
			sb.AppendLine($"| {group.Key} | {group.Count()} |");
		}
		foreach (string missing in missingScanDirs)
		{
			sb.AppendLine($"| {missing} | (MISSING) |");
		}
		sb.AppendLine();

		sb.AppendLine("## Duplicate distribution (per base identifier)");
		sb.AppendLine();
		sb.AppendLine("| Copies | Base identifiers |");
		sb.AppendLine("|--------|------------------|");
		foreach (IGrouping<int, IGrouping<string, ExportedInventoryEntry>> copiesGroup in inventory.Entries
			.GroupBy(e => e.BaseAssetIdentifier, StringComparer.OrdinalIgnoreCase)
			.GroupBy(g => g.Count())
			.OrderBy(g => g.Key))
		{
			sb.AppendLine($"| {copiesGroup.Key} | {copiesGroup.Count()} |");
		}
		sb.AppendLine();

		sb.AppendLine("## Top duplicated identifiers (>= 3 copies)");
		sb.AppendLine();
		sb.AppendLine("| Base identifier | Copies |");
		sb.AppendLine("|-----------------|--------|");
		foreach (IGrouping<string, ExportedInventoryEntry> group in inventory.Entries
			.GroupBy(e => e.BaseAssetIdentifier, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() >= 3)
			.OrderByDescending(g => g.Count()))
		{
			sb.AppendLine($"| {group.Key} | {group.Count()} |");
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
	}
}