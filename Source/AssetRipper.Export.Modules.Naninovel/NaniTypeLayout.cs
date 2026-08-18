namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniTypeLayout
{
	public string FullClassName { get; set; } = string.Empty;
	public List<NaniTypeField> Fields { get; set; } = new();

	public NaniTypeLayout() { }

	public NaniTypeLayout(string fullClassName, List<NaniTypeField> fields)
	{
		FullClassName = fullClassName;
		Fields = fields;
	}
}