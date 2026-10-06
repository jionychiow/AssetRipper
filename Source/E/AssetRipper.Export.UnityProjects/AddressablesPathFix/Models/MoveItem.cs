namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class MoveItem
{
	public required string SourcePath { get; init; }
	public required string SourceMetaPath { get; init; }
	public required string TargetPath { get; init; }
	public required string TargetMetaPath { get; init; }
	public required string Address { get; init; }
	public required MatchMethod MatchMethod { get; init; }
	public required string ResourceType { get; init; }
}