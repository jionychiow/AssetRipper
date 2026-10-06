using AssetRipper.Primitives;
using AssetRipper.SerializationLogic;
using AssetRipper.Import.Structure.Assembly.Managers;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public static class SourceGeneratedLayoutResolver
{
	private static readonly string[] engineAssemblyPrefixes = ["UnityEngine.", "Unity."];
	private static readonly string[] engineAssemblyExactNames = ["Unity.TextMeshPro", "TMPro"];
	private static readonly string[] engineNamespacePrefixes = ["UnityEngine", "TMPro"];

	public static bool IsEngineType(string? assemblyName, string? @namespace, string? className)
	{
		if (string.IsNullOrEmpty(assemblyName) || string.IsNullOrEmpty(@namespace))
		{
			return false;
		}

		foreach (string prefix in engineAssemblyPrefixes)
		{
			if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		foreach (string exact in engineAssemblyExactNames)
		{
			if (string.Equals(assemblyName, exact, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		foreach (string prefix in engineNamespacePrefixes)
		{
			if (@namespace.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	public static bool TryGetLayout(
		string assemblyName,
		string @namespace,
		string className,
		UnityVersion version,
		IAssemblyManager assemblyManager,
		out SerializableType? layout)
	{
		layout = null;
		try
		{
			ScriptIdentifier scriptID = assemblyManager.GetScriptID(assemblyName, @namespace, className);
			if (!assemblyManager.IsValid(scriptID))
			{
				return false;
			}

			if (assemblyManager.TryGetSerializableType(scriptID, version, out layout, out _))
			{
				return layout is not null;
			}
		}
		catch
		{
		}

		return false;
	}
}