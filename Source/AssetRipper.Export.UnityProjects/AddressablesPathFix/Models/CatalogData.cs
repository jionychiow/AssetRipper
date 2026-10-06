using AssetRipper.Import.Logging;

namespace AssetRipper.Export.UnityProjects.AddressablesPathFix.Models;

public sealed class CatalogData
{
	public required IReadOnlyList<CatalogKeyEntry> Keys { get; init; }
	public required IReadOnlyList<string> InternalIds { get; init; }
	public required IReadOnlyList<ResourceTypeEntry> ResourceTypes { get; init; }
	public required IReadOnlyDictionary<string, string> AddressByGuid { get; init; }
	public required IReadOnlyDictionary<string, IReadOnlyList<string>> AddressesByPrefix { get; init; }
	public IReadOnlyDictionary<int, int>? EntryData { get; init; }
	public IReadOnlyDictionary<int, int>? EntryTypeData { get; init; }
	public IReadOnlyDictionary<string, int>? KeyIndexByAddress { get; init; }

	public ResourceTypeEntry? GetAddressType(string address)
	{
		ResourceTypeEntry? inferred = InferTypeByPrefix(address);
		if (inferred is not null)
		{
			return inferred;
		}

		if (KeyIndexByAddress is not null && EntryTypeData is not null)
		{
			if (KeyIndexByAddress.TryGetValue(address, out int keyIndex) && EntryTypeData.TryGetValue(keyIndex, out int typeIndex))
			{
				if (typeIndex >= 0 && typeIndex < ResourceTypes.Count)
				{
					ResourceTypeEntry candidate = ResourceTypes[typeIndex];
					if (IsResourceType(candidate))
					{
						return candidate;
					}
				}
			}
		}

		return null;
	}

	private static bool IsResourceType(ResourceTypeEntry entry)
	{
		string className = entry.ClassName;
		if (className is "SceneInstance" or "DOTweenSettings" or "Unknown" or "IAssetBundleResource")
		{
			return false;
		}
		return true;
	}

	public AddressLocation GetAddressLocation(string address)
	{
		if (KeyIndexByAddress is not null && EntryData is not null)
		{
			if (KeyIndexByAddress.TryGetValue(address, out int keyIndex) && EntryData.TryGetValue(keyIndex, out int internalIdIndex))
			{
				if (internalIdIndex >= 0 && internalIdIndex < InternalIds.Count)
				{
					string internalId = InternalIds[internalIdIndex];
					if (internalId.Contains("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", StringComparison.Ordinal))
					{
						return AddressLocation.BundleAsset;
					}
					if (internalId.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
					{
						return AddressLocation.ResourcesPath;
					}
					return AddressLocation.DirectFile;
				}
			}
		}

		return AddressLocation.Unknown;
	}

	private ResourceTypeEntry? InferTypeByPrefix(string address)
	{
		int slashIndex = address.IndexOf('/');
		string prefix = slashIndex >= 0 ? address[..slashIndex] : address;
		string lowerPrefix = prefix.ToLowerInvariant();

		return lowerPrefix switch
		{
			"backgrounds" => new ResourceTypeEntry("UnityEngine.CoreModule", "Texture2D"),
			"characters" => new ResourceTypeEntry("UnityEngine.CoreModule", "Sprite"),
			"character" => new ResourceTypeEntry("UnityEngine.CoreModule", "Sprite"),
			"audio" => new ResourceTypeEntry("UnityEngine.AudioModule", "AudioClip"),
			"audios" => new ResourceTypeEntry("UnityEngine.AudioModule", "AudioClip"),
			"nscripts" => new ResourceTypeEntry("UnityEngine.CoreModule", "TextAsset"),
			"scripts" => new ResourceTypeEntry("UnityEngine.CoreModule", "TextAsset"),
			"anims" => new ResourceTypeEntry("UnityEngine.AnimationModule", "AnimationClip"),
			"animations" => new ResourceTypeEntry("UnityEngine.AnimationModule", "AnimationClip"),
			"scenes" => new ResourceTypeEntry("UnityEngine.CoreModule", "SceneAsset"),
			"fonts" => new ResourceTypeEntry("UnityEngine.CoreModule", "Font"),
			"videos" => new ResourceTypeEntry("UnityEngine.VideoModule", "VideoClip"),
			"video" => new ResourceTypeEntry("UnityEngine.VideoModule", "VideoClip"),
			"materials" => new ResourceTypeEntry("UnityEngine.CoreModule", "Material"),
			"sprites" => new ResourceTypeEntry("UnityEngine.CoreModule", "Sprite"),
			"textures" => new ResourceTypeEntry("UnityEngine.CoreModule", "Texture2D"),
			"music" => new ResourceTypeEntry("UnityEngine.AudioModule", "AudioClip"),
			"voicelines" => new ResourceTypeEntry("UnityEngine.AudioModule", "AudioClip"),
			"stops" => new ResourceTypeEntry("UnityEngine.AudioModule", "AudioClip"),
			"movies" => new ResourceTypeEntry("UnityEngine.VideoModule", "VideoClip"),
			_ => null,
		};
	}
}
