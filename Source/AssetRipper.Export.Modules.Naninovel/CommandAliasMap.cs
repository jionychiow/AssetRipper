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
		aliasMap["AppendText"] = "append";
		aliasMap["AppendLineBreak"] = "br";
		aliasMap["AddChoice"] = "choice";
		aliasMap["ClearChoiceHandler"] = "clearChoice";
		aliasMap["SetCustomVariable"] = "set";
		aliasMap["InputCustomVariable"] = "input";
		aliasMap["BeginIf"] = "if";
		aliasMap["EndIf"] = "endif";
		aliasMap["Else"] = "else";
		aliasMap["ElseIf"] = "elseif";
		aliasMap["HideUI"] = "hideUI";
		aliasMap["ShowUI"] = "showUI";
		aliasMap["ShowToastUI"] = "toast";
		aliasMap["HideActors"] = "hide";
		aliasMap["ShowActors"] = "show";
		aliasMap["HideAllCharacters"] = "hideChars";
		aliasMap["HideAllActors"] = "hideAll";
		aliasMap["ArrangeCharacters"] = "arrange";
		aliasMap["SlideActor"] = "slide";
		aliasMap["AnimateActor"] = "animate";
		aliasMap["ModifyCharacter"] = "char";
		aliasMap["ModifyBackground"] = "back";
		aliasMap["ModifyTextPrinter"] = "printer";
		aliasMap["ModifyCamera"] = "camera";
		aliasMap["CameraLook"] = "look";
		aliasMap["HidePrinter"] = "hidePrinter";
		aliasMap["ShowPrinter"] = "showPrinter";
		aliasMap["PlaySfx"] = "sfx";
		aliasMap["PlaySfxFast"] = "sfxFast";
		aliasMap["StopSfx"] = "stopSfx";
		aliasMap["PlayBgm"] = "bgm";
		aliasMap["StopBgm"] = "stopBgm";
		aliasMap["PlayVoice"] = "voice";
		aliasMap["StopVoice"] = "stopVoice";
		aliasMap["PlayMovie"] = "movie";
		aliasMap["Spawn"] = "spawn";
		aliasMap["DestroySpawned"] = "despawn";
		aliasMap["Wait"] = "wait";
		aliasMap["WaitForInput"] = "i";
		aliasMap["SkipInput"] = "skipInput";
		aliasMap["Skip"] = "skip";
		aliasMap["Back"] = "back";
		aliasMap["Set"] = "set";
		aliasMap["SetTextStyle"] = "style";
		aliasMap["StartSceneTransition"] = "startTrans";
		aliasMap["FinishSceneTransition"] = "finishTrans";
		aliasMap["AutoSave"] = "save";
		aliasMap["ExitToTitle"] = "title";
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