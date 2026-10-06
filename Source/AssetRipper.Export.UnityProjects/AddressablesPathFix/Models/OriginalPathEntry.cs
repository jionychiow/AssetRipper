namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class OriginalPathEntry
{
	public required long PathID { get; init; }
	public required string Name { get; init; }
	public required string ClassName { get; init; }
	public string? OriginalPath { get; init; }
	public string? OriginalDirectory { get; init; }
	public string? OriginalName { get; init; }
	public string? ResourcePath { get; init; }
	public bool HasResourcePath => ResourcePath is not null;
}