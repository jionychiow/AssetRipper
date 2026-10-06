namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class PathIdNameMap
{
	public IReadOnlyDictionary<long, PathIdNameEntry> ByPathId { get; init; } = new Dictionary<long, PathIdNameEntry>();
	public IReadOnlyDictionary<string, IReadOnlyList<PathIdNameEntry>> ByName { get; init; } = new Dictionary<string, IReadOnlyList<PathIdNameEntry>>();
	public int Count => ByPathId.Count;
}