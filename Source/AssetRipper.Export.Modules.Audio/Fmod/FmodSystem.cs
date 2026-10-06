using System;

namespace AssetRipper.Export.Modules.Audio.Fmod;

public sealed class FmodSystem : FmodObjectBase, IDisposable
{
	internal FmodSystem(IntPtr ptr) : base(ptr) { }

	public RESULT Init(int maxChannels, INITFLAGS flags, IntPtr extraDriverData) =>
		FmodNative.FMOD_System_Init(rawPtr, maxChannels, flags, extraDriverData);

	public RESULT CreateSound(byte[] data, MODE mode, ref CREATESOUNDEXINFO exinfo, out FmodSound? sound)
	{
		sound = null;
		exinfo.cbsize = System.Runtime.InteropServices.Marshal.SizeOf<CREATESOUNDEXINFO>();
		RESULT result = FmodNative.FMOD_System_CreateSound(rawPtr, data, mode, ref exinfo, out IntPtr ptr);
		if (result == RESULT.OK)
		{
			sound = new FmodSound(ptr);
		}
		return result;
	}

	public RESULT Release() => FmodNative.FMOD_System_Release(rawPtr);

	public void Dispose()
	{
		if (IsValid)
		{
			Release();
		}
	}
}