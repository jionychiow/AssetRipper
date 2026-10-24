namespace AssetRipper.Tools.NaninovelAuditor.Models;

/// <summary>
/// 副本溯源记录：导出副本文件 × 原始 PathID × 所在数据源/bundle × 成因。
/// </summary>
public sealed record DuplicateOriginTrace
{
	public required string DuplicateFilePath { get; init; }
	public required long SourcePathId { get; init; }
	public required string SourceBundle { get; init; }
	public required DuplicateOriginCause Cause { get; init; }
	/// <summary>哈希碰撞歧义时标注"序号规律辅助推断"。</summary>
	public bool InferredByIndexPattern { get; init; }
}

/// <summary>
/// 存量 path_id_map.json 的兼容输入模型（schema 与导出侧一致，不重复定义其语义）。
/// </summary>
public sealed record PathIdMap
{
	public required IReadOnlyList<PathIdMapFile> Files { get; init; }
}

public sealed record PathIdMapFile
{
	public required string Name { get; init; }
	public required IReadOnlyList<PathIdMapEntry> Assets { get; init; }
}

public sealed record PathIdMapEntry
{
	public required long PathID { get; init; }
	public required string Name { get; init; }
	public required string Type { get; init; }
}