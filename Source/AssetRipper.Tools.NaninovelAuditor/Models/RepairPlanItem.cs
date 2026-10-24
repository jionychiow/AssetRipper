namespace AssetRipper.Tools.NaninovelAuditor.Models;

/// <summary>
/// 修复方案条目（spec 6.5-4）：修改源码文件/修改意图/预期验证标准/快照要求。
/// </summary>
public sealed record RepairPlanItem
{
	public required int ItemId { get; init; }
	public required string TargetSourceFile { get; init; }
	public required string ModificationIntent { get; init; }
	public required string VerificationStandard { get; init; }
	public required ModificationSnapshot SnapshotRequirement { get; init; }
}