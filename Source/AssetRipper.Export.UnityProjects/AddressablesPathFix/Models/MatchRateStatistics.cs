namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record MatchRateStatistics
{
	public required string PathPrefix { get; init; }
	public required int TotalAddresses { get; init; }
	public required int MatchedCount { get; init; }
	public required int UnmatchedCount { get; init; }
	public required double MatchRate { get; init; }
}