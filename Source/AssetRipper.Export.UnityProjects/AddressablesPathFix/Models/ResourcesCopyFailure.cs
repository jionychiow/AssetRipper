namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record ResourcesCopyFailure
{
	public required string SourcePath { get; init; }
	public required string TargetPath { get; init; }
	public required string Exception { get; init; }
	public required string Stage { get; init; }
}