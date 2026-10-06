namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class NaninovelModuleConfig
{
	public required string ModuleName { get; init; }
	public string DefaultPathPrefix { get; init; } = string.Empty;
	public required IReadOnlyList<string> MetadataPathPrefixes { get; init; } = [];
	public required IReadOnlyList<string> ProviderTypes { get; init; } = [];
	public required IReadOnlyList<string> MetadataIds { get; init; } = [];

	public IReadOnlyList<string> AllPathPrefixes
	{
		get
		{
			HashSet<string> prefixes = new(StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrEmpty(DefaultPathPrefix))
			{
				prefixes.Add(DefaultPathPrefix);
			}
			foreach (string p in MetadataPathPrefixes)
			{
				if (!string.IsNullOrEmpty(p))
				{
					prefixes.Add(p);
				}
			}
			return prefixes.ToList();
		}
	}
}