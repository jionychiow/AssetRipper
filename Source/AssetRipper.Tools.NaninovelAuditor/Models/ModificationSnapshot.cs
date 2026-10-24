namespace AssetRipper.Tools.NaninovelAuditor.Models;

/// <summary>
/// 防污染快照模型（2026-10-21 教训的强制前置）：修复阶段每个条目必须内嵌。
/// 执行约定：
/// 1. 修改前落盘本快照（目标文件清单 + fileID 级记录 + 修改前内容哈希）；
/// 2. 断言"实际修改数 == 计划修改数"，不等即中止；
/// 3. 回退时断言"回退数 == 修改数"，不等即中止并人工介入。
/// </summary>
public sealed record ModificationSnapshot
{
	public required IReadOnlyList<string> TargetFileList { get; init; }
	public required IReadOnlyList<FileIdRecord> FileIds { get; init; }
	/// <summary>修改前目标文件内容的 SHA-256（多文件按路径排序后逐文件哈希再汇总）。</summary>
	public required string PreStateHash { get; init; }
	public required int PlannedModificationCount { get; init; }
}

/// <summary>fileID 级记录：prefab/asset 内具体对象的 fileID 与修改前字段值片段。</summary>
public sealed record FileIdRecord
{
	public required string FilePath { get; init; }
	public required long FileId { get; init; }
	public required string FieldName { get; init; }
	public required string OldValue { get; init; }
}