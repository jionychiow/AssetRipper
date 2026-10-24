namespace AssetRipper.Tools.NaninovelAuditor.Models;

/// <summary>
/// 警告错误条目（spec 6.4）：级别/消息模板/涉及资产路径/出现次数/分类/可修复性/清零阶段。
/// </summary>
public sealed record DiagnosticEntry
{
	public required DiagnosticLevel Level { get; init; }
	public required string MessageTemplate { get; init; }
	public required IReadOnlyList<string> AssetPaths { get; init; }
	public required int OccurrenceCount { get; init; }
	public required DiagnosticCategory Category { get; init; }
	public required Repairability Repairability { get; init; }
	public required ZeroTargetPhase Phase { get; init; }
	public int? RepairPlanItemId { get; init; }
	public string ExemptionBasis { get; init; } = "";
}