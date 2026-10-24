using AssetRipper.Tools.NaninovelAuditor.Models;

namespace AssetRipper.Tools.NaninovelAuditor.Modules.A;

/// <summary>
/// A模块副本溯源（任务9）：_0~_4 副本原始来源指认与成因归类（REQ-A4）。
/// 核心逻辑：每份副本（含本体）按内容哈希关联原始资产实例；
/// 成因归类依据源实例的数据源分布（多数据源=未去重合并、单数据源多副本=重复处理）。
/// </summary>
internal sealed class DuplicateOriginTracer
{
	public TraceAuditResult Trace(SourceInventory sourceInventory, ExportedInventory exportedInventory)
	{
		Dictionary<string, List<SourceInventoryEntry>> sourceById = sourceInventory.Entries
			.Where(e => e.AssetType == NaninovelAssetType.NaniScript)
			.GroupBy(e => e.AssetIdentifier, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

		List<DuplicateOriginTrace> traces = [];
		Dictionary<string, int> causeCounts = new();
		int tracedCopies = 0;
		int untracedCopies = 0;

		foreach (IGrouping<string, ExportedInventoryEntry> group in exportedInventory.Entries
			.Where(e => e.ExportedPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase))
			.GroupBy(e => e.BaseAssetIdentifier, StringComparer.OrdinalIgnoreCase))
		{
			if (!sourceById.TryGetValue(group.Key, out List<SourceInventoryEntry>? sourceEntries) || sourceEntries.Count == 0)
			{
				foreach (ExportedInventoryEntry entry in group)
				{
					traces.Add(new DuplicateOriginTrace
					{
						DuplicateFilePath = entry.ExportedPath,
						SourcePathId = 0,
						SourceBundle = "(untraced: no source instance)",
						Cause = DuplicateOriginCause.Other,
					});
					untracedCopies++;
				}
				causeCounts["untraced"] = causeCounts.GetValueOrDefault("untraced") + group.Count();
				continue;
			}

			// 源实例数据源集合（bundle 分布）
			List<string> sourceBundles = sourceEntries.Select(s => s.DataSourcePath.Split('#')[0]).Distinct().ToList();
			bool multiSource = sourceBundles.Count > 1 || sourceEntries.Count > 1;
			DuplicateOriginCause cause = multiSource
				? DuplicateOriginCause.MultipleDataSourceWithoutDedup
				: DuplicateOriginCause.SameBundleParsedMultipleTimes;

			foreach (ExportedInventoryEntry entry in group)
			{
				SourceInventoryEntry matched = MatchByContentHash(entry, sourceEntries) ?? sourceEntries[0];
				bool inferred = MatchByContentHash(entry, sourceEntries) is null && sourceEntries.Count > 1;
				traces.Add(new DuplicateOriginTrace
				{
					DuplicateFilePath = entry.ExportedPath,
					SourcePathId = matched.PathId,
					SourceBundle = matched.DataSourcePath.Split('#')[0],
					Cause = cause,
					InferredByIndexPattern = inferred,
				});
				tracedCopies++;
			}
			causeCounts[cause.ToString()] = causeCounts.GetValueOrDefault(cause.ToString()) + group.Count();
		}

		// 典型脚本溯源表（≥5个，优先高频重复）
		List<TypicalTrace> typical = exportedInventory.Entries
			.Where(e => e.ExportedPath.EndsWith(".nani", StringComparison.OrdinalIgnoreCase))
			.GroupBy(e => e.BaseAssetIdentifier, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() >= 3)
			.OrderByDescending(g => g.Count())
			.Take(10)
			.Select(g => new TypicalTrace(
				g.Key,
				g.Count(),
				string.Join("; ", sourceById.TryGetValue(g.Key, out List<SourceInventoryEntry>? src) ? src.Select(s => s.DataSourcePath) : ["(none)"])))
			.ToList();

		return new TraceAuditResult(traces, causeCounts, tracedCopies, untracedCopies, typical);
	}

	private static SourceInventoryEntry? MatchByContentHash(ExportedInventoryEntry entry, List<SourceInventoryEntry> sourceEntries)
	{
		foreach (SourceInventoryEntry source in sourceEntries)
		{
			if (source.BaselineTextSha256 is not null && string.Equals(source.BaselineTextSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return source;
			}
		}
		return null;
	}
}

internal sealed record TypicalTrace(string BaseId, int ExportedFiles, string SourceInstances);

internal sealed record TraceAuditResult(
	IReadOnlyList<DuplicateOriginTrace> Traces,
	IReadOnlyDictionary<string, int> CauseCounts,
	int TracedCopies,
	int UntracedCopies,
	IReadOnlyList<TypicalTrace> TypicalTraces);