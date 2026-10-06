namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class NaninovelConfig
{
	public required IReadOnlyList<NaninovelModuleConfig> Modules { get; init; }
	public required IReadOnlySet<string> PathPrefixes { get; init; }
	public bool UseAddressables { get; init; }

	public bool IsPathPrefixInScope(string prefix)
	{
		return PathPrefixes.Contains(prefix);
	}
}