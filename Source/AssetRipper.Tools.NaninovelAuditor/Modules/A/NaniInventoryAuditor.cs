using System.Text;
using AssetRipper.Tools.NaninovelAuditor.Models;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.A;

/// <summary>
/// A模块数量三维核对与数字裁决（任务7）：基础脚本ID × 原始存在性 × 导出副本数三维核对，
/// 产出双向缺口清单与"用户陈述 111 vs 实测 645（去重后 110）"数字关系裁决（REQ-A1/A2）。
/// </summary>
internal sealed class NaniInventoryAuditor
{
	private const int UserClaimedBaseIdCount = 111;
	private const int ObservedExportedFileCount = 645;

	public InventoryAuditResult Audit(SourceInventory sourceInventory, ExportedInventory exportedInventory)
	{
		Dictionary<string, List<SourceInventoryEntry>> sourceById = sourceInventory.Entries
			.Where(e => e.AssetType == NaninovelAssetType.NaniScript)
			.GroupBy(e => e.AssetIdentifier, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		Dictionary<string, List<ExportedInventoryEntry>> exportedById = exportedInventory.Entries
			.Where(e => e.ExportedPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase))
			.GroupBy(e => e.BaseAssetIdentifier, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		List<DiffRecord> diffs = [];
		List<ThreeWayRow> rows = [];
		int diffId = 1;

		foreach (KeyValuePair<string, List<SourceInventoryEntry>> sourcePair in sourceById)
		{
			exportedById.TryGetValue(sourcePair.Key, out List<ExportedInventoryEntry>? exportedEntries);
			List<ExportedInventoryEntry> copies = exportedEntries ?? [];
			rows.Add(new ThreeWayRow(sourcePair.Key, sourcePair.Value.Count, copies.Count,
				string.Join("; ", sourcePair.Value.Select(s => s.DataSourcePath))));

			if (copies.Count == 0)
			{
				diffs.Add(new DiffRecord
				{
					DiffId = diffId++,
					ExportedPath = $"(missing) Assets/Resources/nScripts/{sourcePair.Key}.nani",
					SourcePath = string.Join("; ", sourcePair.Value.Select(s => s.DataSourcePath)),
					DiffType = DiffType.QuantityMissing,
					Severity = Severity.Fatal,
					RelatedModule = AuditModule.A,
					LinkedLegacyIssue = "功能缺失：原始脚本未导出",
					Detail = $"Source has {sourcePair.Value.Count} instance(s), export has 0 file(s).",
				});
			}
		}

		foreach (KeyValuePair<string, List<ExportedInventoryEntry>> exportedPair in exportedById)
		{
			if (sourceById.ContainsKey(exportedPair.Key))
			{
				continue;
			}
			rows.Add(new ThreeWayRow(exportedPair.Key, 0, exportedPair.Value.Count, "(no source)"));
			diffs.Add(new DiffRecord
			{
				DiffId = diffId++,
				ExportedPath = string.Join("; ", exportedPair.Value.Select(e => e.ExportedPath)),
				SourcePath = "(no source instance found)",
				DiffType = DiffType.QuantityMissing,
				Severity = Severity.Severe,
				RelatedModule = AuditModule.A,
				Detail = $"Export has {exportedPair.Value.Count} file(s) but source inventory has no instance with this identifier.",
			});
		}

		NumberVerdict verdict = BuildVerdict(sourceById, exportedById);
		CopyDistribution distribution = BuildCopyDistribution(exportedById);

		return new InventoryAuditResult(rows, diffs, verdict, distribution);
	}

	private static NumberVerdict BuildVerdict(
		Dictionary<string, List<SourceInventoryEntry>> sourceById,
		Dictionary<string, List<ExportedInventoryEntry>> exportedById)
	{
		int sourceInstanceCount = sourceById.Values.Sum(v => v.Count);
		int sourceUniqueIds = sourceById.Count;
		int exportedUniqueIds = exportedById.Count;
		int exportedFileCount = exportedById.Values.Sum(v => v.Count);

		StringBuilder sb = new();
		sb.AppendLine($"- 原始实例数: {sourceInstanceCount}（唯一基础脚本ID: {sourceUniqueIds}）");
		sb.AppendLine($"- 导出文件数: {exportedFileCount}（去重后基础ID: {exportedUniqueIds}）");
		sb.AppendLine($"- 用户陈述基础ID数: {UserClaimedBaseIdCount}；实测导出文件总数: {ObservedExportedFileCount}");
		sb.AppendLine($"- 裁决: 导出文件数 {exportedFileCount} ≈ 去重基础ID {exportedUniqueIds} × 平均副本数 {(exportedUniqueIds == 0 ? 0 : (double)exportedFileCount / exportedUniqueIds):F1}");
		sb.AppendLine($"- 数字关系: 原始唯一ID({sourceUniqueIds}) 与 导出去重ID({exportedUniqueIds}) 的差值 = {sourceUniqueIds - exportedUniqueIds}（负值=导出多出、正值=导出缺失、0=数量一致）");
		sb.AppendLine($"- 用户陈述 {UserClaimedBaseIdCount} 与实测 {exportedUniqueIds} 差异: {UserClaimedBaseIdCount - exportedUniqueIds}（以实测为准，用户陈述为约数）");

		return new NumberVerdict(sourceInstanceCount, sourceUniqueIds, exportedFileCount, exportedUniqueIds, sb.ToString());
	}

	private static CopyDistribution BuildCopyDistribution(Dictionary<string, List<ExportedInventoryEntry>> exportedById)
	{
		Dictionary<int, int> copiesToIds = [];
		foreach (List<ExportedInventoryEntry> entries in exportedById.Values)
		{
			int copies = entries.Count;
			copiesToIds[copies] = copiesToIds.GetValueOrDefault(copies) + 1;
		}
		int totalDuplicateFiles = exportedById.Values.Sum(v => v.Count(e => e.DuplicateIndex is not null));
		return new CopyDistribution(copiesToIds, totalDuplicateFiles);
	}
}

internal sealed record ThreeWayRow(string BaseId, int SourceInstances, int ExportedFiles, string SourcePaths);

internal sealed record NumberVerdict(
	int SourceInstanceCount,
	int SourceUniqueIds,
	int ExportedFileCount,
	int ExportedUniqueIds,
	string VerdictText);

internal sealed record CopyDistribution(IReadOnlyDictionary<int, int> CopiesToBaseIdCounts, int TotalDuplicateFiles);

internal sealed record InventoryAuditResult(
	IReadOnlyList<ThreeWayRow> Rows,
	IReadOnlyList<DiffRecord> Diffs,
	NumberVerdict Verdict,
	CopyDistribution Distribution);