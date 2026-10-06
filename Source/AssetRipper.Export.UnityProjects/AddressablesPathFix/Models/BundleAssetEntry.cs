namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record BundleAssetEntry
{
	public required long PathID { get; init; }
	public required string Name { get; init; }
	public required string TypeName { get; init; }
	public required string BundleName { get; init; }
}