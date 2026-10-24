namespace AssetRipper.Tools.NaninovelAuditor.Models;

/// <summary>
/// 差异记录（spec 6.3）：差异编号/双路径必填/差异类型/严重级别/关联模块/修复条目引用。
/// </summary>
public sealed record DiffRecord
{
	public required int DiffId { get; init; }
	public required string ExportedPath { get; init; }
	public required string SourcePath { get; init; }
	public required DiffType DiffType { get; init; }
	public required Severity Severity { get; init; }
	public required AuditModule RelatedModule { get; init; }
	public int? RepairPlanItemId { get; init; }
	public string LinkedLegacyIssue { get; init; } = "";
	public string Detail { get; init; } = "";
}