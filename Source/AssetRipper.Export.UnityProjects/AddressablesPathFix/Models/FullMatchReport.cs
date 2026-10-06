namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class FullMatchReport
{
	public required int TotalResources { get; init; }
	public required int MatchedResources { get; init; }
	public required int SkippedResources { get; init; }
	public required int FailedResources { get; init; }
	public required double OverallMatchRate { get; init; }
	public required MatchMethodStatistics MatchMethodStats { get; init; }
	public required IReadOnlyList<MatchRateStatistics> PathPrefixStats { get; init; } = [];
	public required IReadOnlyList<string> UnmappedAddresses { get; init; } = [];
	public required bool FullMatchAchieved { get; init; }
	public required IReadOnlyList<string> StaleAddresses { get; init; } = [];
}