using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Export.UnityProjects.Naninovel;

public sealed class CanvasRenderModeSceneFixer : IPostExporter
{
	private static readonly Regex CanvasBlockRegex = new(@"^Canvas:\s*$", RegexOptions.Compiled);
	private static readonly Regex RenderModeRegex = new(@"^(\s*m_RenderMode:\s*)(\d+)$", RegexOptions.Compiled);
	private static readonly Regex CameraRefRegex = new(@"^(\s*m_Camera:\s*)\{fileID:\s*(\d+)\}$", RegexOptions.Compiled);
	private static readonly Regex GameObjectBlockRegex = new(@"^GameObject:\s*$", RegexOptions.Compiled);
	private static readonly Regex ComponentRegex = new(@"^\s*- \{fileID:\s*(\d+), type: (\d+)\}$", RegexOptions.Compiled);
	private static readonly Regex FileHeaderRegex = new(@"^--- !u!(\d+) &(\d+)$", RegexOptions.Compiled);

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		Logger.Info(LogCategory.Export, "CanvasRenderModeSceneFixer: fixing Canvas renderMode for Scene view visibility...");

		int totalFixed = 0;
		string assetsPath = settings.AssetsPath;

		foreach (string filePath in EnumerateSceneAndPrefabFiles(assetsPath, fileSystem))
		{
			try
			{
				totalFixed += FixCanvasRenderModeInFile(filePath, fileSystem);
			}
			catch (Exception ex)
			{
				Logger.Warning(LogCategory.Export, $"CanvasRenderModeSceneFixer: failed to process {filePath}: {ex.Message}");
			}
		}

		if (totalFixed > 0)
		{
			Logger.Info(LogCategory.Export, $"CanvasRenderModeSceneFixer: fixed {totalFixed} Canvas renderMode(s) from ScreenSpaceOverlay to ScreenSpaceCamera.");
		}
		else
		{
			Logger.Info(LogCategory.Export, "CanvasRenderModeSceneFixer: no Canvas renderMode fixes needed.");
		}
	}

	private static IEnumerable<string> EnumerateSceneAndPrefabFiles(string assetsPath, FileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists(assetsPath))
		{
			yield break;
		}

		foreach (string file in fileSystem.Directory.EnumerateFiles(assetsPath, "*.unity", SearchOption.AllDirectories))
		{
			yield return file;
		}

		foreach (string file in fileSystem.Directory.EnumerateFiles(assetsPath, "*.prefab", SearchOption.AllDirectories))
		{
			yield return file;
		}
	}

	private static int FixCanvasRenderModeInFile(string filePath, FileSystem fileSystem)
	{
		string content = fileSystem.File.ReadAllText(filePath);
		string[] lines = content.Split('\n');

		bool inCanvasBlock = false;
		int canvasBlockStart = -1;
		int renderModeLineIndex = -1;
		int cameraRefLineIndex = -1;
		int currentRenderMode = -1;
		int fixedCount = 0;

		for (int i = 0; i < lines.Length; i++)
		{
			Match headerMatch = FileHeaderRegex.Match(lines[i]);
			if (headerMatch.Success)
			{
				inCanvasBlock = false;
				renderModeLineIndex = -1;
				cameraRefLineIndex = -1;
				currentRenderMode = -1;
				continue;
			}

			if (CanvasBlockRegex.IsMatch(lines[i]))
			{
				inCanvasBlock = true;
				canvasBlockStart = i;
				continue;
			}

			if (inCanvasBlock)
			{
				Match renderModeMatch = RenderModeRegex.Match(lines[i]);
				if (renderModeMatch.Success)
				{
					currentRenderMode = int.Parse(renderModeMatch.Groups[2].Value);
					renderModeLineIndex = i;
					continue;
				}

				Match cameraMatch = CameraRefRegex.Match(lines[i]);
				if (cameraMatch.Success)
				{
					cameraRefLineIndex = i;
					continue;
				}

				if (renderModeLineIndex >= 0 && currentRenderMode == 0)
				{
					lines[renderModeLineIndex] = RenderModeRegex.Replace(lines[renderModeLineIndex], "${1}1");

					if (cameraRefLineIndex >= 0)
					{
						lines[cameraRefLineIndex] = CameraRefRegex.Replace(lines[cameraRefLineIndex], "${1}{fileID: 0}");
					}

					fixedCount++;
					inCanvasBlock = false;
					renderModeLineIndex = -1;
					cameraRefLineIndex = -1;
					currentRenderMode = -1;
				}
			}
		}

		if (fixedCount > 0)
		{
			string newContent = string.Join('\n', lines);
			fileSystem.File.WriteAllText(filePath, newContent);
		}

		return fixedCount;
	}
}