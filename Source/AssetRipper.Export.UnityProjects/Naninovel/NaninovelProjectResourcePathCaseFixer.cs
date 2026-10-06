using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel;

public sealed class NaninovelProjectResourcePathCaseFixer : IPostExporter
{
	private const string UnityCommonFolder = "UnityCommon";
	private const string ProjectResourcesFile = "ProjectResources.asset";
	private const string ResourcesFolder = "Resources";
	private const string NScriptsFolder = "nScripts";
	private const string CharactersPrefix = "Characters/";
	private static readonly Regex PathLineRegex = new(@"^(\s*- Path: )(.+)$", RegexOptions.Compiled);


	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "NaninovelProjectResourcePathCaseFixer: fixing ProjectResources.asset path casing to match .nani actor names...");

		string projectResourcesPath = fileSystem.Path.Join(settings.AssetsPath, ResourcesFolder, UnityCommonFolder, ProjectResourcesFile);
		if (!fileSystem.File.Exists(projectResourcesPath))
		{
			Logger.Info(LogCategory.Export, $"ProjectResources.asset not found at: {projectResourcesPath}");
			return;
		}

		string nScriptsPath = fileSystem.Path.Join(settings.AssetsPath, ResourcesFolder, NScriptsFolder);
		if (!fileSystem.Directory.Exists(nScriptsPath))
		{
			Logger.Info(LogCategory.Export, $"nScripts directory not found: {nScriptsPath}");
			return;
		}

		Dictionary<string, string> actorNameMap = ExtractActorNamesFromNani(nScriptsPath, fileSystem);
		Logger.Info(LogCategory.Export, $"NaninovelProjectResourcePathCaseFixer: extracted {actorNameMap.Count} actor names from .nani scripts.");

		if (actorNameMap.Count == 0)
		{
			Logger.Info(LogCategory.Export, "NaninovelProjectResourcePathCaseFixer: no actor names found, skipping.");
			return;
		}

		string content = fileSystem.File.ReadAllText(projectResourcesPath);
		string[] lines = content.Split('\n');

		int fixedCount = 0;
		StringBuilder result = new(content.Length);

		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			Match match = PathLineRegex.Match(line);

			if (match.Success)
			{
				string prefix = match.Groups[1].Value;
				string registeredPath = match.Groups[2].Value;

				string? fixedPath = FixPathCasing(registeredPath, actorNameMap);
				if (fixedPath is not null && fixedPath != registeredPath)
				{
					result.AppendLine(prefix + fixedPath);
					fixedCount++;
					continue;
				}
			}

			result.Append(line);
			if (i < lines.Length - 1)
			{
				result.Append('\n');
			}
		}

		if (fixedCount > 0)
		{
			fileSystem.File.WriteAllText(projectResourcesPath, result.ToString());
			Logger.Info(LogCategory.Export, $"NaninovelProjectResourcePathCaseFixer: fixed {fixedCount} resource path(s) casing to match .nani actor names.");
		}
		else
		{
			Logger.Info(LogCategory.Export, "NaninovelProjectResourcePathCaseFixer: no path casing fixes needed.");
		}
	}

	private static Dictionary<string, string> ExtractActorNamesFromNani(string nScriptsDir, FileSystem fileSystem)
	{
		Dictionary<string, string> actorNames = new(StringComparer.OrdinalIgnoreCase);

		foreach (string naniFile in fileSystem.Directory.EnumerateFiles(nScriptsDir, "*.nani", SearchOption.AllDirectories))
		{
			try
			{
				string content = fileSystem.File.ReadAllText(naniFile);
				string[] lines = content.Split('\n');
				foreach (string line in lines)
				{
					string trimmed = line.TrimStart();
					if (!trimmed.StartsWith("@char ") && !trimmed.StartsWith("@show "))
					{
						continue;
					}

					string[] tokens = trimmed.Split(' ');
					for (int t = 1; t < tokens.Length; t++)
					{
						string token = tokens[t];
						if (token.Contains(':'))
						{
							continue;
						}

						int dotIndex = token.IndexOf('.');
						string actorName = dotIndex > 0 ? token[..dotIndex] : token;
						if (actorName.Length > 0 && !actorNames.ContainsKey(actorName))
						{
							actorNames[actorName] = actorName;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"NaninovelProjectResourcePathCaseFixer: failed to read {naniFile}: {ex.Message}");
			}
		}

		return actorNames;
	}

	private static string? FixPathCasing(string registeredPath, Dictionary<string, string> actorNameMap)
	{
		if (!registeredPath.StartsWith(CharactersPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		string afterCharacters = registeredPath[CharactersPrefix.Length..];
		int slashIndex = afterCharacters.IndexOf('/');
		if (slashIndex <= 0)
		{
			return null;
		}

		string actorSegment = afterCharacters[..slashIndex];
		string rest = afterCharacters[slashIndex..];

		if (actorNameMap.TryGetValue(actorSegment, out string? correctName))
		{
			if (correctName != actorSegment)
			{
				return CharactersPrefix + correctName + rest;
			}
		}

		return null;
	}
}
