namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniManagedReference
{
	public long Rid { get; set; }
	public string Class { get; set; } = string.Empty;
	public string Namespace { get; set; } = string.Empty;
	public string Assembly { get; set; } = string.Empty;
	public Dictionary<string, object?>? Fields { get; set; }
}