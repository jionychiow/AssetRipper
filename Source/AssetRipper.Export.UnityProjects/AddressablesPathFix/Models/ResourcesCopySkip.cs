namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record ResourcesCopySkip
{
	public required string SourcePath { get; init; }
	public required string TargetPath { get; init; }
	public required string Reason { get; init; }
}