using AssetRipper.IO.Files;

namespace AssetRipper.Export.UnityProjects.Naninovel.Package;

/// <summary>
/// Detects whether an export project is a Naninovel project.
/// </summary>
public static class NaninovelProjectDetector
{
	/// <summary>
	/// Returns true if the export project contains Naninovel identifiers.
	/// </summary>
	public static bool IsNaninovelProject(string assetsPath, FileSystem fileSystem)
	{
		string pluginsPath = fileSystem.Path.Join(assetsPath, "Plugins", "GameLibs");
		string runtimeDllPath = fileSystem.Path.Join(pluginsPath, "Elringus.Naninovel.Runtime.dll");
		if (fileSystem.File.Exists(runtimeDllPath))
		{
			return true;
		}

		string asmdefPath = fileSystem.Path.Join(assetsPath, "Naninovel", "Runtime", "Elringus.Naninovel.Runtime.asmdef");
		if (fileSystem.File.Exists(asmdefPath))
		{
			return true;
		}

		string naninovelResourcesPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel");
		if (fileSystem.Directory.Exists(naninovelResourcesPath))
		{
			return true;
		}

		return false;
	}
}