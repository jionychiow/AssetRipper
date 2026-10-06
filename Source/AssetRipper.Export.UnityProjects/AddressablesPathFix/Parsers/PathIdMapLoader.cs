using System.Text.Json;
using System.Text.Json.Serialization;
using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

#pragma warning disable IL2026, IL3050
using AssetRipper.Import.Logging;
using AssetRipper.Processing;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Parsers;

public sealed class PathIdMapLoader
{
	public PathIdNameMap Load(string? pathIdMapPath, GameData gameData, FileSystem fileSystem)
	{
		PathIdNameMap? jsonMap = TryLoadFromJson(pathIdMapPath, fileSystem);
		if (jsonMap is not null && jsonMap.Count > 0)
		{
			Logger.Info(LogCategory.Export, $"Loaded PathID map from JSON: {jsonMap.Count} entries");
			return jsonMap;
		}

		Logger.Info(LogCategory.Export, "PathID map JSON not available, building from GameBundle...");
		return BuildFromGameBundle(gameData);
	}

	private static PathIdNameMap? TryLoadFromJson(string? pathIdMapPath, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(pathIdMapPath) || !fileSystem.File.Exists(pathIdMapPath))
		{
			return null;
		}

		try
		{
			string json = fileSystem.File.ReadAllText(pathIdMapPath);
			SerializedGameInfoDto? dto = JsonSerializer.Deserialize<SerializedGameInfoDto>(json);
			if (dto is null || dto.Files.Count == 0)
			{
				return null;
			}

			Dictionary<long, PathIdNameEntry> byPathId = new();
			Dictionary<string, List<PathIdNameEntry>> byName = new(StringComparer.OrdinalIgnoreCase);

			foreach (SerializedFileInfoDto file in dto.Files)
			{
				foreach (SerializedAssetInfoDto asset in file.Assets)
				{
					string name = asset.Name ?? string.Empty;
					string type = asset.Type ?? string.Empty;
					PathIdNameEntry entry = new(asset.PathID, name, type, file.Name ?? string.Empty);
					byPathId[asset.PathID] = entry;

					if (!string.IsNullOrEmpty(name))
					{
						if (!byName.TryGetValue(name, out List<PathIdNameEntry>? list))
						{
							list = [];
							byName[name] = list;
						}
						list.Add(entry);
					}
				}
			}

			return new PathIdNameMap
			{
				ByPathId = byPathId,
				ByName = byName.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<PathIdNameEntry>)kvp.Value, StringComparer.OrdinalIgnoreCase),
			};
		}
		catch (Exception ex)
		{
			Logger.Info(LogCategory.Export, $"Failed to load path_id_map.json: {ex.Message}");
			return null;
		}
	}

	private static PathIdNameMap BuildFromGameBundle(GameData gameData)
	{
		try
		{
			Dictionary<long, PathIdNameEntry> byPathId = new();
			Dictionary<string, List<PathIdNameEntry>> byName = new(StringComparer.OrdinalIgnoreCase);

			foreach (SerializedAssetCollection collection in gameData.GameBundle.FetchAssetCollections().OfType<SerializedAssetCollection>())
			{
				foreach (IUnityObjectBase asset in collection)
				{
					string name = (asset as INamed)?.Name ?? string.Empty;
					string type = asset.ClassName;
					PathIdNameEntry entry = new(asset.PathID, name, type, collection.Name);
					byPathId[asset.PathID] = entry;

					if (!string.IsNullOrEmpty(name))
					{
						if (!byName.TryGetValue(name, out List<PathIdNameEntry>? list))
						{
							list = [];
							byName[name] = list;
						}
						list.Add(entry);
					}
				}
			}

			Logger.Info(LogCategory.Export, $"从 GameBundle 重建 PathID 映射: {byPathId.Count} 个条目");

			return new PathIdNameMap
			{
				ByPathId = byPathId,
				ByName = byName.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<PathIdNameEntry>)kvp.Value, StringComparer.OrdinalIgnoreCase),
			};
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to build PathID map from GameBundle: {ex.Message}");
			return new PathIdNameMap();
		}
	}

	private sealed class SerializedGameInfoDto
	{
		[JsonPropertyName("Files")]
		public List<SerializedFileInfoDto> Files { get; set; } = new();
	}

	private sealed class SerializedFileInfoDto
	{
		[JsonPropertyName("Name")]
		public string? Name { get; set; }
		[JsonPropertyName("Assets")]
		public List<SerializedAssetInfoDto> Assets { get; set; } = new();
	}

	private sealed class SerializedAssetInfoDto
	{
		[JsonPropertyName("PathID")]
		public long PathID { get; set; }
		[JsonPropertyName("Name")]
		public string? Name { get; set; }
		[JsonPropertyName("Type")]
		public string? Type { get; set; }
	}
}