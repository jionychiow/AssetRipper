namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record MatchMethodStatistics
{
	public required int PathIdAddressCount { get; init; }
	public required int OriginalGuidCount { get; init; }
	public required int PathIdCount { get; init; }
	public required int NameCount { get; init; }
	public int Total => PathIdAddressCount + OriginalGuidCount + PathIdCount + NameCount;
}