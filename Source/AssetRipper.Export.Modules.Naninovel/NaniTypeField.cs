namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniTypeField
{
	public string Name { get; set; } = string.Empty;
	public NaniFieldType FieldType { get; set; }
	public bool IsManagedReference { get; set; }
	public bool IsArray { get; set; }
	public int ArrayDepth { get; set; }
	public string ParameterAlias { get; set; } = string.Empty;
	public string ParameterSubType { get; set; } = string.Empty;

	public NaniTypeField() { }

	public NaniTypeField(string name, NaniFieldType fieldType)
	{
		Name = name;
		FieldType = fieldType;
	}

	public NaniTypeField(string name, NaniFieldType fieldType, string parameterAlias, string parameterSubType)
	{
		Name = name;
		FieldType = fieldType;
		ParameterAlias = parameterAlias;
		ParameterSubType = parameterSubType;
	}

	public bool IsBaseField => Name is "Wait" or "ConditionalExpression" or "playbackSpot";
}