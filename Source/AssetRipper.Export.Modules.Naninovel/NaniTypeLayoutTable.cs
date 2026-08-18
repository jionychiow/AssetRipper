namespace AssetRipper.Export.Modules.Naninovel;

public sealed class NaniTypeLayoutTable
{
	private readonly Dictionary<string, NaniTypeLayout> layouts = new();

	public NaniTypeLayoutTable()
	{
		AddScriptLineLayouts();
		AddCommandLayouts();
	}

	public NaniTypeLayout? GetLayout(string classFullName)
	{
		return layouts.TryGetValue(classFullName, out NaniTypeLayout? layout) ? layout : null;
	}

	public void AddOrUpdateLayout(string classFullName, NaniTypeLayout layout)
	{
		layouts[classFullName] = layout;
	}

	private void AddScriptLineLayouts()
	{
		layouts["LabelScriptLine"] = new NaniTypeLayout("LabelScriptLine", new List<NaniTypeField>
		{
			new("lineIndex", NaniFieldType.Int32),
			new("lineHash", NaniFieldType.String),
			new("labelText", NaniFieldType.String),
		});

		layouts["CommandScriptLine"] = new NaniTypeLayout("CommandScriptLine", new List<NaniTypeField>
		{
			new("lineIndex", NaniFieldType.Int32),
			new("lineHash", NaniFieldType.String),
			new("command", NaniFieldType.ManagedReference) { IsManagedReference = true },
		});

		layouts["CommentScriptLine"] = new NaniTypeLayout("CommentScriptLine", new List<NaniTypeField>
		{
			new("lineIndex", NaniFieldType.Int32),
			new("lineHash", NaniFieldType.String),
			new("commentText", NaniFieldType.String),
		});

		layouts["GenericTextScriptLine"] = new NaniTypeLayout("GenericTextScriptLine", new List<NaniTypeField>
		{
			new("lineIndex", NaniFieldType.Int32),
			new("lineHash", NaniFieldType.String),
			new("inlinedCommands", NaniFieldType.ManagedReference) { IsManagedReference = true, IsArray = true },
		});

		layouts["EmptyScriptLine"] = new NaniTypeLayout("EmptyScriptLine", new List<NaniTypeField>
		{
			new("lineIndex", NaniFieldType.Int32),
			new("lineHash", NaniFieldType.String),
		});
	}

