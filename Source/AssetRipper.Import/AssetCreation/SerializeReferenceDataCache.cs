using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using System.Runtime.CompilerServices;

namespace AssetRipper.Import.AssetCreation;

public static class SerializeReferenceDataCache
{
	private sealed class Entry
	{
		public byte[] Data { get; }
		public EndianType EndianType { get; }
		public Entry(byte[] data, EndianType endianType)
		{
			Data = data;
			EndianType = endianType;
		}
	}

	private static readonly ConditionalWeakTable<IMonoBehaviour, Entry> cache = new();

	public static void Store(IMonoBehaviour monoBehaviour, byte[] data, EndianType endianType)
	{
		cache.AddOrUpdate(monoBehaviour, new Entry(data, endianType));
	}

	public static bool TryGetData(IMonoBehaviour monoBehaviour, out byte[]? data, out EndianType endianType)
	{
		if (cache.TryGetValue(monoBehaviour, out Entry? entry))
		{
			data = entry.Data;
			endianType = entry.EndianType;
			return true;
		}
		data = null;
		endianType = EndianType.LittleEndian;
		return false;
	}
}