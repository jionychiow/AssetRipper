using AsmResolver.DotNet.Signatures;

namespace AssetRipper.SerializationLogic;

public sealed class SerializableManagedReferenceType : SerializableType
{
	public TypeSignature OriginalType { get; }

	public SerializableManagedReferenceType(TypeSignature originalType) 
		: base(null, PrimitiveType.Int, "managedReference")
	{
		OriginalType = originalType;
		MaxDepth = 0;
	}
}
