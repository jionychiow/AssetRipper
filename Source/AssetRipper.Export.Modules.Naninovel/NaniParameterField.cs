namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniParameterField
{
	public bool HasValue { get; set; }
	public object? Value { get; set; }
	public string? ValueText { get; set; }
	public string[]? Expressions { get; set; }
	public bool DynamicValue { get; set; }
	public string ParameterAlias { get; set; } = string.Empty;
	public bool IsList { get; set; }
	public bool IsNamed { get; set; }
	public string? NamedName { get; set; }
	public string? NamedValue { get; set; }
	public bool NamedValueHasValue { get; set; }
}