namespace AssetRipper.Export.Modules.Naninovel;

public sealed class CommandAliasMap
{
	private readonly Dictionary<string, string> aliasMap = new();

	public CommandAliasMap()
	{
		aliasMap["Goto"] = "goto";
		aliasMap["Gosub"] = "gosub";
		aliasMap["Return"] = "return";
		aliasMap["Stop"] = "stop";
		aliasMap["PrintText"] = "print";
		aliasMap["AddChoice"] = "choice";
		aliasMap["SetCustomVariable"] = "set";
		aliasMap["BeginIf"] = "if";
		aliasMap["EndIf"] = "endif";
		aliasMap["Else"] = "else";
		aliasMap["ElseIf"] = "elseif";
		aliasMap["HideUI"] = "hideUI";
		aliasMap["ShowUI"] = "showUI";
		aliasMap["HideActors"] = "hideChars";
		aliasMap["ShowActors"] = "showChars";
		aliasMap["PlaySfx"] = "playSfx";
		aliasMap["StopSfx"] = "stopSfx";
		aliasMap["PlayBgm"] = "playBgm";
		aliasMap["StopBgm"] = "stopBgm";
		aliasMap["PlayVoice"] = "voice";
		aliasMap["StopVoice"] = "stopVoice";
		aliasMap["Spawn"] = "spawn";
		aliasMap["DestroySpawned"] = "destroy";
		aliasMap["Wait"] = "wait";
		aliasMap["WaitForInput"] = "i";
		aliasMap["SkipInput"] = "skipInput";
		aliasMap["Back"] = "back";
		aliasMap["Set"] = "set";
	}

	public string? GetAlias(string className)
	{
		return aliasMap.TryGetValue(className, out string? alias) ? alias : null;
	}

	public void AddOrUpdate(string className, string alias)
	{
		aliasMap[className] = alias;
	}
}