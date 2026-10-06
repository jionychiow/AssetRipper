using System;

namespace AssetRipper.Export.Modules.Audio.Fmod;

public sealed class FmodSound : FmodObjectBase, IDisposable
{
	internal FmodSound(IntPtr ptr) : base(ptr) { }

	public RESULT GetSubSound(int index, out FmodSound? subSound)
	{
		subSound = null;
		RESULT result = FmodNative.FMOD_Sound_GetSubSound(rawPtr, index, out IntPtr ptr);
		if (result == RESULT.OK)
		{
			subSound = new FmodSound(ptr);
		}
		return result;
	}

	public RESULT GetFormat(out SOUND_TYPE type, out SOUND_FORMAT format, out int channels, out int bits) =>
		FmodNative.FMOD_Sound_GetFormat(rawPtr, out type, out format, out channels, out bits);

	public RESULT GetDefaults(out float frequency, out int priority) =>
		FmodNative.FMOD_Sound_GetDefaults(rawPtr, out frequency, out priority);

	public RESULT GetLength(out uint length, TIMEUNIT lengthType) =>
		FmodNative.FMOD_Sound_GetLength(rawPtr, out length, lengthType);

	public RESULT Lock(uint offset, uint length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2) =>
		FmodNative.FMOD_Sound_Lock(rawPtr, offset, length, out ptr1, out ptr2, out len1, out len2);

	public RESULT Unlock(IntPtr ptr1, IntPtr ptr2, uint len1, uint len2) =>
		FmodNative.FMOD_Sound_Unlock(rawPtr, ptr1, ptr2, len1, len2);

	public RESULT Release() => FmodNative.FMOD_Sound_Release(rawPtr);

	public void Dispose()
	{
		if (IsValid)
		{
			Release();
		}
	}
}