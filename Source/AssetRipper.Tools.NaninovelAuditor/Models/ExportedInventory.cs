namespace AssetRipper.Tools.NaninovelAuditor.Models;

public sealed record ExportedInventory
{
	public required IReadOnlyList<ExportedInventoryEntry> Entries { get; init; }
}

/// <summary>
/// 导出资产清单条目（spec 6.2）：基础标识（已剥离 _数字 后缀）/导出路径/副本序号/内容特征/差异状态。
/// </summary>
public sealed record ExportedInventoryEntry
{
	public required string BaseAssetIdentifier { get; init; }
	public required string ExportedPath { get; init; }
	/// <summary>本体为 null，副本为 0~N。</summary>
	public int? DuplicateIndex { get; init; }
	public required int ByteLength { get; init; }
	public required string Sha256 { get; init; }
	public ExportDiffStatus DiffStatus { get; init; } = ExportDiffStatus.NotExported;
	public string? MetaGuid { get; init; }
}