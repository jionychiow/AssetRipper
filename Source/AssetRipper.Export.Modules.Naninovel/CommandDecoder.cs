using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Modules.Naninovel;

public sealed class CommandDecoder
{
	private readonly CommandAliasMap commandAliasMap;
	private readonly NaniTypeLayoutTable layoutTable;
	private readonly ParameterEncoder parameterEncoder;

	private static readonly HashSet<string> ForceWaitCommands = new()
	{
		"WaitForInput",
		"Stop",
		"Goto",
		"Gosub",
		"Return",
	};

	public CommandDecoder(CommandAliasMap commandAliasMap, NaniTypeLayoutTable layoutTable, ParameterEncoder parameterEncoder)
	{
		this.commandAliasMap = commandAliasMap;
		this.layoutTable = layoutTable;
		this.parameterEncoder = parameterEncoder;
	}

	public string Decode(NaniManagedReference commandReference)
	{
		string? alias = commandAliasMap.GetAlias(commandReference.Class);
		if (alias is null)
		{
			Logger.Warning(LogCategory.Export, $"[NaninovelExport] Unknown command type '{commandReference.Class}'.");
			alias = commandReference.Class;
		}

		if (commandReference.Fields is null)
		{
			return alias;
		}

		NaniTypeLayout? layout = layoutTable.GetLayout(commandReference.Class);
		if (layout is null)
		{
			return alias;
		}

		List<string> paramParts = new();
		string? conditionalExpr = null;
		bool? waitValue = null;

		foreach (NaniTypeField field in layout.Fields)
		{
			if (!commandReference.Fields.TryGetValue(field.Name, out object? fieldValue) || fieldValue is null)
			{
				continue;
			}

			if (field.Name == "Wait" && fieldValue is NaniParameterField waitParam)
			{
				if (waitParam.HasValue)
				{
					waitValue = (bool)waitParam.Value;
				}
				continue;
			}

			if (field.Name == "ConditionalExpression" && fieldValue is NaniParameterField condParam)
			{
				if (condParam.HasValue && condParam.Value is string condStr && !string.IsNullOrEmpty(condStr))
				{
					conditionalExpr = condStr;
				}
				continue;
			}

			if (field.Name == "playbackSpot")
			{
				continue;
			}

			if (fieldValue is NaniParameterField param)
			{
				string encoded = parameterEncoder.Encode(param, field);
				if (!string.IsNullOrEmpty(encoded))
				{
					paramParts.Add(encoded);
				}
			}
		}

		if (conditionalExpr is not null)
		{
			paramParts.Add($"if:{conditionalExpr}");
		}

		if (waitValue == false && !ForceWaitCommands.Contains(commandReference.Class))
		{
			paramParts.Add("wait:false");
		}

		return paramParts.Count > 0 ? $"{alias} {string.Join(" ", paramParts)}" : alias;
	}
}