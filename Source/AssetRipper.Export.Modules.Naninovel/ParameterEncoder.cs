using System.Text;

namespace AssetRipper.Export.Modules.Naninovel;

public sealed class ParameterEncoder
{
	public string Encode(NaniParameterField param, NaniTypeField field)
	{
		if (!param.HasValue)
		{
			return field.IsRequired ? GetRequiredEmptyPlaceholder(field) : string.Empty;
		}

		string encodedValue = EncodeValue(param);
		if (string.IsNullOrEmpty(encodedValue))
		{
			return field.IsRequired ? GetRequiredEmptyPlaceholder(field) : string.Empty;
		}

		if (encodedValue.Contains(' '))
		{
			encodedValue = QuoteValue(encodedValue);
		}

		if (field.ParameterAlias == "")
		{
			return encodedValue;
		}

		return $"{field.ParameterAlias}:{encodedValue}";
	}

	private static string GetRequiredEmptyPlaceholder(NaniTypeField field)
	{
		const string placeholder = "\"\"";
		return field.ParameterAlias == "" ? placeholder : $"{field.ParameterAlias}:{placeholder}";
	}

	private static string QuoteValue(string value)
	{
		StringBuilder sb = new(value.Length + 2);
		sb.Append('"');
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '"' && (i == 0 || value[i - 1] != '\\'))
			{
				sb.Append('\\');
			}
			sb.Append(value[i]);
		}
		sb.Append('"');
		return sb.ToString();
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
			return string.Join(",", parts);
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