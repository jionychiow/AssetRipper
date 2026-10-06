namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record MetaValidationResult
{
	public required IReadOnlyList<string> MissingMetaFiles { get; init; }
	public required IReadOnlyList<string> InvalidGuidFiles { get; init; }
	public required IReadOnlyDictionary<string, IReadOnlyList<string>> DuplicateGuids { get; init; }
	public required int TotalResourceFiles { get; init; }
	public required int TotalMetaFiles { get; init; }
	public bool IsValid => MissingMetaFiles.Count == 0 && InvalidGuidFiles.Count == 0 && DuplicateGuids.Count == 0;
}