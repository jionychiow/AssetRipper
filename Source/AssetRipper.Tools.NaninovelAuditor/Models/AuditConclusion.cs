namespace AssetRipper.Tools.NaninovelAuditor.Models;

public sealed record AuditConclusion
{
	public required IReadOnlyList<ModuleCoverageEntry> ModuleCoverage { get; init; }
	public required IReadOnlyList<QuantityReconciliationEntry> QuantityReconciliation { get; init; }
	public required IReadOnlyList<RootCauseVerdict> RootCauses { get; init; }
	public required IReadOnlyList<RepairPlanItem> RepairPlan { get; init; }
	public required string ReexportVerificationStandard { get; init; }
}

public sealed record ModuleCoverageEntry
{
	public required AuditModule Module { get; init; }
	public required string Status { get; init; }
	public string Reason { get; init; } = "";
}

public sealed record QuantityReconciliationEntry
{
	/// <summary>对账组名：nani / text / configuration。</summary>
	public required string GroupName { get; init; }
	public required int SourceCount { get; init; }
	public required int ExportedDeduplicatedCount { get; init; }
	public required int DuplicateCopyCount { get; init; }
	public required string Notes { get; init; }
}

public sealed record RootCauseVerdict
{
	/// <summary>五类遗留问题之一：DialogueTextBlack / HasMovedFalse / TextCompEmpty / ScriptPlayerNotFound / WarningsAndErrors。</summary>
	public required string LegacyIssue { get; init; }
	/// <summary>资产侧根因 / 路径侧根因 / 混合 / 排除资产侧。</summary>
	public required string Verdict { get; init; }
	public required string EvidenceReference { get; init; }
}