	private static List<NaniTypeField> CommandBaseFields() => new()
	{
		new("Wait", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		new("ConditionalExpression", NaniFieldType.CommandParameter, "if", "StringParameter"),
		new("playbackSpot", NaniFieldType.PlaybackSpot),
	};

	private static List<NaniTypeField> ModifyActorFields() => new()
	{
		new("Id", NaniFieldType.CommandParameter, "", "StringParameter"),
		new("Appearance", NaniFieldType.CommandParameter, "", "StringParameter"),
		new("Pose", NaniFieldType.CommandParameter, "", "StringParameter"),
		new("Transition", NaniFieldType.CommandParameter, "", "StringParameter"),
		new("TransitionParams", NaniFieldType.CommandParameter, "params", "DecimalListParameter"),
		new("DissolveTexturePath", NaniFieldType.CommandParameter, "dissolve", "StringParameter"),
		new("Visible", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		new("Position", NaniFieldType.CommandParameter, "", "DecimalListParameter"),
		new("Rotation", NaniFieldType.CommandParameter, "", "DecimalListParameter"),
		new("Scale", NaniFieldType.CommandParameter, "", "DecimalListParameter"),
		new("TintColor", NaniFieldType.CommandParameter, "tint", "StringParameter"),
		new("EasingTypeName", NaniFieldType.CommandParameter, "easing", "StringParameter"),
		new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
	};

	private static List<NaniTypeField> ModifyOrthoActorFields()
	{
		List<NaniTypeField> fields = ModifyActorFields();
		fields.Add(new("ScenePosition", NaniFieldType.CommandParameter, "pos", "DecimalListParameter"));
		return fields;
	}

	private void AddCommandLayouts()
	{
		AddCommand("Goto", new()
		{
			new("Path", NaniFieldType.CommandParameter, "", "NamedStringParameter"),
			new("ResetState", NaniFieldType.CommandParameter, "reset", "StringListParameter"),
		});

		AddCommand("Gosub", new()
		{
			new("Path", NaniFieldType.CommandParameter, "", "NamedStringParameter"),
			new("ResetState", NaniFieldType.CommandParameter, "reset", "StringListParameter"),
		});

		AddCommand("Return", new()
		{
			new("ResetState", NaniFieldType.CommandParameter, "reset", "StringListParameter"),
		});
		AddCommand("Stop", new());

		AddCommand("PrintText", new()
		{
			new("Text", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("PrinterId", NaniFieldType.CommandParameter, "printer", "StringParameter"),
			new("AuthorId", NaniFieldType.CommandParameter, "author", "StringParameter"),
			new("RevealSpeed", NaniFieldType.CommandParameter, "speed", "DecimalParameter"),
			new("ResetPrinter", NaniFieldType.CommandParameter, "reset", "BooleanParameter"),
			new("DefaultPrinter", NaniFieldType.CommandParameter, "default", "BooleanParameter"),
			new("WaitForInput", NaniFieldType.CommandParameter, "waitInput", "BooleanParameter"),
			new("LineBreaks", NaniFieldType.CommandParameter, "br", "IntegerParameter"),
			new("ChangeVisibilityDuration", NaniFieldType.CommandParameter, "fadeTime", "DecimalParameter"),
			new("AutoVoiceId", NaniFieldType.CommandParameter, "voiceId", "StringParameter"),
		});

		AddCommand("AppendText", new()
		{
			new("Text", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("PrinterId", NaniFieldType.CommandParameter, "printer", "StringParameter"),
			new("AuthorId", NaniFieldType.CommandParameter, "author", "StringParameter"),
		});

		AddCommand("AppendLineBreak", new()
		{
			new("Count", NaniFieldType.CommandParameter, "", "IntegerParameter"),
			new("PrinterId", NaniFieldType.CommandParameter, "printer", "StringParameter"),
			new("AuthorId", NaniFieldType.CommandParameter, "author", "StringParameter"),
		});

		AddCommand("ResetText", new()
		{
			new("PrinterId", NaniFieldType.CommandParameter, "", "StringParameter"),
		});

		AddCommand("HidePrinter", new()
		{
			new("PrinterId", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("ShowPrinter", new()
		{
			new("PrinterId", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("AddChoice", new()
		{
			new("ChoiceSummary", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("ButtonPath", NaniFieldType.CommandParameter, "button", "StringParameter"),
			new("ButtonPosition", NaniFieldType.CommandParameter, "pos", "DecimalListParameter"),
			new("HandlerId", NaniFieldType.CommandParameter, "handler", "StringParameter"),
			new("GotoPath", NaniFieldType.CommandParameter, "goto", "NamedStringParameter"),
			new("GosubPath", NaniFieldType.CommandParameter, "gosub", "NamedStringParameter"),
			new("SetExpression", NaniFieldType.CommandParameter, "set", "StringParameter"),
			new("OnSelected", NaniFieldType.CommandParameter, "do", "StringListParameter"),
			new("AutoPlay", NaniFieldType.CommandParameter, "play", "BooleanParameter"),
			new("ShowHandler", NaniFieldType.CommandParameter, "show", "BooleanParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("SetCustomVariable", new()
		{
			new("Expression", NaniFieldType.CommandParameter, "", "StringParameter"),
		});

		AddCommand("BeginIf", new()
		{
			new("Expression", NaniFieldType.CommandParameter, "", "StringParameter"),
		});
		AddCommand("EndIf", new());
		AddCommand("Else", new());
		AddCommand("ElseIf", new()
		{
			new("Expression", NaniFieldType.CommandParameter, "", "StringParameter"),
		});

		AddCommand("HideUI", new()
		{
			new("UINames", NaniFieldType.CommandParameter, "", "StringListParameter"),
			new("AllowToggle", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("ShowUI", new()
		{
			new("UINames", NaniFieldType.CommandParameter, "", "StringListParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("ShowToastUI", new()
		{
			new("Text", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Appearance", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("HideActors", new()
		{
			new("ActorIds", NaniFieldType.CommandParameter, "", "StringListParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("HideAllActors", new()
		{
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("HideAllCharacters", new()
		{
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("ShowActors", new()
		{
			new("ActorIds", NaniFieldType.CommandParameter, "", "StringListParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("PlaySfx", new()
		{
			new("SfxPath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Volume", NaniFieldType.CommandParameter, "", "DecimalParameter"),
			new("Loop", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("FadeInDuration", NaniFieldType.CommandParameter, "fade", "DecimalParameter"),
			new("GroupPath", NaniFieldType.CommandParameter, "group", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("PlaySfxFast", new()
		{
			new("SfxPath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Volume", NaniFieldType.CommandParameter, "", "DecimalParameter"),
			new("Restart", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("Additive", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("GroupPath", NaniFieldType.CommandParameter, "group", "StringParameter"),
		});
		AddCommand("StopSfx", new()
		{
			new("SfxPath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("FadeOutDuration", NaniFieldType.CommandParameter, "fade", "DecimalParameter"),
		});

		AddCommand("PlayBgm", new()
		{
			new("BgmPath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("IntroBgmPath", NaniFieldType.CommandParameter, "intro", "StringParameter"),
			new("Volume", NaniFieldType.CommandParameter, "", "DecimalParameter"),
			new("Loop", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("FadeInDuration", NaniFieldType.CommandParameter, "fade", "DecimalParameter"),
			new("GroupPath", NaniFieldType.CommandParameter, "group", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});
		AddCommand("StopBgm", new()
		{
			new("BgmPath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("FadeOutDuration", NaniFieldType.CommandParameter, "fade", "DecimalParameter"),
		});

		AddCommand("PlayVoice", new()
		{
			new("VoicePath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Volume", NaniFieldType.CommandParameter, "", "DecimalParameter"),
			new("GroupPath", NaniFieldType.CommandParameter, "group", "StringParameter"),
			new("AuthorId", NaniFieldType.CommandParameter, "", "StringParameter"),
		});
		AddCommand("StopVoice", new());

		AddCommand("PlayMovie", new()
		{
			new("MovieName", NaniFieldType.CommandParameter, "", "StringParameter"),
		});

		AddCommand("Spawn", new()
		{
			new("Path", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Params", NaniFieldType.CommandParameter, "", "StringListParameter"),
		});
		AddCommand("DestroySpawned", new()
		{
			new("Path", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Params", NaniFieldType.CommandParameter, "", "StringListParameter"),
		});

		AddCommand("Wait", new()
		{
			new("WaitMode", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("OnFinished", NaniFieldType.CommandParameter, "do", "StringListParameter"),
		});
		AddCommand("WaitForInput", new());
		AddCommand("SkipInput", new());
		AddCommand("Skip", new()
		{
			new("Enable", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		});

		AddCommand("AutoSave", new());
		AddCommand("PurgeRollback", new());
		AddCommand("ExitToTitle", new());
		AddCommand("ClearBacklog", new());
		AddCommand("ClearChoiceHandler", new()
		{
			new("HandlerId", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Hide", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		});
		AddCommand("Lock", new()
		{
			new("Id", NaniFieldType.CommandParameter, "", "StringParameter"),
		});
		AddCommand("Unlock", new()
		{
			new("Id", NaniFieldType.CommandParameter, "", "StringParameter"),
		});

		AddCommand("ResetState", new()
		{
			new("Exclude", NaniFieldType.CommandParameter, "", "StringListParameter"),
			new("Only", NaniFieldType.CommandParameter, "", "StringListParameter"),
		});

		AddCommand("LoadScene", new()
		{
			new("SceneName", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Additive", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		});

		AddCommand("InputCustomVariable", new()
		{
			new("VariableName", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Summary", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("PredefinedValue", NaniFieldType.CommandParameter, "value", "StringParameter"),
			new("PlayOnSubmit", NaniFieldType.CommandParameter, "play", "BooleanParameter"),
		});

		AddCommand("LipSync", new()
		{
			new("CharIdAndAllow", NaniFieldType.CommandParameter, "", "NamedBooleanParameter"),
		});

		AddCommand("CameraLook", new()
		{
			new("Enable", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("LookZone", NaniFieldType.CommandParameter, "zone", "DecimalListParameter"),
			new("LookSpeed", NaniFieldType.CommandParameter, "speed", "DecimalListParameter"),
			new("Gravity", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		});

		AddCommand("ModifyCamera", new()
		{
			new("Offset", NaniFieldType.CommandParameter, "", "DecimalListParameter"),
			new("Roll", NaniFieldType.CommandParameter, "", "DecimalParameter"),
			new("Rotation", NaniFieldType.CommandParameter, "", "DecimalListParameter"),
			new("Zoom", NaniFieldType.CommandParameter, "", "DecimalParameter"),
			new("Orthographic", NaniFieldType.CommandParameter, "ortho", "BooleanParameter"),
			new("ToggleTypeNames", NaniFieldType.CommandParameter, "toggle", "StringListParameter"),
			new("SetTypeNames", NaniFieldType.CommandParameter, "set", "NamedBooleanListParameter"),
			new("EasingTypeName", NaniFieldType.CommandParameter, "easing", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("AnimateActor", new()
		{
			new("ActorIds", NaniFieldType.CommandParameter, "", "StringListParameter"),
			new("Loop", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("Appearance", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Transition", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Visibility", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("ScenePositionX", NaniFieldType.CommandParameter, "posX", "StringParameter"),
			new("ScenePositionY", NaniFieldType.CommandParameter, "posY", "StringParameter"),
			new("PositionZ", NaniFieldType.CommandParameter, "posZ", "StringParameter"),
			new("Rotation", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Scale", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("TintColor", NaniFieldType.CommandParameter, "tint", "StringParameter"),
			new("EasingTypeName", NaniFieldType.CommandParameter, "easing", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "StringParameter"),
		});

		AddCommand("ArrangeCharacters", new()
		{
			new("CharacterPositions", NaniFieldType.CommandParameter, "", "NamedDecimalListParameter"),
			new("LookAtOrigin", NaniFieldType.CommandParameter, "look", "BooleanParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("SlideActor", new()
		{
			new("IdAndAppearance", NaniFieldType.CommandParameter, "", "NamedStringParameter"),
			new("FromPosition", NaniFieldType.CommandParameter, "from", "DecimalListParameter"),
			new("ToPosition", NaniFieldType.CommandParameter, "to", "DecimalListParameter"),
			new("Visible", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("EasingTypeName", NaniFieldType.CommandParameter, "easing", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("ProcessInput", new()
		{
			new("InputEnabled", NaniFieldType.CommandParameter, "", "BooleanParameter"),
			new("SetEnabled", NaniFieldType.CommandParameter, "set", "NamedBooleanListParameter"),
		});

		AddCommand("StartSceneTransition", new());
		AddCommand("FinishSceneTransition", new()
		{
			new("Transition", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("TransitionParams", NaniFieldType.CommandParameter, "params", "DecimalListParameter"),
			new("DissolveTexturePath", NaniFieldType.CommandParameter, "dissolve", "StringParameter"),
			new("EasingTypeName", NaniFieldType.CommandParameter, "easing", "StringParameter"),
			new("Duration", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommandWithBase("ModifyBackground", ModifyOrthoActorFields(), new()
		{
			new("AppearanceAndTransition", NaniFieldType.CommandParameter, "", "NamedStringParameter"),
		});

		AddCommandWithBase("ModifyCharacter", ModifyOrthoActorFields(), new()
		{
			new("IdAndAppearance", NaniFieldType.CommandParameter, "", "NamedStringParameter"),
			new("LookDirection", NaniFieldType.CommandParameter, "look", "StringParameter"),
			new("AvatarTexturePath", NaniFieldType.CommandParameter, "avatar", "StringParameter"),
		});

		AddCommandWithBase("ModifyTextPrinter", ModifyOrthoActorFields(), new()
		{
			new("IdAndAppearance", NaniFieldType.CommandParameter, "", "NamedStringParameter"),
			new("MakeDefault", NaniFieldType.CommandParameter, "default", "BooleanParameter"),
			new("HideOther", NaniFieldType.CommandParameter, "", "BooleanParameter"),
		});

		AddCommand("Back", new()
		{
			new("BackPath", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Scale", NaniFieldType.CommandParameter, "scale", "DecimalParameter"),
			new("Time", NaniFieldType.CommandParameter, "time", "DecimalParameter"),
		});

		AddCommand("Set", new()
		{
			new("Expression", NaniFieldType.CommandParameter, "", "StringParameter"),
		});

		AddCommand("Comment", new());
		AddCommand("Label", new());
		AddCommand("Empty", new());

		AddCommand("AddItem", new()
		{
			new("ItemId", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("SlotId", NaniFieldType.CommandParameter, "SlotId", "IntegerParameter"),
			new("Amount", NaniFieldType.CommandParameter, "Amount", "IntegerParameter"),
		});
		AddCommand("RemoveItem", new()
		{
			new("ItemId", NaniFieldType.CommandParameter, "", "StringParameter"),
			new("Amount", NaniFieldType.CommandParameter, "Amount", "IntegerParameter"),
		});
		AddCommand("RemoveItemAt", new()
		{
			new("SlotId", NaniFieldType.CommandParameter, "", "IntegerParameter"),
			new("Amount", NaniFieldType.CommandParameter, "Amount", "IntegerParameter"),
		});
		AddCommand("UseItem", new()
		{
			new("ItemId", NaniFieldType.CommandParameter, "", "StringParameter"),
		});
		AddCommand("RemoveAllItems", new());
	}

	private void AddCommand(string className, List<NaniTypeField> derivedFields)
	{
		List<NaniTypeField> fields = CommandBaseFields();
		fields.AddRange(derivedFields);
		layouts[className] = new NaniTypeLayout(className, fields);
	}

	private void AddCommandWithBase(string className, List<NaniTypeField> baseFields, List<NaniTypeField> derivedFields)
	{
		List<NaniTypeField> fields = CommandBaseFields();
		fields.AddRange(baseFields);
		fields.AddRange(derivedFields);
		layouts[className] = new NaniTypeLayout(className, fields);
	}
}
