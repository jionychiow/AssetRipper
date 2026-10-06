using System.IO;
using System.Text;
using AsmResolver.DotNet;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.Scripts;

public sealed class AssemblyDependencyPostExporter : IPostExporter
{
	private const string UnityManagedPath = @"C:\Program Files\Unity 2019.4.35f1\Editor\Data\Managed";
	private const string PluginsFolderName = "Plugins";
	private const string GameLibsFolderName = "GameLibs";

	private static readonly string[] UnityPackagePrefixes = ["Unity."];

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		string pluginsPath = fileSystem.Path.Join(settings.AssetsPath, PluginsFolderName, GameLibsFolderName);
		if (!fileSystem.Directory.Exists(pluginsPath))
		{
			return;
		}

		FixScriptingRuntimeVersion(pluginsPath);

		if (!Directory.Exists(UnityManagedPath))
		{
			Logger.Warning(LogCategory.Export, $"AssemblyDependencyPostExporter: Unity managed path not found: {UnityManagedPath}");
			return;
		}

		HashSet<string> existingDlls = new(StringComparer.OrdinalIgnoreCase);
		foreach (string f in Directory.EnumerateFiles(pluginsPath, "*.dll"))
		{
			existingDlls.Add(Path.GetFileName(f));
		}

		HashSet<string> missingAssemblies = new(StringComparer.OrdinalIgnoreCase);
		foreach (string dllPath in Directory.EnumerateFiles(pluginsPath, "*.dll"))
		{
			try
			{
				ModuleDefinition module = ModuleDefinition.FromFile(dllPath);
				foreach (AssemblyReference reference in module.AssemblyReferences)
				{
					string refName = reference.Name ?? "";
					if (!IsUnityPackageAssembly(refName))
					{
						continue;
					}
					string dllName = refName + ".dll";
					if (!existingDlls.Contains(dllName) && !missingAssemblies.Contains(dllName))
					{
						missingAssemblies.Add(dllName);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"AssemblyDependencyPostExporter: failed to parse {dllPath}: {ex.Message}");
			}
		}

		if (missingAssemblies.Count == 0)
		{
			Logger.Info(LogCategory.Export, "AssemblyDependencyPostExporter: no missing Unity package assemblies found");
			return;
		}

		int copied = 0;
		foreach (string missingDll in missingAssemblies)
		{
			string sourcePath = Path.Join(UnityManagedPath, missingDll);
			if (File.Exists(sourcePath))
			{
				string destPath = Path.Join(pluginsPath, missingDll);
				File.Copy(sourcePath, destPath, overwrite: true);
				GeneratePluginMeta(destPath);
				copied++;
				Logger.Info(LogCategory.Export, $"AssemblyDependencyPostExporter: copied {missingDll} from Unity managed path");
			}
			else
			{
				Logger.Warning(LogCategory.Export, $"AssemblyDependencyPostExporter: not found in Unity managed path: {missingDll}");
			}
		}

		Logger.Info(LogCategory.Export, $"AssemblyDependencyPostExporter: copied {copied}/{missingAssemblies.Count} missing assemblies");
	}

	private static bool IsUnityPackageAssembly(string name)
	{
		foreach (string prefix in UnityPackagePrefixes)
		{
			if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void GeneratePluginMeta(string dllPath)
	{
		string metaPath = dllPath + ".meta";
		if (File.Exists(metaPath))
		{
			return;
		}
		string guid = System.Guid.NewGuid().ToString("N");
		StringBuilder sb = new(500);
		sb.Append("fileFormatVersion: 2\n");
		sb.Append($"guid: {guid}\n");
		sb.Append("PluginImporter:\n");
		sb.Append("  externalObjects: {}\n");
		sb.Append("  serializedVersion: 2\n");
		sb.Append("  iconMap: {}\n");
		sb.Append("  executionOrder: {}\n");
		sb.Append("  defineConstraints: []\n");
		sb.Append("  isPreloaded: 0\n");
		sb.Append("  isOverridable: 0\n");
		sb.Append("  isExplicitlyReferenced: 0\n");
		sb.Append("  validateReferences: 1\n");
		sb.Append("  platformData:\n");
		sb.Append("  - first:\n");
		sb.Append("      Any: 1\n");
		sb.Append("    second:\n");
		sb.Append("      enabled: 1\n");
		sb.Append("      settings: {}\n");
		sb.Append("  - first:\n");
		sb.Append("      Editor: Editor\n");
		sb.Append("    second:\n");
		sb.Append("      enabled: 0\n");
		sb.Append("      settings:\n");
		sb.Append("        DefaultValue: 1\n");
		sb.Append("        CPU: AnyCPU\n");
		sb.Append("        OS: AnyOS\n");
		sb.Append("  userData: \n");
		sb.Append("  assetBundleName: \n");
		sb.Append("  assetBundleVariant: \n");
		File.WriteAllText(metaPath, sb.ToString(), new UTF8Encoding(false));
	}

	private static void FixScriptingRuntimeVersion(string pluginsPath)
	{
		int fixedCount = 0;
		foreach (string metaPath in Directory.EnumerateFiles(pluginsPath, "*.dll.meta"))
		{
			try
			{
				string content = File.ReadAllText(metaPath);
				if (content.Contains("scriptingRuntimeVersion: 0"))
				{
					content = content.Replace("scriptingRuntimeVersion: 0", "scriptingRuntimeVersion: 1");
					File.WriteAllText(metaPath, content);
					fixedCount++;
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"AssemblyDependencyPostExporter: failed to fix scriptingRuntimeVersion in {metaPath}: {ex.Message}");
			}
		}
		Logger.Info(LogCategory.Export, $"AssemblyDependencyPostExporter: fixed scriptingRuntimeVersion in {fixedCount} DLL .meta file(s).");
	}
}