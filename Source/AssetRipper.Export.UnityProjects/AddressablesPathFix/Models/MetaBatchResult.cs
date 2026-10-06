namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record MetaBatchResult
{
	public required IReadOnlyList<MetaGenerationResult> Results { get; init; }
	public required int CreatedCount { get; init; }
	public required int SkippedCount { get; init; }
	public required int FailedCount { get; init; }
}