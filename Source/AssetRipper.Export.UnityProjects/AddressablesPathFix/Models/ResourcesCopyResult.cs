namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record ResourcesCopyResult
{
	public required IReadOnlyList<ResourcesCopySuccess> CopiedFiles { get; init; }
	public required IReadOnlyList<ResourcesCopySkip> SkippedFiles { get; init; }
	public required IReadOnlyList<ResourcesCopyFailure> FailedFiles { get; init; }
	public required IReadOnlyList<string> ExcludedFiles { get; init; }
	public required int TotalScanned { get; init; }
}
