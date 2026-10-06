namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class CatalogParseException : Exception
{
	public string FieldName { get; }
	public int? Offset { get; }

	public CatalogParseException(string message, string fieldName, int? offset = null, Exception? inner = null)
		: base(message, inner)
	{
		FieldName = fieldName;
		Offset = offset;
	}
}