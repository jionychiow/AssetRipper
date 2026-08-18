using System.Text;

namespace AssetRipper.Export.Modules.Naninovel;

public static class TextEscaper
{
	public static string Escape(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text ?? string.Empty;
		}

		return text
			.Replace("[", "\\[")
			.Replace("]", "\\]")
			.Replace("{", "\\{")
			.Replace("}", "\\}");
	}

	public static string EscapeLineStart(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text ?? string.Empty;
		}

		if (text[0] is '@' or '#' or ';')
		{
			return "\\" + text;
		}

		return text;
	}

	public static string SanitizeControlChars(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text ?? string.Empty;
		}

		StringBuilder sb = new(text.Length);
		foreach (char c in text)
		{
			if (char.IsControl(c) && c is not '\t' and not '\n' and not '\r')
			{
				sb.Append('\uFFFD');
			}
			else
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}
}