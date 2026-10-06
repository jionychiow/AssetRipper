namespace AssetRipper.Export.UnityProjects.Project;

/// <summary>
/// Maps Unity package DLL names to their corresponding Unity Package Manager package names and versions.
/// </summary>
public static class UnityPackageMapping
{
	/// <summary>
	/// Maps DLL assembly names (without .dll extension) to package names.
	/// </summary>
	private static readonly Dictionary<string, string> dllToPackage = new()
	{
		["Unity.TextMeshPro"] = "com.unity.textmeshpro",
		["Unity.Timeline"] = "com.unity.timeline",
		["Unity.Addressables"] = "com.unity.addressables",
		["Unity.ResourceManager"] = "com.unity.addressables",
		["Unity.ScriptableBuildPipeline"] = "com.unity.scriptablebuildpipeline",
		["UnityEngine.UI"] = "com.unity.ugui",
	};

	/// <summary>
	/// Gets the package name for a given DLL assembly name, or null if not a Unity package DLL.
	/// </summary>
	public static string? GetPackageName(string? assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return null;
		}
		return dllToPackage.TryGetValue(assemblyName, out string? packageName) ? packageName : null;
	}

	/// <summary>
	/// Determines whether the given assembly name corresponds to a Unity package DLL.
	/// </summary>
	public static bool IsUnityPackageDll(string? assemblyName)
	{
		return GetPackageName(assemblyName) is not null;
	}

	/// <summary>
	/// Gets the default package dependencies for a given Unity version.
	/// </summary>
	public static IReadOnlyDictionary<string, string> GetPackageDependencies(UnityVersion version)
	{
		Dictionary<string, string> packages = new();

		// TextMeshPro - available since 2018.1
		if (version.GreaterThanOrEquals(2018, 1))
		{
			if (version.LessThan(2019, 2))
			{
				packages.Add("com.unity.textmeshpro", "1.4.1");
			}
			else if (version.LessThan(2020, 1))
			{
				packages.Add("com.unity.textmeshpro", "2.0.1");
			}
			else if (version.LessThan(2021, 1))
			{
				packages.Add("com.unity.textmeshpro", "2.1.6");
			}
			else if (version.LessThan(2022, 1))
			{
				packages.Add("com.unity.textmeshpro", "3.0.6");
			}
			else
			{
				packages.Add("com.unity.textmeshpro", "3.0.6");
			}
		}

		// Timeline - available since 2017.1
		if (version.GreaterThanOrEquals(2017, 1))
		{
			if (version.LessThan(2019, 2))
			{
				packages.Add("com.unity.timeline", "1.0.0");
			}
			else if (version.LessThan(2020, 1))
			{
				packages.Add("com.unity.timeline", "1.2.18");
			}
			else if (version.LessThan(2021, 1))
			{
				packages.Add("com.unity.timeline", "1.4.8");
			}
			else if (version.LessThan(2022, 1))
			{
				packages.Add("com.unity.timeline", "1.5.7");
			}
			else
			{
				packages.Add("com.unity.timeline", "1.7.6");
			}
		}

		// Addressables - available since 2019.1
		if (version.GreaterThanOrEquals(2019, 1))
		{
			if (version.LessThan(2020, 1))
			{
				packages.Add("com.unity.addressables", "1.16.19");
			}
			else if (version.LessThan(2021, 1))
			{
				packages.Add("com.unity.addressables", "1.17.17");
			}
			else if (version.LessThan(2022, 1))
			{
				packages.Add("com.unity.addressables", "1.19.19");
			}
			else
			{
				packages.Add("com.unity.addressables", "1.21.20");
			}
		}

		// UGUI - built into Unity as com.unity.modules.ui; don't add separate package to avoid version conflicts
		// packages.Add("com.unity.ugui", "1.0.0");

		// Scriptable Build Pipeline - available since 2018.1
		if (version.GreaterThanOrEquals(2018, 1))
		{
			packages.Add("com.unity.scriptablebuildpipeline", "1.0.0");
		}

		// UI Elements (UI Toolkit) - available since 2021.1
		if (version.GreaterThanOrEquals(2021, 1))
		{
			packages.Add("com.unity.ui", "1.0.0");
		}

		// UI Builder - available since 2020.1
		if (version.GreaterThanOrEquals(2020, 1))
		{
			packages.Add("com.unity.ui.builder", "1.0.0");
		}

		return packages;
	}
}