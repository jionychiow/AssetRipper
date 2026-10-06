using System;

namespace AssetRipper.Export.Modules.Audio.Fmod;

public abstract class FmodObjectBase
{
	protected readonly IntPtr rawPtr;

	protected FmodObjectBase(IntPtr ptr) => rawPtr = ptr;

	public bool IsValid => rawPtr != IntPtr.Zero;
	public IntPtr RawPtr => rawPtr;

	public override bool Equals(object? obj) => obj is FmodObjectBase other && rawPtr == other.rawPtr;
	public override int GetHashCode() => rawPtr.GetHashCode();
}