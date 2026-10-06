namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class PathIdAddressMap
{
	public IReadOnlyDictionary<long, PathIdAddressEntry> ByPathId { get; init; } = new Dictionary<long, PathIdAddressEntry>();
	public IReadOnlyDictionary<string, PathIdAddressEntry> ByName { get; init; } = new Dictionary<string, PathIdAddressEntry>(StringComparer.OrdinalIgnoreCase);
	public int Count => ByPathId.Count;

	public bool TryGetAddress(long pathId, out string? address)
	{
		if (ByPathId.TryGetValue(pathId, out PathIdAddressEntry? entry) && entry is not null)
		{
			address = entry.Address;
			return true;
		}
		address = null;
		return false;
	}

	public bool TryGetAddressByName(string name, out string? address)
	{
		if (ByName.TryGetValue(name, out PathIdAddressEntry? entry) && entry is not null)
		{
			address = entry.Address;
			return true;
		}
		address = null;
		return false;
	}

	public bool TryGetEntry(long pathId, out PathIdAddressEntry? entry)
	{
		return ByPathId.TryGetValue(pathId, out entry);
	}
}
