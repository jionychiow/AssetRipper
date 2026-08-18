using System.Text;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Modules.Naninovel;

public sealed class ScriptLineDecoder
{
	private readonly Dictionary<long, NaniManagedReference> referenceMap;
	private readonly CommandDecoder commandDecoder;

	public ScriptLineDecoder(Dictionary<long, NaniManagedReference> referenceMap, CommandDecoder commandDecoder)
	{
		this.referenceMap = referenceMap;
		this.commandDecoder = commandDecoder;
	}

	public string Decode(NaniManagedReference lineReference)
	{
		return lineReference.Class switch
		{
			"LabelScriptLine" => DecodeLabelLine(lineReference),
			"CommentScriptLine" => DecodeCommentLine(lineReference),
			"EmptyScriptLine" => DecodeEmptyLine(),
			"CommandScriptLine" => DecodeCommandLine(lineReference),
			"GenericTextScriptLine" => DecodeGenericTextLine(lineReference),
			_ => DecodeUnknownLine(lineReference),
		};
	}

	private static string DecodeLabelLine(NaniManagedReference reference)
	{
		string labelText = GetFieldString(reference, "labelText");
		return $"#{TextEscaper.EscapeLineStart(labelText)}";
	}

	private static string DecodeCommentLine(NaniManagedReference reference)
	{
		string commentText = GetFieldString(reference, "commentText");
		return $";{commentText}";
	}

	private static string DecodeEmptyLine()
	{
		return string.Empty;
	}

	private string DecodeCommandLine(NaniManagedReference reference)
	{
		if (reference.Fields is null || !reference.Fields.TryGetValue("command", out object? commandRidObj) || commandRidObj is null)
		{
			int lineIndex = GetFieldInt(reference, "lineIndex");
			return $";[NaninovelExport] Dangling command reference at line {lineIndex}";
		}

		long commandRid = (long)commandRidObj;
		if (!referenceMap.TryGetValue(commandRid, out NaniManagedReference? commandRef))
		{
			int lineIndex = GetFieldInt(reference, "lineIndex");
			return $";[NaninovelExport] Dangling reference rid={commandRid} at line {lineIndex}";
		}

		string commandText = commandDecoder.Decode(commandRef);
		return $"@{commandText}";
	}

	private string DecodeGenericTextLine(NaniManagedReference reference)
	{
		if (reference.Fields is null || !reference.Fields.TryGetValue("inlinedCommands", out object? inlinedObj) || inlinedObj is null)
		{
			return string.Empty;
		}

		if (inlinedObj is not List<long> inlinedRids)
		{
			return string.Empty;
		}

		StringBuilder textBuilder = new();
		foreach (long rid in inlinedRids)
		{
			if (!referenceMap.TryGetValue(rid, out NaniManagedReference? cmdRef))
			{
				continue;
			}

			if (cmdRef.Class == "PrintText")
			{
				string text = GetCommandParameterString(cmdRef, "Text");
				string authorId = GetCommandParameterString(cmdRef, "AuthorId");

				text = TextEscaper.SanitizeControlChars(text);
				text = TextEscaper.EscapeLineStart(text);

				if (!string.IsNullOrEmpty(authorId))
				{
					textBuilder.Append($"{authorId}: {text}");
				}
				else
				{
					textBuilder.Append(text);
				}
			}
			else
			{
				string commandText = commandDecoder.Decode(cmdRef);
				textBuilder.Append($"[{commandText}]");
			}
		}

		return textBuilder.ToString();
	}

	private static string DecodeUnknownLine(NaniManagedReference reference)
	{
		int lineIndex = GetFieldInt(reference, "lineIndex");
		Logger.Warning(LogCategory.Export, $"[NaninovelExport] Unknown line type '{reference.Class}' at index {lineIndex}.");
		return $";[NaninovelExport] Unknown line type {reference.Class} at index {lineIndex}";
	}

	private static string GetFieldString(NaniManagedReference reference, string fieldName)
	{
		if (reference.Fields is not null && reference.Fields.TryGetValue(fieldName, out object? value) && value is string s)
		{
			return s;
		}
		return string.Empty;
	}

	private static int GetFieldInt(NaniManagedReference reference, string fieldName)
	{
		if (reference.Fields is not null && reference.Fields.TryGetValue(fieldName, out object? value) && value is int i)
		{
			return i;
		}
		return -1;
	}

	private static string GetCommandParameterString(NaniManagedReference commandRef, string paramName)
	{
		if (commandRef.Fields is not null && commandRef.Fields.TryGetValue(paramName, out object? value) && value is NaniParameterField param)
		{
			if (param.HasValue && param.Value is string s)
			{
				return s;
			}
		}
		return string.Empty;
	}
}