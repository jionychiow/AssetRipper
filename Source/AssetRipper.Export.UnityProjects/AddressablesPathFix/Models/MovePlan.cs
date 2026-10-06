namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class MovePlan
{
	public required IReadOnlyList<MoveItem> MoveItems { get; init; } = [];
	public required IReadOnlyList<UnmappedResource> UnmappedResources { get; init; } = [];
	public int TotalCount => MoveItems.Count + UnmappedResources.Count;
}