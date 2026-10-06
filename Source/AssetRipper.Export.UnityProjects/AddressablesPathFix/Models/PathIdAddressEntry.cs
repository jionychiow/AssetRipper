namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed record PathIdAddressEntry
{
	public required long PathID { get; init; }
	public required string Address { get; init; }
	public required string ResourceType { get; init; }
	public required string BundleName { get; init; }
}