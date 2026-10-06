namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record ResourcesCopySuccess
{
	public required string SourcePath { get; init; }
	public required string TargetPath { get; init; }
	public required long FileSizeBytes { get; init; }
}