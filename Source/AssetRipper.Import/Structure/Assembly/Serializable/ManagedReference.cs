namespace AssetRipper.Import.Structure.Assembly.Serializable;

public sealed class ManagedReference
{
	public long Rid { get; set; }
	public string? Class { get; set; }
	public string? Namespace { get; set; }
	public string? Assembly { get; set; }
	public SerializableStructure? Data { get; set; }

	public string FullName => string.IsNullOrEmpty(Namespace) 
		? $"{Class}" 
		: $"{Namespace}.{Class}";

	public string TypeName => string.IsNullOrEmpty(Assembly) 
		? FullName 
		: $"{FullName}, {Assembly}";
}
