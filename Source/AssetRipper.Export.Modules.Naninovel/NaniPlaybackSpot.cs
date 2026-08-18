namespace AssetRipper.Export.Modules.Naninovel;

public struct NaniPlaybackSpot
{
	public string ScriptName { get; set; }
	public int LineIndex { get; set; }
	public int InlineIndex { get; set; }

	public NaniPlaybackSpot(string scriptName, int lineIndex, int inlineIndex)
	{
		ScriptName = scriptName;
		LineIndex = lineIndex;
		InlineIndex = inlineIndex;
	}
}