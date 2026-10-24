namespace AssetRipper.Tools.NaninovelAuditor.Models;

public sealed record SourceInventory
{
	public required IReadOnlyList<SourceInventoryEntry> Entries { get; init; }
}

/// <summary>
/// 原始资产清单条目（spec 6.1）：资产类型/资产标识/数据源路径/字节长度/SHA-256/权威版本标记。
/// </summary>
public sealed record SourceInventoryEntry
{
	public required NaninovelAssetType AssetType { get; init; }
	public required string AssetIdentifier { get; init; }
	public required string DataSourcePath { get; init; }
	public required int ByteLength { get; init; }
	public required string Sha256 { get; init; }
	public bool IsAuthoritative { get; init; }
	public string AuthorityBasis { get; init; } = "";
	public long PathId { get; init; }
	public string? BaselineTextSha256 { get; init; }
}