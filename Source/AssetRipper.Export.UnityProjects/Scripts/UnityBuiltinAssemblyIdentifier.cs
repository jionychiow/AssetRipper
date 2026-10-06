namespace AssetRipper.Export.UnityProjects.Scripts;

public static class UnityBuiltinAssemblyIdentifier
{
	private const string UnityBuiltinPrefix = "Unity.";
	private const string UnityEngineEnginePrefix = "UnityEngine.";

	public static bool IsUnityBuiltinAssembly(string? assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return false;
		}

		return assemblyName.StartsWith(UnityBuiltinPrefix, StringComparison.OrdinalIgnoreCase)
			|| assemblyName.StartsWith(UnityEngineEnginePrefix, StringComparison.OrdinalIgnoreCase);
	}
}