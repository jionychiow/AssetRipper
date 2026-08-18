namespace AssetRipper.Export.Modules.Naninovel;

public sealed class ParameterEncoder
{
	public string Encode(NaniParameterField param, NaniTypeField field)
	{
		if (!param.HasValue)
		{
			return string.Empty;
		}

		string encodedValue = EncodeValue(param);
		if (string.IsNullOrEmpty(encodedValue))
		{
			return string.Empty;
		}

		if (field.ParameterAlias == "")
		{
			return encodedValue;
		}

		return $"{field.ParameterAlias}:{encodedValue}";
	}

	private string EncodeValue(NaniParameterField param)
	{
		if (param.DynamicValue && !string.IsNullOrEmpty(param.ValueText))
		{
			return param.ValueText;
		}

		if (param.IsNamed)
		{
			string name = param.NamedName ?? string.Empty;
			if (param.NamedValueHasValue && param.NamedValue is not null)
			{
				return $"{name}.{param.NamedValue}";
			}
			return name;
		}

		if (param.IsList && param.Value is List<string> list)
		{
			List<string> parts = new(list.Count);
			foreach (string? item in list)
			{
				parts.Add(item is null ? string.Empty : TextEscaper.Escape(item));
			}
			return $"[{string.Join(",", parts)}]";
		}

		return FormatValue(param.Value);
	}

	private static string FormatValue(object? value)
	{
		return value switch
		{
			null => string.Empty,
			bool b => b ? "true" : "false",
			string s => TextEscaper.Escape(s),
			_ => value.ToString() ?? string.Empty,
		};
	}
}