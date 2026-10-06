using AssetRipper.Assets;
using AssetRipper.IO.Endian;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public interface IFallbackStructure
{
	byte[] OriginalData { get; }
	EndianType EndianType { get; }
	string ScriptFullName { get; }
}