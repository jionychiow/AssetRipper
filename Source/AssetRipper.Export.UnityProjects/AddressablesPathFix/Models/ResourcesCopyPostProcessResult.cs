namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record ResourcesCopyPostProcessResult
{
	public required ResourcesCopyResult? CopyResult { get; init; }
	public required MetaBatchResult? MetaResult { get; init; }
	public required MetaValidationResult? ValidationResult { get; init; }
	public bool Skipped { get; init; }
	public string? SkipReason { get; init; }
}