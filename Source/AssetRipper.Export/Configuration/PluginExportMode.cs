namespace AssetRipper.Export.Configuration;

public enum PluginExportMode
{
	/// <summary>
	/// Extract third-party plugin DLLs from the game assembly and save them to the <c>Assets/Plugins/</c> directory.
	/// This is the default behavior to ensure existing users have no perceptible change.
	/// </summary>
	FromGame,
	/// <summary>
	/// Skip exporting third-party plugin DLLs. The user should manually import the original plugin packages
	/// (e.g. <c>.unitypackage</c> or <c>.zip</c> files) in the Unity Editor.
	/// </summary>
	Skip,
}