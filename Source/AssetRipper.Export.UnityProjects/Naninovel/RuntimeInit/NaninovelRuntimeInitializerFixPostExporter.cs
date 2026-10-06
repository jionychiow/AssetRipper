using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;

namespace AssetRipper.Export.UnityProjects.Naninovel.RuntimeInit;

public sealed class NaninovelRuntimeInitializerFixPostExporter : IPostExporter
{
	private readonly NaninovelRuntimeInitializerFixSettings _settings;

	public NaninovelRuntimeInitializerFixPostExporter() : this(new NaninovelRuntimeInitializerFixSettings()) { }

	public NaninovelRuntimeInitializerFixPostExporter(NaninovelRuntimeInitializerFixSettings settings)
	{
		_settings = settings;
	}

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		try
		{
			string assetsPath = settings.AssetsPath;
			if (!fileSystem.Directory.Exists(assetsPath))
			{
				return;
			}

			string configDir = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration");
			if (!fileSystem.Directory.Exists(configDir))
			{
				return;
			}

			Logger.Info(LogCategory.Export, "NaninovelRuntimeInitializerFixPostExporter: starting...");

		int c1 = FixProjectRootPath(assetsPath, fileSystem);
		int c2 = CleanConfigurationDuplicates(assetsPath, fileSystem);
		int c3 = NaninovelConfigurationReferenceValidator.FixInvalidReferences(assetsPath, fileSystem);
		int c4 = VerifyTypeAssemblies(assetsPath, fileSystem);
		int c5 = ProjectResourcesPathValidator.CleanMissingPaths(assetsPath, fileSystem);
		bool c6 = VerifyNanoGameComponent(assetsPath, fileSystem);
		int c7 = TextPrinterResourceRegistrar.Register(assetsPath, fileSystem);
		int c8 = PrefabScriptReferenceValidator.Validate(assetsPath, fileSystem, "Resources/TextPrinters/Wide.prefab");
		int c9 = TextPrinterProviderConfigFixer.Fix(assetsPath, fileSystem);
		int c10 = ProjectResourcePathNormalizer.Normalize(assetsPath, fileSystem);
		int c11 = AsyncExceptionLogTypeFixer.Fix(assetsPath, fileSystem);
		int c12 = ProviderTypesYamlFormatFixer.Fix(assetsPath, fileSystem);
		int c13 = ProjectResourcePathFilesystemCaseValidator.Validate(assetsPath, fileSystem);
		int c14 = StateConfigurationFixer.Fix(assetsPath, fileSystem);

		Logger.Info(LogCategory.Export, $"NaninovelRuntimeInitializerFixPostExporter: done (ProjectRootPath={c1}, DupConfigs={c2}, Refs={c3}, Asms={c4}, Paths={c5}, NanoGame={c6}, TextPrinters={c7}, PrefabRefs={c8}, ProviderCfg={c9}, PathNorm={c10}, AsyncLog={c11}, YamlFmt={c12}, FsCase={c13}, StateCfg={c14})");
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Export, $"NaninovelRuntimeInitializerFixPostExporter failed: {ex}");
		}
	}

	private int FixProjectRootPath(string assetsPath, FileSystem fileSystem)
	{
		string configPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration", "ResourceProviderConfiguration.asset");
		if (!fileSystem.File.Exists(configPath))
		{
			Logger.Warning(LogCategory.Export, $"ResourceProviderConfiguration.asset not found at {configPath}");
			return 0;
		}

		try
		{
			string content = File.ReadAllText(configPath);
			string[] lines = content.Split('\n');

			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].Trim();
				if (trimmed.StartsWith("ProjectRootPath:"))
				{
					string value = trimmed["ProjectRootPath:".Length..].Trim();
					string indent = lines[i][..lines[i].IndexOf("ProjectRootPath:")];
					lines[i] = $"{indent}ProjectRootPath: {_settings.ProjectRootPathDefaultValue}";
					File.WriteAllText(configPath, string.Join('\n', lines));
					Logger.Info(LogCategory.Export, $"Fixed ProjectRootPath from '{value}' -> '{_settings.ProjectRootPathDefaultValue}'");
					return 1;
				}
			}

			Logger.Warning(LogCategory.Export, "ProjectRootPath line not found in ResourceProviderConfiguration.asset");
			return 0;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to fix ProjectRootPath: {ex.Message}");
			return 0;
		}
	}

	private int CleanConfigurationDuplicates(string assetsPath, FileSystem fileSystem)
	{
		string configDir = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration");
		if (!fileSystem.Directory.Exists(configDir))
		{
			return 0;
		}

		int deletedCount = 0;
		foreach (string suffix in _settings.ConfigurationDuplicateSuffixes)
		{
			foreach (string assetFile in fileSystem.Directory.EnumerateFiles(configDir, $"*{suffix}.asset", SearchOption.TopDirectoryOnly))
			{
				string fileName = Path.GetFileName(assetFile);
				int suffixIndex = fileName.IndexOf(suffix + ".asset");
				string baseName = fileName[..suffixIndex] + ".asset";
				string basePath = Path.Join(configDir, baseName);

				if (!File.Exists(basePath))
				{
					Logger.Warning(LogCategory.Export, $"Skipping deletion of {fileName}: base version {baseName} does not exist");
					continue;
				}

				try
				{
					File.Delete(assetFile);
					deletedCount++;

					string metaPath = assetFile + ".meta";
					if (File.Exists(metaPath))
					{
						File.Delete(metaPath);
					}
				}
				catch (Exception ex)
				{
					Logger.Warning(LogCategory.Export, $"Failed to delete duplicate {assetFile}: {ex.Message}");
				}
			}
		}

		if (deletedCount > 0)
		{
			Logger.Info(LogCategory.Export, $"Cleaned {deletedCount} configuration duplicate files");
		}
		return deletedCount;
	}

	private int VerifyTypeAssemblies(string assetsPath, FileSystem fileSystem)
	{
		string engineConfigPath = fileSystem.Path.Join(assetsPath, "Resources", "naninovel", "configuration", "EngineConfiguration.asset");
		if (!fileSystem.File.Exists(engineConfigPath))
		{
			Logger.Warning(LogCategory.Export, "EngineConfiguration.asset not found");
			return 0;
		}

		string pluginsDir = fileSystem.Path.Join(assetsPath, "Plugins", "GameLibs");
		if (!fileSystem.Directory.Exists(pluginsDir))
		{
			Logger.Warning(LogCategory.Export, "Plugins/GameLibs directory not found");
			return 0;
		}

		try
		{
			string content = File.ReadAllText(engineConfigPath);
			string[] lines = content.Split('\n');

			List<int> linesToRemove = [];
			int removedCount = 0;

			for (int i = 0; i < lines.Length; i++)
			{
				string trimmed = lines[i].TrimStart();
				if (!trimmed.StartsWith("- "))
				{
					continue;
				}

				bool inTypeAssemblies = false;
				for (int j = i - 1; j >= 0; j--)
				{
					string t = lines[j].TrimStart();
					if (t.StartsWith("TypeAssemblies:"))
					{
						inTypeAssemblies = true;
						break;
					}
					if (!t.StartsWith("- ") && !string.IsNullOrEmpty(t))
					{
						break;
					}
				}

				if (!inTypeAssemblies)
				{
					continue;
				}

				string asmName = trimmed[2..].Trim();
				if (string.IsNullOrEmpty(asmName))
				{
					continue;
				}

				if (asmName == "Assembly-CSharp")
				{
					continue;
				}

				string dllPath = Path.Join(pluginsDir, asmName + ".dll");
				if (!File.Exists(dllPath))
				{
					Logger.Warning(LogCategory.Export, $"TypeAssemblies entry '{asmName}' has no corresponding DLL, removing");
					linesToRemove.Add(i);
					removedCount++;
				}
			}

			string[] workingLines = lines;
			if (removedCount > 0)
			{
				workingLines = lines
					.Select((line, index) => (line, index))
					.Where(x => !linesToRemove.Contains(x.index))
					.Select(x => x.line)
					.ToArray();
				Logger.Info(LogCategory.Export, $"Removed {removedCount} missing TypeAssemblies entries");
			}
			else
			{
				Logger.Info(LogCategory.Export, "All TypeAssemblies entries verified");
			}

			int injectedCount = 0;
			injectedCount += EnsureTypeAssembliesEntry(ref workingLines, "Assembly-CSharp", false, pluginsDir);
			injectedCount += EnsureTypeAssembliesEntry(ref workingLines, "Elringus.NaninovelInventory.Runtime", true, pluginsDir);

			if (removedCount > 0 || injectedCount > 0)
			{
				File.WriteAllText(engineConfigPath, string.Join('\n', workingLines));
			}

			return removedCount;
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to verify TypeAssemblies: {ex.Message}");
			return 0;
		}
	}

	private static int EnsureTypeAssembliesEntry(ref string[] lines, string entryName, bool verifyDll, string dllDir)
	{
		int typeAssembliesLineIndex = -1;
		for (int i = 0; i < lines.Length; i++)
		{
			if (lines[i].TrimStart().StartsWith("TypeAssemblies:"))
			{
				typeAssembliesLineIndex = i;
				break;
			}
		}

		if (typeAssembliesLineIndex == -1)
		{
			Logger.Warning(LogCategory.Export, $"TypeAssemblies section not found, cannot inject {entryName}");
			return 0;
		}

		int insertIndex = -1;
		string indent = "  - ";
		for (int i = typeAssembliesLineIndex + 1; i < lines.Length; i++)
		{
			string trimmed = lines[i].TrimStart();
			if (trimmed.StartsWith("- "))
			{
				string existingName = trimmed[2..].Trim();
				if (existingName == entryName)
				{
					return 0;
				}
				indent = lines[i].Substring(0, lines[i].Length - trimmed.Length) + "- ";
				insertIndex = i + 1;
			}
			else if (!string.IsNullOrEmpty(trimmed))
			{
				if (insertIndex == -1)
				{
					insertIndex = i;
				}
				break;
			}
		}

		if (insertIndex == -1)
		{
			insertIndex = typeAssembliesLineIndex + 1;
		}

		if (verifyDll)
		{
			string dllPath = Path.Join(dllDir, entryName + ".dll");
			if (!File.Exists(dllPath))
			{
				Logger.Warning(LogCategory.Export, $"Cannot inject TypeAssemblies entry '{entryName}': DLL not found at {dllPath}");
				return 0;
			}
		}

		List<string> result = new(lines.Length + 1);
		result.AddRange(lines[..insertIndex]);
		result.Add($"{indent}{entryName}");
		result.AddRange(lines[insertIndex..]);
		lines = result.ToArray();

		Logger.Info(LogCategory.Export, $"Injected required TypeAssemblies entry: {entryName}");
		return 1;
	}

	private bool VerifyNanoGameComponent(string assetsPath, FileSystem fileSystem)
	{
		string scenePath = fileSystem.Path.Join(assetsPath, "Scenes", "Game.unity");
		if (!fileSystem.File.Exists(scenePath))
		{
			Logger.Warning(LogCategory.Export, "Game.unity not found, skipping NanoGame verification");
			return false;
		}

		try
		{
			string content = File.ReadAllText(scenePath);
			bool hasNaninovelScript = content.Contains("guid: c3783f8a60b5812e721a9b1e90c49f42");

			if (hasNaninovelScript)
			{
				Logger.Info(LogCategory.Export, "NanoGame component found in Game.unity with valid Naninovel DLL reference");
				return true;
			}
			else
			{
				Logger.Warning(LogCategory.Export, "No Naninovel component reference found in Game.unity");
				return false;
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to verify NanoGame component: {ex.Message}");
			return false;
		}
	}
}